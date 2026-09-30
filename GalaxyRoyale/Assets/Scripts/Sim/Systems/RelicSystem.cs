// The Relic Vault (the Citadel, 2026-09-30). Claiming a relic in the Wilds now
// also brings home a relic of one of six kinds (Data/Relics), picked from the
// sector and its survey count so a reload finds the same one. The vault puts
// them on display: each copy of a kind adds its bonus, up to as many copies as
// the vault has levels. The bonuses join the research totals.
using System;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class RelicSystem
    {
        public static int Count(GameState s, RelicKind kind) => s.Relics.TryGetValue(kind, out var n) ? n : 0;

        /// <summary>Copies of a kind on display: all of them, up to the vault's level.</summary>
        public static int OnDisplay(GameState s, RelicKind kind) =>
            Math.Min(Count(s, kind), s.Buildings[BuildingId.RelicVault].Level);

        /// <summary>The vault's share of an effect (joins ResearchSystem.EffectTotal).</summary>
        public static float EffectTotal(GameState s, TechEffectKind effect)
        {
            if (s.Relics.Count == 0 || s.Buildings[BuildingId.RelicVault].Level < 1) return 0f;
            float sum = 0f;
            foreach (var def in Relics.All)
                if (def.Effect == effect) sum += def.PerCopy * OnDisplay(s, def.Kind);
            return sum;
        }

        /// <summary>The relic a claimed Wilds relic brings home, and it's added to the collection.</summary>
        public static RelicKind Grant(GameState s, int sectorIndex, int surveys)
        {
            double r = Rng.Hash2d(unchecked((uint)s.Seed ^ 0x2E11Cu), sectorIndex * 31 + 7, surveys * 17 + 3);
            var kind = (RelicKind)Math.Min(Relics.All.Count - 1, (int)(r * Relics.All.Count));
            s.Relics[kind] = Count(s, kind) + 1;
            return kind;
        }
    }
}
