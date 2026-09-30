// Weekly galaxy twists (Data/Twists, 2026-09-30). A pure function of galaxy
// time, so every empire (the player and all 249 rivals) lives under the same
// twist; the systems it bends ask here. Tick only announces a new week.
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class TwistSystem
    {
        /// <summary>Galaxy week of a tick (0 = the first).</summary>
        public static int Week(int tick) => System.Math.Max(0, tick) / Twists.WeekSec;

        /// <summary>The twist live at a tick: none in week 0, then the rotation.</summary>
        /// <summary>Test hook (GR_TWIST): this twist, whatever the week.</summary>
        public static TwistKind? DebugForce;

        public static TwistDef At(int tick)
        {
            if (DebugForce is { } forced) return Twists.Def(forced);
            int week = Week(tick);
            return week == 0 ? Twists.None : Twists.Rotation[(week - 1) % Twists.Rotation.Count];
        }

        public static TwistKind KindAt(int tick) => At(tick).Kind;
        public static TwistDef Next(int tick) => At((Week(tick) + 1) * Twists.WeekSec);
        public static int LeftSec(int tick) => (Week(tick) + 1) * Twists.WeekSec - tick;

        static bool Is(GameState s, TwistKind kind) => KindAt(s.Tick) == kind;

        // ---- the rules it bends ----
        public static float SpeedMult(GameState s) => Is(s, TwistKind.LowGravity) ? 1.25f : 1f;
        // (Seasonal festivals, 2026-09-30, fold their rule in here too.)
        public static float CampLootMult(GameState s) => (Is(s, TwistKind.PirateUprising) ? 1.5f : 1f) * FestivalSystem.CampLootMult;
        public static float GatherMult(GameState s) => (Is(s, TwistKind.RichVeins) ? 1.5f : 1f) * FestivalSystem.GatherMult;
        public static float ShipTimeMult(GameState s) => (Is(s, TwistKind.ShipwrightsWeek) ? 0.7f : 1f) * FestivalSystem.ShipTimeMult;
        public static float BuildTimeMult(GameState s) => Is(s, TwistKind.BuildersBoom) ? 0.8f : 1f;
        public static float ResearchTimeMult(GameState s) => (Is(s, TwistKind.ScholarsWeek) ? 0.75f : 1f) * FestivalSystem.ResearchTimeMult;
        public static float ProductionMult(GameState s) => (Is(s, TwistKind.SolarMaximum) ? 1.2f : 1f) * FestivalSystem.ProductionMult;
        public static float SalvageMult(GameState s) => Is(s, TwistKind.SalvageStorm) ? 2f : 1f;
        public static float LordRewardMult(GameState s) => Is(s, TwistKind.HuntersMoon) ? 1.5f : 1f;
        public static float SupplyIntervalMult(GameState s) => Is(s, TwistKind.SupplySurge) ? 0.5f : 1f;

        /// <summary>Per tick (cheap): announce the week's twist once, and under the
        /// Hunter's Moon send a beaten lord back at once.</summary>
        public static void Tick(GameState s, SimEventBus events)
        {
            int week = Week(s.Tick);
            if (week == s.TwistWeek) return;
            s.TwistWeek = week;
            var twist = At(s.Tick);
            if (twist.Kind == TwistKind.None) return;
            if (twist.Kind == TwistKind.HuntersMoon && s.Campaign.NextRematchTick > s.Tick)
                s.Campaign.NextRematchTick = s.Tick;
            events.Emit(new TwistBegan(twist.Kind));
        }
    }
}
