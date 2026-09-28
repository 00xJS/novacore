// Round-by-round fleet strengths for the battle replay, rebuilt from a report:
// every report stores both starting fleets and each round's losses per hull,
// so any battle in the mailbox — old ones included — can be played back.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Combat
{
    public sealed class BattleTimeline
    {
        /// <summary>Attacker fleet before the first volley ([0]) and after each round.</summary>
        public readonly List<Dictionary<HullId, int>> Attacker = new();
        /// <summary>Defender fleet before the first volley ([0]) and after each round.</summary>
        public readonly List<Dictionary<HullId, int>> Defender = new();

        public int Rounds => Attacker.Count - 1;

        public static BattleTimeline From(BattleReport report)
        {
            var timeline = new BattleTimeline();
            var atk = Positive(report.Attacker);
            var def = Positive(report.Defender);
            timeline.Attacker.Add(new Dictionary<HullId, int>(atk));
            timeline.Defender.Add(new Dictionary<HullId, int>(def));
            foreach (var round in report.Rounds)
            {
                Subtract(atk, round.AttackerLosses);
                Subtract(def, round.DefenderLosses);
                timeline.Attacker.Add(new Dictionary<HullId, int>(atk));
                timeline.Defender.Add(new Dictionary<HullId, int>(def));
            }
            return timeline;
        }

        public static int Count(Dictionary<HullId, int> fleet)
        {
            int n = 0;
            foreach (var v in fleet.Values) n += v;
            return n;
        }

        static Dictionary<HullId, int> Positive(Dictionary<HullId, int> fleet)
        {
            var copy = new Dictionary<HullId, int>();
            foreach (var kv in fleet)
                if (kv.Value > 0) copy[kv.Key] = kv.Value;
            return copy;
        }

        static void Subtract(Dictionary<HullId, int> fleet, Dictionary<HullId, int> losses)
        {
            foreach (var kv in losses)
                if (fleet.TryGetValue(kv.Key, out var n))
                    fleet[kv.Key] = Math.Max(0, n - kv.Value);
        }
    }
}
