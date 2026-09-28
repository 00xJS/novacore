// Radar Station defender flow — single-player edition. Watches the simulated
// galaxy's inbound attacks (BotGalaxy.Inbound); when a contact enters the
// radar's lead window (30s × level before arrival) it fires a warning:
// mailbox item (detail gated by RadarSystem.DetailTier), toast, and a pulsing
// red alert halo around the home planet. Fleet-kind contacts also surface in
// the Queues FLEETS tab via DetectedThreats, and the map draws their track.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Systems;
using UnityEngine;

namespace GalaxyRoyale.Game
{
    /// <summary>A detected inbound contact, tier-sanitized for the UI. Tick-based.</summary>
    public sealed class RadarContact
    {
        public int Id;
        public bool IsFleet;
        public int LaunchTick;
        public int ArrivesAtTick;
        public int FromX, FromY;
        public string AttackerName = "";
        public int FleetCount;
        public Dictionary<HullId, int> FleetComp = new();
    }

    public sealed class RadarService : MonoBehaviour
    {
        public const float PollSeconds = 2f;
        /// <summary>The alert halo lingers this long after a contact arrives.</summary>
        public const int ArrivedGraceSec = 60;

        GameContext _ctx = null!;
        float _nextPoll;
        /// <summary>Attack ids already turned into a mailbox warning (per session — a
        /// still-inbound contact re-warns after an app restart, which is fine).</summary>
        readonly HashSet<int> _warned = new();
        readonly List<RadarContact> _detected = new();
        SpriteRenderer? _alertHalo;
        /// <summary>ArrivesAtTick of recently landed contacts (keeps the halo lit through the grace window).</summary>
        readonly List<int> _recentArrivals = new();

        /// <summary>Fleet-kind contacts inside the radar window, for the fleet/queues UI. Never null.</summary>
        public static IReadOnlyList<RadarContact> DetectedThreats => _instance != null
            ? _instance._detected : Array.Empty<RadarContact>();
        static RadarService? _instance;

        void Awake()
        {
            _instance = this;
            _ctx = GetComponent<GameContext>();
            _nextPoll = Time.time + 3f; // first look shortly after boot
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void Update()
        {
            if (Time.time >= _nextPoll)
            {
                _nextPoll = Time.time + PollSeconds;
                Scan();
            }
            UpdateHalo();
        }

        void Scan()
        {
            var state = _ctx.State;
            var galaxy = _ctx.Bots;
            if (state == null || galaxy == null) return;
            int level = RadarSystem.Level(state);
            if (level < 1) { _detected.Clear(); return; } // no radar, no ears

            int leadSec = RadarSystem.WarnLeadSeconds(level);
            int tier = RadarSystem.DetailTier(level);
            int now = state.Tick;

            _detected.Clear();
            foreach (var atk in galaxy.Inbound)
            {
                int toArrival = atk.ArrivesAtTick - now;
                if (toArrival > leadSec) continue; // still outside our ears
                if (toArrival <= 0) continue;      // impact passed — halo handles the aftermath

                var bot = galaxy.Find(atk.BotId);
                string attackerName = bot?.Name ?? "unknown";
                int count = 0;
                foreach (var kv in atk.Ships) count += kv.Value;

                // Cards/map obey the same detail tier as the mail (position data
                // isn't tier-gated — a detected contact is a contact).
                _detected.Add(new RadarContact
                {
                    Id = atk.Id,
                    IsFleet = atk.IsFleet,
                    LaunchTick = atk.LaunchTick,
                    ArrivesAtTick = atk.ArrivesAtTick,
                    FromX = atk.From.X,
                    FromY = atk.From.Y,
                    AttackerName = tier >= 3 ? attackerName : "",
                    FleetCount = tier >= 3 ? count : 0,
                    FleetComp = tier >= 4 ? new Dictionary<HullId, int>(atk.Ships) : new(),
                });

                if (_warned.Contains(atk.Id)) continue;
                _warned.Add(atk.Id);
                _recentArrivals.Add(atk.ArrivesAtTick);
                FileWarning(state, atk, attackerName, count, tier, toArrival);
            }
        }

        void FileWarning(GameState state, BotAttack atk, string attackerName, int count,
            int tier, int arrivesInSec)
        {
            string what = tier >= 2 ? (atk.IsFleet ? "war fleet" : "spy probe") : "unknown contact";
            var warning = new RadarWarning
            {
                Id = state.NextReportId++,
                AtTick = state.Tick,
                Target = state.HomeTile,
                // No "⚠" prefix: it tofu-boxes in the runtime font. The mailbox
                // paints a warning icon on radar rows instead.
                Subject = $"RADAR ALERT — {what} inbound",
                ArrivesAtTick = atk.ArrivesAtTick,
                IsFleet = tier >= 2 ? atk.IsFleet : null,
                AttackerName = tier >= 3 ? attackerName : null,
                FleetCount = tier >= 3 ? count : null,
                FleetComp = tier >= 4 ? new Dictionary<HullId, int>(atk.Ships) : null,
            };
            BotSystem.InsertMail(state, warning);

            string eta = arrivesInSec > 0 ? $"{Math.Max(1, arrivesInSec)}s" : "now";
            UI.UIController.Instance?.Toast($"Radar contact — {what}, arrival {eta}", UI.Icon.Warning, UI.UiTheme.Bad);
        }

        // ---------- red alert halo (base view) ----------

        bool ThreatActive()
        {
            var state = _ctx.State;
            if (state == null) return false;
            if (_detected.Count > 0) return true;
            int now = state.Tick;
            _recentArrivals.RemoveAll(t => t + ArrivedGraceSec < now);
            foreach (var t in _recentArrivals)
                if (t <= now) return true; // landed within the grace window
            return false;
        }

        void UpdateHalo()
        {
            bool active = ThreatActive();
            if (!active)
            {
                if (_alertHalo != null) _alertHalo.gameObject.SetActive(false);
                return;
            }
            if (_alertHalo == null)
            {
                var planet = GameObject.Find("Home Planet");
                if (planet == null) return;
                // World-space (the planet root rotates with drag), just behind the
                // sphere from the base camera's point of view (camera sits at −Z,
                // planet radius 3.5 — z+4 clears the surface).
                _alertHalo = MapVisuals.Spawn(null, "Radar Alert Halo",
                    MapVisuals.Glow, Vector3.zero,
                    11f, 11f, new Color(1f, 0.15f, 0.1f, 0.55f), -10);
                _alertHalo.transform.position = planet.transform.position + new Vector3(0f, 0f, 4f);
            }
            _alertHalo.gameObject.SetActive(true);
            float pulse = 0.35f + 0.25f * Mathf.PingPong(Time.time * 0.9f, 1f);
            var c = _alertHalo.color; c.a = pulse; _alertHalo.color = c;
        }
    }
}
