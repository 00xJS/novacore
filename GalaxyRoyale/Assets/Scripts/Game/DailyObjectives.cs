// Daily objectives — the retention loop: five tasks that reset at UTC
// midnight, tracked from sim events + stat deltas, paying Dark Matter on
// claim. Progress lives in PlayerPrefs (device-local v0 — an account-synced
// version can ride the C.0 server later); claimed rewards credit the SAVE,
// so they survive everywhere.
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
        };

        const string PrefsKey = "galaxyroyale.daily";
        static DailyObjectives? s_instance;
        GameContext _ctx = null!;

        // Day-scoped counters. marches/battle derive from stat deltas against a
        // snapshot taken at the first sight of each new UTC day.
        string _day = "";
        int _builds, _spies;
        long _gatherWhole;
        int _marchesBase = -1, _battlesBase = -1;
        readonly HashSet<string> _claimed = new();

        void Awake()
        {
            s_instance = this;
            _ctx = GetComponent<GameContext>();
            LoadPrefs();
        }

        void Start()
        {
            _ctx.Events?.Subscribe(OnSimEvent);
        }

        void OnSimEvent(SimEvent e)
        {
            EnsureToday();
            switch (e)
            {
                case BuildingCompleted:
                    _builds++;
                    SavePrefs();
                    break;
                case SpyReportReceived:
                    _spies++;
                    SavePrefs();
                    break;
                case MarchReturned mr:
                    _gatherWhole += (mr.Cargo.Gold + mr.Cargo.Quartz + mr.Cargo.Helium) / 1000;
                    SavePrefs();
                    break;
            }
        }

        void EnsureToday()
        {
            string today = DateTime.UtcNow.ToString("yyyyMMdd");
            if (_day == today) return;
            _day = today;
            _builds = 0;
            _spies = 0;
            _gatherWhole = 0;
            _claimed.Clear();
            var state = _ctx.State;
            _marchesBase = state?.Stats.MarchesSent ?? 0;
            _battlesBase = state?.Stats.BattlesWon ?? 0;
            SavePrefs();
        }

        // ---------- public API ----------

        public static int Progress(GameState state, ObjectiveDef def)
        {
            var inst = s_instance;
            if (inst == null) return 0;
            inst.EnsureToday();
            long p = def.Id switch
            {
                "builds"  => inst._builds,
                "marches" => Math.Max(0, state.Stats.MarchesSent - inst._marchesBase),
                "gather"  => inst._gatherWhole,
                "battle"  => Math.Max(0, state.Stats.BattlesWon - inst._battlesBase),
                "spy"     => inst._spies,
                _ => 0,
            };
            return (int)Math.Min(def.Target, p);
        }

        public static bool IsClaimed(ObjectiveDef def) =>
            s_instance != null && s_instance._claimed.Contains(def.Id);

        public static bool Claim(GameState state, ObjectiveDef def)
        {
            var inst = s_instance;
            if (inst == null) return false;
            inst.EnsureToday();
            if (inst._claimed.Contains(def.Id)) return false;
            if (Progress(state, def) < def.Target) return false;
            state.Premium.DarkMatter += def.RewardDM;
            inst._claimed.Add(def.Id);
            inst.SavePrefs();
            return true;
        }

        public static bool AnyClaimable(GameState state)
        {
            if (s_instance == null) return false;
            foreach (var def in Defs)
                if (!IsClaimed(def) && Progress(state, def) >= def.Target) return true;
            return false;
        }

        /// <summary>
        /// Start the day's objectives over for a NEW galaxy (NEW GAME / RESET
        /// EMPIRE). Progress lives in PlayerPrefs, outside the save, so counters
        /// and claims used to carry into the fresh empire — the stat baselines
        /// then pointed at the old empire's totals and blocked "Send 5 fleets" /
        /// "Win a battle" until midnight UTC.
        /// </summary>
        public static void ResetProgress()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            var d = s_instance;
            if (d == null) return;
            d._day = "";
            d._builds = 0;
            d._spies = 0;
            d._gatherWhole = 0;
            d._marchesBase = -1;
            d._battlesBase = -1;
            d._claimed.Clear();
        }

        // ---------- persistence ----------

        void LoadPrefs()
        {
            try
            {
                string raw = PlayerPrefs.GetString(PrefsKey, "");
                if (raw.Length == 0) return;
                if (Json.Parse(raw) is not Dictionary<string, object?> d) return;
                _day = d["day"] as string ?? "";
                _builds = I(d, "builds");
                _spies = I(d, "spies");
                _gatherWhole = d.TryGetValue("gather", out var g) && g is long gl ? gl : 0;
                _marchesBase = I(d, "marchesBase");
                _battlesBase = I(d, "battlesBase");
                if (d.TryGetValue("claimed", out var c) && c is List<object?> claimed)
                    foreach (var id in claimed)
                        if (id is string s) _claimed.Add(s);
            }
            catch (Exception) { }
            static int I(Dictionary<string, object?> d, string k) =>
                d.TryGetValue(k, out var v) && v is long l ? (int)l : 0;
        }

        void SavePrefs()
        {
            var claimed = new List<object?>();
            foreach (var id in _claimed) claimed.Add(id);
            PlayerPrefs.SetString(PrefsKey, Json.Write(new Dictionary<string, object?>
            {
                ["day"] = _day,
                ["builds"] = (long)_builds,
                ["spies"] = (long)_spies,
                ["gather"] = _gatherWhole,
                ["marchesBase"] = (long)_marchesBase,
                ["battlesBase"] = (long)_battlesBase,
                ["claimed"] = claimed,
            }));
            PlayerPrefs.Save();
        }
    }
}
