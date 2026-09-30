// Dark Matter for real money (2026-09-30): the game's side of the App Store
// (native side: Plugins/iOS/GRStore.swift, StoreKit 2). It loads the packs'
// localized prices, starts purchases, and for every verified transaction the
// store hands over: credits the pack (PurchaseSystem — once per transaction),
// saves, and only then tells the store the transaction is finished. A purchase
// interrupted by a crash is handed over again at the next launch.
// Outside an iOS device build (the Editor), the store reports itself unavailable.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game
{
    public sealed class StoreService : MonoBehaviour
    {
        public enum Status { Loading, Ready, Unavailable }

        public static StoreService? Instance { get; private set; }

        public Status State { get; private set; } = Status.Loading;
        public string Error { get; private set; } = "";
        /// <summary>Product id → the App Store's localized price ("$4.99", "4,99 €").</summary>
        public readonly Dictionary<string, string> Prices = new();
        /// <summary>A purchase is in progress (the App Store sheet is up).</summary>
        public bool Busy { get; private set; }
        /// <summary>Raised on the main thread when prices load or a purchase ends.</summary>
        public event Action? Changed;

        GameContext _ctx = null!;
        float _retryAt;

        void Awake()
        {
            Instance = this;
            _ctx = GetComponent<GameContext>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
#if UNITY_IOS && !UNITY_EDITOR
            _GRStoreStart();
            Load();
#else
            State = Status.Unavailable;
            Error = "Purchases are only available in the iOS app";
#endif
        }

        public void Load()
        {
#if UNITY_IOS && !UNITY_EDITOR
            State = Status.Loading;
            var ids = new List<string>();
            foreach (var p in DarkMatterPacks.All) ids.Add(p.ProductId);
            _GRStoreLoad(string.Join(",", ids));
#endif
        }

        public void Buy(DarkMatterPack pack)
        {
            if (Busy || State != Status.Ready || !Prices.ContainsKey(pack.ProductId)) return;
            Busy = true;
            Changed?.Invoke();
#if UNITY_IOS && !UNITY_EDITOR
            _GRStoreBuy(pack.ProductId);
#endif
        }

        void Update()
        {
#if UNITY_IOS && !UNITY_EDITOR
            for (int guard = 0; guard < 16; guard++)
            {
                string? json = _GRStorePoll();
                if (json == null) break;
                Handle(json);
            }
            // Offline at launch: try the prices again now and then.
            if (State == Status.Unavailable && Time.unscaledTime >= _retryAt)
            {
                _retryAt = Time.unscaledTime + 60f;
                Load();
            }
#endif
        }

        void Handle(string json)
        {
            if (Json.Parse(json) is not Dictionary<string, object?> e || e["type"] is not string type) return;
            string Str(string key) => e.TryGetValue(key, out var v) && v is string s ? s : "";
            var ui = UI.UIController.Instance;
            switch (type)
            {
                case "products":
                    Prices.Clear();
                    if (e.TryGetValue("items", out var raw) && raw is List<object?> items)
                        foreach (var item in items)
                            if (item is Dictionary<string, object?> p && p["id"] is string id && p["price"] is string price)
                                Prices[id] = price;
                    State = Prices.Count > 0 ? Status.Ready : Status.Unavailable;
                    Error = Prices.Count > 0 ? "" : "The App Store has no Dark Matter for sale right now";
                    break;
                case "products_failed":
                    State = Status.Unavailable;
                    Error = "Couldn't reach the App Store. Check your connection";
                    _retryAt = Time.unscaledTime + 60f;
                    break;
                case "purchased":
                    Credit(Str("product"), Str("transaction"));
                    Busy = false;
                    break;
                case "pending":
                    Busy = false;
                    ui?.Toast("Purchase waiting for approval: the Dark Matter arrives once it's approved", UI.Icon.Clock, UI.UiTheme.Energy);
                    break;
                case "cancelled":
                    Busy = false;
                    break;
                case "failed":
                    Busy = false;
                    ui?.Toast(Str("error").Length > 0 ? Str("error") : "The purchase didn't go through", UI.Icon.Warning, UI.UiTheme.Bad);
                    break;
            }
            Changed?.Invoke();
        }

        void Credit(string productId, string transactionId)
        {
            var state = _ctx.State;
            // No galaxy loaded yet (the title screen): leave it unfinished; the store
            // hands it over again at the next launch, once a game is running.
            if (state == null || !LocalBootstrap.Booted || transactionId.Length == 0) return;
            var outcome = PurchaseSystem.Credit(state, productId, transactionId, out int dm);
            if (outcome == PurchaseSystem.Outcome.UnknownProduct) return; // not ours to finish
            LocalBootstrap.RequestSync(); // saved before the store is told it's done
#if UNITY_IOS && !UNITY_EDITOR
            _GRStoreFinish(transactionId);
#endif
            if (outcome == PurchaseSystem.Outcome.Credited)
            {
                GameAudio.Feedback(Sfx.Coins, Haptic.Success);
                UI.UIController.Instance?.Toast($"+{dm:N0} Dark Matter. Thank you, Commander", UI.Icon.Star, UI.UiTheme.DarkMatter);
            }
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void _GRStoreStart();
        [DllImport("__Internal")] static extern void _GRStoreLoad(string idsCsv);
        [DllImport("__Internal")] static extern void _GRStoreBuy(string productId);
        [DllImport("__Internal")] static extern string? _GRStorePoll();
        [DllImport("__Internal")] static extern void _GRStoreFinish(string transactionId);
#endif
    }
}
