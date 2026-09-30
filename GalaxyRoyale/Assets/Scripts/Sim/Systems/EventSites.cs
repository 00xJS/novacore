// Map events (2026-09-30): what the Comet Pass, the Trade Caravan, the Ion
// Storm and the Supernova Warning put on the map. Everything is a pure function
// of the galaxy seed, the event's instance number and the colony's home, so
// offline catch-up and a reloaded save agree on where things are.
//   - Comet Pass: one comet node ("evt-comet-N") near the colony for the whole
//     event. Gathering it hauls all three resources plus Dark Matter; rival
//     commanders mine it too, so its stock drains every hour.
//   - Trade Caravan: a caravan node that docks at CaravanStops waystations in
//     turn ("evt-caravan-N-K"). ATTACK intercepts it (a battle against its
//     escort, loot on a win); GATHER escorts it: the fleet stays with it and is
//     paid, straight home, when the caravan moves on.
//   - Ion Storm and Supernova: zones (a disc) rather than nodes. The storm
//     slows fleets and blinds the radar; the doomed sector's worlds gather
//     faster and pay more, and when the star goes off every fleet still in it
//     is lost and its worlds are stripped.
// Event nodes carry the "evt-" id prefix; MapSystem leaves them alone.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Map;

namespace GalaxyRoyale.Sim.Systems
{
    public static class EventSites
    {
        public const string IdPrefix = "evt-";

        public readonly struct Zone
        {
            public readonly GalaxyEventKind Kind;
            public readonly TileXY Centre;
            public readonly int Radius;
            public readonly int EndTick;
            public Zone(GalaxyEventKind kind, TileXY centre, int radius, int endTick)
            {
                Kind = kind; Centre = centre; Radius = radius; EndTick = endTick;
            }
            public bool Contains(TileXY t) => TileXY.Distance(t, Centre) <= Radius;
        }

        // ---------- where things are ----------

        static double Hash(GameState s, int instance, int salt) =>
            Rng.Hash2d(unchecked((uint)s.Seed ^ 0xE7E57u), instance * 131 + salt, salt * 7 + 3);

        /// <summary>A spot between <paramref name="minD"/> and <paramref name="maxD"/> tiles
        /// from home, inside the galaxy, outside the Core Zone, preferring a free tile.</summary>
        static TileXY PlaceNear(GameState s, int instance, int salt, int minD, int maxD, bool needFree)
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                double a = Hash(s, instance, salt + attempt * 17) * Math.PI * 2;
                double d = minD + Hash(s, instance, salt + attempt * 17 + 1) * (maxD - minD);
                var t = new TileXY((int)Math.Round(s.HomeTile.X + Math.Cos(a) * d),
                                   (int)Math.Round(s.HomeTile.Y + Math.Sin(a) * d));
                if (!Balance.InGalaxy(t) || Balance.InCoreZone(t)) continue;
                if (!needFree) return t;
                if (MapLookup.NodeAt(s, t) == null) return t;
                if (MapLookup.NearbyFreeTile(s, t) is { } free) return free;
            }
            return s.HomeTile.X < Balance.SectorSize / 2
                ? new TileXY(s.HomeTile.X + minD, s.HomeTile.Y)
                : new TileXY(s.HomeTile.X - minD, s.HomeTile.Y);
        }

        /// <summary>The storm or doomed sector live at <paramref name="tick"/>, if any.</summary>
        public static Zone? ZoneAt(GameState s, int tick)
        {
            var live = EventSystem.Current(tick);
            return live.Def.Kind switch
            {
                GalaxyEventKind.IonStorm => StormZone(s, live.Instance, live.EndTick),
                GalaxyEventKind.Supernova => NovaZone(s, live.Instance, live.EndTick),
                _ => null,
            };
        }

        public static Zone? ZoneNow(GameState s) => ZoneAt(s, s.Tick);

        static Zone StormZone(GameState s, int instance, int endTick) =>
            new(GalaxyEventKind.IonStorm,
                PlaceNear(s, instance, 1, GalaxyEvents.StormOffset / 2, GalaxyEvents.StormOffset, needFree: false),
                GalaxyEvents.StormRadius, endTick);

        static Zone NovaZone(GameState s, int instance, int endTick) =>
            new(GalaxyEventKind.Supernova,
                PlaceNear(s, instance, 2, GalaxyEvents.NovaMinDist, GalaxyEvents.NovaMaxDist, needFree: false),
                GalaxyEvents.NovaRadius, endTick);

        public static bool InStorm(GameState s, TileXY t, int tick) =>
            ZoneAt(s, tick) is { Kind: GalaxyEventKind.IonStorm } z && z.Contains(t);

        public static bool InNova(GameState s, TileXY t, int tick) =>
            ZoneAt(s, tick) is { Kind: GalaxyEventKind.Supernova } z && z.Contains(t);

        /// <summary>A flight between these two tiles runs at this share of its speed (the Ion Storm).</summary>
        public static float SpeedMult(GameState s, TileXY from, TileXY to) =>
            ZoneNow(s) is { Kind: GalaxyEventKind.IonStorm } z && (z.Contains(from) || z.Contains(to))
                ? GalaxyEvents.StormSpeed : 1f;

        /// <summary>The radar can't see raids on a colony inside the storm.</summary>
        public static bool RadarBlind(GameState s) => InStorm(s, s.HomeTile, s.Tick);

        /// <summary>Where the live map event is (its node's tile, or its zone's centre) for
        /// SHOW ON MAP, and the node if there is one.</summary>
        public static (TileXY tile, MapNode? node)? Focus(GameState s)
        {
            if (s.EventSiteId.Length > 0 && s.Map.DynamicNodes.Find(n => n.Id == s.EventSiteId) is { } node
                && MapLookup.IsLive(s, node))
                return (node.Tile, node);
            if (ZoneNow(s) is { } z) return (z.Centre, null);
            return null;
        }

        // ---------- the nodes ----------

        /// <summary>The event node that belongs on the map at <paramref name="tick"/> (null = none).</summary>
        public static MapNode? SiteAt(GameState s, int tick)
        {
            var live = EventSystem.Current(tick);
            int cc = s.Buildings[BuildingId.CommandCenter].Level;
            switch (live.Def.Kind)
            {
                case GalaxyEventKind.CometPass:
                {
                    int amount = (int)Math.Min(int.MaxValue / 2, CometSize(s) * 1000);
                    return new MapNode
                    {
                        Id = $"{IdPrefix}comet-{live.Instance}", Kind = NodeKind.Comet,
                        Tile = PlaceNear(s, live.Instance, 3, GalaxyEvents.CometMinDist, GalaxyEvents.CometMaxDist, needFree: true),
                        Tier = 0, Amount = amount, RatePerSec = 12_000, Resource = null,
                    };
                }
                case GalaxyEventKind.TradeCaravan:
                {
                    int stop = Math.Min(GalaxyEvents.CaravanStops - 1, (tick - live.StartTick) / GalaxyEvents.CaravanStopSec);
                    return new MapNode
                    {
                        Id = $"{IdPrefix}caravan-{live.Instance}-{stop}", Kind = NodeKind.Caravan,
                        Tile = PlaceNear(s, live.Instance, 10 + stop * 5, GalaxyEvents.CaravanMinDist, GalaxyEvents.CaravanMaxDist, needFree: true),
                        Tier = 0, Amount = 0, RatePerSec = 0, Resource = null, CampLevel = CaravanLevel(cc),
                    };
                }
                default:
                    return null;
            }
        }

        /// <summary>Seconds until the live site leaves (the comet at the event's end, the
        /// caravan at its stop's end).</summary>
        public static int SiteLeftSec(GameState s)
        {
            var live = EventSystem.Current(s.Tick);
            if (live.Def.Kind != GalaxyEventKind.TradeCaravan) return Math.Max(0, live.EndTick - s.Tick);
            int into = (s.Tick - live.StartTick) % GalaxyEvents.CaravanStopSec;
            return GalaxyEvents.CaravanStopSec - into;
        }

        /// <summary>The comet's stock (whole units): CometHoursOfOutput of the colony's mines.</summary>
        public static long CometSize(GameState s)
        {
            long hourly = ResourceSystem.GetRates(s).Total / 1000;
            return Math.Max(20_000, (long)(hourly * GalaxyEvents.CometHoursOfOutput));
        }

        /// <summary>The caravan's escort: a camp garrison level that rises with the colony.</summary>
        public static int CaravanLevel(int cc) => Math.Clamp(1 + cc / 3, 1, 5);

        /// <summary>The caravan's escort fleet: its camp level's garrison, half again.</summary>
        public static Dictionary<HullId, int> CaravanEscort(MapNode node)
        {
            var g = new Dictionary<HullId, int>();
            if (Nodes.CampTemplates.TryGetValue(node.CampLevel, out var t))
                foreach (var kv in t) g[kv.Key] = kv.Value * 3 / 2;
            return g;
        }

        /// <summary>An intercepted caravan's cargo (milli): three camp stockpiles.</summary>
        public static ResourceBag CaravanLoot(GameState s, MapNode node)
        {
            long stock = Nodes.CampStockpile(node.CampLevel, s.Buildings[BuildingId.CommandCenter].Level) * 3 * 1000;
            return new ResourceBag(stock * 40 / 100, stock * 35 / 100, stock * 25 / 100);
        }

        /// <summary>What escorting the caravan pays (milli), by the might of the warships
        /// flying with it: full pay from 25 × CC² might (the Home Guard mark).</summary>
        public static (ResourceBag pay, int darkMatter) EscortFee(GameState s, MapNode node, Dictionary<HullId, int> ships)
        {
            long invested = 0;
            foreach (var kv in ships)
                if (ResourceSystem.IsWarship(kv.Key)) invested += (long)kv.Value * Ships.Defs[kv.Key].Cost.Total;
            int cc = s.Buildings[BuildingId.CommandCenter].Level;
            double share = Math.Min(1.0, invested / 10.0 / Balance.HomeGuardFullMight(cc));
            long full = Nodes.CampStockpile(node.CampLevel, cc) * 2 * 1000;
            long pay = (long)(full * share);
            return (new ResourceBag(pay * 40 / 100, pay * 35 / 100, pay * 25 / 100), (int)Math.Round(20 * share));
        }

        // ---------- tick ----------

        /// <summary>Per sim tick: put the live event's node on the map (and take the
        /// last one off), drain the comet, pay the caravan's escorts, set off the star.</summary>
        public static void Tick(GameState s, SimEventBus? events)
        {
            var live = EventSystem.Current(s.Tick);
            // Only when the event or the caravan's stop changes (cheap: two ints).
            int stop = live.Def.Kind == GalaxyEventKind.TradeCaravan
                ? (s.Tick - live.StartTick) / GalaxyEvents.CaravanStopSec : 0;
            if (live.Instance != s.SiteCheckedInstance || stop != s.SiteCheckedStop)
            {
                s.SiteCheckedInstance = live.Instance;
                s.SiteCheckedStop = stop;
                var site = SiteAt(s, s.Tick);
                string want = site?.Id ?? "";
                if (want != s.EventSiteId)
                {
                    if (s.EventSiteId.Length > 0)
                    {
                        Remove(s, s.EventSiteId, events);
                        events?.Emit(new NodeDepleted(s.EventSiteId)); // the map redraws
                    }
                    if (site != null)
                    {
                        s.Map.DynamicNodes.Add(site);
                        events?.Emit(new NodeRespawned(site.Id));
                    }
                    s.EventSiteId = want;
                }
            }
            if (live.Def.Kind == GalaxyEventKind.CometPass && s.Tick % 3600 == 0 && s.EventSiteId.Length > 0
                && s.Map.DynamicNodes.Find(n => n.Id == s.EventSiteId) is { } comet)
                DrainComet(s, comet);

            // The star goes off when its event ends.
            if (live.Def.Kind == GalaxyEventKind.Supernova) s.PendingNova = live.Instance;
            else if (s.PendingNova >= 0)
            {
                int instance = s.PendingNova;
                s.PendingNova = -1;
                Detonate(s, NovaZone(s, instance, s.Tick), events);
            }
        }

        static void DrainComet(GameState s, MapNode comet)
        {
            s.Map.NodeOverrides.TryGetValue(comet.Id, out var ov);
            int remaining = ov?.Remaining ?? comet.Amount;
            if (remaining <= 0) return;
            int take = (int)(comet.Amount * GalaxyEvents.CometRivalDrainPerHour);
            MarchSystem.UpsertOverride(s, comet.Id, o => o.Remaining = Math.Max(0, remaining - take));
        }

        /// <summary>Take an event node off the map. A caravan moving on pays its escorts
        /// and sends them home; fleets headed for a vanished site turn around.</summary>
        static void Remove(GameState s, string id, SimEventBus? events)
        {
            var node = s.Map.DynamicNodes.Find(n => n.Id == id);
            if (node != null)
            {
                foreach (var march in s.Marches.ToArray())
                {
                    if (!march.Node.Equals(node.Tile) || march.Phase == MarchPhase.Returning) continue;
                    if (node.Kind == NodeKind.Caravan && march.Mission == MarchMission.Gather
                        && march.Phase == MarchPhase.Gathering)
                    {
                        var (pay, dm) = EscortFee(s, node, march.Ships);
                        ResourceSystem.Add(s, pay);
                        s.Premium.DarkMatter += dm;
                        s.Stats.CaravansDone++;
                        events?.Emit(new CaravanEscorted(pay, dm));
                    }
                    else if (node.Kind == NodeKind.Comet && march.Phase == MarchPhase.Gathering)
                    {
                        MarchSystem.SettleGather(s, march); // what it mined so far rides home
                    }
                    MarchSystem.ReturnHome(s, march, s.Tick);
                }
                s.Map.DynamicNodes.Remove(node);
            }
            s.Map.NodeOverrides.Remove(id);
        }

        /// <summary>The Supernova: fleets in the doomed sector are lost, its worlds stripped.</summary>
        static void Detonate(GameState s, Zone zone, SimEventBus? events)
        {
            int lost = 0;
            foreach (var march in s.Marches.ToArray())
            {
                if (march.Phase == MarchPhase.Returning) continue;
                if (!zone.Contains(march.Node)) continue;
                foreach (var kv in march.Ships) lost += kv.Value;
                s.Marches.Remove(march);
            }
            foreach (var node in MapLookup.AllNodes(s))
            {
                if (!zone.Contains(node.Tile) || node.Kind == NodeKind.Camp) continue;
                if (node.Id.StartsWith(IdPrefix, StringComparison.Ordinal)) continue;
                if (node.Resource == null && node.Kind != NodeKind.DMField && node.Kind != NodeKind.Derelict) continue;
                MarchSystem.UpsertOverride(s, node.Id, o => o.Remaining = 0);
            }
            events?.Emit(new SupernovaDetonated(zone.Centre, lost));
        }
    }
}
