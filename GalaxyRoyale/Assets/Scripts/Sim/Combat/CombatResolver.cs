// Pure, RNG-free deterministic combat. Direct port of v1's
// `src/sim/combat/CombatResolver.ts` — same inputs always produce the same report.
//
// Damage model: a hull "counters" a specific enemy hull (Fighter > Bomber >
// Cruiser > Fighter, per Ships.Defs[hull].Counters) — if that enemy stack exists,
// all damage goes there at CounterMultiplier. Otherwise damage splits across
// enemy stacks proportional to remaining HP. Haulers take a flat vulnerability
// bonus. HP carries over between rounds within a stack.
//
// Both sides fight with their research (FleetMods): military research counts
// in every battle, and a defender at home adds the Defense branch — including
// Orbital Batteries, planetary guns that hit every attacking stack each round
// straight through ship shields, even when no defending fleet is docked — and
// the Command Bastion's railguns, which fire at the heaviest hull present.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Combat
{
    public static class CombatResolver
    {
        public static int FleetCount(Dictionary<HullId, int> comp)
        {
            int n = 0;
            foreach (var v in comp.Values) n += v;
            return n;
        }

        /// <summary>HP pool per hull — damage carries between rounds within a stack.</summary>
        internal static Dictionary<HullId, int> InitPools(Dictionary<HullId, int> comp)
        {
            var pools = new Dictionary<HullId, int>();
            foreach (var hull in Ships.All)
            {
                int count = comp.TryGetValue(hull, out var c) ? c : 0;
                if (count > 0) pools[hull] = count * Ships.Defs[hull].Hp;
            }
            return pools;
        }

        internal static Dictionary<HullId, int> Survivors(Dictionary<HullId, int> pools)
        {
            var outSurv = new Dictionary<HullId, int>();
            foreach (var hull in Ships.All)
            {
                int pool = pools.TryGetValue(hull, out var p) ? p : 0;
                if (pool > 0) outSurv[hull] = (int)Math.Ceiling(pool / (double)Ships.Defs[hull].Hp);
            }
            return outSurv;
        }

        static int TotalSurvivors(Dictionary<HullId, int> pools)
        {
            var s = Survivors(pools);
            int n = 0;
            foreach (var v in s.Values) n += v;
            return n;
        }

        /// <summary>
        /// Damage one side deals against the other's current pools this round.
        /// Counters target their prey exclusively when it exists; otherwise damage splits
        /// by remaining HP share, with haulers taking HaulerVulnMultiplier extra.
        /// `atkMultFor` scales each hull's outgoing damage (per-hull research); null = ×1.
        /// </summary>
        static Dictionary<HullId, float> DamageAgainst(Dictionary<HullId, int> own, Dictionary<HullId, int> enemy,
            Func<HullId, float>? atkMultFor = null)
        {
            var dealt = new Dictionary<HullId, float>();
            int enemyTotalPool = 0;
            foreach (var h in Ships.All)
                if (enemy.TryGetValue(h, out var p)) enemyTotalPool += p;
            if (enemyTotalPool <= 0) return dealt;

            var ownSurvivors = Survivors(own);
            foreach (var hull in Ships.All)
            {
                int count = ownSurvivors.TryGetValue(hull, out var c) ? c : 0;
                if (count <= 0) continue;
                var def = Ships.Defs[hull];
                float raw = count * def.Atk * (atkMultFor?.Invoke(hull) ?? 1f);

                if (def.Counters is HullId target && enemy.TryGetValue(target, out var targetPool) && targetPool > 0)
                {
                    Add(dealt, target, raw * Balance.CounterMultiplier);
                }
                else
                {
                    foreach (var t in Ships.All)
                    {
                        int pool = enemy.TryGetValue(t, out var p) ? p : 0;
                        if (pool <= 0) continue;
                        float share = (float)pool / enemyTotalPool;
                        // Freighters (Hauler + Atlas) draw extra fire — fat, slow targets.
                        float mult = t == HullId.Hauler || t == HullId.Atlas
                            ? Balance.HaulerVulnMultiplier : 1f;
                        Add(dealt, t, raw * share * mult);
                    }
                }
            }
            return dealt;
        }

        static void Add(Dictionary<HullId, float> d, HullId h, float amount)
        {
            d[h] = (d.TryGetValue(h, out var cur) ? cur : 0f) + amount;
        }

        /// <summary>Area fire split across every stack by remaining HP share, with
        /// freighters drawing extra — the Orbital Batteries' pattern.</summary>
        internal static Dictionary<HullId, float> SpreadByHp(Dictionary<HullId, int> pools, float damage)
        {
            var spread = new Dictionary<HullId, float>();
            int total = 0;
            foreach (var h in Ships.All)
                if (pools.TryGetValue(h, out var p)) total += p;
            if (total <= 0) return spread;
            foreach (var h in Ships.All)
            {
                int pool = pools.TryGetValue(h, out var p) ? p : 0;
                if (pool <= 0) continue;
                float mult = h == HullId.Hauler || h == HullId.Atlas ? Balance.HaulerVulnMultiplier : 1f;
                Add(spread, h, damage * pool / total * mult);
            }
            return spread;
        }

        /// <summary>
        /// Regenerating shields (fleet expansion, 2026-07-05): each round a stack
        /// projects `survivors × Shield` of protection. Damage burns the shield
        /// FIRST; only the overflow reaches the HP pool. Shields restore every
        /// round — swarms of weak hits die on the shield, concentrated heavy
        /// fire punches through.
        /// </summary>
        static Dictionary<HullId, float> AbsorbShields(Dictionary<HullId, float> damage,
            Dictionary<HullId, int> survivorsBefore, float shieldMult)
        {
            var outD = new Dictionary<HullId, float>();
            foreach (var kv in damage)
            {
                int count = survivorsBefore.TryGetValue(kv.Key, out var c) ? c : 0;
                float shield = count * Ships.Defs[kv.Key].Shield * shieldMult;
                float after = kv.Value - shield;
                if (after > 0) outD[kv.Key] = after;
            }
            return outD;
        }

        internal static void ApplyDamage(Dictionary<HullId, int> pools, Dictionary<HullId, float> damage)
        {
            foreach (var hull in Ships.All)
            {
                float dmg = damage.TryGetValue(hull, out var d) ? d : 0f;
                if (dmg <= 0) continue;
                int cur = pools.TryGetValue(hull, out var p) ? p : 0;
                pools[hull] = Math.Max(0, cur - (int)Math.Round(dmg));
            }
        }

        internal static Dictionary<HullId, int> LossesBetween(Dictionary<HullId, int> before, Dictionary<HullId, int> after)
        {
            var outL = new Dictionary<HullId, int>();
            foreach (var hull in Ships.All)
            {
                int b = before.TryGetValue(hull, out var bv) ? bv : 0;
                int a = after.TryGetValue(hull, out var av) ? av : 0;
                int lost = b - a;
                if (lost > 0) outL[hull] = lost;
            }
            return outL;
        }

        public static BattleReport Resolve(
            Dictionary<HullId, int> attacker,
            Dictionary<HullId, int> defender,
            FleetMods atkMods = default,
            FleetMods defMods = default)
        {
            var atkPools = InitPools(attacker);
            var defPools = InitPools(defender);
            var rounds = new List<RoundLog>();
            int battery = Math.Max(0, defMods.BatteryLevel);
            float batteryDamage = battery * (float)Balance.BatteryDamagePerLevel;
            int turret = Math.Max(0, defMods.TurretDamage);
            bool guns = battery > 0 || turret > 0;

            int round = 0;
            while (round < Balance.MaxCombatRounds
                && TotalSurvivors(atkPools) > 0
                // The planetary guns keep firing a few rounds even with no fleet home.
                && (TotalSurvivors(defPools) > 0 || (guns && round < Balance.BatteryOnlyRounds)))
            {
                round++;
                var atkBefore = Survivors(atkPools);
                var defBefore = Survivors(defPools);

                // Each side's shields soak raw damage first (Deflector Array, and
                // Planetary Deflectors at home), THEN armor research shrinks what
                // reaches each hull.
                var dmgToDefRaw = AbsorbShields(
                    DamageAgainst(atkPools, defPools, atkMods.AtkFor), defBefore, defMods.ShieldFor());
                var dmgToAtkRaw = AbsorbShields(
                    DamageAgainst(defPools, atkPools, defMods.AtkFor), atkBefore, atkMods.ShieldFor());
                var dmgToDef = new Dictionary<HullId, float>();
                foreach (var kv in dmgToDefRaw) dmgToDef[kv.Key] = kv.Value / defMods.HpFor(kv.Key);
                var dmgToAtk = new Dictionary<HullId, float>();
                foreach (var kv in dmgToAtkRaw) dmgToAtk[kv.Key] = kv.Value / atkMods.HpFor(kv.Key);
                // Planetary guns are capital-scale: straight through ship shields.
                if (batteryDamage > 0f)
                    foreach (var kv in SpreadByHp(atkPools, batteryDamage))
                        Add(dmgToAtk, kv.Key, kv.Value / atkMods.HpFor(kv.Key));
                // Bastion railguns: anti-capital — the heaviest hull still standing takes it all.
                if (turret > 0 && HeaviestHull(atkPools) is HullId heavy)
                    Add(dmgToAtk, heavy, turret / atkMods.HpFor(heavy));
                ApplyDamage(defPools, dmgToDef);
                ApplyDamage(atkPools, dmgToAtk);

                rounds.Add(new RoundLog
                {
                    Round = round,
                    AttackerLosses = LossesBetween(atkBefore, Survivors(atkPools)),
                    DefenderLosses = LossesBetween(defBefore, Survivors(defPools)),
                });
            }

            bool atkAlive = TotalSurvivors(atkPools) > 0;
            bool defAlive = TotalSurvivors(defPools) > 0;
            // A raid that outlasts the batteries still takes an empty colony; one
            // the batteries wipe out is repelled.
            var winner = (atkAlive && !defAlive) ? BattleWinner.Attacker
                       : (!atkAlive && (defAlive || guns)) ? BattleWinner.Defender
                       : BattleWinner.Draw;

            return new BattleReport
            {
                Attacker = new Dictionary<HullId, int>(attacker),
                Defender = new Dictionary<HullId, int>(defender),
                Winner = winner,
                Rounds = rounds,
                AttackerSurvivors = Survivors(atkPools),
                DefenderSurvivors = Survivors(defPools),
                DefenderBattery = battery,
                DefenderTurret = turret,
            };
        }

        /// <summary>The attacking hull with the most HP per ship that still has ships left.</summary>
        static HullId? HeaviestHull(Dictionary<HullId, int> pools)
        {
            HullId? best = null;
            int bestHp = -1;
            foreach (var hull in Ships.All)
            {
                if (!pools.TryGetValue(hull, out var pool) || pool <= 0) continue;
                int hp = Ships.Defs[hull].Hp;
                if (hp > bestHp) { bestHp = hp; best = hull; }
            }
            return best;
        }
    }
}
