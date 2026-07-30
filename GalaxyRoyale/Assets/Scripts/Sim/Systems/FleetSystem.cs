// Ship production. Direct port of v1's `src/sim/systems/FleetSystem.ts`.
//
// The ship queue can hold multiple orders; the first Balance.ShipQueueSlots of
// them build IN PARALLEL (2026-07-08 standardization — everyone runs two
// production lines). Within an order, ships build sequentially — one every
// ShipBuildTime seconds. nextDoneAtTick == 0 means "not yet anchored"; the
// tick loop anchors an order when it slides into the active window, and
// re-anchors after each ship completes.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class FleetSystem
    {
        /// <summary>Per-ship build time in seconds, after research build-time cuts.</summary>
        public static int ShipBuildTime(GameState state, HullId hull)
        {
            var def = Ships.Defs[hull];
            return Math.Max(1, (int)Math.Ceiling(def.BuildTimeSec * ResearchSystem.ShipTimeMult(state)));
        }

        /// <summary>First unmet unlock requirement for a hull, or null when buildable.</summary>
        public static string? UnlockBlocker(GameState state, HullId hull)
        {
            var def = Ships.Defs[hull];
            if (state.Buildings[BuildingId.Shipyard].Level < def.ShipyardLevelReq)
                return $"Requires Shipyard level {def.ShipyardLevelReq}";
            foreach (var req in def.TechReqs)
                if (ResearchSystem.TechLevel(state, req.Tech) < req.Level)
                    return $"Requires {Techs.Defs[req.Tech].Name} level {req.Level}";
            return null;
        }

        public static SimResult QueueShips(GameState state, HullId hull, int count)
        {
            if (count < 1) return SimResult.Fail("Invalid ship count");
            var def = Ships.Defs[hull];
            if (UnlockBlocker(state, hull) is string blocked) return SimResult.Fail(blocked);
            var cost = def.Cost.Scaled(count).Milli();
            var paid = ResourceSystem.Spend(state, cost);
            if (!paid.Ok) return paid;
            state.ShipQueue.Add(new ShipOrder { Hull = hull, Remaining = count, NextDoneAtTick = 0 });
            return SimResult.Success;
        }

        /// <summary>Ships docked at home and free to march (marching ships live on their march). Copy.</summary>
        public static Dictionary<HullId, int> GetIdleShips(GameState state)
        {
            var copy = new Dictionary<HullId, int>();
            foreach (var kv in state.Ships) copy[kv.Key] = kv.Value;
            return copy;
        }

        /// <summary>Largest count of `hull` the player can currently afford + is unlocked for.</summary>
        public static int MaxBuildable(GameState state, HullId hull)
        {
            var def = Ships.Defs[hull];
            if (UnlockBlocker(state, hull) != null) return 0;
            long max = long.MaxValue;
            bool any = false;
            foreach (var res in Resources.All)
            {
                long perShip = def.Cost.Get(res);
                if (perShip > 0)
                {
                    any = true;
                    max = Math.Min(max, state.Resources.Get(res) / (perShip * 1000));
                }
            }
            return any ? (int)Math.Max(0, Math.Min(int.MaxValue, max)) : 999;
        }

        /// <summary>
        /// Apply `sec` of speed-up to a ship order, cascading through the batch: finish the
        /// current ship, then spend the remainder completing whole ships (each takes buildTime),
        /// and finally shave what's left off the next ship's timer.
        /// </summary>
        public static SimResult SpeedUpShipOrder(GameState state, int index, int sec)
        {
            if (index < 0 || index >= state.ShipQueue.Count) return SimResult.Fail("No such order");
            var order = state.ShipQueue[index];
            int buildTime = ShipBuildTime(state, order.Hull);
            if (order.NextDoneAtTick == 0) order.NextDoneAtTick = state.Tick + buildTime;

            int budget = sec;
            int curLeft = order.NextDoneAtTick - state.Tick;
            while (order.Remaining > 0 && budget >= curLeft)
            {
                budget -= curLeft;
                state.Ships[order.Hull]++;
                order.Remaining--;
                curLeft = buildTime;
            }
            if (order.Remaining <= 0) state.ShipQueue.RemoveAt(index);
            else order.NextDoneAtTick = state.Tick + Math.Max(0, curLeft - budget);
            return SimResult.Success;
        }

        /// <summary>Cancel a queued ship order, refunding its remaining ships' cost.</summary>
        public static SimResult CancelShipOrder(GameState state, int index)
        {
            if (index < 0 || index >= state.ShipQueue.Count) return SimResult.Fail("No such order");
            var entry = state.ShipQueue[index];
            var refund = Ships.Defs[entry.Hull].Cost.Scaled(entry.Remaining).Milli();
            ResourceSystem.Add(state, refund);
            state.ShipQueue.RemoveAt(index);
            // Orders sliding into the active window still carry NextDoneAtTick == 0
            // (they were never anchored) and anchor on the next tick; already-anchored
            // lines keep their in-progress ship — no reset needed.
            return SimResult.Success;
        }

        public static void Tick(GameState state, SimEventBus events)
        {
            // The first ShipQueueSlots orders are parallel production lines.
            for (int i = 0; i < state.ShipQueue.Count && i < Balance.ShipQueueSlots; i++)
            {
                var order = state.ShipQueue[i];
                // 0 = freshly queued or just slid into the window: anchor it now.
                if (order.NextDoneAtTick == 0)
                {
                    order.NextDoneAtTick = state.Tick + ShipBuildTime(state, order.Hull);
                    continue;
                }
                if (state.Tick < order.NextDoneAtTick) continue;
                state.Ships[order.Hull]++;
                order.Remaining--;
                events.Emit(new ShipsCompleted(order.Hull, 1));
                if (order.Remaining <= 0) { state.ShipQueue.RemoveAt(i); i--; }
                else order.NextDoneAtTick = state.Tick + ShipBuildTime(state, order.Hull);
            }
        }
    }
}
