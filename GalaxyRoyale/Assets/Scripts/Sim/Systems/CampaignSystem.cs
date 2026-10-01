// The campaign, "The Long Night" (Data/Campaign, 2026-09-30). One chapter at a
// time: it opens when the colony reaches its Command Center level and enough
// galaxy days have passed, takes a baseline of the counters its objectives
// read (so they count from the chapter's start), and puts its Pirate Lord's
// lair on the map. When every objective is met the player claims the chapter
// and the next one waits for its own time.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public sealed class CampaignState
    {
        /// <summary>The current chapter (index into Campaign.Chapters); Count = the saga is done.</summary>
        public int Chapter;
        /// <summary>The current chapter has begun (baselines taken, lair on the map).</summary>
        public bool Open;
        public int OpenedTick;
        /// <summary>Each objective's counter when the chapter opened.</summary>
        public List<long> Baseline = new();
        /// <summary>Objectives already announced as done (bit per objective; bit 7 = all done).</summary>
        public int Announced;
        /// <summary>The chapter lord's lair node id ("" = none on the map).</summary>
        public string LairId = "";
        /// <summary>Pirate Lord index → times beaten (LairSystem).</summary>
        public Dictionary<int, int> LordWins = new();
        /// <summary>A beaten lord's rematch lair, and when the next one rises (0 = none yet).</summary>
        public string RematchId = "";
        public int NextRematchTick;
        public int RematchCursor;
    }

    public static class CampaignSystem
    {
        const int Day = 24 * 3600;
        const int AllDoneBit = 1 << 7;

        public static bool Finished(GameState s) => s.Campaign.Chapter >= Campaign.Chapters.Count;

        public static ChapterDef? Current(GameState s) =>
            Finished(s) ? null : Campaign.Chapters[s.Campaign.Chapter];

        /// <summary>Why the next chapter hasn't opened yet (null = it can).</summary>
        public static string? Blocker(GameState s)
        {
            if (Current(s) is not { } ch) return "The saga is complete";
            int cc = s.Buildings[BuildingId.CommandCenter].Level;
            if (cc < ch.UnlockCc) return $"Opens at Command Center {ch.UnlockCc}";
            // A TESTING game (App Review, quick looks) skips the waiting: CC levels only.
            int at = ch.UnlockDay * Day;
            if (!s.TestMode && s.Tick < at) return $"Opens in {FmtLeft(at - s.Tick)}";
            return null;
        }

        /// <summary>The tick the next chapter's clock allows it (its CC level aside).</summary>
        public static int OpensAtTick(GameState s) => Current(s) is { } ch ? ch.UnlockDay * Day : 0;

        static string FmtLeft(int sec) =>
            sec >= Day ? $"{sec / Day}d {sec % Day / 3600}h" : sec >= 3600 ? $"{sec / 3600}h {sec % 3600 / 60}m" : $"{Math.Max(1, sec / 60)}m";

        /// <summary>A goal's counter, whole units (Relics and TerraformStage read absolute values).</summary>
        public static long Counter(GameState s, CampaignGoal goal, int lord) => goal switch
        {
            CampaignGoal.DefeatLord => LairSystem.Wins(s, lord),
            CampaignGoal.CampsCleared => s.Stats.CampsCleared,
            CampaignGoal.BattlesWon => s.Stats.BattlesWon,
            CampaignGoal.Gathered => s.Stats.GatheredMilli / 1000,
            CampaignGoal.WildsSurveyed => WildsSurveys(s),
            CampaignGoal.Expeditions => s.Stats.ExpeditionsDone,
            CampaignGoal.BossStrikes => s.Stats.BossStrikes,
            CampaignGoal.EventsCompleted => s.Stats.EventsCompleted,
            CampaignGoal.ShipsBuilt => s.Stats.ShipsBuilt,
            CampaignGoal.ContractsDone => s.Stats.ContractsDone,
            CampaignGoal.ResearchLevels => s.Stats.ResearchDone,
            CampaignGoal.Relics => RelicTotal(s),
            CampaignGoal.TerraformStage => s.Terraform.Stage,
            _ => 0,
        };

        static bool Absolute(CampaignGoal goal) => goal is CampaignGoal.Relics or CampaignGoal.TerraformStage;

        static long WildsSurveys(GameState s)
        {
            long n = 0;
            foreach (var sector in s.Wilds.Sectors.Values) n += sector.Surveys;
            return n;
        }

        static long RelicTotal(GameState s)
        {
            long n = 0;
            foreach (var kv in s.Relics) n += kv.Value;
            return n;
        }

        /// <summary>An objective of the open chapter: (have, need).</summary>
        public static (long have, long need) Progress(GameState s, int objective)
        {
            var ch = Current(s);
            if (ch == null || objective >= ch.Objectives.Count) return (0, 1);
            var o = ch.Objectives[objective];
            if (!s.Campaign.Open) return (0, o.Target);
            long now = Counter(s, o.Goal, ch.Lord);
            long from = Absolute(o.Goal) || objective >= s.Campaign.Baseline.Count ? 0 : s.Campaign.Baseline[objective];
            return (Math.Min(o.Target, Math.Max(0, now - from)), o.Target);
        }

        public static bool Done(GameState s, int objective)
        {
            var (have, need) = Progress(s, objective);
            return have >= need;
        }

        public static bool CanClaim(GameState s)
        {
            if (!s.Campaign.Open || Current(s) is not { } ch) return false;
            for (int i = 0; i < ch.Objectives.Count; i++) if (!Done(s, i)) return false;
            return true;
        }

        /// <summary>What claiming the chapter pays (milli, DM).</summary>
        public static (ResourceBag payMilli, int darkMatter) Reward(GameState s)
        {
            if (Current(s) is not { } ch) return (new ResourceBag(), 0);
            var hourly = ResourceSystem.MineOutputPerHour(s);
            return (new ResourceBag(hourly.Gold * ch.RewardHours, hourly.Quartz * ch.RewardHours,
                hourly.Helium * ch.RewardHours), ch.RewardDM);
        }

        public static SimResult Claim(GameState s, SimEventBus? events = null)
        {
            if (!CanClaim(s)) return SimResult.Fail("The chapter isn't finished yet");
            var c = s.Campaign;
            int number = c.Chapter;
            var (pay, dm) = Reward(s);
            ResourceSystem.Add(s, pay);
            s.Premium.DarkMatter += dm;
            LairSystem.Remove(s, c.LairId, events); // (already gone once its lord fell)
            c.LairId = "";
            c.Chapter++;
            c.Open = false;
            c.Baseline.Clear();
            c.Announced = 0;
            events?.Emit(new ChapterClaimed(number, pay, dm));
            return SimResult.Success;
        }

        static void Open(GameState s, SimEventBus events)
        {
            var c = s.Campaign;
            var ch = Campaign.Chapters[c.Chapter];
            c.Open = true;
            c.OpenedTick = s.Tick;
            c.Announced = 0;
            c.Baseline.Clear();
            foreach (var o in ch.Objectives) c.Baseline.Add(Counter(s, o.Goal, ch.Lord));
            c.LairId = LairSystem.Spawn(s, ch.Lord, 0, events).Id;
            events.Emit(new ChapterBegan(c.Chapter));
        }

        /// <summary>Per tick (the work runs once a minute): open the next chapter when its
        /// time comes, keep its lair on the map, announce objectives as they're met,
        /// and let a beaten lord rise again.</summary>
        public static void Tick(GameState s, SimEventBus events)
        {
            if (s.Tick % 60 != 0) return;
            var c = s.Campaign;
            if (!Finished(s))
            {
                var ch = Campaign.Chapters[c.Chapter];
                if (!c.Open && Blocker(s) == null) Open(s, events);
                else if (c.Open)
                {
                    // A lost lair (an old save, a cleared map) comes back while its lord stands.
                    if (c.LairId.Length == 0 && !Done(s, IndexOfLord(ch)))
                        c.LairId = LairSystem.Spawn(s, ch.Lord, 0, events).Id;
                    bool all = true;
                    for (int i = 0; i < ch.Objectives.Count; i++)
                    {
                        if (!Done(s, i)) { all = false; continue; }
                        if ((c.Announced & (1 << i)) != 0) continue;
                        c.Announced |= 1 << i;
                        events.Emit(new CampaignObjectiveDone(c.Chapter, i));
                    }
                    if (all && (c.Announced & AllDoneBit) == 0)
                    {
                        c.Announced |= AllDoneBit;
                        events.Emit(new ChapterReady(c.Chapter));
                    }
                }
            }
            LairSystem.Tick(s, events);
        }

        static int IndexOfLord(ChapterDef ch)
        {
            for (int i = 0; i < ch.Objectives.Count; i++)
                if (ch.Objectives[i].Goal == CampaignGoal.DefeatLord) return i;
            return 0;
        }

        /// <summary>Test hook (GR_CAMPAIGN): jump to a chapter and open it now, gates aside.</summary>
        public static void DebugOpen(GameState s, int chapter)
        {
            var c = s.Campaign;
            if (chapter < 0 || chapter >= Campaign.Chapters.Count) return;
            LairSystem.Remove(s, c.LairId, null);
            c.LairId = "";
            c.Chapter = chapter;
            Open(s, new SimEventBus());
        }

        /// <summary>Chapters claimed.</summary>
        public static int ChaptersDone(GameState s) => s.Campaign.Chapter;
    }
}
