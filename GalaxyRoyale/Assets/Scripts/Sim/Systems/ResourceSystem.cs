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
            supply = (int)Math.Floor(supply * TerraformSystem.EnergyMult(state)); // the Temperate path
            supply = (int)Math.Floor(supply * MegaprojectSystem.EnergyMult(state)); // the Dyson Swarm

            float factor = demand <= supply ? 1f : (float)supply / demand;
            return new EnergyBalance(supply, demand, factor);
        }

        /// <summary>Effective production in milli-units/hour per resource (after energy factor + buffs).</summary>
        public static ResourceBag GetRates(GameState state)
        {
            var bal = GetEnergyBalance(state);
            float boost = state.Buffs.ProdBoostUntilTick > state.Tick ? Balance.ProdBoostFactor : 1f;
            // Gold Rush (galaxy event) lifts every empire's production.
            float research = ResearchSystem.ProdMultiplier(state) * EventSystem.ProductionMult(state) * TwistSystem.ProductionMult(state)
                * (1f + HomeGuardBonus(state));
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
            // The Terraformer's path (the Citadel, 2026-09-30).
            if (state.Terraform.Stage > 0)
                foreach (var res in Resources.All)
                    rates.Set(res, (long)(rates.Get(res) * (double)TerraformSystem.ResourceMult(state, res)));
            return rates;
        }

        /// <summary>What the colony's mines make in an hour at full energy, before any
        /// boost (milli-units): the yardstick for rewards that keep pace with the colony
        /// (supply drops, event rewards).</summary>
        public static ResourceBag MineOutputPerHour(GameState state)
        {
            var bag = new ResourceBag();
            void Add(BuildingId id, int level)
            {
                var def = Buildings.Defs[id];
                if (def.Kind != BuildingKind.Producer || level < 1) return;
                long milli = Balance.ProdPerHour(def.BaseProdPerHour, level) * 1000L;
                switch (def.Resource)
                {
                    case "gold": bag.Gold += milli; break;
                    case "quartz": bag.Quartz += milli; break;
                    case "helium": bag.Helium += milli; break;
                }
            }
            foreach (var id in Buildings.All) Add(id, state.Buildings[id].Level);
            foreach (var mine in state.ExtraMines) Add(MineTypes.ToBuildingId(mine.Type), mine.Level);
            return bag;
        }

        /// <summary>Home Guard (balance pass 2026-09-30): warships docked at home lift
        /// production, up to Balance.HomeGuardMaxBonus once their might reaches
        /// Balance.HomeGuardFullMight(CC). Fleets out flying don't count, which makes
        /// every raid a small trade-off.</summary>
        public static float HomeGuardBonus(GameState state)
        {
            long full = Balance.HomeGuardFullMight(state.Buildings[BuildingId.CommandCenter].Level);
            return Balance.HomeGuardMaxBonus * Math.Min(1f, HomeGuardMight(state) / (float)full);
        }

        /// <summary>Might of the warships docked at home (whole units / 10, like PowerSystem).</summary>
        public static long HomeGuardMight(GameState state)
        {
            long invested = 0;
            foreach (var kv in state.Ships)
                if (kv.Value > 0 && IsWarship(kv.Key)) invested += (long)kv.Value * Ships.Defs[kv.Key].Cost.Total;
            return invested / 10;
        }

        /// <summary>Everything but the cargo and recon hulls.</summary>
        public static bool IsWarship(HullId hull) =>
            hull != HullId.Hauler && hull != HullId.Atlas && hull != HullId.Scavenger && hull != HullId.Probe;

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
            // GetRates is the costliest thing in a tick (every producer's curve, the
            // research totals, energy): an 8 h offline catch-up spent 90% of its
            // time here. The rates only change when one of their inputs does, so
            // they're kept until RatesKey moves (2026-09-30).
            long key = RatesKey(state);
            if (state.RatesCache == null || key != state.RatesCacheKey)
            {
                state.RatesCache = GetRates(state);
                state.RatesCacheKey = key;
            }
            var rates = state.RatesCache;
            foreach (var res in Resources.All)
            {
                long gain = ProducedBetween(rates.Get(res), state.Tick - 1, state.Tick);
                if (gain > 0) state.Resources.Set(res, state.Resources.Get(res) + (int)gain);
            }
        }

        /// <summary>A fingerprint of everything GetRates reads: building and mine levels,
        /// research and commander skills, relics, the Terraformer, the timed boosts,
        /// the live galaxy event and the docked fleet.</summary>
        public static long RatesKey(GameState state)
        {
            unchecked
            {
                long h = 17;
                void Mix(long v) => h = h * 1_000_003 + v;
                foreach (var kv in state.Buildings) Mix(kv.Value.Level);
                foreach (var m in state.ExtraMines) { Mix(m.Level); Mix((int)m.Type); }
                foreach (var kv in state.Research) { Mix((int)kv.Key); Mix(kv.Value); }
                foreach (var kv in state.Commander.Skills) { Mix(kv.Key.Length); Mix(kv.Key[0]); Mix(kv.Value); }
                foreach (var kv in state.Relics) { Mix((int)kv.Key); Mix(kv.Value); }
                Mix((int)state.Terraform.Path);
                Mix(state.Terraform.Stage);
                Mix(state.Buffs.ProdBoostUntilTick > state.Tick ? 1 : 0);
                Mix(state.Buffs.EnergyBoostUntilTick > state.Tick ? 1 : 0);
                Mix((int)EventSystem.KindAt(state.Tick));
                Mix((int)TwistSystem.KindAt(state.Tick));
                foreach (var kv in state.Mega.Stages) { Mix(100 + (int)kv.Key); Mix(kv.Value); }
                foreach (var kv in state.Ships) { Mix((int)kv.Key); Mix(kv.Value); }
                return h;
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
