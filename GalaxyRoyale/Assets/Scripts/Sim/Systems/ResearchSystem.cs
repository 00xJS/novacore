// Research tech tree progression + effect accessors.
// Direct port of v1's `src/sim/systems/ResearchSystem.ts`.
//
// Systems consume effects via the summed accessors (ProdMultiplier, BuildTimeMult, etc.)
// so adding a new tech never touches the systems that read it.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class ResearchSystem
    {
        /// <summary>Current researched level of a tech. 0 = not researched.</summary>
        public static int TechLevel(GameState state, TechId id) =>
            state.Research.TryGetValue(id, out var l) ? l : 0;

        public static ResourceBag GetResearchCost(TechId id, int toLevel)
        {
            var def = Techs.Defs[id];
            float factor = (float)Math.Pow(def.CostGrowth, toLevel - 1);
            return def.BaseCost.Scaled(factor).Milli();
        }

        public static int GetResearchTime(GameState state, TechId id, int toLevel)
        {
            var def = Techs.Defs[id];
            double raw = def.BaseTimeSec * Math.Pow(def.TimeGrowth, toLevel - 1);
            // Research Surge (galaxy event) cuts research started while it runs.
            return Math.Max(1, (int)Math.Ceiling(raw * ResearchTimeMult(state) * EventSystem.ResearchTimeMult(state) * TwistSystem.ResearchTimeMult(state)));
        }

        static int LabLevel(GameState state) => state.Buildings[BuildingId.ResearchLab].Level;

        /// <summary>Concurrent research slots: base 2 (the standard everyone runs),
        /// +1 while Research Overclock is up — the shop buys a THIRD line.</summary>
        public static int ResearchSlots(GameState state)
        {
            int boosted = state.Buffs.ExtraResearchSlotUntilTick > state.Tick ? 1 : 0;
            return Math.Min(Balance.MaxResearchSlots, Balance.BaseResearchSlots + boosted);
        }

        public static bool IsResearching(GameState state, TechId id)
        {
            foreach (var o in state.ResearchQueue)
                if (o.TechId == id) return true;
            return false;
        }

        public static bool PrereqMet(GameState state, TechId id)
        {
            var def = Techs.Defs[id];
            if (LabLevel(state) < def.LabLevelReq) return false;
            if (def.Requires is TechRequirement req && TechLevel(state, req.Tech) < req.Level) return false;
            return true;
        }

        public static SimResult CheckResearch(GameState state, TechId id)
        {
            if (LabLevel(state) < 1) return SimResult.Fail("Build a Research Lab first");
            if (state.ResearchQueue.Count >= ResearchSlots(state)) return SimResult.Fail("All research slots are busy");
            if (IsResearching(state, id)) return SimResult.Fail("Already researching this");
            var def = Techs.Defs[id];
            int toLevel = TechLevel(state, id) + 1;
            if (toLevel > def.MaxLevel) return SimResult.Fail($"{def.Name} is fully researched");
            if (LabLevel(state) < def.LabLevelReq) return SimResult.Fail($"Requires Research Lab level {def.LabLevelReq}");
            if (toLevel > LabLevel(state)) return SimResult.Fail($"Requires Research Lab level {toLevel}");
            if (def.Requires is TechRequirement req && TechLevel(state, req.Tech) < req.Level)
                return SimResult.Fail($"Requires {Techs.Defs[req.Tech].Name} level {req.Level}");
            if (!ResourceSystem.CanAfford(state, GetResearchCost(id, toLevel)))
                return SimResult.Fail("Not enough resources");
            return SimResult.Success;
        }

        public static SimResult StartResearch(GameState state, TechId id)
        {
            var check = CheckResearch(state, id);
            if (!check.Ok) return check;
            int toLevel = TechLevel(state, id) + 1;
            var paid = ResourceSystem.Spend(state, GetResearchCost(id, toLevel));
            if (!paid.Ok) return paid;
            state.ResearchQueue.Add(new ResearchOrder
            {
                TechId = id,
                ToLevel = toLevel,
                EndsAtTick = state.Tick + GetResearchTime(state, id, toLevel),
            });
            return SimResult.Success;
        }

        public static SimResult CancelResearch(GameState state, int index = 0)
        {
            if (index < 0 || index >= state.ResearchQueue.Count) return SimResult.Fail("Nothing is being researched");
            var order = state.ResearchQueue[index];
            ResourceSystem.Add(state, GetResearchCost(order.TechId, order.ToLevel));
            state.ResearchQueue.RemoveAt(index);
            return SimResult.Success;
        }

        public static void Tick(GameState state, SimEventBus events)
        {
            var done = new List<ResearchOrder>();
            foreach (var o in state.ResearchQueue)
                if (state.Tick >= o.EndsAtTick) done.Add(o);
            foreach (var order in done)
            {
                state.Research[order.TechId] = order.ToLevel;
                state.Stats.ResearchDone++;
                events.Emit(new ResearchCompleted(order.TechId, order.ToLevel));
            }
            if (done.Count > 0)
                state.ResearchQueue.RemoveAll(o => done.Contains(o));
        }

        // ---------- effect accessors (summed by effect kind across all techs) ----------

        /// <summary>Summed magnitude of every GLOBAL tech with a given effect (Σ level × perLevel),
        /// plus the commander's skills of that kind. Hull-scoped techs are excluded — use EffectTotalFor.</summary>
        public static float EffectTotal(GameState state, TechEffectKind kind)
        {
            float sum = 0f;
            foreach (var id in Techs.All)
            {
                var def = Techs.Defs[id];
                if (def.Effect != kind || def.HullScope != null) continue;
                sum += TechLevel(state, id) * def.PerLevel;
            }
            // The Citadel (2026-09-30): relics on display and the Terraformer's path.
            return sum + CommanderSystem.EffectTotal(state, kind) + RelicSystem.EffectTotal(state, kind)
                + TerraformSystem.EffectTotal(state, kind) + MegaprojectSystem.EffectTotal(state, kind)
                + TwistSystem.EffectTotal(state, kind);
        }

        /// <summary>Summed magnitude of techs whose effect is scoped to one hull.</summary>
        public static float EffectTotalFor(GameState state, TechEffectKind kind, HullId hull)
        {
            float sum = 0f;
            foreach (var id in Techs.All)
            {
                var def = Techs.Defs[id];
                if (def.Effect != kind || def.HullScope != hull) continue;
                sum += TechLevel(state, id) * def.PerLevel;
            }
            return sum;
        }

        public static float ProdMultiplier(GameState state)  => 1f + EffectTotal(state, TechEffectKind.ProdMultiplier);
        public static float MarchSpeedMult(GameState state)  => 1f + EffectTotal(state, TechEffectKind.MarchSpeedMult);
        public static float CargoMult(GameState state)       => 1f + EffectTotal(state, TechEffectKind.CargoMult);
        public static float AtkMult(GameState state)         => 1f + EffectTotal(state, TechEffectKind.AtkMult);
        public static float HpMult(GameState state)          => 1f + EffectTotal(state, TechEffectKind.HpMult);
        public static float GatherRateMult(GameState state)  => 1f + EffectTotal(state, TechEffectKind.GatherRateMult);
        public static float ShieldCapMult(GameState state)   => 1f + EffectTotal(state, TechEffectKind.ShieldCapMult);
        public static float ShieldMult(GameState state)      => 1f + EffectTotal(state, TechEffectKind.ShieldMult);

        /// <summary>Global + hull-scoped attack multiplier for one hull.</summary>
        public static float AtkMultFor(GameState state, HullId hull) =>
            AtkMult(state) + EffectTotalFor(state, TechEffectKind.AtkMult, hull) + ModuleSystem.AtkFor(state, hull);

        /// <summary>Global + hull-scoped durability multiplier for one hull.</summary>
        public static float HpMultFor(GameState state, HullId hull) =>
            HpMult(state) + EffectTotalFor(state, TechEffectKind.HpMult, hull) + ModuleSystem.HpFor(state, hull);

        /// <summary>Orbital Batteries level (planetary guns, home defense only): the
        /// research plus the commander's Orbital Gunners.</summary>
        public static int BatteryLevel(GameState state) => TechLevel(state, TechId.OrbitalBatteries)
            + (int)Math.Round(CommanderSystem.EffectTotal(state, TechEffectKind.OrbitalBattery));

        /// <summary>Home-defense combat mods: military research (it counts in every
        /// battle) plus the Defense branch and the Orbital Batteries (home only).</summary>
        public static Combat.FleetMods DefenseMods(GameState state)
        {
            float atk = EffectTotal(state, TechEffectKind.DefAtkMult);
            float hp = EffectTotal(state, TechEffectKind.DefHpMult);
            var atkByHull = new Dictionary<HullId, float>();
            var hpByHull = new Dictionary<HullId, float>();
            foreach (var hull in Ships.All)
            {
                atkByHull[hull] = AtkMultFor(state, hull) + atk;
                hpByHull[hull] = HpMultFor(state, hull) + hp;
            }
            // The Command Bastion's armoured docks protect everything docked at home.
            int bastion = state.Buildings.TryGetValue(BuildingId.CommandBastion, out var b) ? b.Level : 0;
            float docks = bastion * Balance.BastionDockArmourPerLevel;
            if (docks > 0f)
                foreach (var hull in Ships.All) hpByHull[hull] += docks;
            return new Combat.FleetMods(AtkMult(state) + atk, HpMult(state) + hp + docks, atkByHull, hpByHull,
                ShieldMult(state) + EffectTotal(state, TechEffectKind.DefShieldMult), BatteryLevel(state),
                Balance.BastionDamage(bastion));
        }

        /// <summary>Attacking combat mods — military research only (global scalars +
        /// per-hull dictionaries). The Defense branch stays home.</summary>
        public static Combat.FleetMods CombatMods(GameState state)
        {
            var atkByHull = new Dictionary<HullId, float>();
            var hpByHull = new Dictionary<HullId, float>();
            foreach (var hull in Ships.All)
            {
                atkByHull[hull] = AtkMultFor(state, hull);
                hpByHull[hull] = HpMultFor(state, hull);
            }
            return new Combat.FleetMods(AtkMult(state), HpMult(state), atkByHull, hpByHull,
                ShieldMult(state));
        }

        /// <summary>A fleet's combat mods: CombatMods, stronger when your commander leads it
        /// (the Academy, 2026-09-30).</summary>
        public static Combat.FleetMods CombatModsFor(GameState state, int marchId)
        {
            if (!AcademySystem.Leads(state, marchId)) return CombatMods(state);
            float k = 1f + AcademySystem.CaptainBonus(state);
            var atkByHull = new Dictionary<HullId, float>();
            var hpByHull = new Dictionary<HullId, float>();
            foreach (var hull in Ships.All)
            {
                atkByHull[hull] = AtkMultFor(state, hull) * k;
                hpByHull[hull] = HpMultFor(state, hull) * k;
            }
            return new Combat.FleetMods(AtkMult(state) * k, HpMult(state) * k, atkByHull, hpByHull, ShieldMult(state));
        }

        static float Reduce(GameState state, TechEffectKind kind) =>
            Math.Max(Techs.ResearchReduceFloor, 1f - EffectTotal(state, kind));

        /// <summary>Helium-cost multiplier (≤1).</summary>
        public static float HeliumMult(GameState state)       => Reduce(state, TechEffectKind.HeliumReduce);
        /// <summary>Build-time multiplier (≤1) for buildings and ships.</summary>
        public static float BuildTimeMult(GameState state) => Reduce(state, TechEffectKind.BuildTimeReduce);
        /// <summary>Ship build-time multiplier (≤1) — BuildTimeReduce and ShipTimeReduce stack, same floor.</summary>
        public static float ShipTimeMult(GameState state) =>
            Math.Max(Techs.ResearchReduceFloor, BuildTimeMult(state) * Reduce(state, TechEffectKind.ShipTimeReduce));
        /// <summary>Research-time multiplier (≤1) — its own line, split from BuildTimeReduce.</summary>
        public static float ResearchTimeMult(GameState state) => Reduce(state, TechEffectKind.ResearchTimeReduce);
    }
}
