// The codex (Data/Codex, 2026-09-30). Worlds are recorded as a fleet reaches
// them (MarchSystem); everything else is read off the record once a minute:
// hulls owned, lords beaten, relics and blueprints held, the live event and
// twist, dreadnoughts struck (their reports), expeditions home. A full
// collection earns its skin (and its achievement, via AchievementSystem).
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Map;

namespace GalaxyRoyale.Sim.Systems
{
    public static class CodexSystem
    {
        public static bool Has(GameState s, string key) => s.Codex.Contains(key);

        public static (int found, int total) Progress(GameState s, CodexCategoryDef cat)
        {
            int n = 0;
            foreach (var e in cat.Entries) if (s.Codex.Contains(e.Key)) n++;
            return (n, cat.Entries.Count);
        }

        public static int Found(GameState s, string categoryId) =>
            Data.Codex.ById(categoryId) is { } cat ? Progress(s, cat).found : 0;

        /// <summary>A world a fleet reached (MarchSystem.ArriveAtNode).</summary>
        public static void OnArrive(GameState s, MapNode node, SimEventBus? events)
        {
            string kind = LairSystem.IsLair(node) ? "Lair" : node.Kind.ToString();
            Record(s, $"world:{kind}", events);
        }

        public static void Record(GameState s, string key, SimEventBus? events)
        {
            if (!s.Codex.Add(key)) return;
            foreach (var cat in Data.Codex.Categories)
            {
                CodexEntryDef? entry = null;
                foreach (var e in cat.Entries) if (e.Key == key) entry = e;
                if (entry == null) continue;
                events?.Emit(new CodexEntryFound(cat.Id, entry.Label));
                var (found, total) = Progress(s, cat);
                if (found < total) return;
                if (cat.SkinId != null && !s.Skins.Owned.Contains(cat.SkinId)) s.Skins.Owned.Add(cat.SkinId);
                events?.Emit(new CodexCategoryComplete(cat.Id));
                return;
            }
        }

        /// <summary>Once a minute: whatever the record now shows.</summary>
        public static void Tick(GameState s, SimEventBus events)
        {
            if (s.Tick % 60 != 0) return;
            foreach (var kv in s.Ships) if (kv.Value > 0) Record(s, $"hull:{kv.Key}", events);
            foreach (var kv in s.Campaign.LordWins) if (kv.Value > 0) Record(s, $"lord:{kv.Key}", events);
            foreach (var kv in s.Relics) if (kv.Value > 0) Record(s, $"relic:{kv.Key}", events);
            foreach (var kv in s.Modules.Blueprints) if (kv.Value > 0) Record(s, $"blueprint:{kv.Key}", events);
            var live = EventSystem.Current(s.Tick);
            if (!EventSystem.IsQuiet(live)) Record(s, $"event:{live.Def.Kind}", events);
            var twist = TwistSystem.KindAt(s.Tick);
            if (twist != TwistKind.None) Record(s, $"twist:{twist}", events);
            foreach (var m in s.Mailbox)
                if (m is BossReport { Kind: BossReportKind.Strike } b)
                    Record(s, $"boss:{BossSystem.VariantFor(b.Visit)}", events);
            foreach (var log in s.ExpeditionLog) Record(s, $"exp:{log.Kind}", events);
        }
    }
}
