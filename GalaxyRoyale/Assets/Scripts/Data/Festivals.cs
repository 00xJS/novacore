// Seasonal festivals (late-game content, user-approved 2026-09-30): themed
// events on the real calendar, so the galaxy has something new at the same
// times of year as the player's own world. Each bends one rule for every
// empire while it runs, sets a goal counted from its first day, and pays Dark
// Matter and a planet skin that can only be earned there (once a year).
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public enum FestivalGoal { CampsCleared, Gathered, UpgradesDone, ShipsBuilt, ResearchLevels }

    public sealed class FestivalDef
    {
        public string Id = "";
        public string Name = "";
        public string Blurb = "";
        /// <summary>First day (UTC month and day) and how many days it runs (may cross New Year).</summary>
        public int Month, Day, Days;
        public string Effect = "";
        public FestivalGoal Goal;
        public long Target;
        public string GoalText = "";
        public int RewardDM;
        public string SkinId = "", SkinName = "", Tint = "#FFFFFF";
        // The rule it bends (1 = untouched).
        public float CampLoot = 1f, Gather = 1f, Production = 1f, ResearchTime = 1f, ShipTime = 1f;
    }

    public static class Festivals
    {
        public static readonly IReadOnlyList<FestivalDef> All = new[]
        {
            new FestivalDef
            {
                Id = "void-harvest", Name = "Void Harvest", Month = 10, Day = 20, Days = 15,
                Blurb = "The void is thin this time of year, and the pirate camps are heavy with plunder.",
                Effect = "Pirate camps carry 25% more loot", CampLoot = 1.25f,
                Goal = FestivalGoal.CampsCleared, Target = 20, GoalText = "Clear 20 pirate camps",
                RewardDM = 300, SkinId = "skin-harvest", SkinName = "Harvest Moon", Tint = "#FF7A2E",
            },
            new FestivalDef
            {
                Id = "frost-nebula", Name = "Frost Nebula", Month = 12, Day = 15, Days = 22,
                Blurb = "A cold front of ice crystal drifts through the galaxy. Every field glitters.",
                Effect = "Fleets gather 25% faster", Gather = 1.25f,
                Goal = FestivalGoal.Gathered, Target = 300_000, GoalText = "Haul 300,000 resources home",
                RewardDM = 300, SkinId = "skin-frost", SkinName = "Frostbound", Tint = "#BFEFFF",
            },
            new FestivalDef
            {
                Id = "lantern-festival", Name = "Lantern Festival", Month = 1, Day = 25, Days = 17,
                Blurb = "Colonies string lanterns between their moons and the whole galaxy works late.",
                Effect = "+15% resource production", Production = 1.15f,
                Goal = FestivalGoal.UpgradesDone, Target = 12, GoalText = "Finish 12 building upgrades",
                RewardDM = 300, SkinId = "skin-lantern", SkinName = "Lantern World", Tint = "#FF4D4D",
            },
            new FestivalDef
            {
                Id = "spring-bloom", Name = "Spring Bloom", Month = 4, Day = 1, Days = 14,
                Blurb = "Fresh alloys from the spring yards: every shipwright is taking orders.",
                Effect = "Ships build 20% faster", ShipTime = 0.8f,
                Goal = FestivalGoal.ShipsBuilt, Target = 300, GoalText = "Build 300 ships",
                RewardDM = 300, SkinId = "skin-verdant", SkinName = "Verdant", Tint = "#7CFF6B",
            },
            new FestivalDef
            {
                Id = "solar-flare", Name = "Solar Flare", Month = 7, Day = 1, Days = 21,
                Blurb = "The stars run hot all summer, and the labs run on the surplus power.",
                Effect = "Research runs 20% faster", ResearchTime = 0.8f,
                Goal = FestivalGoal.ResearchLevels, Target = 8, GoalText = "Complete 8 research levels",
                RewardDM = 300, SkinId = "skin-sunforged", SkinName = "Sunforged", Tint = "#FFB000",
            },
        };

        public static FestivalDef? ById(string id)
        {
            foreach (var f in All) if (f.Id == id) return f;
            return null;
        }
    }
}
