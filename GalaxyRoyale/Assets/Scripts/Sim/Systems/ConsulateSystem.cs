// The Trade Consulate (the Citadel, 2026-09-30): commanders nearby post delivery
// contracts — "bring me 20K quartz, I'll pay 26K helium and some Dark Matter".
// Accept one and Haulers carrying the goods fly to the client's colony; they
// come home with the payment. A new board goes up every WindowSec, and each
// contract can be taken once. Pay rises with the consulate's level.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;

namespace GalaxyRoyale.Sim.Systems
{
    public sealed class Contract
    {
        /// <summary>Unique for the board it's on: window × 10 + slot + 1.</summary>
        public int Code;
        public int ClientId;
        public string ClientName = "";
        public TileXY ClientTile;
        public ResourceId Give, Get;
        public long GiveMilli, GetMilli;
        public int DarkMatter;
    }

    public static class ConsulateSystem
    {
        public const int WindowSec = 8 * 3600;
        public const double ClientRange = 600;

        public static int Level(GameState s) => s.Buildings[BuildingId.TradeConsulate].Level;
        public static int Window(int tick) => tick / WindowSec;
        public static int BoardLeftSec(GameState s) => WindowSec - s.Tick % WindowSec;

        /// <summary>What the client pays per unit you bring: 1.3×, +1% a level, up to 1.6×.</summary>
        public static double Rate(int level) => Math.Min(1.6, 1.3 + 0.01 * level);

        /// <summary>This window's board: 2 contracts, +1 every 10 levels, up to 4.</summary>
        public static List<Contract> Board(GameState s, BotGalaxy galaxy)
        {
            var list = new List<Contract>();
            int level = Level(s);
            if (level < 1) return list;
            var clients = new List<BotEmpire>();
            foreach (var bot in galaxy.Bots)
                if (TileXY.Distance(bot.HomeTile, s.HomeTile) <= ClientRange) clients.Add(bot);
            if (clients.Count == 0) return list;
            clients.Sort((a, b) => a.Id.CompareTo(b.Id));
            int window = Window(s.Tick);
            int slots = Math.Min(4, 2 + level / 10);
            long hourly = Math.Max(3_000_000L, ResourceSystem.MineOutputPerHour(s).Total / 3);
            for (int slot = 0; slot < slots; slot++)
            {
                double H(int salt) => Rng.Hash2d(unchecked((uint)s.Seed ^ 0xC0A5Au), window * 7 + slot, salt);
                var client = clients[Math.Min(clients.Count - 1, (int)(H(1) * clients.Count))];
                var give = (ResourceId)Math.Min(2, (int)(H(2) * 3));
                var get = (ResourceId)(((int)give + 1 + (int)(H(3) * 2)) % 3);
                // 1.5-3 hours of one resource's output: a delivery a handful of Haulers can fly.
                long giveMilli = (long)(hourly * (1.5 + H(4) * 1.5)) / 1000 * 1000;
                list.Add(new Contract
                {
                    Code = window * 10 + slot + 1,
                    ClientId = client.Id,
                    ClientName = client.Name,
                    ClientTile = client.HomeTile,
                    Give = give,
                    Get = get,
                    GiveMilli = giveMilli,
                    GetMilli = (long)(giveMilli * Rate(level)) / 1000 * 1000,
                    DarkMatter = 5 + level,
                });
            }
            return list;
        }

        public static bool Taken(GameState s, int code) => s.ContractsTaken.Contains(code);

        /// <summary>Haulers needed to carry a contract's goods.</summary>
        public static int HaulersNeeded(GameState s, Contract c)
        {
            long per = MarchSystem.EffCargoCap(s, new Dictionary<HullId, int> { [HullId.Hauler] = 1 });
            return (int)Math.Max(1, (c.GiveMilli + per - 1) / Math.Max(1, per));
        }

        public static SimResult CanAccept(GameState s, Contract c)
        {
            if (Level(s) < 1) return SimResult.Fail("Build the Trade Consulate first");
            if (Taken(s, c.Code)) return SimResult.Fail("Already taken");
            if (s.Resources.Get(c.Give) < c.GiveMilli) return SimResult.Fail("Not enough to deliver");
            int need = HaulersNeeded(s, c);
            int have = s.Ships.TryGetValue(HullId.Hauler, out var h) ? h : 0;
            if (have < need) return SimResult.Fail($"Needs {need} Haulers docked");
            return SimResult.Success;
        }

        /// <summary>Accept: Haulers load the goods and fly to the client.</summary>
        public static SimResult Accept(GameState s, Contract c, out int marchId)
        {
            marchId = 0;
            var can = CanAccept(s, c);
            if (!can.Ok) return can;
            var ships = new Dictionary<HullId, int> { [HullId.Hauler] = HaulersNeeded(s, c) };
            var target = new Position(c.ClientTile.X, c.ClientTile.Y);
            int arrive = s.Tick + MarchSystem.FlightSeconds(s, ships, target);
            var sent = MarchSystem.SendFlight(s, ships, target, arrive, MarchMission.Trade, out var march);
            if (!sent.Ok || march == null) return sent;
            s.Resources.Set(c.Give, s.Resources.Get(c.Give) - c.GiveMilli);
            march.Cargo.Set(c.Give, c.GiveMilli);
            march.TargetFleetId = c.Code;
            march.GuardEmpireId = c.ClientId;
            march.ContractPay = new ResourceBag();
            march.ContractPay.Set(c.Get, c.GetMilli);
            march.ContractDm = c.DarkMatter;
            march.ContractClient = c.ClientName;
            s.ContractsTaken.Add(c.Code);
            // Old boards' codes drop off.
            int oldest = (Window(s.Tick) - 3) * 10;
            s.ContractsTaken.RemoveWhere(code => code < oldest);
            marchId = march.Id;
            return SimResult.Success;
        }

        /// <summary>Per tick, after the marches: deliveries that reached their client swap
        /// the goods for the payment and fly home.</summary>
        public static void Tick(GameState s, SimEventBus events)
        {
            foreach (var m in s.Marches)
            {
                if (m.Mission != MarchMission.Trade || m.Phase != MarchPhase.Gathering) continue;
                m.Cargo = m.ContractPay?.Clone() ?? new ResourceBag();
                m.CargoDm += m.ContractDm * 1000L;
                s.Stats.ContractsDone++;
                events.Emit(new ContractDelivered(m.ContractClient, m.Cargo.Clone(), m.ContractDm));
                MarchSystem.ReturnHome(s, m, s.Tick);
            }
        }
    }
}
