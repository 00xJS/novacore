// Passive resource production, spending, and energy-balance calculation.
// Direct port of v1's `src/sim/systems/ResourceSystem.ts`.
//
// Storage does not cap resources — resources are uncapped and accumulate freely.
// GetProtected returns the amount shielded from raid plunder (scales with Warehouse).
using System;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public readonly struct EnergyBalance
    {
        public readonly int Supply;
        public readonly int Demand;
        /// <summary>1.0 when supply ≥ demand; supply/demand ratio otherwise.</summary>
        public readonly float Factor;

        public EnergyBalance(int supply, int demand, float factor)
        {
            Supply = supply;
            Demand = demand;
            Factor = factor;
        }
    }

    public static class ResourceSystem
    {
        const int Hour = 3600;

        /// <summary>
        /// Exact integral of a constant milli/hour rate over the tick interval [t0, t1].
        /// Summing per-tick calls telescopes to the closed form — zero drift.
        /// </summary>
        public static long ProducedBetween(long rateMilliPerHour, int t0, int t1) =>
            rateMilliPerHour * t1 / Hour - rateMilliPerHour * t0 / Hour;

        public static EnergyBalance GetEnergyBalance(GameState state)
        {
            int supply = 0;
            int demand = 0;

            foreach (var id in Buildings.All)
            {
                var def = Buildings.Defs[id];
                int level = state.Buildings[id].Level;
                if (def.Kind == BuildingKind.Energy) supply += Balance.EnergyOut(level);
                demand += Balance.EnergyUse(def.BaseEnergyUse, level);
            }
            foreach (var mine in state.ExtraMines)
            {
                var def = Buildings.Defs[MineTypes.ToBuildingId(mine.Type)];
                demand += Balance.EnergyUse(def.BaseEnergyUse, mine.Level);
            }

            if (state.Buffs.EnergyBoostUntilTick > state.Tick)
                supply = (int)Math.Floor(supply * Balance.EnergyBoostFactor);

            float factor = demand <= supply ? 1f : (float)supply / demand;
            return new EnergyBalance(supply, demand, factor);
        }

        /// <summary>Effective production in milli-units/hour per resource (after energy factor + buffs).</summary>
        public static ResourceBag GetRates(GameState state)
        {
            var bal = GetEnergyBalance(state);
            float boost = state.Buffs.ProdBoostUntilTick > state.Tick ? Balance.ProdBoostFactor : 1f;
            // Gold Rush (galaxy event) lifts every empire's production.
            float research = ResearchSystem.ProdMultiplier(state) * EventSystem.ProductionMult(state);
            var rates = new ResourceBag();

            void AddProd(BuildingId type, int level)
            {
                var def = Buildings.Defs[type];
                if (def.Kind != BuildingKind.Producer || def.Resource is null) return;
                int wholePerHour = Balance.ProdPerHour(def.BaseProdPerHour, level);
                // long: the handoff's known follow-up — extreme levels push milli/hour past int range.
                long milliPerHour = (long)Math.Floor(wholePerHour * 1000d * bal.Factor * boost * research);
                var res = ResourceFromString(def.Resource);
                if (res is null) return;
                rates.Set(res.Value, rates.Get(res.Value) + milliPerHour);
            }

            foreach (var id in Buildings.All) AddProd(id, state.Buildings[id].Level);
            foreach (var mine in state.ExtraMines) AddProd(MineTypes.ToBuildingId(mine.Type), mine.Level);
            return rates;
        }

        /// <summary>Milli-units per resource shielded from raid plunder (Warehouse-scaled).</summary>
        public static ResourceBag GetProtected(GameState state)
        {
            var warehouseDef = Buildings.Defs[BuildingId.Warehouse];
            int bonus = Balance.StorageBonus(warehouseDef.BaseStorageBonus, state.Buildings[BuildingId.Warehouse].Level);
            long milliAmt = (long)Math.Round((Balance.BaseStorage + bonus) * 1000L
                * (double)ResearchSystem.ShieldCapMult(state) * Difficulties.VaultMult(state.Difficulty));
            return new ResourceBag(milliAmt, milliAmt, milliAmt);
        }

        public static void Tick(GameState state, SimEventBus events)
        {
            var rates = GetRates(state);
            foreach (var res in Resources.All)
            {
                long gain = ProducedBetween(rates.Get(res), state.Tick - 1, state.Tick);
                if (gain > 0) state.Resources.Set(res, state.Resources.Get(res) + (int)gain);
            }
        }

        public static bool CanAfford(GameState state, ResourceBag costMilli)
        {
            foreach (var res in Resources.All)
                if (state.Resources.Get(res) < costMilli.Get(res)) return false;
            return true;
        }

        public static SimResult Spend(GameState state, ResourceBag costMilli)
        {
            if (!CanAfford(state, costMilli)) return SimResult.Fail("Not enough resources");
            foreach (var res in Resources.All)
                state.Resources.Set(res, state.Resources.Get(res) - costMilli.Get(res));
            return SimResult.Success;
        }

        /// <summary>Add milli-resources (uncapped). Negative entries in bagMilli are clamped to 0.</summary>
        public static void Add(GameState state, ResourceBag bagMilli)
        {
            foreach (var res in Resources.All)
                state.Resources.Set(res, state.Resources.Get(res) + Math.Max(0, bagMilli.Get(res)));
        }

        static ResourceId? ResourceFromString(string s) => s switch
        {
            "gold"   => ResourceId.Gold,
            "quartz" => ResourceId.Quartz,
            "helium"     => ResourceId.Helium,
            _ => null,
        };
    }
}
