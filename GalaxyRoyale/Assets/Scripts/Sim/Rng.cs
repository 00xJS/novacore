// Deterministic PRNG utilities. Same seed → same sequence, always.
// Direct port of v1's `src/sim/rng.ts`. All arithmetic wraps modulo 2^32
// (via `unchecked`) so results match v1's JS Math.imul + `| 0` semantics.
using System;

namespace GalaxyRoyale.Sim
{
    public static class Rng
    {
        /// <summary>mulberry32 — fast 32-bit seeded PRNG returning floats in [0, 1).</summary>
        public static Func<double> Mulberry32(uint seed)
        {
            uint a = seed;
            return () =>
            {
                a = unchecked(a + 0x6d2b79f5u);
                uint t = unchecked((a ^ (a >> 15)) * (1u | a));
                t = unchecked((t + unchecked((t ^ (t >> 7)) * (61u | t))) ^ t);
                return (t ^ (t >> 14)) / 4294967296.0;
            };
        }

        /// <summary>
        /// Hash (seed, x, y) → [0, 1) with no sequence coupling. Lets map generation
        /// derive per-tile values independent of iteration order.
        /// </summary>
        public static double Hash2d(uint seed, int x, int y)
        {
            uint h = seed;
            h = unchecked((h ^ (uint)x * 0x9e3779b1u) * 0x85ebca6bu);
            h = unchecked((h ^ (uint)y * 0xc2b2ae35u) * 0x27d4eb2fu);
            h ^= h >> 15;
            h = unchecked(h * 0x2c1b3c6du);
            h ^= h >> 12;
            return h / 4294967296.0;
        }
    }
}
