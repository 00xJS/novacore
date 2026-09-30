// Expeditions (2026-09-30): a fleet leaves the charted galaxy for a few hours
// (off the map; its ships are away from the docks). Halfway there, something
// happens and you choose (Data/Expeditions): BOLD — double the haul and a shot at
// a relic, if it goes right — or SAFE. Undecided at the end means SAFE. The
// odds rise with the fleet's might against the destination's, and with your
// commander leading (the Academy). A new board of destinations every WindowSec.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public sealed class Expedition
    {
        public int Id;
        public ExpeditionKind Kind;
        public Dictionary<HullId, int> Ships = new();
        public int StartTick, EndTick;
        /// <summary>The might this destination calls for.</summary>
        public long Recommended;
        /// <summary>-1 = not chosen yet, 0 = bold, 1 = safe.</summary>
        public int Choice = -1;
        public bool Led;
        /// <summary>The halfway moment has been announced.</summary>
        public bool Announced;
        public int MidTick => StartTick + (EndTick - StartTick) / 2;
    }

    public sealed class ExpeditionOffer
    {
        public int Code;
        public ExpeditionKind Kind;
        public int Hours;
        public long Recommended;
    }

    public sealed class ExpeditionLog
    {
        public int Id;
        public ExpeditionKind Kind;
        public int AtTick;
        public bool Bold, Won;
        public string Story = "";
        public ResourceBag LootMilli = new();
        public int DarkMatter;
        public RelicKind? Relic;
        public int ShipsLost;
    }

    public static class ExpeditionSystem
    {
        public const int WindowSec = 12 * 3600;
        public const int MaxActive = 2;
        public const int LogCap = 10;
        static readonly int[] HoursChoices = { 2, 4, 6, 8 };

        public static int Window(int tick) => tick / WindowSec;
        public static int BoardLeftSec(GameState s) => WindowSec - s.Tick % WindowSec;

        static double Hash(GameState s, int a, int b) => Rng.Hash2d(unchecked((uint)s.Seed ^ 0xE4E0u), a, b);

        /// <summary>This window's three destinations.</summary>
        public static List<ExpeditionOffer> Board(GameState s)
        {
            var list = new List<ExpeditionOffer>();
            int window = Window(s.Tick);
            long full = Balance.HomeGuardFullMight(s.Buildings[BuildingId.CommandCenter].Level);
            for (int slot = 0; slot < 3; slot++)
            {
                int hours = HoursChoices[Math.Min(3, (int)(Hash(s, window * 5 + slot, 2) * 4))];
                list.Add(new ExpeditionOffer
                {
                    Code = window * 10 + slot + 1,
                    Kind = (ExpeditionKind)Math.Min(Expeditions.All.Count - 1, (int)(Hash(s, window * 5 + slot, 1) * Expeditions.All.Count)),
                    Hours = hours,
                    Recommended = Math.Max(40, (long)(full * (0.25 + 0.15 * hours / 2.0))),
                });
            }
            return list;
        }

        public static long Might(Dictionary<HullId, int> ships)
        {
            long invested = 0;
            foreach (var kv in ships) if (kv.Value > 0) invested += (long)kv.Value * Ships.Defs[kv.Key].Cost.Total;
            return invested / 10;
        }

        public static bool Taken(GameState s, int code) => s.ExpeditionsTaken.Contains(code);

        public static SimResult CanSend(GameState s, ExpeditionOffer offer, Dictionary<HullId, int> ships)
        {
            if (Taken(s, offer.Code)) return SimResult.Fail("Already sent");
            if (s.Expeditions.Count >= MaxActive) return SimResult.Fail($"{MaxActive} expeditions are already out");
            int n = 0;
            foreach (var kv in ships)
            {
                if (kv.Value < 0) return SimResult.Fail("Invalid fleet");
                if (kv.Value > (s.Ships.TryGetValue(kv.Key, out var d) ? d : 0)) return SimResult.Fail($"Not enough {Ships.Defs[kv.Key].Name}s docked");
                n += kv.Value;
            }
            return n < 1 ? SimResult.Fail("No ships selected") : SimResult.Success;
        }

        public static SimResult Send(GameState s, ExpeditionOffer offer, Dictionary<HullId, int> ships, bool lead, out int id)
        {
            id = 0;
            var can = CanSend(s, offer, ships);
            if (!can.Ok) return can;
            foreach (var kv in ships) s.Ships[kv.Key] -= kv.Value;
            var exp = new Expedition
            {
                Id = ++s.NextExpeditionId,
                Kind = offer.Kind,
                Ships = new Dictionary<HullId, int>(ships),
                StartTick = s.Tick,
                EndTick = s.Tick + offer.Hours * 3600,
                Recommended = offer.Recommended,
            };
            s.Expeditions.Add(exp);
            s.ExpeditionsTaken.Add(offer.Code);
            int oldest = (Window(s.Tick) - 2) * 10;
            s.ExpeditionsTaken.RemoveWhere(c => c < oldest);
            if (lead && AcademySystem.CanLead(s).Ok)
            {
                exp.Led = true;
                s.CaptainMarchId = -exp.Id; // negative: an expedition, not a march
            }
            id = exp.Id;
            return SimResult.Success;
        }

        /// <summary>Make the halfway call: bold or safe.</summary>
        public static SimResult Decide(GameState s, int id, bool bold)
        {
            var exp = s.Expeditions.Find(e => e.Id == id);
            if (exp == null) return SimResult.Fail("No such expedition");
            if (s.Tick < exp.MidTick) return SimResult.Fail("Nothing to decide yet");
            if (exp.Choice >= 0) return SimResult.Fail("Already decided");
            exp.Choice = bold ? 0 : 1;
            return SimResult.Success;
        }

        /// <summary>The odds a bold call comes off.</summary>
        public static double BoldOdds(GameState s, Expedition exp)
        {
            double ratio = Math.Min(1.0, Might(exp.Ships) / (double)Math.Max(1, exp.Recommended));
            double captain = exp.Led ? AcademySystem.CaptainBonus(s) : 0;
            return Math.Clamp(0.3 + 0.45 * ratio + captain, 0.15, 0.92);
        }

        public static void Tick(GameState s, SimEventBus events)
        {
            for (int i = s.Expeditions.Count - 1; i >= 0; i--)
            {
                var exp = s.Expeditions[i];
                if (!exp.Announced && s.Tick >= exp.MidTick)
                {
                    exp.Announced = true;
                    events.Emit(new ExpeditionMoment(exp.Id, exp.Kind));
                }
                if (s.Tick < exp.EndTick) continue;
                s.Expeditions.RemoveAt(i);
                var log = Resolve(s, exp);
                events.Emit(new ExpeditionReturned(log));
            }
        }

        static ExpeditionLog Resolve(GameState s, Expedition exp)
        {
            var def = Expeditions.Def(exp.Kind);
            bool bold = exp.Choice == 0;
            int hours = Math.Max(1, (exp.EndTick - exp.StartTick) / 3600);
            double ratio = Math.Max(0.2, Math.Min(1.5, Might(exp.Ships) / (double)Math.Max(1, exp.Recommended)));
            double loot = ResourceSystem.MineOutputPerHour(s).Total * hours * ratio;
            bool won = !bold || Hash(s, exp.Id * 13 + 1, s.Seed & 0xFFFF) < BoldOdds(s, exp);
            var log = new ExpeditionLog { Id = exp.Id, Kind = exp.Kind, AtTick = exp.EndTick, Bold = bold, Won = won };
            double relicChance;
            if (bold && won)
            {
                loot *= 2.2;
                log.DarkMatter = 25 + 5 * hours;
                relicChance = 0.35 + (exp.Led ? AcademySystem.CaptainBonus(s) : 0);
                log.Story = def.BoldWin;
            }
            else if (bold)
            {
                loot *= 0.4;
                log.DarkMatter = 5;
                relicChance = 0;
                log.Story = def.BoldLoss;
                double share = 0.15 + 0.2 * Hash(s, exp.Id * 13 + 2, 7);
                foreach (var hull in Ships.All)
                {
                    if (!exp.Ships.TryGetValue(hull, out var n) || n <= 0) continue;
                    int lost = (int)Math.Floor(n * share);
                    exp.Ships[hull] = n - lost;
                    log.ShipsLost += lost;
                }
            }
            else
            {
                log.DarkMatter = 10 + 2 * hours;
                relicChance = 0.08;
                log.Story = def.Safe;
            }
            long total = (long)loot;
            log.LootMilli = new ResourceBag(total * 40 / 100, total * 35 / 100, total * 25 / 100);
            if (Hash(s, exp.Id * 13 + 3, 11) < relicChance)
            {
                var kind = Hash(s, exp.Id * 13 + 4, 5) < 0.6 ? def.Relic
                    : (RelicKind)Math.Min(Relics.All.Count - 1, (int)(Hash(s, exp.Id * 13 + 5, 3) * Relics.All.Count));
                s.Relics[kind] = RelicSystem.Count(s, kind) + 1;
                log.Relic = kind;
            }

            foreach (var kv in exp.Ships)
                s.Ships[kv.Key] = (s.Ships.TryGetValue(kv.Key, out var d) ? d : 0) + kv.Value;
            ResourceSystem.Add(s, log.LootMilli);
            s.Premium.DarkMatter += log.DarkMatter;
            s.Stats.ExpeditionsDone++;
            if (exp.Led && s.CaptainMarchId == -exp.Id)
            {
                s.CaptainMarchId = 0;
                if (bold && !won) s.CaptainWoundedUntilTick = s.Tick + AcademySystem.WoundSec;
            }
            s.ExpeditionLog.Insert(0, log);
            if (s.ExpeditionLog.Count > LogCap) s.ExpeditionLog.RemoveRange(LogCap, s.ExpeditionLog.Count - LogCap);
            return log;
        }
    }
}
