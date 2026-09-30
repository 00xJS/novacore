// The codex (late-game content, user-approved 2026-09-30): a record of
// everything the commander has met, in nine collections. Filling one earns a
// title (an achievement) and, for four of them, a planet skin that can only be
// earned, never bought. CodexSystem fills it.
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public sealed class CodexEntryDef
    {
        public string Key = "";
        public string Label = "";
    }

    public sealed class CodexCategoryDef
    {
        public string Id = "";
        public string Name = "";
        /// <summary>How an entry gets in, one line.</summary>
        public string How = "";
        public IReadOnlyList<CodexEntryDef> Entries = new CodexEntryDef[0];
        /// <summary>The planet skin a full collection earns (null = a title only).</summary>
        public string? SkinId;
        public string? SkinName;
    }

    public static class Codex
    {
        static CodexEntryDef E(string key, string label) => new() { Key = key, Label = label };

        static IReadOnlyList<CodexEntryDef> Hulls()
        {
            var list = new List<CodexEntryDef>();
            foreach (var h in Ships.All) list.Add(E($"hull:{h}", Ships.Defs[h].Name));
            return list;
        }

        static IReadOnlyList<CodexEntryDef> Lords()
        {
            var list = new List<CodexEntryDef>();
            foreach (var l in PirateLords.All) list.Add(E($"lord:{l.Index}", l.FullName));
            return list;
        }

        static IReadOnlyList<CodexEntryDef> RelicEntries()
        {
            var list = new List<CodexEntryDef>();
            foreach (var r in Relics.All) list.Add(E($"relic:{r.Kind}", r.Name));
            return list;
        }

        static IReadOnlyList<CodexEntryDef> Blueprints()
        {
            var list = new List<CodexEntryDef>();
            foreach (var m in Modules.All) list.Add(E($"blueprint:{m.Kind}", m.Name));
            return list;
        }

        static IReadOnlyList<CodexEntryDef> EventEntries()
        {
            var list = new List<CodexEntryDef>();
            var seen = new HashSet<GalaxyEventKind>();
            foreach (var e in GalaxyEvents.Rotation)
                if (seen.Add(e.Kind)) list.Add(E($"event:{e.Kind}", e.Name));
            return list;
        }

        static IReadOnlyList<CodexEntryDef> TwistEntries()
        {
            var list = new List<CodexEntryDef>();
            foreach (var t in Twists.Rotation) list.Add(E($"twist:{t.Kind}", t.Name));
            return list;
        }

        static IReadOnlyList<CodexEntryDef> ExpeditionEntries()
        {
            var list = new List<CodexEntryDef>();
            foreach (var x in Expeditions.All) list.Add(E($"exp:{x.Kind}", x.Name));
            return list;
        }

        public static readonly IReadOnlyList<CodexCategoryDef> Categories = new[]
        {
            new CodexCategoryDef
            {
                Id = "worlds", Name = "Worlds", How = "Send a fleet to one (gather, attack, escort or spy)",
                Entries = new[]
                {
                    E("world:Asteroid", "Gold asteroid"), E("world:Nebula", "Quartz nebula"), E("world:HeliumCloud", "Helium cloud"),
                    E("world:Derelict", "Derelict"), E("world:Camp", "Pirate camp"), E("world:DMField", "Dark Matter field"),
                    E("world:Comet", "Comet"), E("world:Caravan", "Trade caravan"), E("world:Lair", "Pirate Lord's lair"),
                },
                SkinId = "skin-aurora", SkinName = "Aurora World",
            },
            new CodexCategoryDef
            {
                Id = "fleet", Name = "Fleet", How = "Build one of each hull",
                Entries = Hulls(), SkinId = "skin-forge", SkinName = "Forge World",
            },
            new CodexCategoryDef
            {
                Id = "lords", Name = "Pirate Lords", How = "Beat them in their lairs (MORE › STORY)",
                Entries = Lords(), SkinId = "skin-obsidian", SkinName = "Obsidian Crown",
            },
            new CodexCategoryDef { Id = "relics", Name = "Relics", How = "Claim them in the Wilds, find them on expeditions", Entries = RelicEntries() },
            new CodexCategoryDef { Id = "blueprints", Name = "Blueprints", How = "Every Pirate Lord carries one", Entries = Blueprints() },
            new CodexCategoryDef { Id = "events", Name = "Galaxy events", How = "Live through each one", Entries = EventEntries() },
            new CodexCategoryDef
            {
                Id = "twists", Name = "Weekly twists", How = "Live through each week's twist",
                Entries = TwistEntries(), SkinId = "skin-solar", SkinName = "Solar Crown",
            },
            new CodexCategoryDef
            {
                Id = "dreadnoughts", Name = "Dreadnoughts", How = "Strike each kind of Pirate Dreadnought",
                Entries = new[]
                {
                    E("boss:Dreadnought", "Pirate Dreadnought"), E("boss:Carrier", "Pirate Carrier"),
                    E("boss:Siege", "Siege Dreadnought"), E("boss:Stealth", "Stealth Dreadnought"),
                },
            },
            new CodexCategoryDef { Id = "expeditions", Name = "Expeditions", How = "Bring each kind of expedition home", Entries = ExpeditionEntries() },
        };

        public static CodexCategoryDef? ById(string id)
        {
            foreach (var c in Categories) if (c.Id == id) return c;
            return null;
        }

        /// <summary>The earned skins' map tints (#rrggbb), for MapView.</summary>
        public static readonly IReadOnlyDictionary<string, string> SkinTints = new Dictionary<string, string>
        {
            ["skin-aurora"] = "#5CF0C8", ["skin-forge"] = "#FF8A3D", ["skin-obsidian"] = "#B03050", ["skin-solar"] = "#FFD84D",
        };
    }
}
