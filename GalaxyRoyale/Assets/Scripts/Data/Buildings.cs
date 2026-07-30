// Building definitions and IDs. Direct port of iGalaxy v1's `src/data/buildings.ts`.
// Curves live in Balance.cs — each building specifies only its type and base numbers.
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public enum BuildingId
    {
        CommandCenter,
        GoldMine,
        QuartzExtractor,
        HeliumRefinery,
        PowerPlant,
        Shipyard,
        Warehouse,
        ResearchLab,
        RadarStation,
    }

    public enum BuildingKind
    {
        Producer,   // gold / quartz / helium mines
        Energy,     // power plant
        Storage,    // warehouse
        Ship,       // shipyard
        Research,   // lab
        Command,    // command center (gates every other level)
        Radar,      // radar station (incoming-threat warnings + probe speed)
    }

    public sealed class BuildingDef
    {
        public string Name = "";
        /// <summary>One-line flavor/utility blurb shown in the building panel (v1 desc).</summary>
        public string Desc = "";
        public BuildingKind Kind;
        /// <summary>Per-hour production at L=1 (for producers).</summary>
        public int BaseProdPerHour;
        /// <summary>Storage bonus at L=1 (for storage).</summary>
        public int BaseStorageBonus;
        /// <summary>Energy consumed at L=1 (for anything that pulls energy).</summary>
        public int BaseEnergyUse;
        /// <summary>Ship build-time discount (for shipyard).</summary>
        public float ShipBuildTimeMult = 1f;
        /// <summary>Base upgrade cost at L=1.</summary>
        public ResourceBag BaseCost = new();
        /// <summary>Base upgrade time at L=1 in seconds.</summary>
        public int BaseTimeSec;
        /// <summary>Max level the CC caps everything else at.</summary>
        public int MaxLevel = 30;
        /// <summary>The resource id this producer yields.</summary>
        public string? Resource;
    }

    public static class Buildings
    {
        public static readonly IReadOnlyList<BuildingId> All = new[]
        {
            BuildingId.CommandCenter,
            BuildingId.GoldMine,
            BuildingId.QuartzExtractor,
            BuildingId.HeliumRefinery,
            BuildingId.PowerPlant,
            BuildingId.Shipyard,
            BuildingId.Warehouse,
            BuildingId.ResearchLab,
            BuildingId.RadarStation,
        };

        public static readonly IReadOnlyDictionary<BuildingId, BuildingDef> Defs =
            new Dictionary<BuildingId, BuildingDef>
        {
            [BuildingId.CommandCenter] = new BuildingDef
            {
                Name = "Command Center",
                Desc = "Caps the level of every other building. Upgrade to expand your colony.",
                Kind = BuildingKind.Command,
                BaseCost = new ResourceBag(400, 200, 0),
                BaseTimeSec = 60,
                MaxLevel = 30,
            },
            [BuildingId.GoldMine] = new BuildingDef
            {
                Name = "Gold Mine",
                Desc = "Extracts Gold from the planetary crust.",
                Kind = BuildingKind.Producer,
                Resource = "gold",
                BaseProdPerHour = 30,
                BaseEnergyUse = 10,
                BaseCost = new ResourceBag(60, 15, 0),
                BaseTimeSec = 30,
                MaxLevel = 30,
            },
            [BuildingId.QuartzExtractor] = new BuildingDef
            {
                Name = "Quartz Extractor",
                Desc = "Harvests Quartz veins for advanced components.",
                Kind = BuildingKind.Producer,
                Resource = "quartz",
                BaseProdPerHour = 20,
                BaseEnergyUse = 10,
                BaseCost = new ResourceBag(48, 24, 0),
                BaseTimeSec = 35,
                MaxLevel = 30,
            },
            [BuildingId.HeliumRefinery] = new BuildingDef
            {
                Name = "Helium Refinery",
                Desc = "Synthesizes Helium — the fuel every fleet burns to fly.",
                Kind = BuildingKind.Producer,
                Resource = "helium",
                BaseProdPerHour = 10,
                BaseEnergyUse = 20,
                BaseCost = new ResourceBag(225, 75, 0),
                BaseTimeSec = 40,
                MaxLevel = 30,
            },
            [BuildingId.PowerPlant] = new BuildingDef
            {
                Name = "Power Plant",
                Desc = "Generates Energy. Shortfalls slow all production.",
                Kind = BuildingKind.Energy,
                BaseCost = new ResourceBag(75, 30, 0),
                BaseTimeSec = 30,
                MaxLevel = 30,
            },
            [BuildingId.Shipyard] = new BuildingDef
            {
                Name = "Shipyard",
                Desc = "Builds ships. Higher levels unlock heavier hulls.",
                Kind = BuildingKind.Ship,
                BaseEnergyUse = 20,
                ShipBuildTimeMult = 1f,
                BaseCost = new ResourceBag(400, 200, 100),
                BaseTimeSec = 240,
                MaxLevel = 30,
            },
            [BuildingId.Warehouse] = new BuildingDef
            {
                Name = "Warehouse",
                Desc = "Shields a portion of your resources from raiders. Resources are otherwise uncapped.",
                Kind = BuildingKind.Storage,
                BaseStorageBonus = 5000,
                BaseCost = new ResourceBag(1000, 0, 0),
                BaseTimeSec = 60,
                MaxLevel = 30,
            },
            [BuildingId.ResearchLab] = new BuildingDef
            {
                Name = "Research Lab",
                Desc = "Unlocks the tech tree. Its level caps how far each technology can advance.",
                Kind = BuildingKind.Research,
                BaseEnergyUse = 20,
                BaseCost = new ResourceBag(200, 400, 100),
                BaseTimeSec = 180,
                MaxLevel = 30,
            },
            [BuildingId.RadarStation] = new BuildingDef
            {
                Name = "Radar Station",
                Desc = "Early-warning array. Detects inbound spy probes and war fleets — " +
                       "each level warns 30s earlier and sharpens the picture. Your own probes fly faster too.",
                Kind = BuildingKind.Radar,
                BaseEnergyUse = 15,
                BaseCost = new ResourceBag(600, 400, 150),
                BaseTimeSec = 300,
                MaxLevel = 30,
            },
        };
    }
}
