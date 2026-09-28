// Resolve-at-arrival for raids/spies against the simulated commanders: the
// fleet flies for real, and the battle/intel resolves only when it ARRIVES —
// against the bot's LIVE state at that moment (bots keep building while your
// fleet is in flight, so what you spied earlier may not be what you meet).
//
// Mechanics: RaidService registers a pending entry per launched march
// (persisted in PlayerPrefs so app restarts / offline catch-up don't lose it).
// This component scans the pending list every second; once a march is no
// longer Outbound (it reached the target and the sim's empty-tile turnaround
// flipped it — live or during catch-up), it resolves synchronously:
//   - march still flying home → survivors + loot ride the march;
//   - march already docked (offline catch-up finished the round trip) →
//     losses subtract from docked ships, loot credits directly.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;
using UnityEngine;

namespace GalaxyRoyale.Game
{
    public sealed class RaidArrivals : MonoBehaviour
    {
        const string PrefsKey = "galaxyroyale.pending_pvp";

        sealed class Pending
        {
            public int MarchId;
            public int TargetBotId;
            public string TargetName = "";
            public bool IsRaid;
            public Dictionary<HullId, int> Sent = new();
            public int TargetX, TargetY;
        }

        static RaidArrivals? _instance;
        GameContext _ctx = null!;
        readonly List<Pending> _pending = new();
        float _nextScan;

        void Awake()
        {
            _instance = this;
            _ctx = GetComponent<GameContext>();
            Load();
            // A recalled fleet never lands — drop its pending raid/spy so it can't
            // file a phantom victory (user-reported bug). RecallMarch is UI-only
            // (always live), so this fires before the march ever completes.
            MarchSystem.OnMarchRecalled = OnMarchRecalled;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (MarchSystem.OnMarchRecalled == OnMarchRecalled) MarchSystem.OnMarchRecalled = null;
        }

        void OnMarchRecalled(int marchId)
        {
            var gone = _pending.FindAll(p => p.MarchId == marchId);
            if (gone.Count == 0) return;
            StandDownWings(marchId);
            _pending.RemoveAll(p => p.MarchId == marchId);
            Persist();
            UI.UIController.Instance?.Toast("Fleet recalled — no engagement");
        }

        /// <summary>A joint strike that never fought: the clanmates' wings head home untouched.</summary>
        void StandDownWings(int marchId)
        {
            var galaxy = _ctx.Bots;
            var state = _ctx.State;
            if (galaxy != null && state != null) StrikeSystem.TurnEscortsHome(galaxy, marchId, state.Tick);
        }

        /// <summary>Called by RaidService at launch. `sent` = the full fleet that flew.</summary>
        public static void Register(int marchId, int targetBotId, string targetName,
            bool isRaid, Dictionary<HullId, int> sent, TileXY target)
        {
            var inst = _instance;
            if (inst == null) return;
            inst._pending.Add(new Pending
            {
                MarchId = marchId,
                TargetBotId = targetBotId,
                TargetName = targetName,
                IsRaid = isRaid,
                Sent = new Dictionary<HullId, int>(sent),
                TargetX = target.X,
                TargetY = target.Y,
            });
            inst.Persist();
        }

        void Update()
        {
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + 1f;
            var state = _ctx.State;
            var galaxy = _ctx.Bots;
            if (state == null || galaxy == null || _pending.Count == 0) return;

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var entry = _pending[i];
                var march = state.Marches.Find(m => m.Id == entry.MarchId);
                if (march != null && march.Phase == MarchPhase.Outbound && !march.Recalled)
                    continue; // still flying out
                // Recalled mid-flight → aborted, no battle/intel (defensive net in
                // case the OnMarchRecalled hook was missed, e.g. across a reload).
                if (march != null && march.Recalled)
                {
                    StandDownWings(entry.MarchId);
                    _pending.RemoveAt(i);
                    Persist();
                    continue;
                }

                var bot = galaxy.Find(entry.TargetBotId);
                if (bot == null)
                {
                    UI.UIController.Instance?.Toast(
                        $"Contact lost with {entry.TargetName} — no engagement");
                }
                else if (bot.HomeTile.X != entry.TargetX || bot.HomeTile.Y != entry.TargetY)
                {
                    // The bot ported away while the fleet was in flight — the
                    // coordinates hold nothing but vacuum (their escape worked).
                    UI.UIController.Instance?.Toast(
                        $"{entry.TargetName} relocated — your {(entry.IsRaid ? "fleet" : "probe")} found empty space");
                }
                else if (entry.IsRaid && ClanSystem.SameClanAsPlayer(state, bot))
                {
                    // They joined your clan while the fleet was in flight — it stands down.
                    UI.UIController.Instance?.Toast(
                        $"{entry.TargetName} is your clanmate now — your fleet stood down");
                }
                else if (entry.IsRaid) ResolveRaid(state, bot, entry);
                else ResolveSpy(state, bot, entry);

                StandDownWings(entry.MarchId); // no-op when the raid resolved (it settles its wings)
                _pending.RemoveAt(i);
                Persist();
            }
        }

        void ResolveRaid(GameState state, BotEmpire bot, Pending entry)
        {
            // The fight itself — your fleet and any joint-strike wings against their
            // garrison and clan — lives in the sim (StrikeSystem) so it's testable.
            var mail = StrikeSystem.ResolveRaid(state, _ctx.Bots!, bot, entry.MarchId, entry.Sent,
                new TileXY(entry.TargetX, entry.TargetY));
            _ctx.Events!.Emit(new BattleResolved(mail.Report));
        }

        void ResolveSpy(GameState state, BotEmpire bot, Pending entry)
        {
            var snapshot = BotSystem.SnapshotOf(bot);
            var report = new SpyReport
            {
                Id = state.NextReportId++,
                AtTick = state.Tick,
                Target = new TileXY(entry.TargetX, entry.TargetY),
                Subject = $"Recon: {snapshot.CommanderName}",
                Intel = new SpyIntel
                {
                    Kind = NodeKind.Camp, // renders the garrison block in mail detail
                    Garrison = new Dictionary<HullId, int>(snapshot.Ships),
                    // Full recon (user spec): the rival's whole tech stack + base
                    // levels + wallet (lootable vs shielded).
                    Research = new Dictionary<TechId, int>(snapshot.Research),
                    Buildings = new Dictionary<BuildingId, int>(snapshot.Buildings),
                    LootableMilli = snapshot.LootableMilli.Clone(),
                    ProtectedMilli = snapshot.ProtectedMilli.Clone(),
                },
            };
            RaidService.InsertMail(state, report);
            _ctx.Events!.Emit(new SpyReportReceived(report));
            UI.UIController.Instance?.Toast($"Probe over {snapshot.CommanderName} — intel filed to Mail");

            // Reactive bots (user spec): a scanned commander usually notices the
            // sweep and sends a probe of their own back at YOU (your Radar Station
            // files the counter-scan mail when it arrives).
            if (Sim.Rng.Hash2d(unchecked((uint)state.Seed * 71u + (uint)bot.Id),
                    entry.MarchId, state.Tick) < BotSystem.SpyBackChance)
                bot.SpyBackAtTick = state.Tick + 300
                    + (int)(Sim.Rng.Hash2d((uint)bot.Id, state.Tick, 977) * 1200);
        }

        /// <summary>
        /// Forget every pending resolution — called when a NEW galaxy is founded
        /// (NEW GAME / RESET EMPIRE). The list lives in PlayerPrefs, outside the
        /// save, and march ids restart at 1 in a fresh game: a stale entry could
        /// resolve against the new empire (ships removed, loot credited) or even
        /// hijack a new march that happened to reuse the id.
        /// </summary>
        public static void ClearPending()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            _instance?._pending.Clear();
        }

        // ---------- persistence (device-local, like Favorites) ----------

        void Persist()
        {
            var rows = new List<object?>();
            foreach (var p in _pending)
                rows.Add(new Dictionary<string, object?>
                {
                    ["marchId"] = (long)p.MarchId,
                    ["botId"] = (long)p.TargetBotId,
                    ["targetName"] = p.TargetName,
                    ["kind"] = p.IsRaid ? "raid" : "spy",
                    ["sent"] = SaveCodec.Comp(p.Sent),
                    ["tx"] = (long)p.TargetX,
                    ["ty"] = (long)p.TargetY,
                });
            PlayerPrefs.SetString(PrefsKey, Json.Write(rows));
            // Flush now (Unity only writes prefs at quit otherwise): a raid lost
            // to a crash would strand the clanmates' ships that flew with it.
            PlayerPrefs.Save();
        }

        void Load()
        {
            _pending.Clear();
            string raw = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(raw)) return;
            try
            {
                if (Json.Parse(raw) is not List<object?> rows) return;
                foreach (var r in rows)
                {
                    if (r is not Dictionary<string, object?> d) continue;
                    _pending.Add(new Pending
                    {
                        MarchId = d["marchId"] is long id ? (int)id : 0,
                        TargetBotId = d["botId"] is long bid ? (int)bid : 0,
                        TargetName = d["targetName"] as string ?? "",
                        IsRaid = (d["kind"] as string) != "spy",
                        Sent = d["sent"] is Dictionary<string, object?> comp
                            ? SaveCodec.DecComp(comp) : new Dictionary<HullId, int>(),
                        TargetX = d["tx"] is long tx ? (int)tx : 0,
                        TargetY = d["ty"] is long ty ? (int)ty : 0,
                    });
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RaidArrivals] corrupt pending list dropped ({e.Message})");
            }
        }
    }
}
