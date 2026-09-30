// The Terraformer (the Citadel, 2026-09-30): reshape the planet down one of four
// paths, a stage at a time. Each stage is a long project that costs resources
// (hours of the colony's own output, so it keeps pace) and raises the path's
// bonus; the Terraformer's level decides how many stages you can reach.
// Switching paths starts over from stage 0.
//   Oceanic      +5% helium a stage     Crystalline  +5% quartz a stage
//   Metallic     +5% gold a stage       Temperate    −3% build time, +4% energy a stage
using System;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public enum TerraformPath { None, Oceanic, Crystalline, Metallic, Temperate }

    public sealed class TerraformState
    {
        public TerraformPath Path;
        public int Stage;
        /// <summary>The stage project under way ends at this tick (0 = none).</summary>
        public int ProjectEndsTick;
    }

    public static class TerraformSystem
    {
        public const int MaxStages = 5;
        public const float ResourcePerStage = 0.05f, BuildPerStage = 0.03f, EnergyPerStage = 0.04f;
        /// <summary>A stage project costs this many hours of the colony's mine output, × the stage.</summary>
        public const double CostHoursPerStage = 6;
        public const int SecondsPerStage = 4 * 3600;

        public static string Name(TerraformPath p) => p switch
        {
            TerraformPath.Oceanic => "Oceanic",
            TerraformPath.Crystalline => "Crystalline",
            TerraformPath.Metallic => "Metallic",
            TerraformPath.Temperate => "Temperate",
            _ => "Untouched",
        };

        public static string Bonus(TerraformPath p, int stage) => p switch
        {
            TerraformPath.Oceanic => $"+{stage * 5}% helium",
            TerraformPath.Crystalline => $"+{stage * 5}% quartz",
            TerraformPath.Metallic => $"+{stage * 5}% gold",
            TerraformPath.Temperate => $"−{stage * 3}% build time, +{stage * 4}% energy",
            _ => "no bonus yet",
        };

        /// <summary>Stages the Terraformer's level allows: 1 at level 1, one more every 3 levels, up to 5.</summary>
        public static int MaxStage(int level) => level < 1 ? 0 : Math.Min(MaxStages, 1 + (level - 1) / 3);

        public static int Level(GameState s) => s.Buildings[BuildingId.Terraformer].Level;

        public static ResourceBag ProjectCost(GameState s, int toStage)
        {
            long total = (long)(ResourceSystem.MineOutputPerHour(s).Total * CostHoursPerStage * toStage);
            total = Math.Max(total, 20_000_000L * toStage);
            return new ResourceBag(total * 35 / 100, total * 35 / 100, total * 30 / 100);
        }

        public static int ProjectSeconds(int toStage) => SecondsPerStage * toStage;

        public static SimResult Check(GameState s, TerraformPath path)
        {
            if (Level(s) < 1) return SimResult.Fail("Build the Terraformer first");
            if (path == TerraformPath.None) return SimResult.Fail("Pick a path");
            var t = s.Terraform;
            if (t.ProjectEndsTick > 0) return SimResult.Fail("A project is already under way");
            int stage = t.Path == path ? t.Stage : 0;
            if (stage >= MaxStage(Level(s)))
                return SimResult.Fail(stage >= MaxStages ? "This path is complete" : "Upgrade the Terraformer to reach the next stage");
            if (!ResourceSystem.CanAfford(s, ProjectCost(s, stage + 1))) return SimResult.Fail("Not enough resources");
            return SimResult.Success;
        }

        /// <summary>Start the next stage down a path (a different path starts over from stage 0).</summary>
        public static SimResult Start(GameState s, TerraformPath path)
        {
            var check = Check(s, path);
            if (!check.Ok) return check;
            var t = s.Terraform;
            if (t.Path != path) { t.Path = path; t.Stage = 0; }
            ResourceSystem.Spend(s, ProjectCost(s, t.Stage + 1));
            t.ProjectEndsTick = s.Tick + ProjectSeconds(t.Stage + 1);
            return SimResult.Success;
        }

        public static void Tick(GameState s, SimEventBus events)
        {
            var t = s.Terraform;
            if (t.ProjectEndsTick <= 0 || s.Tick < t.ProjectEndsTick) return;
            t.ProjectEndsTick = 0;
            t.Stage = Math.Min(MaxStages, t.Stage + 1);
            events.Emit(new TerraformStageDone(t.Path, t.Stage));
        }

        /// <summary>A resource's production multiplier from the path.</summary>
        public static float ResourceMult(GameState s, ResourceId res)
        {
            var t = s.Terraform;
            if (t.Stage < 1) return 1f;
            return t.Path switch
            {
                TerraformPath.Oceanic when res == ResourceId.Helium => 1f + ResourcePerStage * t.Stage,
                TerraformPath.Crystalline when res == ResourceId.Quartz => 1f + ResourcePerStage * t.Stage,
                TerraformPath.Metallic when res == ResourceId.Gold => 1f + ResourcePerStage * t.Stage,
                _ => 1f,
            };
        }

        public static float EnergyMult(GameState s) =>
            s.Terraform.Path == TerraformPath.Temperate ? 1f + EnergyPerStage * s.Terraform.Stage : 1f;

        /// <summary>The Temperate path's build-time cut (joins ResearchSystem.EffectTotal).</summary>
        public static float EffectTotal(GameState s, TechEffectKind kind) =>
            kind == TechEffectKind.BuildTimeReduce && s.Terraform.Path == TerraformPath.Temperate
                ? BuildPerStage * s.Terraform.Stage : 0f;
    }
}
