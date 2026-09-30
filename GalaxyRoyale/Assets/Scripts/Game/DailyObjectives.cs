// Daily objectives — the retention loop: tasks that reset at UTC midnight,
// tracked from sim events + stat deltas, paying Dark Matter on claim. Since
// 2026-09-30 the day's progress lives in the save (GameState.Daily), so it
// survives an iCloud restore or a reinstall.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Save;
using UnityEngine;

namespace GalaxyRoyale.Game
{
    public sealed class ObjectiveDef
    {
        public string Id = "";
        public string Label = "";
        public int Target;
        public int RewardDM;
    }

    [AddComponentMenu("GalaxyRoyale/Daily Objectives")]
    [RequireComponent(typeof(GameContext))]
    public sealed class DailyObjectives : MonoBehaviour
    {
        public static readonly ObjectiveDef[] Defs =
        {
            new() { Id = "builds",  Label = "Complete 3 building upgrades", Target = 3,       RewardDM = 150 },
            new() { Id = "marches", Label = "Send 5 fleets",                Target = 5,       RewardDM = 100 },
            new() { Id = "gather",  Label = "Gather 100K resources",        Target = 100_000, RewardDM = 150 },
            new() { Id = "battle",  Label = "Win a battle",                 Target = 1,       RewardDM = 200 },
            new() { Id = "spy",     Label = "Send a spy probe",             Target = 1,       RewardDM = 100 },
            // Fleet goal (balance pass 2026-09-30): ship spending fell to nothing.
            new() { Id = "ships",   Label = "Build 10 ships",               Target = 10,      RewardDM = 120 },
        };

        const string PrefsKey = "galaxyroyale.daily";
        static DailyObjectives? s_instance;
        GameContext _ctx = null!;
        bool _migrated;

        // The day's counters live in the save (GameState.Daily, 2026-09-30) so an
        // iCloud restore or a reinstall keeps them; they used to be in PlayerPrefs.
        // marches/battle/ships count stat deltas from baselines taken at the first
        // sight of each new UTC day.
        DailyState? D => _ctx.State?.Daily;

        void Awake()
        {
            s_instance = this;
            _ctx = GetComponent<GameContext>();
        }

        void Start()
        {
            _ctx.Events?.Subscribe(OnSimEvent);
        }

        void OnSimEvent(SimEvent e)
        {
            var d = D;
            if (d == null) return;
            EnsureToday();
            switch (e)
            {
                case BuildingCompleted: d.Builds++; break;
                case SpyReportReceived: d.Spies++; break;
                case MarchReturned mr: d.GatherWhole += (mr.Cargo.Gold + mr.Cargo.Quartz + mr.Cargo.Helium) / 1000; break;
            }
        }

        void EnsureToday()
        {
            var state = _ctx.State;
            if (state == null) return;
            MigrateOnce(state);
            var d = state.Daily;
            string today = DateTime.UtcNow.ToString("yyyyMMdd");
            if (d.Day == today) return;
            d.Day = today;
            d.Builds = 0;
            d.Spies = 0;
            d.GatherWhole = 0;
            d.Claimed.Clear();
            d.MarchesBase = state.Stats.MarchesSent;
            d.BattlesBase = state.Stats.BattlesWon;
            d.ShipsBase = state.Stats.ShipsBuilt;
        }

        /// <summary>Progress saved in PlayerPrefs by older builds moves into the save once.</summary>
        void MigrateOnce(GameState state)
        {
            if (_migrated) return;
            _migrated = true;
            try
            {
                string raw = PlayerPrefs.GetString(PrefsKey, "");
                PlayerPrefs.DeleteKey(PrefsKey);
                if (raw.Length == 0 || state.Daily.Day.Length > 0) return;
                if (Json.Parse(raw) is not Dictionary<string, object?> o) return;
                var d = state.Daily;
                d.Day = o.TryGetValue("day", out var day) && day is string ds ? ds : "";
                d.Builds = I(o, "builds");
                d.Spies = I(o, "spies");
                d.GatherWhole = o.TryGetValue("gather", out var g) && g is long gl ? gl : 0;
                d.MarchesBase = I(o, "marchesBase");
                d.BattlesBase = I(o, "battlesBase");
                d.ShipsBase = o.TryGetValue("shipsBase", out var sb) && sb is long sbl ? (int)sbl : state.Stats.ShipsBuilt;
                if (o.TryGetValue("claimed", out var c) && c is List<object?> claimed)
                    foreach (var id in claimed)
                        if (id is string cs) d.Claimed.Add(cs);
            }
            catch (Exception) { }
            static int I(Dictionary<string, object?> d, string k) =>
                d.TryGetValue(k, out var v) && v is long l ? (int)l : 0;
        }

        // ---------- public API ----------

        public static int Progress(GameState state, ObjectiveDef def)
        {
            s_instance?.EnsureToday();
            var d = state.Daily;
            long p = def.Id switch
            {
                "builds"  => d.Builds,
                "marches" => Math.Max(0, state.Stats.MarchesSent - d.MarchesBase),
                "gather"  => d.GatherWhole,
                "battle"  => Math.Max(0, state.Stats.BattlesWon - d.BattlesBase),
                "spy"     => d.Spies,
                "ships"   => Math.Max(0, state.Stats.ShipsBuilt - d.ShipsBase),
                _ => 0,
            };
            return (int)Math.Min(def.Target, p);
        }

        public static bool IsClaimed(ObjectiveDef def) =>
            s_instance?.D is { } d && d.Claimed.Contains(def.Id);

        public static bool Claim(GameState state, ObjectiveDef def)
        {
            s_instance?.EnsureToday();
            var d = state.Daily;
            if (d.Claimed.Contains(def.Id)) return false;
            if (Progress(state, def) < def.Target) return false;
            state.Premium.DarkMatter += def.RewardDM;
            state.Stats.DailiesClaimed++; // commander XP
            d.Claimed.Add(def.Id);
            return true;
        }

        public static bool AnyClaimable(GameState state)
        {
            foreach (var def in Defs)
                if (!IsClaimed(def) && Progress(state, def) >= def.Target) return true;
            return false;
        }

        /// <summary>
        /// Start the day's objectives over for a NEW galaxy (NEW GAME / RESET
        /// EMPIRE). The new game's state starts with an empty day; this only
        /// clears what older builds kept in PlayerPrefs.
        /// </summary>
        public static void ResetProgress()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            if (s_instance != null) s_instance._migrated = true;
        }
    }
}
