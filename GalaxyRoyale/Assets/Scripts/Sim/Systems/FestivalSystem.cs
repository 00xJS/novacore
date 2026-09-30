// Seasonal festivals (Data/Festivals, 2026-09-30). The sim has no calendar of
// its own, so the Game layer tells it today's date (Today); tests set it too.
// A festival's rule reaches every empire through TwistSystem's getters; its
// goal counts from the day the colony first saw it, and its reward is
// claimed once a year.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public sealed class FestivalState
    {
        /// <summary>"{id}-{year}" of the festival the baseline belongs to.</summary>
        public string Instance = "";
        public long Baseline;
        /// <summary>Instances already claimed.</summary>
        public HashSet<string> Claimed = new();
    }

    public static class FestivalSystem
    {
        /// <summary>Today's date (UTC), set by the Game layer; null = no festival.</summary>
        public static DateTime? Today;

        static DateTime? s_cachedFor;
        static (FestivalDef def, int year)? s_cached;

        /// <summary>The festival running today and the year it began in (null = none).
        /// Cached per day: the rules it bends are read on hot paths.</summary>
        public static (FestivalDef def, int year)? Current()
        {
            if (Today == s_cachedFor) return s_cached;
            s_cachedFor = Today;
            s_cached = Find();
            return s_cached;
        }

        static (FestivalDef def, int year)? Find()
        {
            if (Today is not { } today) return null;
            foreach (var f in Festivals.All)
                foreach (int year in new[] { today.Year, today.Year - 1 })
                {
                    var start = new DateTime(year, f.Month, f.Day);
                    if (today >= start && today < start.AddDays(f.Days)) return (f, year);
                }
            return null;
        }

        public static FestivalDef? Active => Current()?.def;

        public static int DaysLeft()
        {
            if (Current() is not { } c || Today is not { } today) return 0;
            return (int)Math.Ceiling((new DateTime(c.year, c.def.Month, c.def.Day).AddDays(c.def.Days) - today).TotalDays);
        }

        /// <summary>The next festival and the day it starts.</summary>
        public static (FestivalDef def, DateTime start)? Next()
        {
            if (Today is not { } today) return null;
            (FestivalDef, DateTime)? best = null;
            foreach (var f in Festivals.All)
            {
                var start = new DateTime(today.Year, f.Month, f.Day);
                if (start <= today) start = start.AddYears(1);
                if (best == null || start < best.Value.Item2) best = (f, start);
            }
            return best;
        }

        // ---- the rules (TwistSystem folds these in) ----
        public static float CampLootMult => Active?.CampLoot ?? 1f;
        public static float GatherMult => Active?.Gather ?? 1f;
        public static float ProductionMult => Active?.Production ?? 1f;
        public static float ResearchTimeMult => Active?.ResearchTime ?? 1f;
        public static float ShipTimeMult => Active?.ShipTime ?? 1f;

        // ---- the goal ----

        static long Counter(GameState s, FestivalGoal goal) => goal switch
        {
            FestivalGoal.CampsCleared => s.Stats.CampsCleared,
            FestivalGoal.Gathered => s.Stats.GatheredMilli / 1000,
            FestivalGoal.UpgradesDone => s.Stats.UpgradesDone,
            FestivalGoal.ShipsBuilt => s.Stats.ShipsBuilt,
            FestivalGoal.ResearchLevels => s.Stats.ResearchDone,
            _ => 0,
        };

        static string InstanceOf((FestivalDef def, int year) c) => $"{c.def.Id}-{c.year}";

        /// <summary>This festival's goal: (have, need); the baseline is taken the first time the colony sees it.</summary>
        public static (long have, long need) Progress(GameState s)
        {
            if (Current() is not { } c) return (0, 1);
            string instance = InstanceOf(c);
            if (s.Festival.Instance != instance)
            {
                s.Festival.Instance = instance;
                s.Festival.Baseline = Counter(s, c.def.Goal);
            }
            return (Math.Min(c.def.Target, Counter(s, c.def.Goal) - s.Festival.Baseline), c.def.Target);
        }

        public static bool Claimed(GameState s) => Current() is { } c && s.Festival.Claimed.Contains(InstanceOf(c));

        public static bool CanClaim(GameState s)
        {
            if (Current() is null || Claimed(s)) return false;
            var (have, need) = Progress(s);
            return have >= need;
        }

        public static SimResult Claim(GameState s)
        {
            if (!CanClaim(s) || Current() is not { } c) return SimResult.Fail("The festival's goal isn't met yet");
            s.Festival.Claimed.Add(InstanceOf(c));
            s.Premium.DarkMatter += c.def.RewardDM;
            if (!s.Skins.Owned.Contains(c.def.SkinId)) s.Skins.Owned.Add(c.def.SkinId);
            return SimResult.Success;
        }

        /// <summary>Once a minute: note a festival that just began (its baseline and a toast).</summary>
        public static void Tick(GameState s, SimEventBus events)
        {
            if (s.Tick % 60 != 0 || Current() is not { } c) return;
            if (s.Festival.Instance == InstanceOf(c)) return;
            Progress(s); // takes the baseline
            events.Emit(new FestivalBegan(c.def.Id));
        }
    }
}
