// Building upgrade queue, cost/time formulas, and extra-mine placement.
// Direct port of v1's `src/sim/systems/BuildingSystem.ts`.
//
// The build queue holds AT MOST buildSlots() orders. Because inserting a new
// order is rejected when the queue is already full, every order in the queue
// is always "active" (endsAtTick set at queue time). There's no queued-but-
// inactive state to promote — same invariant as v1.
using System;
using System.Collections.Generic;
using System.Linq;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class BuildingSystem
    {
        /// <summary>Cost to reach `toLevel`, in milli-units.</summary>
        public static ResourceBag GetUpgradeCost(BuildingId id, int toLevel)
        {
            var def = Buildings.Defs[id];
            float factor = (float)Math.Pow(Balance.CostGrowth, toLevel - 1);
            return def.BaseCost.Scaled(factor).Milli();
        }

        /// <summary>Build time to reach `toLevel`, in seconds/ticks, after research cuts.</summary>
        public static int GetBuildTime(GameState state, BuildingId id, int toLevel)
        {
            var def = Buildings.Defs[id];
            double raw = def.BaseTimeSec * Math.Pow(Balance.TimeGrowth, toLevel - 1);
            return Math.Max(1, (int)Math.Ceiling(raw * ResearchSystem.BuildTimeMult(state)));
        }

        /// <summary>Concurrent build slots: base 2 (the standard everyone runs),
        /// +1 while Construction Overdrive is up — the shop buys a THIRD line.</summary>
        public static int BuildSlots(GameState state)
        {
            int boosted = state.Buffs.ExtraBuildSlotUntilTick > state.Tick ? 1 : 0;
            return Math.Min(Balance.MaxBuildSlots, Balance.BaseBuildSlots + boosted);
        }

        /// <summary>Queue index of the order for a building (or an extra mine), or -1.</summary>
        public static int FindOrderIndex(GameState state, BuildingId building, int? mineId = null)
        {
            for (int i = 0; i < state.BuildQueue.Count; i++)
            {
                var o = state.BuildQueue[i];
                bool match = mineId.HasValue
                    ? o.MineId == mineId.Value
                    : o.Building == building && o.MineId is null;
                if (match) return i;
            }
            return -1;
        }

        static int QueuedEndsAt(GameState state, BuildingId id, int toLevel)
        {
            bool active = state.BuildQueue.Count < BuildSlots(state);
            return active ? state.Tick + GetBuildTime(state, id, toLevel) : 0;
        }

        /// <summary>Validate every rule for a singleton-building upgrade. Payment side-effect not applied.</summary>
        public static SimResult CheckUpgrade(GameState state, BuildingId id)
        {
            if (state.BuildQueue.Count >= BuildSlots(state)) return SimResult.Fail("Build queue is busy");
            if (FindOrderIndex(state, id) >= 0) return SimResult.Fail("Already in the build queue");
            var def = Buildings.Defs[id];
            int toLevel = state.Buildings[id].Level + 1;
            if (toLevel > def.MaxLevel) return SimResult.Fail($"{def.Name} is at max level");
            int cc = state.Buildings[BuildingId.CommandCenter].Level;
            if (def.UnlockCc > cc) return SimResult.Fail($"Unlocks at Command Center {def.UnlockCc}");
            if (id != BuildingId.CommandCenter && toLevel > cc)
                return SimResult.Fail($"Requires Command Center level {toLevel}");
            if (!ResourceSystem.CanAfford(state, GetUpgradeCost(id, toLevel)))
                return SimResult.Fail("Not enough resources");
            return SimResult.Success;
        }

        public static SimResult StartUpgrade(GameState state, BuildingId id)
        {
            var check = CheckUpgrade(state, id);
            if (!check.Ok) return check;
            int toLevel = state.Buildings[id].Level + 1;
            int endsAtTick = QueuedEndsAt(state, id, toLevel);
            var paid = ResourceSystem.Spend(state, GetUpgradeCost(id, toLevel));
            if (!paid.Ok) return paid;
            state.BuildQueue.Add(new BuildOrder { Building = id, ToLevel = toLevel, EndsAtTick = endsAtTick });
            return SimResult.Success;
        }

        /// <summary>Cancel a queued build with full refund. Removes an orphaned extra-mine plot.</summary>
        public static SimResult CancelUpgrade(GameState state, int index = 0)
        {
            if (index < 0 || index >= state.BuildQueue.Count) return SimResult.Fail("Nothing is being built");
            var order = state.BuildQueue[index];
            ResourceSystem.Add(state, GetUpgradeCost(order.Building, order.ToLevel));
            if (order.MineId is int mineId)
            {
                var mine = state.ExtraMines.Find(m => m.Id == mineId);
                if (mine != null && mine.Level == 0)
                    state.ExtraMines.RemoveAll(m => m.Id == mineId);
            }
            state.BuildQueue.RemoveAt(index);
            return SimResult.Success;
        }

        /// <summary>
        /// Recommended next upgrade when the queue is idle: lowest-level building within
        /// the Command Center cap, preferring affordable ones, tie-broken by cheapest cost.
        /// The core nine only: the Frontier's buildings are a choice, not a chore.
        /// </summary>
        public static BuildingId NextBestUpgrade(GameState state)
        {
            int cc = state.Buildings[BuildingId.CommandCenter].Level;
            var candidates = new List<BuildingId>();
            foreach (var id in Buildings.Core)
            {
                var def = Buildings.Defs[id];
                int lvl = state.Buildings[id].Level;
                if (lvl >= def.MaxLevel) continue;
                if (id != BuildingId.CommandCenter && lvl + 1 > cc) continue;
                candidates.Add(id);
            }
            if (candidates.Count == 0) return BuildingId.CommandCenter;

            long TotalCost(BuildingId id)
            {
                var c = GetUpgradeCost(id, state.Buildings[id].Level + 1);
                return c.Gold + c.Quartz + c.Helium;
            }

            var affordable = candidates
                .Where(id => ResourceSystem.CanAfford(state, GetUpgradeCost(id, state.Buildings[id].Level + 1)))
                .ToList();
            var pool = affordable.Count > 0 ? affordable : candidates;
            pool.Sort((a, b) =>
            {
                int levelDiff = state.Buildings[a].Level - state.Buildings[b].Level;
                return levelDiff != 0 ? levelDiff : TotalCost(a).CompareTo(TotalCost(b));
            });
            return pool[0];
        }

        /// <summary>
        /// Shave `seconds` off the completion time of a queued build. Placeholder for a real
        /// speedup-item flow — for now callers just supply a fixed delta. Clamped so completion
        /// never lands before the current tick (that'd insta-complete on the next Tick).
        /// </summary>
        public static SimResult SpeedUpBuildOrder(GameState state, int index, int seconds)
        {
            if (index < 0 || index >= state.BuildQueue.Count) return SimResult.Fail("No such order");
            var order = state.BuildQueue[index];
            order.EndsAtTick = Math.Max(state.Tick, order.EndsAtTick - seconds);
            return SimResult.Success;
        }

        public static void Tick(GameState state, SimEventBus events)
        {
            int active = Math.Min(BuildSlots(state), state.BuildQueue.Count);
            var completed = new List<BuildOrder>();
            for (int i = 0; i < active; i++)
            {
                var o = state.BuildQueue[i];
                if (state.Tick >= o.EndsAtTick) completed.Add(o);
            }
            foreach (var order in completed)
            {
                if (order.MineId is int mineId)
                {
                    var mine = state.ExtraMines.Find(m => m.Id == mineId);
                    if (mine != null) mine.Level = order.ToLevel;
                }
                else
                {
                    state.Buildings[order.Building].Level = order.ToLevel;
                }
                state.Stats.UpgradesDone++;
                events.Emit(new BuildingCompleted(order.Building, order.ToLevel, order.MineId));
            }
            if (completed.Count > 0)
                state.BuildQueue.RemoveAll(o => completed.Contains(o));
        }

        // ---------- multiple mines (extra production buildings) ----------

        /// <summary>How many instances of a mine type the current Command Center level unlocks.</summary>
        public static int AllowedMinesForType(GameState state)
        {
            int cc = state.Buildings[BuildingId.CommandCenter].Level;
            int unlocked = 0;
            foreach (int required in Balance.MineSlotUnlocks)
                if (cc >= required) unlocked++;
            return Math.Min(Balance.MaxMinesPerType, unlocked);
        }

        /// <summary>Total instances of a mine type (the singleton counts as #1).</summary>
        public static int MineCountOfType(GameState state, MineType type)
        {
            int extras = 0;
            foreach (var m in state.ExtraMines) if (m.Type == type) extras++;
            return 1 + extras;
        }

        public static ExtraMine? GetMine(GameState state, int mineId) =>
            state.ExtraMines.Find(m => m.Id == mineId);

        public static SimResult CheckBuildMine(GameState state, MineType type)
        {
            if (state.BuildQueue.Count >= BuildSlots(state)) return SimResult.Fail("Build queue is busy");
            if (MineCountOfType(state, type) >= AllowedMinesForType(state))
                return SimResult.Fail("Upgrade the Command Center to unlock another");
            if (!ResourceSystem.CanAfford(state, GetUpgradeCost(MineTypes.ToBuildingId(type), 1)))
                return SimResult.Fail("Not enough resources");
            return SimResult.Success;
        }

        /// <summary>Place a new extra mine at `plot`; it builds up to level 1 via the queue.
        /// On success, sets `mineId` to the new instance's id.</summary>
        public static SimResult BuildMine(GameState state, MineType type, int plot, out int mineId)
        {
            mineId = 0;
            var check = CheckBuildMine(state, type);
            if (!check.Ok) return check;

            var buildingId = MineTypes.ToBuildingId(type);
            int endsAtTick = QueuedEndsAt(state, buildingId, 1);
            var paid = ResourceSystem.Spend(state, GetUpgradeCost(buildingId, 1));
            if (!paid.Ok) return paid;

            var mine = new ExtraMine { Id = state.NextMineId++, Type = type, Level = 0, Plot = plot };
            state.ExtraMines.Add(mine);
            state.BuildQueue.Add(new BuildOrder { Building = buildingId, ToLevel = 1, EndsAtTick = endsAtTick, MineId = mine.Id });
            mineId = mine.Id;
            return SimResult.Success;
        }

        public static SimResult CheckUpgradeMine(GameState state, int mineId)
        {
            if (state.BuildQueue.Count >= BuildSlots(state)) return SimResult.Fail("Build queue is busy");
            var mine = GetMine(state, mineId);
            if (mine == null) return SimResult.Fail("No such mine");
            var buildingId = MineTypes.ToBuildingId(mine.Type);
            if (FindOrderIndex(state, buildingId, mineId) >= 0) return SimResult.Fail("Already in the build queue");
            var def = Buildings.Defs[buildingId];
            int toLevel = mine.Level + 1;
            if (toLevel > def.MaxLevel) return SimResult.Fail($"{def.Name} is at max level");
            if (toLevel > state.Buildings[BuildingId.CommandCenter].Level)
                return SimResult.Fail($"Requires Command Center level {toLevel}");
            if (!ResourceSystem.CanAfford(state, GetUpgradeCost(buildingId, toLevel)))
                return SimResult.Fail("Not enough resources");
            return SimResult.Success;
        }

        public static SimResult StartUpgradeMine(GameState state, int mineId)
        {
            var check = CheckUpgradeMine(state, mineId);
            if (!check.Ok) return check;
            var mine = GetMine(state, mineId)!;
            var buildingId = MineTypes.ToBuildingId(mine.Type);
            int toLevel = mine.Level + 1;
            int endsAtTick = QueuedEndsAt(state, buildingId, toLevel);
            var paid = ResourceSystem.Spend(state, GetUpgradeCost(buildingId, toLevel));
            if (!paid.Ok) return paid;
            state.BuildQueue.Add(new BuildOrder { Building = buildingId, ToLevel = toLevel, EndsAtTick = endsAtTick, MineId = mineId });
            return SimResult.Success;
        }
    }
}
