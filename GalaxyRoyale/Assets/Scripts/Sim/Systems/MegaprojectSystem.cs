// Mega-projects (Data/Megaprojects, 2026-09-30): one stage under way at a time;
// a finished stage's bonus joins the research effect totals (and, for the Dyson
// Swarm, the Power Plant's output). Rivals build them too (Bots/BotCareer).
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public sealed class MegaprojectState
    {
        /// <summary>Stages finished, by project.</summary>
        public Dictionary<MegaprojectKind, int> Stages = new();
        /// <summary>The stage under way (Active = -1: none) and when it's done.</summary>
        public int Active = -1;
        public int EndsTick;
    }

    public static class MegaprojectSystem
    {
        const int Hour = 3600;

        public static int Stage(GameState s, MegaprojectKind kind) =>
            s.Mega.Stages.TryGetValue(kind, out var n) ? n : 0;

        public static bool Busy(GameState s) => s.Mega.Active >= 0;

        public static bool Unlocked(GameState s, MegaprojectKind kind) =>
            s.Buildings[BuildingId.CommandCenter].Level >= Megaprojects.Def(kind).UnlockCc;

        /// <summary>What the next stage costs (milli): stage × HoursPerStage hours of mine output.</summary>
        public static ResourceBag Cost(GameState s, MegaprojectKind kind)
        {
            int next = Stage(s, kind) + 1;
            var hourly = ResourceSystem.MineOutputPerHour(s);
            long hours = (long)next * Megaprojects.HoursPerStage;
            return new ResourceBag(hourly.Gold * hours, hourly.Quartz * hours, hourly.Helium * hours);
        }

        public static int BuildSeconds(GameState s, MegaprojectKind kind) =>
            (Stage(s, kind) + 1) * Megaprojects.BuildHoursPerStage * Hour;

        public static SimResult Check(GameState s, MegaprojectKind kind)
        {
            var def = Megaprojects.Def(kind);
            if (!Unlocked(s, kind)) return SimResult.Fail($"Opens at Command Center {def.UnlockCc}");
            if (Stage(s, kind) >= Megaprojects.Stages) return SimResult.Fail($"{def.Name} is complete");
            if (Busy(s)) return SimResult.Fail("Another stage is under way");
            if (!ResourceSystem.CanAfford(s, Cost(s, kind))) return SimResult.Fail("Not enough resources");
            return SimResult.Success;
        }

        public static SimResult Start(GameState s, MegaprojectKind kind)
        {
            var check = Check(s, kind);
            if (!check.Ok) return check;
            ResourceSystem.Spend(s, Cost(s, kind));
            s.Mega.Active = (int)kind;
            s.Mega.EndsTick = s.Tick + BuildSeconds(s, kind);
            return SimResult.Success;
        }

        public static void Tick(GameState s, SimEventBus events)
        {
            var m = s.Mega;
            if (m.Active < 0 || s.Tick < m.EndsTick) return;
            var kind = (MegaprojectKind)m.Active;
            m.Active = -1;
            m.EndsTick = 0;
            m.Stages[kind] = Stage(s, kind) + 1;
            events.Emit(new MegaprojectStageDone(kind, m.Stages[kind]));
        }

        /// <summary>The projects' share of an effect (joins ResearchSystem.EffectTotal).</summary>
        public static float EffectTotal(GameState s, TechEffectKind kind)
        {
            if (s.Mega.Stages.Count == 0) return 0f;
            float sum = 0f;
            foreach (var kv in s.Mega.Stages)
                if (Megaprojects.Def(kv.Key).PerStage.TryGetValue(kind, out var per)) sum += per * kv.Value;
            return sum;
        }

        /// <summary>The Dyson Swarm's boost to the Power Plant's output.</summary>
        public static float EnergyMult(GameState s) =>
            1f + Megaprojects.Def(MegaprojectKind.DysonSwarm).EnergyPerStage * Stage(s, MegaprojectKind.DysonSwarm);

        /// <summary>Projects fully built.</summary>
        public static int Completed(GameState s)
        {
            int n = 0;
            foreach (var kv in s.Mega.Stages) if (kv.Value >= Megaprojects.Stages) n++;
            return n;
        }
    }
}
