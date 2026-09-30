// Pirate Lord lairs (late-game content, 2026-09-30). A lair is a pirate camp
// node with a named lord's fleet in it: it rides the camp plumbing (attack,
// forecast, spy, loot) and everything that makes it a lair is in its id,
// "lair-{lord}-{tier}-{budget}", so the garrison never changes under a fleet
// already on its way and the save needs nothing new for it. Tier 0 is the
// campaign's showdown; a beaten lord comes back every few days a tier higher
// (a rematch), so the late game always has a lord to hunt.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Map;

namespace GalaxyRoyale.Sim.Systems
{
    public static class LairSystem
    {
        public const string IdPrefix = "lair-";
        public const int RematchEverySec = 4 * 24 * 3600;
        const int MinDist = 45, MaxDist = 95;

        public static bool IsLair(MapNode? node) =>
            node != null && node.Id.StartsWith(IdPrefix, StringComparison.Ordinal);

        /// <summary>(lord, tier, budget) from a lair's id.</summary>
        public static (int lord, int tier, long budget) Parse(MapNode node)
        {
            var parts = node.Id.Split('-');
            return (int.Parse(parts[1]), int.Parse(parts[2]), long.Parse(parts[3]));
        }

        public static PirateLordDef LordOf(MapNode node) => PirateLords.Def(Parse(node).lord);

        /// <summary>The lord's fleet: their doctrine's mix, spent to the lair's budget.</summary>
        public static Dictionary<HullId, int> Garrison(MapNode node)
        {
            var (lord, _, budget) = Parse(node);
            return Garrison(lord, budget);
        }

        public static Dictionary<HullId, int> Garrison(int lord, long budget)
        {
            var g = new Dictionary<HullId, int>();
            foreach (var kv in PirateLords.Def(lord).Fleet)
                g[kv.Key] = (int)Math.Max(1, Math.Round(budget * kv.Value / Ships.Defs[kv.Key].Cost.Total));
            return g;
        }

        /// <summary>What a lair's fleet costs (whole units). Sized by battle, not by
        /// price (a swarm of cheap hulls fights far above its cost): the smallest of
        /// the lord's fleets that would beat the colony's whole war fleet today, with
        /// its research, then a margin that grows down the roster and with each
        /// rematch. The lair keeps that fleet while the colony grows: it's the
        /// chapter's goal to build up to. A floor keeps a colony with no warships
        /// from finding an empty lair.</summary>
        public static long Budget(GameState s, int lord, int tier)
        {
            var fleet = new Dictionary<HullId, int>();
            void Add(Dictionary<HullId, int> ships)
            {
                foreach (var kv in ships)
                    if (kv.Value > 0 && ResourceSystem.IsWarship(kv.Key))
                        fleet[kv.Key] = (fleet.TryGetValue(kv.Key, out var n) ? n : 0) + kv.Value;
            }
            Add(s.Ships);
            foreach (var m in s.Marches) Add(m.Ships);
            long floor = 4000L * (1 + lord) * (1 + lord) * (1 + tier);
            double margin = Math.Min(2.0, 1.15 + 0.03 * lord + 0.1 * tier);
            if (fleet.Count == 0) return floor;
            var mods = ResearchSystem.CombatMods(s);
            bool Holds(long budget) =>
                Combat.BattleForecast.Predict(fleet, Garrison(lord, budget), mods).Winner != Combat.BattleWinner.Attacker;
            // Double up to a budget that holds, then halve the gap down to it.
            long lo = Math.Max(1000, floor / 4), hi = lo;
            int guard = 0;
            while (!Holds(hi) && guard++ < 40) { lo = hi; hi *= 2; }
            for (int i = 0; i < 10 && hi - lo > hi / 50; i++)
            {
                long mid = (lo + hi) / 2;
                if (Holds(mid)) hi = mid; else lo = mid;
            }
            return Math.Max(floor, (long)(hi * margin));
        }

        public static MapNode Spawn(GameState s, int lord, int tier, SimEventBus? events)
        {
            long budget = Budget(s, lord, tier);
            var node = new MapNode
            {
                Id = $"{IdPrefix}{lord}-{tier}-{budget}",
                Kind = NodeKind.Camp,
                Tile = EventSites.PlaceNear(s, 7000 + lord * 31 + tier * 7, 5, MinDist + lord * 3, MaxDist + lord * 3, needFree: true),
                Tier = 0,
                CampLevel = 5,
            };
            s.Map.DynamicNodes.Add(node);
            events?.Emit(new NodeRespawned(node.Id));
            return node;
        }

        public static void Remove(GameState s, string id, SimEventBus? events)
        {
            if (id.Length == 0) return;
            s.Map.DynamicNodes.RemoveAll(n => n.Id == id);
            s.Map.NodeOverrides.Remove(id);
            events?.Emit(new NodeDepleted(id));
        }

        public static MapNode? Find(GameState s, string id) =>
            id.Length == 0 ? null : s.Map.DynamicNodes.Find(n => n.Id == id);

        public static int Wins(GameState s, int lord) => s.Campaign.LordWins.TryGetValue(lord, out var n) ? n : 0;

        /// <summary>Lords beaten at least once.</summary>
        public static int LordsBeaten(GameState s)
        {
            int n = 0;
            foreach (var kv in s.Campaign.LordWins) if (kv.Value > 0) n++;
            return n;
        }

        /// <summary>What beating a lord pays: hours of the colony's mine output and Dark
        /// Matter, more for the first time (which also brings a relic home).</summary>
        public static (ResourceBag payMilli, int darkMatter) Reward(GameState s, int lord, int tier)
        {
            int hours = tier == 0 ? 10 + 2 * lord : 6 + 2 * Math.Min(tier, 6);
            var hourly = ResourceSystem.MineOutputPerHour(s);
            var pay = new ResourceBag(hourly.Gold * hours, hourly.Quartz * hours, hourly.Helium * hours);
            int dm = tier == 0 ? 100 + 25 * lord : 50 + 10 * Math.Min(tier, 10);
            float moon = TwistSystem.LordRewardMult(s); // Hunter's Moon
            if (moon != 1f) { pay = new ResourceBag((long)(pay.Gold * moon), (long)(pay.Quartz * moon), (long)(pay.Helium * moon)); dm = (int)(dm * moon); }
            return (pay, dm);
        }

        /// <summary>A fleet beat the lair (MarchSystem, after the battle report is written).</summary>
        public static void OnDefeated(GameState s, MapNode node, SimEventBus events)
        {
            var (lord, tier, _) = Parse(node);
            var c = s.Campaign;
            c.LordWins[lord] = Wins(s, lord) + 1;
            var (pay, dm) = Reward(s, lord, tier);
            ResourceSystem.Add(s, pay);
            s.Premium.DarkMatter += dm;
            RelicKind? relic = null;
            if (tier == 0)
            {
                var kind = (RelicKind)(lord % Relics.All.Count);
                s.Relics[kind] = RelicSystem.Count(s, kind) + 1;
                relic = kind;
            }
            if (node.Id == c.LairId) c.LairId = "";
            if (node.Id == c.RematchId) c.RematchId = "";
            Remove(s, node.Id, events);
            if (c.NextRematchTick == 0) c.NextRematchTick = s.Tick + RematchEverySec;
            events.Emit(new LordDefeated(lord, tier, pay, dm, relic));
        }

        /// <summary>Once a minute: a beaten lord rises again when their time comes.</summary>
        public static void Tick(GameState s, SimEventBus events)
        {
            var c = s.Campaign;
            if (c.NextRematchTick == 0 || s.Tick < c.NextRematchTick || c.RematchId.Length > 0) return;
            // The next beaten lord in turn who isn't holding the campaign's lair right now.
            int chapterLord = c.LairId.Length > 0 && Find(s, c.LairId) is { } held ? Parse(held).lord : -1;
            for (int step = 0; step < PirateLords.All.Count; step++)
            {
                int lord = (c.RematchCursor + step) % PirateLords.All.Count;
                if (Wins(s, lord) == 0 || lord == chapterLord) continue;
                c.RematchCursor = lord + 1;
                var node = Spawn(s, lord, Wins(s, lord), events);
                c.RematchId = node.Id;
                c.NextRematchTick = s.Tick + RematchEverySec;
                events.Emit(new LordReturns(lord, Wins(s, lord), node.Tile));
                return;
            }
            c.NextRematchTick = s.Tick + RematchEverySec;
        }
    }
}
