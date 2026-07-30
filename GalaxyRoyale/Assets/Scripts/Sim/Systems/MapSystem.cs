// Keeps the sector alive: depleted resource tiles + cleared pirate camps disappear,
// then — after Balance.NodeRespawnSec ticks — respawn as fresh nodes at a NEARBY
// empty tile (so the map layout keeps shifting). If no nearby tile is free the
// node refreshes in place. Runs every tick, so offline catch-up works too.
//
// Direct port of v1's `src/sim/systems/MapSystem.ts`.
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Map;

namespace GalaxyRoyale.Sim.Systems
{
    public static class MapSystem
    {
        public static void Tick(GameState state, SimEventBus events)
        {
            // Snapshot the keys because Relocate may mutate the dictionary.
            var ids = new List<string>(state.Map.NodeOverrides.Keys);
            foreach (var id in ids)
            {
                if (!state.Map.NodeOverrides.TryGetValue(id, out var ov)) continue;
                if (ov.Retired) continue;
                bool depleted = ov.Cleared || (ov.Remaining.HasValue && ov.Remaining.Value <= 0);

                if (ov.RespawnAtTick == 0)
                {
                    if (depleted) ov.RespawnAtTick = state.Tick + Balance.NodeRespawnSec;
                }
                else if (state.Tick >= ov.RespawnAtTick)
                {
                    Relocate(state, id, events);
                }
            }
        }

        /// <summary>Retire the depleted node; spawn a fresh copy at a nearby empty tile if one exists.</summary>
        static void Relocate(GameState state, string id, SimEventBus events)
        {
            var src = MapLookup.NodeById(state, id);
            if (src == null)
            {
                state.Map.NodeOverrides.Remove(id);
                return;
            }
            var tile = MapLookup.NearbyFreeTile(state, src.Tile);
            if (tile is null)
            {
                // nowhere nearby — refresh the node in place
                state.Map.NodeOverrides.Remove(id);
                if (!id.StartsWith("dyn-", System.StringComparison.Ordinal))
                    events.Emit(new NodeRespawned(id));
                return;
            }

            var fresh = new MapNode
            {
                Id = $"dyn-{state.Map.NextDynId++}",
                Kind = src.Kind,
                Tile = tile.Value,
                Tier = src.Tier,
                Amount = src.Amount,
                RatePerSec = src.RatePerSec,
                Resource = src.Resource,
                CampLevel = src.CampLevel,
            };
            state.Map.DynamicNodes.Add(fresh);

            // remove the source: dynamic nodes vanish; seed nodes retire permanently
            if (id.StartsWith("dyn-", System.StringComparison.Ordinal))
            {
                state.Map.DynamicNodes.RemoveAll(n => n.Id == id);
                state.Map.NodeOverrides.Remove(id);
            }
            else
            {
                state.Map.NodeOverrides[id] = new NodeOverride { Cleared = true, Retired = true };
            }
            events.Emit(new NodeRespawned(fresh.Id));
        }
    }
}
