// Timed galaxy events (Data/GalaxyEvents): which event is live is a pure
// function of galaxy time, so every empire — and the offline catch-up — agrees
// on it. Goal progress counts a stat from the moment the event began; Tick
// captures that baseline inside the sim, so an event that starts while the
// app is closed still counts from its real start.
using System;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class EventSystem
    {
        public static readonly int CycleSec = SumDurations();

        public readonly struct Live
        {
            public readonly GalaxyEventDef Def;
            /// <summary>Unique number of this occurrence (cycle × events + slot).</summary>
            public readonly int Instance;
            public readonly int StartTick;
            public readonly int EndTick;

            public Live(GalaxyEventDef def, int instance, int startTick, int endTick)
            {
                Def = def; Instance = instance; StartTick = startTick; EndTick = endTick;
            }
        }

        /// <summary>The event live at <paramref name="tick"/> — GalaxyEvents.Quiet
        /// (instance -1) during a new galaxy's lead-in.</summary>
        /// <summary>Debug only (DebugLaunch's GR_EVENT): make this event live from this
        /// tick, for a look at it without waiting for the rotation. Never set in play.</summary>
        public static (GalaxyEventKind Kind, int StartTick)? DebugForce;

        public static Live Current(int tick)
        {
            if (DebugForce is { } force && tick >= force.StartTick)
                foreach (var d in GalaxyEvents.Rotation)
                    if (d.Kind == force.Kind && tick < force.StartTick + d.DurationSec)
                        return new Live(d, 900_000 + (int)d.Kind, force.StartTick, force.StartTick + d.DurationSec);
            if (tick < GalaxyEvents.LeadInSec)
                return new Live(GalaxyEvents.Quiet, -1, 0, GalaxyEvents.LeadInSec);
            int t = tick - GalaxyEvents.LeadInSec;
            int cycle = t / CycleSec;
            int into = t % CycleSec;
            int origin = GalaxyEvents.LeadInSec + cycle * CycleSec;
            int start = 0;
            for (int i = 0; i < GalaxyEvents.Rotation.Count; i++)
            {
                var def = GalaxyEvents.Rotation[i];
                if (into < start + def.DurationSec)
                    return new Live(def, cycle * GalaxyEvents.Rotation.Count + i,
                        origin + start, origin + start + def.DurationSec);
                start += def.DurationSec;
            }
            var last = GalaxyEvents.Rotation[GalaxyEvents.Rotation.Count - 1];
            return new Live(last, cycle * GalaxyEvents.Rotation.Count + GalaxyEvents.Rotation.Count - 1,
                origin + CycleSec - last.DurationSec, origin + CycleSec);
        }

        public static bool IsQuiet(Live live) => live.Def.Kind == GalaxyEventKind.None;

        /// <summary>The event after the current one (for "next up" in the panel).</summary>
        public static Live Next(int tick) => Current(Current(tick).EndTick);

        public static GalaxyEventKind KindAt(GameState state) => Current(state.Tick).Def.Kind;
        public static GalaxyEventKind KindAt(int tick) => Current(tick).Def.Kind;

        public static float ProductionMult(GameState state) =>
            KindAt(state) == GalaxyEventKind.GoldRush ? GalaxyEvents.GoldRushProduction : 1f;

        public static float ResearchTimeMult(GameState state) =>
            KindAt(state) == GalaxyEventKind.ResearchSurge ? GalaxyEvents.ResearchSurgeTime : 1f;

        public static float CampLootMult(GameState state) =>
            KindAt(state) == GalaxyEventKind.PirateArmada ? GalaxyEvents.PirateArmadaLoot : 1f;

        public static float RaidLootMult(GameState state) =>
            KindAt(state) == GalaxyEventKind.WarGames ? GalaxyEvents.WarGamesLoot : 1f;

        /// <summary>The stat an event's goal counts.</summary>
        public static long Counter(GameState state, GalaxyEventKind kind) => kind switch
        {
            GalaxyEventKind.GoldRush => state.Stats.UpgradesDone,
            GalaxyEventKind.ResearchSurge => state.Stats.ResearchDone,
            GalaxyEventKind.PirateArmada => state.Stats.CampsCleared,
            GalaxyEventKind.WarGames => state.Stats.BattlesWon,
            GalaxyEventKind.CometPass => state.Stats.CometHauled,
            GalaxyEventKind.TradeCaravan => state.Stats.CaravansDone,
            GalaxyEventKind.IonStorm => state.Stats.StormCampsCleared,
            GalaxyEventKind.Supernova => state.Stats.NovaHauled,
            _ => 0,
        };

        /// <summary>Per sim tick: when a new event begins, remember where its goal
        /// counter stands (and reset the claim).</summary>
        public static void Tick(GameState state, SimEventBus? events = null)
        {
            EventSites.Tick(state, events);
            var live = Current(state.Tick);
            if (live.Instance < 0 || live.Instance == state.EventInstance) return;
            state.EventInstance = live.Instance;
            state.EventBaseline = Counter(state, live.Def.Kind);
            state.EventClaimed = false;
        }

        public static (long have, long need) Progress(GameState state)
        {
            var live = Current(state.Tick);
            long need = live.Def.Target;
            if (IsQuiet(live) || live.Instance != state.EventInstance) return (0, need);
            long have = Math.Max(0, Counter(state, live.Def.Kind) - state.EventBaseline);
            return (Math.Min(have, need), need);
        }

        public static bool CanClaim(GameState state)
        {
            var live = Current(state.Tick);
            if (IsQuiet(live)) return false;
            var (have, need) = Progress(state);
            return have >= need && !state.EventClaimed && live.Instance == state.EventInstance;
        }

        public static SimResult Claim(GameState state)
        {
            if (state.EventClaimed) return SimResult.Fail("Reward already claimed");
            if (!CanClaim(state)) return SimResult.Fail("Event goal not reached yet");
            var def = Current(state.Tick).Def;
            var milli = def.Reward.Milli();
            state.Resources.Gold += milli.Gold;
            state.Resources.Quartz += milli.Quartz;
            state.Resources.Helium += milli.Helium;
            state.Premium.DarkMatter += def.RewardDM;
            state.EventClaimed = true;
            state.Stats.EventsCompleted++;
            return SimResult.Success;
        }

        static int SumDurations()
        {
            int sum = 0;
            foreach (var e in GalaxyEvents.Rotation) sum += e.DurationSec;
            return sum;
        }
    }
}
