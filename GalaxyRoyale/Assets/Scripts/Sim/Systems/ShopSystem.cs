// Dark Matter shop + inventory effects. Direct port of v1's
// `src/sim/systems/ShopSystem.ts`, with v1's stringly-typed effect dispatch
// replaced by the typed ShopEffect enum on ShopItemDef.
using System.Collections.Generic;
using System.Linq;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class ShopSystem
    {
        /// <summary>Buy with Dark Matter. Consumables land in the inventory; skins unlock.</summary>
        public static SimResult Buy(GameState state, string itemId, SimEventBus events)
        {
            if (!Shop.ById.TryGetValue(itemId, out var item)) return SimResult.Fail("Unknown item");
            if (item.IsSkin && state.Skins.Owned.Contains(itemId)) return SimResult.Fail("Already owned");
            if (state.Premium.DarkMatter < item.PriceDM) return SimResult.Fail("Not enough Dark Matter");
            state.Premium.DarkMatter -= item.PriceDM;

            if (item.IsSkin)
            {
                state.Skins.Owned.Add(itemId);
            }
            else
            {
                var slot = state.Inventory.FirstOrDefault(i => i.ItemId == itemId);
                if (slot != null) slot.Count++;
                else state.Inventory.Add(new InventoryEntry { ItemId = itemId, Count = 1 });
            }
            events.Emit(new ItemPurchased(itemId));
            return SimResult.Success;
        }

        /// <summary>
        /// Remove one of an item from the inventory without applying any effect (the
        /// targeted speed-up picker applies the effect itself).
        /// </summary>
        public static SimResult ConsumeItem(GameState state, string itemId)
        {
            var slot = state.Inventory.FirstOrDefault(i => i.ItemId == itemId);
            if (slot == null || slot.Count < 1) return SimResult.Fail("None in inventory");
            slot.Count--;
            if (slot.Count == 0) state.Inventory.Remove(slot);
            state.Stats.ItemsUsed++;
            return SimResult.Success;
        }

        /// <summary>Use a consumable from the inventory.</summary>
        public static SimResult UseItem(GameState state, string itemId)
        {
            var slot = state.Inventory.FirstOrDefault(i => i.ItemId == itemId);
            if (slot == null || slot.Count < 1) return SimResult.Fail("None in inventory");
            var applied = ApplyEffect(state, itemId);
            if (!applied.Ok) return applied;
            slot.Count--;
            if (slot.Count == 0) state.Inventory.Remove(slot);
            state.Stats.ItemsUsed++;
            return SimResult.Success;
        }

        /// <summary>Permanently rename the commander (costs the name-change item's DM price).</summary>
        public static SimResult ChangeCommanderName(GameState state, string newName)
        {
            var name = newName.Trim();
            if (name.Length < 2 || name.Length > 16) return SimResult.Fail("Name must be 2–16 characters");
            int price = Shop.ById["name-change"].PriceDM;
            if (state.Premium.DarkMatter < price) return SimResult.Fail("Not enough Dark Matter");
            state.Premium.DarkMatter -= price;
            state.Profile.Name = name;
            return SimResult.Success;
        }

        /// <summary>Equip an owned planet skin.</summary>
        public static SimResult ApplySkin(GameState state, string skinId)
        {
            if (!state.Skins.Owned.Contains(skinId)) return SimResult.Fail("Skin not owned");
            state.Skins.ActivePlanet = skinId;
            return SimResult.Success;
        }

        /// <summary>
        /// Timer speed-up: shave `sec` seconds off the single most relevant active timer.
        /// Priority: current building upgrade → head ship order → head research → soonest march.
        /// Timers never drop below the current tick (a speed-up completes, never rewinds).
        /// </summary>
        static SimResult ApplySpeedup(GameState state, int sec)
        {
            var order = state.BuildQueue.FirstOrDefault();
            if (order != null)
            {
                order.EndsAtTick = System.Math.Max(state.Tick, order.EndsAtTick - sec);
                return SimResult.Success;
            }
            var head = state.ShipQueue.FirstOrDefault();
            if (head != null)
            {
                // A freshly-promoted order (NextDoneAtTick 0) hasn't anchored yet — anchor first.
                int done = head.NextDoneAtTick != 0
                    ? head.NextDoneAtTick
                    : state.Tick + FleetSystem.ShipBuildTime(state, head.Hull);
                head.NextDoneAtTick = System.Math.Max(state.Tick, done - sec);
                return SimResult.Success;
            }
            var research = state.ResearchQueue.FirstOrDefault();
            if (research != null)
            {
                research.EndsAtTick = System.Math.Max(state.Tick, research.EndsAtTick - sec);
                return SimResult.Success;
            }
            var march = state.Marches
                .Where(m => m.Phase != MarchPhase.Gathering)
                .OrderBy(m => m.ArrivesAtTick)
                .FirstOrDefault();
            if (march != null)
            {
                march.ArrivesAtTick = System.Math.Max(state.Tick, march.ArrivesAtTick - sec);
                return SimResult.Success;
            }
            return SimResult.Fail("No active timer to speed up");
        }

        /// <summary>
        /// Universal finisher: instantly completes whichever active build/research/ship
        /// timer will finish soonest.
        /// </summary>
        static SimResult FinishMostImminent(GameState state)
        {
            var cands = new List<(int ends, System.Action done)>();
            var build = state.BuildQueue.FirstOrDefault();
            if (build != null && build.EndsAtTick > 0)
                cands.Add((build.EndsAtTick, () => build.EndsAtTick = state.Tick));
            var rq = state.ResearchQueue.FirstOrDefault();
            if (rq != null)
                cands.Add((rq.EndsAtTick, () => rq.EndsAtTick = state.Tick));
            var ship = state.ShipQueue.FirstOrDefault();
            if (ship != null)
            {
                int ends = ship.NextDoneAtTick != 0
                    ? ship.NextDoneAtTick
                    : state.Tick + FleetSystem.ShipBuildTime(state, ship.Hull);
                cands.Add((ends, () => ship.NextDoneAtTick = state.Tick));
            }
            if (cands.Count == 0) return SimResult.Fail("Nothing is building, researching or training");
            cands.OrderBy(c => c.ends).First().done();
            return SimResult.Success;
        }

        static SimResult ApplyEffect(GameState state, string itemId)
        {
            if (!Shop.ById.TryGetValue(itemId, out var item)) return SimResult.Fail("Item has no effect");
            switch (item.Effect)
            {
                case ShopEffect.Speedup:
                    return ApplySpeedup(state, item.SpeedupSec ?? 0);
                case ShopEffect.ResourceGrant:
                    // Uncapped credit — storage protects production, it doesn't cap grants.
                    state.Resources.Add(item.ResourceGrant!.Milli());
                    return SimResult.Success;
                case ShopEffect.FinishBuild:
                {
                    var order = state.BuildQueue.FirstOrDefault();
                    if (order == null) return SimResult.Fail("Nothing is being built");
                    order.EndsAtTick = state.Tick; // completes on the next tick
                    return SimResult.Success;
                }
                case ShopEffect.FinishShips:
                {
                    var head = state.ShipQueue.FirstOrDefault();
                    if (head == null) return SimResult.Fail("Ship queue is empty");
                    head.NextDoneAtTick = state.Tick;
                    return SimResult.Success;
                }
                case ShopEffect.FinishResearch:
                {
                    var rq = state.ResearchQueue.FirstOrDefault();
                    if (rq == null) return SimResult.Fail("Nothing is being researched");
                    rq.EndsAtTick = state.Tick;
                    return SimResult.Success;
                }
                case ShopEffect.FinishAny:
                    return FinishMostImminent(state);
                case ShopEffect.ProdBoost:
                    state.Buffs.ProdBoostUntilTick =
                        System.Math.Max(state.Tick, state.Buffs.ProdBoostUntilTick) + Balance.ProdBoostDurationSec;
                    return SimResult.Success;
                case ShopEffect.ExtraBuildSlot:
                    state.Buffs.ExtraBuildSlotUntilTick =
                        System.Math.Max(state.Tick, state.Buffs.ExtraBuildSlotUntilTick) + Balance.ExtraBuildSlotDurationSec;
                    return SimResult.Success;
                case ShopEffect.EnergyBoost:
                    state.Buffs.EnergyBoostUntilTick =
                        System.Math.Max(state.Tick, state.Buffs.EnergyBoostUntilTick) + Balance.EnergyBoostDurationSec;
                    return SimResult.Success;
                case ShopEffect.ExtraResearchSlot:
                    state.Buffs.ExtraResearchSlotUntilTick =
                        System.Math.Max(state.Tick, state.Buffs.ExtraResearchSlotUntilTick) + Balance.ExtraResearchSlotDurationSec;
                    return SimResult.Success;
                case ShopEffect.RelocateRandom:
                    return MarchSystem.RelocateHomeRandom(state);
                case ShopEffect.PlanetShield:
                    // Usable ANY TIME (user spec) — stacks by extending, like other buffs.
                    state.Buffs.ShieldUntilTick =
                        System.Math.Max(state.Tick, state.Buffs.ShieldUntilTick) + (item.ShieldSec ?? 0);
                    return SimResult.Success;
                case ShopEffect.RerollPlanetLook:
                {
                    // Deterministic-but-unpredictable: hashed from the current state,
                    // so the sim stays replay-pure while every roll feels random.
                    // One-way by design (the item description says so) — the old
                    // offset is gone the moment this lands.
                    unchecked
                    {
                        uint h = (uint)state.VisualSeedOffset * 0x9E3779B1u
                               ^ (uint)state.Tick * 0x85EBCA6Bu
                               ^ (uint)state.Seed * 0xC2B2AE35u;
                        h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12;
                        int fresh = (int)h;
                        if (fresh == state.VisualSeedOffset) fresh++; // always visibly new
                        state.VisualSeedOffset = fresh;
                    }
                    return SimResult.Success;
                }
                // RelocateTarget is applied from the map targeting flow, not here.
                default:
                    return SimResult.Fail("Item has no effect");
            }
        }
    }
}
