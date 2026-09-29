// Fleet dispatch, mid-flight state, arrival resolution (gather/attack/spy),
// recall math, home relocation, and mailbox delivery.
// Direct port of v1's `src/sim/systems/MarchSystem.ts`.
//
// Node/tile lookup helpers live in Sim.Map.MapLookup so this file focuses on
// march-lifecycle logic, not the sector cache.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Map;

namespace GalaxyRoyale.Sim.Systems
{
    public static class MarchSystem
    {
        const int MailboxCap = 50;

        // ---------- fleet math ----------

        /// <summary>Slowest hull's Speed (tiles per minute). MaxValue when the comp is empty.</summary>
        public static int FleetSpeed(Dictionary<HullId, int> ships)
        {
            int slowest = int.MaxValue;
            foreach (var hull in Ships.All)
                if (ships.TryGetValue(hull, out var c) && c > 0)
                    slowest = Math.Min(slowest, Ships.Defs[hull].Speed);
            return slowest;
        }

        public static int FleetFuelPerTile(Dictionary<HullId, int> ships)
        {
            int fuel = 0;
            foreach (var hull in Ships.All)
                if (ships.TryGetValue(hull, out var c)) fuel += c * Ships.Defs[hull].FuelPerTile;
            return fuel;
        }

        /// <summary>Total cargo capacity in milli-units.</summary>
        public static long FleetCargoCap(Dictionary<HullId, int> ships)
        {
            long cap = 0;
            foreach (var hull in Ships.All)
                if (ships.TryGetValue(hull, out var c)) cap += (long)c * Ships.Defs[hull].Cargo;
            return cap;
        }

        /// <summary>Research-adjusted march speed (probe-only fleets also get the radar bonus).</summary>
        public static double EffSpeed(GameState state, Dictionary<HullId, int> ships) =>
            FleetSpeed(ships) * ResearchSystem.MarchSpeedMult(state) * RadarSystem.FleetSpeedMult(state, ships);

        /// <summary>Research-adjusted cargo capacity, milli-units.</summary>
        public static long EffCargoCap(GameState state, Dictionary<HullId, int> ships) =>
            (long)Math.Floor(FleetCargoCap(ships) * ResearchSystem.CargoMult(state));

        public static int FleetCount(Dictionary<HullId, int> ships)
        {
            int n = 0;
            foreach (var v in ships.Values) n += v;
            return n;
        }

        /// <summary>Cargo space a march still has (milli) — capacity minus what it already carries.</summary>
        public static long FreeCargo(GameState state, March march) =>
            Math.Max(0, EffCargoCap(state, march.Ships) - march.Cargo.Total - march.CargoDm);

        /// <summary>
        /// Load resources from the stockpile onto a march at launch (the fleet-deploy
        /// cargo screen). Only allowed the same tick the march departed — cargo rides
        /// out with the fleet (and is lost with it if the fleet is wiped).
        /// </summary>
        public static SimResult LoadCargo(GameState state, int marchId, ResourceBag milli)
        {
            var march = state.Marches.Find(m => m.Id == marchId);
            if (march == null) return SimResult.Fail("No such fleet");
            if (march.Phase != MarchPhase.Outbound || march.DepartedAtTick != state.Tick)
                return SimResult.Fail("Cargo can only be loaded at launch");
            if (milli.Gold < 0 || milli.Quartz < 0 || milli.Helium < 0)
                return SimResult.Fail("Invalid cargo");
            if (milli.Total > FreeCargo(state, march))
                return SimResult.Fail("Not enough cargo space");
            if (!ResourceSystem.CanAfford(state, milli))
                return SimResult.Fail("Not enough resources");
            var paid = ResourceSystem.Spend(state, milli);
            if (!paid.Ok) return paid;
            march.Cargo.Gold += milli.Gold;
            march.Cargo.Quartz += milli.Quartz;
            march.Cargo.Helium += milli.Helium;
            return SimResult.Success;
        }

        // ---------- camp helpers ----------

        public static Dictionary<HullId, int> CampGarrison(MapNode node)
        {
            var copy = new Dictionary<HullId, int>();
            if (Nodes.CampTemplates.TryGetValue(node.CampLevel, out var tmpl))
                foreach (var kv in tmpl) copy[kv.Key] = kv.Value;
            return copy;
        }

        /// <summary>True once a spy report for this tile's camp garrison has landed in the mailbox.</summary>
        public static bool HasSpyIntel(GameState state, TileXY target)
        {
            foreach (var m in state.Mailbox)
            {
                if (m is SpyReport r
                    && r.Target.X == target.X && r.Target.Y == target.Y
                    && r.Intel.Kind == NodeKind.Camp
                    && r.Intel.Garrison != null)
                    return true;
            }
            return false;
        }

        /// <summary>Camp loot: garrison build cost × loot factor, same resource mix.</summary>
        public static ResourceBag CampLoot(MapNode node)
        {
            var garrison = CampGarrison(node);
            var loot = new ResourceBag();
            foreach (var hull in Ships.All)
            {
                int count = garrison.TryGetValue(hull, out var c) ? c : 0;
                if (count == 0) continue;
                var cost = Ships.Defs[hull].Cost;
                loot.Gold   += (int)Math.Round(cost.Gold   * count * Nodes.CampLootFactor) * 1000;
                loot.Quartz += (int)Math.Round(cost.Quartz * count * Nodes.CampLootFactor) * 1000;
                loot.Helium     += (int)Math.Round(cost.Helium     * count * Nodes.CampLootFactor) * 1000;
            }
            return loot;
        }

        // ---------- home relocation ----------

        /// <summary>Deterministically pick a blank tile (seed+tick), scanning with a coprime stride.</summary>
        public static TileXY? RandomBlankTile(GameState state)
        {
            var sector = MapLookup.GetSector(state);
            int size = sector.Size;
            int n = size * size;
            uint seed = unchecked((uint)state.Seed) ^ 0x9e37u;
            int start = (int)Math.Floor(Rng.Hash2d(seed, state.Tick, state.HomeTile.X + 1) * n);
            for (int i = 0; i < n; i++)
            {
                int idx = (start + i * 104729) % n; // 104729 prime → coprime with 2^k, covers all tiles
                var tile = new TileXY(idx % size, idx / size);
                if (InCoreExclusion(tile)) continue; // never land a Blind Jump in the core
                if (MapLookup.IsBlankTile(state, tile)) return tile;
            }
            return null;
        }

        /// <summary>Inside the 100×100 forbidden square around the supernova core?</summary>
        public static bool InCoreExclusion(TileXY tile)
        {
            int c = Balance.SectorSize / 2;
            return Math.Abs(tile.X - c) <= Balance.CoreExclusionHalf
                && Math.Abs(tile.Y - c) <= Balance.CoreExclusionHalf;
        }

        public static SimResult RelocateHome(GameState state, TileXY tile)
        {
            // User rule (2026-07-05): no jumping while ANY fleet is in flight —
            // mid-jump the outbound lines pointed at empty space and read as a glitch.
            if (state.Marches.Count > 0)
                return SimResult.Fail("Recall all fleets before relocating");
            if (InCoreExclusion(tile))
                return SimResult.Fail("The supernova core is forbidden space");
            if (!MapLookup.IsBlankTile(state, tile)) return SimResult.Fail("Pick an empty tile");
            state.HomeTile = new TileXY(tile.X, tile.Y);
            return SimResult.Success;
        }

        public static SimResult RelocateHomeRandom(GameState state)
        {
            var tile = RandomBlankTile(state);
            if (tile is null) return SimResult.Fail("No empty space found");
            return RelocateHome(state, tile.Value);
        }

        // ---------- march planning ----------

        public sealed class MarchPreview
        {
            public bool Ok = true;
            public string? Reason;
            public int TravelSec;
            /// <summary>Milli-helium.</summary>
            public int HeliumCost;
            /// <summary>Milli-units.</summary>
            public long CargoCap;
            public double Distance;
        }

        public static MarchPreview PreviewMarch(GameState state, Dictionary<HullId, int> ships, TileXY target)
        {
            double dist = TileXY.Distance(state.HomeTile, target);
            int rawSpeed = FleetSpeed(ships);
            double speed = EffSpeed(state, ships);
            double rawHelium = Balance.HeliumCostMilli(dist, FleetFuelPerTile(ships));
            var preview = new MarchPreview
            {
                Ok = true,
                TravelSec = rawSpeed == int.MaxValue ? 0 : Balance.TravelSeconds(dist, (int)speed),
                HeliumCost = (int)Math.Ceiling(rawHelium * ResearchSystem.HeliumMult(state)),
                CargoCap = EffCargoCap(state, ships),
                Distance = dist,
            };
            if (FleetCount(ships) < 1) { preview.Ok = false; preview.Reason = "No ships selected"; return preview; }
            foreach (var hull in Ships.All)
            {
                int n = ships.TryGetValue(hull, out var c) ? c : 0;
                if (n < 0) { preview.Ok = false; preview.Reason = "Invalid fleet"; return preview; }
                int docked = state.Ships.TryGetValue(hull, out var d) ? d : 0;
                if (n > docked) { preview.Ok = false; preview.Reason = $"Not enough {Ships.Defs[hull].Name}s docked"; return preview; }
            }
            if (state.Resources.Helium < preview.HeliumCost) { preview.Ok = false; preview.Reason = "Not enough Helium"; return preview; }
            return preview;
        }

        public static SimResult SendMarch(
            GameState state,
            Dictionary<HullId, int> ships,
            TileXY target,
            MarchMission mission,
            out int marchId)
        {
            marchId = 0;
            var node = MapLookup.NodeAt(state, target);
            if (node == null) return SimResult.Fail("Nothing to do there");
            state.Map.NodeOverrides.TryGetValue(node.Id, out var overrideForNode);

            if (mission != MarchMission.Spy && overrideForNode != null && overrideForNode.Cleared)
                return SimResult.Fail("Already cleared");
            if (mission == MarchMission.Gather)
            {
                if (node.Kind == NodeKind.Camp) return SimResult.Fail("Pirates hold this — attack instead");
                if (node.Kind != NodeKind.Derelict)
                {
                    int remaining = overrideForNode?.Remaining ?? node.Amount;
                    if (remaining <= 0) return SimResult.Fail("Depleted");
                }
            }
            else if (mission == MarchMission.Attack && node.Kind != NodeKind.Camp)
            {
                return SimResult.Fail("Nothing to attack there");
            }

            var preview = PreviewMarch(state, ships, target);
            if (!preview.Ok) return SimResult.Fail(preview.Reason ?? "Cannot launch");

            var paid = ResourceSystem.Spend(state, new ResourceBag(0, 0, preview.HeliumCost));
            if (!paid.Ok) return paid;
            foreach (var hull in Ships.All)
            {
                int take = ships.TryGetValue(hull, out var c) ? c : 0;
                if (take > 0) state.Ships[hull] -= take;
            }

            var march = new March
            {
                Id = state.NextMarchId++,
                Phase = MarchPhase.Outbound,
                Ships = new Dictionary<HullId, int>(ships),
                Node = target,
                LegFrom = state.HomeTile,
                LegTo = target,
                DepartedAtTick = state.Tick,
                ArrivesAtTick = state.Tick + preview.TravelSec,
                Cargo = new ResourceBag(),
                HeliumSpent = preview.HeliumCost,
                Mission = mission,
            };
            state.Marches.Add(march);
            state.Stats.MarchesSent++;
            marchId = march.Id;
            return SimResult.Success;
        }

        /// <summary>
        /// Phase C.4 PvP: march to an arbitrary tile — another player's colony,
        /// which exists only server-side, so there is deliberately NO node
        /// validation here. The battle is resolved by the caller against the
        /// target's published defense snapshot at LAUNCH; the caller then writes
        /// the outcome onto the returned march (survivors + plunder cargo).
        /// Arrival reuses Tick's empty-tile turnaround, so offline catch-up and
        /// recalls work with zero special-casing.
        /// </summary>
        public static SimResult SendRaidMarch(
            GameState state, Dictionary<HullId, int> ships, TileXY target, out int marchId,
            MarchMission mission = MarchMission.Attack)
        {
            marchId = 0;
            if (target.Equals(state.HomeTile)) return SimResult.Fail("That's your own colony");
            if (InCoreExclusion(target)) return SimResult.Fail("The supernova core is forbidden space");

            var preview = PreviewMarch(state, ships, target);
            if (!preview.Ok) return SimResult.Fail(preview.Reason ?? "Cannot launch");

            var paid = ResourceSystem.Spend(state, new ResourceBag(0, 0, preview.HeliumCost));
            if (!paid.Ok) return paid;
            foreach (var hull in Ships.All)
            {
                int take = ships.TryGetValue(hull, out var c) ? c : 0;
                if (take > 0) state.Ships[hull] -= take;
            }

            var march = new March
            {
                Id = state.NextMarchId++,
                Phase = MarchPhase.Outbound,
                Ships = new Dictionary<HullId, int>(ships),
                Node = target,
                LegFrom = state.HomeTile,
                LegTo = target,
                DepartedAtTick = state.Tick,
                ArrivesAtTick = state.Tick + preview.TravelSec,
                Cargo = new ResourceBag(),
                HeliumSpent = preview.HeliumCost,
                // NOTE: player-recon probes fly as Attack (not Spy) so the arrival
                // branch doesn't file a junk "empty space" report at the colony tile.
                Mission = mission,
            };
            state.Marches.Add(march);
            state.Stats.MarchesSent++;
            marchId = march.Id;
            return SimResult.Success;
        }

        // ---------- mid-flight ----------

        /// <summary>Fractional position along the current leg (0..1).</summary>
        public static double LegProgress(March march, int tick)
        {
            int span = march.ArrivesAtTick - march.DepartedAtTick;
            if (span <= 0) return 1;
            return Math.Min(1, Math.Max(0, (tick - march.DepartedAtTick) / (double)span));
        }

        /// <summary>Interpolated sub-tile position along the current leg.</summary>
        public static Position GetPosition(March march, int tick)
        {
            double t = LegProgress(march, tick);
            return new Position(
                march.LegFrom.X + (march.LegTo.X - march.LegFrom.X) * t,
                march.LegFrom.Y + (march.LegTo.Y - march.LegFrom.Y) * t);
        }

        /// <summary>Fractional-tick position for smooth rendering between 1 Hz sim
        /// ticks (visual-only; the sim itself only ever uses integer ticks).</summary>
        public static Position GetPositionSmooth(March march, double preciseTick)
        {
            int span = march.ArrivesAtTick - march.DepartedAtTick;
            double t = span <= 0 ? 1
                : Math.Min(1, Math.Max(0, (preciseTick - march.DepartedAtTick) / span));
            return new Position(
                march.LegFrom.X + (march.LegTo.X - march.LegFrom.X) * t,
                march.LegFrom.Y + (march.LegTo.Y - march.LegFrom.Y) * t);
        }

        /// <summary>
        /// Fired when a march is recalled before finishing its mission (Game layer
        /// listens so a recalled PvP raid/spy drops its pending resolution — a
        /// recalled fleet never lands, so it must not file a battle/intel report).
        /// Static, single-slot: the Game bootstrap owns it; cleared on teardown.
        /// </summary>
        public static System.Action<int>? OnMarchRecalled;

        /// <summary>
        /// Recall a march. Refund covers only helium that will now never burn.
        /// Recalling mid-gather keeps whatever was gathered so far.
        /// </summary>
        public static SimResult RecallMarch(GameState state, int marchId)
        {
            var march = FindMarch(state, marchId);
            if (march == null) return SimResult.Fail("March not found");
            if (march.Phase == MarchPhase.Returning) return SimResult.Fail("Already returning");

            double speed = EffSpeed(state, march.Ships);
            var pos = GetPosition(march, state.Tick);
            double distHome = Position.DistanceToTile(pos, state.HomeTile);
            int returnSec = Balance.TravelSeconds(distHome, (int)speed);

            if (march.Phase == MarchPhase.Outbound)
            {
                // Never reached the target — flag it so no raid/spy resolves against
                // the destination, and refund the gas for the distance it won't fly.
                march.Recalled = true;
                double p = LegProgress(march, state.Tick);
                int refund = (int)Math.Floor(march.HeliumSpent * (1 - p) * Balance.RecallRefundRate);
                if (refund > 0) ResourceSystem.Add(state, new ResourceBag(0, 0, refund));
            }
            else if (march.Phase == MarchPhase.Gathering && march.Mission == MarchMission.Gather)
            {
                // partial harvest: rate × time on station, bounded by cargo + node stock
                var node = MapLookup.NodeAt(state, march.Node);
                if (node != null && node.RatePerSec > 0)
                {
                    state.Map.NodeOverrides.TryGetValue(node.Id, out var ov);
                    int remaining = ov?.Remaining ?? node.Amount;
                    int elapsed = state.Tick - march.DepartedAtTick;
                    long free = FreeCargo(state, march); // pre-loaded launch cargo takes space
                    long harvested = (long)Math.Floor(elapsed * (double)node.RatePerSec * ResearchSystem.GatherRateMult(state));
                    int gathered = (int)Math.Min(Math.Min(remaining, free), harvested);
                    if (gathered > 0 && node.Resource is ResourceId res)
                    {
                        march.Cargo.Set(res, march.Cargo.Get(res) + gathered);
                        UpsertOverride(state, node.Id, o => o.Remaining = remaining - gathered);
                    }
                    else if (gathered > 0 && node.Kind == NodeKind.DMField)
                    {
                        march.CargoDm += gathered; // partial DM harvest on recall (v13)
                        UpsertOverride(state, node.Id, o => o.Remaining = remaining - gathered);
                    }
                }
            }

            march.Phase = MarchPhase.Returning;
            march.LegFrom = pos;
            march.LegTo = state.HomeTile;
            march.DepartedAtTick = state.Tick;
            march.ArrivesAtTick = state.Tick + returnSec;
            OnMarchRecalled?.Invoke(marchId); // drop any pending PvP resolution for it
            return SimResult.Success;
        }

        /// <summary>
        /// Redirect a spy probe mid-flight. Combat fleets can't be redirected.
        /// No extra helium charged — recon drones sip fuel.
        /// </summary>
        public static SimResult RedirectMarch(GameState state, int marchId, TileXY target)
        {
            var march = FindMarch(state, marchId);
            if (march == null) return SimResult.Fail("March not found");
            if (march.Mission != MarchMission.Spy) return SimResult.Fail("Only spy probes can be redirected");
            if (march.Phase == MarchPhase.Gathering) return SimResult.Fail("Probe is busy scanning");

            var pos = GetPosition(march, state.Tick);
            double speed = EffSpeed(state, march.Ships);
            double dist = Position.DistanceToTile(pos, target);

            march.Phase = MarchPhase.Outbound;
            march.Node = target;
            march.LegFrom = pos;
            march.LegTo = target;
            march.DepartedAtTick = state.Tick;
            march.ArrivesAtTick = state.Tick + Balance.TravelSeconds(dist, (int)speed);
            return SimResult.Success;
        }

        // ---------- tick ----------

        // ArriveAtNode can REMOVE a march (a camp battle that wipes the fleet),
        // so the tick walks a snapshot. Mutating state.Marches under a foreach
        // threw InvalidOperationException — during offline catch-up that aborted
        // the whole load (no autosave afterwards, and every relaunch replayed it).
        static readonly List<March> s_tickSnapshot = new();

        public static void Tick(GameState state, SimEventBus events)
        {
            if (state.Marches.Count == 0) return;
            var done = new List<March>();

            s_tickSnapshot.Clear();
            s_tickSnapshot.AddRange(state.Marches);
            foreach (var march in s_tickSnapshot)
            {
                if (state.Tick < march.ArrivesAtTick) continue;
                if (march.Phase == MarchPhase.Outbound) ArriveAtNode(state, events, march);
                else if (march.Phase == MarchPhase.Gathering) FinishGathering(state, events, march);
                else done.Add(march);
            }

            foreach (var march in done)
            {
                foreach (var hull in Ships.All)
                {
                    int back = march.Ships.TryGetValue(hull, out var c) ? c : 0;
                    if (back > 0) state.Ships[hull] += back;
                }
                ResourceSystem.Add(state, march.Cargo);
                if (march.CargoDm > 0)
                    state.Premium.DarkMatter += (int)(march.CargoDm / 1000); // milli → whole DM
                state.Marches.RemoveAll(m => m.Id == march.Id);
                events.Emit(new MarchReturned(march.Id, march.Cargo, march.CargoDm));
            }
        }

        static void StartReturn(GameState state, March march)
        {
            double speed = EffSpeed(state, march.Ships);
            double dist = TileXY.Distance(march.Node, state.HomeTile);
            march.Phase = MarchPhase.Returning;
            march.LegFrom = march.Node;
            march.LegTo = state.HomeTile;
            march.DepartedAtTick = state.Tick;
            march.ArrivesAtTick = state.Tick + Balance.TravelSeconds(dist, (int)speed);
        }

        /// <summary>Park a march where it landed with no due tick (a fly-to, an
        /// intercept waiting to engage, a garrison on guard). RecallMarch or the
        /// system that owns it brings it home.</summary>
        static void Hold(GameState state, March march)
        {
            march.Phase = MarchPhase.Gathering;
            march.LegFrom = march.LegTo;
            // Stamp the landing tick (not "now"): the systems that settle held marches
            // replay them in the order they landed.
            march.DepartedAtTick = Math.Min(state.Tick, march.ArrivesAtTick);
            march.ArrivesAtTick = int.MaxValue;
        }

        /// <summary>Send a march home from where it stands, as of <paramref name="fromTick"/>
        /// (an intercept settled during offline catch-up flies home from the
        /// moment of the battle, not from the end of the catch-up).</summary>
        public static void ReturnHome(GameState state, March march, int fromTick)
        {
            var from = march.Phase == MarchPhase.Gathering ? march.LegTo : GetPosition(march, fromTick);
            double speed = Math.Max(1, EffSpeed(state, march.Ships));
            march.Phase = MarchPhase.Returning;
            march.LegFrom = from;
            march.LegTo = state.HomeTile;
            march.DepartedAtTick = fromTick;
            march.ArrivesAtTick = fromTick + Balance.TravelSeconds(Position.DistanceToTile(from, state.HomeTile), (int)speed);
        }

        /// <summary>Seconds the player's <paramref name="ships"/> need from home to <paramref name="target"/>.</summary>
        public static int FlightSeconds(GameState state, Dictionary<HullId, int> ships, Position target) =>
            Balance.TravelSeconds(Position.DistanceToTile(target, state.HomeTile), Math.Max(1, (int)EffSpeed(state, ships)));

        /// <summary>
        /// Launch an intercept or a garrison: straight to <paramref name="target"/>
        /// (sub-tile — an intercept point on a rival's flight path), landing at
        /// <paramref name="arriveTick"/> (no earlier than the fleet could fly it).
        /// Pays the helium, takes the ships off the dock; the caller fills in the
        /// mission's own fields.
        /// </summary>
        public static SimResult SendFlight(GameState state, Dictionary<HullId, int> ships, Position target,
            int arriveTick, MarchMission mission, out March? march)
        {
            march = null;
            var tile = new TileXY((int)Math.Round(target.X), (int)Math.Round(target.Y));
            var preview = PreviewMarch(state, ships, tile);
            if (!preview.Ok) return SimResult.Fail(preview.Reason ?? "Cannot launch");
            if (arriveTick < state.Tick + FlightSeconds(state, ships, target))
                return SimResult.Fail("Your fleet can't get there in time");

            var paid = ResourceSystem.Spend(state, new ResourceBag(0, 0, preview.HeliumCost));
            if (!paid.Ok) return paid;
            foreach (var hull in Ships.All)
            {
                int take = ships.TryGetValue(hull, out var c) ? c : 0;
                if (take > 0) state.Ships[hull] -= take;
            }

            march = new March
            {
                Id = state.NextMarchId++,
                Phase = MarchPhase.Outbound,
                Ships = new Dictionary<HullId, int>(ships),
                Node = tile,
                LegFrom = state.HomeTile,
                LegTo = target,
                DepartedAtTick = state.Tick,
                ArrivesAtTick = arriveTick,
                Cargo = new ResourceBag(),
                HeliumSpent = preview.HeliumCost,
                Mission = mission,
            };
            state.Marches.Add(march);
            state.Stats.MarchesSent++;
            return SimResult.Success;
        }

        static void ArriveAtNode(GameState state, SimEventBus events, March march)
        {
            // Intercepts, garrisons and core assaults hold where they land —
            // StrikeSystem fights the intercept, BotSystem a raid on the guarded
            // colony, CoreSystem the core — even when the spot happens to be a
            // resource node or a camp.
            if (march.Mission == MarchMission.Intercept || march.Mission == MarchMission.Garrison
                || march.Mission == MarchMission.Core || march.Mission == MarchMission.Boss)
            {
                Hold(state, march);
                events.Emit(new MarchPhaseChanged(march.Id, march.Phase));
                return;
            }

            var node = MapLookup.NodeAt(state, march.Node);
            NodeOverride? overrideForNode = null;
            if (node != null) state.Map.NodeOverrides.TryGetValue(node.Id, out overrideForNode);

            if (march.Mission == MarchMission.Spy)
            {
                var report = BuildSpyReport(state, node, overrideForNode);
                state.Mailbox.Insert(0, report);
                TrimMailbox(state);
                events.Emit(new SpyReportReceived(report));
                StartReturn(state, march);
                events.Emit(new MarchPhaseChanged(march.Id, march.Phase));
                return;
            }

            if (node == null || (overrideForNode != null && overrideForNode.Cleared))
            {
                // Fly-to: a gather march sent to EMPTY space holds position there
                // until recalled (Phase=Gathering with a never-due arrival — no new
                // save fields, offline-catch-up-safe; RecallMarch brings it home).
                if (node == null && march.Mission == MarchMission.Gather)
                {
                    march.Phase = MarchPhase.Gathering;
                    march.LegFrom = march.Node;
                    march.LegTo = march.Node;
                    march.DepartedAtTick = state.Tick;
                    march.ArrivesAtTick = int.MaxValue;
                    events.Emit(new MarchPhaseChanged(march.Id, march.Phase));
                    return;
                }
                StartReturn(state, march);
                events.Emit(new MarchPhaseChanged(march.Id, march.Phase));
                return;
            }

            if (march.Mission == MarchMission.Attack && node.Kind == NodeKind.Camp)
            {
                // The camp composer's forecast (BattleForecast.Predict) makes this
                // same call at launch — keep the inputs in step.
                var report = CombatResolver.Resolve(
                    march.Ships,
                    CampGarrison(node),
                    ResearchSystem.CombatMods(state));
                report.Location = march.Node;
                report.DefenderName = $"Pirate camp Lv{node.CampLevel}";
                march.Ships = report.AttackerSurvivors;

                if (report.Winner == BattleWinner.Attacker)
                {
                    state.Stats.BattlesWon++;
                    state.Stats.CampsCleared++;
                    UpsertOverride(state, node.Id, o => o.Cleared = true);
                    var loot = CampLoot(node);
                    // Pirate Armada (galaxy event): camps carry double loot.
                    float armada = EventSystem.CampLootMult(state);
                    if (armada != 1f)
                        loot = new ResourceBag((long)(loot.Gold * armada), (long)(loot.Quartz * armada),
                            (long)(loot.Helium * armada));
                    long free = FreeCargo(state, march); // launch cargo keeps its space
                    long total = (long)loot.Gold + loot.Quartz + loot.Helium;
                    double scale = total > 0 ? Math.Min(1.0, free / (double)total) : 0;
                    var taken = new ResourceBag(
                        (long)Math.Floor(loot.Gold * scale),
                        (long)Math.Floor(loot.Quartz * scale),
                        (long)Math.Floor(loot.Helium * scale));
                    march.Cargo.Gold   += taken.Gold;
                    march.Cargo.Quartz += taken.Quartz;
                    march.Cargo.Helium     += taken.Helium;
                    report.Loot = taken;
                    state.Stats.LootMilli += taken.Total;
                }
                else
                {
                    state.Stats.BattlesLost++;
                }

                string subject = report.Winner switch
                {
                    BattleWinner.Attacker => $"Victory at {march.Node.X},{march.Node.Y} — {report.DefenderName ?? "defender"}",
                    BattleWinner.Defender => $"Defeat at {march.Node.X},{march.Node.Y} — {report.DefenderName ?? "defender"}",
                    _                     => $"Stalemate at {march.Node.X},{march.Node.Y}",
                };
                var mail = new BattleMailReport
                {
                    Id = state.NextReportId++,
                    AtTick = state.Tick,
                    Target = march.Node,
                    Subject = subject,
                    Report = report,
                    Read = false,
                    Favorite = false,
                };
                SalvageSystem.OnMail(state, mail); // the Salvage Yard strips the wrecks
                state.Mailbox.Insert(0, mail);
                TrimMailbox(state);
                // Emit AFTER the report is filed: the UI pops Mailbox[0] on this
                // event, and emitting first showed the PREVIOUS battle's report.
                events.Emit(new BattleResolved(report));

                if (FleetCount(march.Ships) == 0)
                {
                    state.Marches.RemoveAll(m => m.Id == march.Id);
                    return;
                }
                StartReturn(state, march);
                events.Emit(new MarchPhaseChanged(march.Id, march.Phase));
                return;
            }

            if (node.Kind == NodeKind.Derelict)
            {
                int remaining = overrideForNode?.Remaining ?? node.Amount;
                long take = Math.Min(remaining, FreeCargo(state, march));
                // fixed 50/30/20 salvage mix
                march.Cargo.Gold   += (int)Math.Floor(take * 0.5);
                march.Cargo.Quartz += (int)Math.Floor(take * 0.3);
                march.Cargo.Helium     += (int)Math.Floor(take * 0.2);
                UpsertOverride(state, node.Id, o => o.Cleared = true);
                StartReturn(state, march);
                events.Emit(new MarchPhaseChanged(march.Id, march.Phase));
                return;
            }

            // resource node: park and gather
            int remainingRes = overrideForNode?.Remaining ?? node.Amount;
            long gatherable = Math.Min(remainingRes, FreeCargo(state, march));
            if (gatherable <= 0 || node.RatePerSec <= 0)
            {
                StartReturn(state, march);
                events.Emit(new MarchPhaseChanged(march.Id, march.Phase));
                return;
            }
            march.Phase = MarchPhase.Gathering;
            march.LegFrom = march.Node;
            march.LegTo = march.Node;
            march.DepartedAtTick = state.Tick;
            march.ArrivesAtTick = state.Tick +
                (int)Math.Ceiling(gatherable / (node.RatePerSec * (double)ResearchSystem.GatherRateMult(state)));
            events.Emit(new MarchPhaseChanged(march.Id, MarchPhase.Gathering));
        }

        static void FinishGathering(GameState state, SimEventBus events, March march)
        {
            var node = MapLookup.NodeAt(state, march.Node);
            if (node != null && (node.Resource is ResourceId || node.Kind == NodeKind.DMField))
            {
                state.Map.NodeOverrides.TryGetValue(node.Id, out var ov);
                int remaining = ov?.Remaining ?? node.Amount;
                int gathered = (int)Math.Min(remaining, FreeCargo(state, march));
                if (gathered > 0)
                {
                    if (node.Resource is ResourceId res)
                        march.Cargo.Set(res, march.Cargo.Get(res) + gathered);
                    else
                        march.CargoDm += gathered; // Dark Matter field (v13)
                    UpsertOverride(state, node.Id, o => o.Remaining = remaining - gathered);
                    if (remaining - gathered <= 0) events.Emit(new NodeDepleted(node.Id));
                }
            }
            StartReturn(state, march);
            events.Emit(new MarchPhaseChanged(march.Id, march.Phase));
        }

        /// <summary>
        /// Ring-buffer the mailbox to MailboxCap items — but favorited items are protected
        /// and never evict. If everything is favorited we keep everything.
        /// </summary>
        public static void TrimMailbox(GameState state)
        {
            if (state.Mailbox.Count <= MailboxCap) return;
            var kept = new List<MailItem>();
            var unfavored = new List<MailItem>();
            foreach (var item in state.Mailbox)
            {
                if (item.Favorite) kept.Add(item);
                else unfavored.Add(item);
            }
            int budget = Math.Max(0, MailboxCap - kept.Count);
            var combined = new List<MailItem>(kept);
            for (int i = 0; i < budget && i < unfavored.Count; i++) combined.Add(unfavored[i]);
            combined.Sort((a, b) => b.AtTick - a.AtTick);
            state.Mailbox = combined;
        }

        static SpyReport BuildSpyReport(GameState state, MapNode? node, NodeOverride? overrideForNode)
        {
            var target = node?.Tile ?? new TileXY(0, 0);
            var baseReport = new SpyReport
            {
                Id = state.NextReportId++,
                AtTick = state.Tick,
                Target = target,
                Read = false,
                Favorite = false,
            };

            if (node == null || (overrideForNode != null && overrideForNode.Cleared))
            {
                baseReport.Subject = $"Recon {target.X},{target.Y} — nothing of note";
                baseReport.Intel = new SpyIntel { Kind = null }; // "empty"
                return baseReport;
            }
            if (node.Kind == NodeKind.Camp)
            {
                baseReport.Subject = $"Recon {node.Tile.X},{node.Tile.Y} — pirate camp Lv{node.CampLevel}";
                baseReport.Intel = new SpyIntel
                {
                    Kind = NodeKind.Camp,
                    Tier = node.Tier,
                    CampLevel = node.CampLevel,
                    Garrison = CampGarrison(node),
                };
                return baseReport;
            }
            baseReport.Subject = $"Recon {node.Tile.X},{node.Tile.Y} — {node.Kind}";
            baseReport.Intel = new SpyIntel
            {
                Kind = node.Kind,
                Tier = node.Tier,
                Remaining = overrideForNode?.Remaining ?? node.Amount,
            };
            return baseReport;
        }

        // ---------- private plumbing ----------

        static March? FindMarch(GameState state, int marchId)
        {
            foreach (var m in state.Marches) if (m.Id == marchId) return m;
            return null;
        }

        /// <summary>Get-or-create a NodeOverride for `id` and let the caller mutate it.</summary>
        static void UpsertOverride(GameState state, string id, Action<NodeOverride> mutate)
        {
            if (!state.Map.NodeOverrides.TryGetValue(id, out var ov))
            {
                ov = new NodeOverride();
                state.Map.NodeOverrides[id] = ov;
            }
            mutate(ov);
        }
    }
}
