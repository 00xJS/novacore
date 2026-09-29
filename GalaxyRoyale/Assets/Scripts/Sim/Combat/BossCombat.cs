// A strike on the Pirate Dreadnought (world boss). Unlike a battle there is no
// fleet to beat: every ship fires at the one hull for a few rounds, and the
// dreadnought's guns answer — split across the strikers by HP share, straight
// through ship shields like planetary batteries, and harder as its hull fails.
// Deterministic, so the strike screen's forecast is exact.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Combat
{
    public static class BossCombat
    {
        public const int Rounds = 3;
        /// <summary>The guns hit up to this much harder (×1.5) as the hull nears zero.</summary>
        public const double Desperation = 0.5;

        public sealed class Result
        {
            public long Damage;
            public bool Killed;
            public int RoundsFought;
            public Dictionary<HullId, int> Survivors = new();
            public Dictionary<HullId, int> Losses = new();
        }

        public static Result Strike(Dictionary<HullId, int> fleet, FleetMods mods, long hp, long maxHp, int cannon)
        {
            var pools = CombatResolver.InitPools(fleet);
            var result = new Result();
            long left = Math.Max(0, hp);
            for (int round = 1; round <= Rounds && left > 0; round++)
            {
                var alive = CombatResolver.Survivors(pools);
                if (alive.Count == 0) break;
                result.RoundsFought = round;
                double raw = 0;
                foreach (var kv in alive) raw += (double)kv.Value * Ships.Defs[kv.Key].Atk * mods.AtkFor(kv.Key);
                long dealt = Math.Min(left, (long)raw);
                left -= dealt;
                result.Damage += dealt;
                if (left <= 0) { result.Killed = true; break; } // it breaks apart before it can fire back

                double fury = 1 + Desperation * (1 - left / (double)Math.Max(1, maxHp));
                var incoming = new Dictionary<HullId, float>();
                foreach (var kv in CombatResolver.SpreadByHp(pools, (float)(cannon * fury)))
                    incoming[kv.Key] = kv.Value / mods.HpFor(kv.Key);
                CombatResolver.ApplyDamage(pools, incoming);
            }
            result.Survivors = CombatResolver.Survivors(pools);
            result.Losses = CombatResolver.LossesBetween(fleet, result.Survivors);
            return result;
        }
    }
}
