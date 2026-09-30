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
        // The Frontier (2026-09-28), appended so saved enum values stay put.
        CommandBastion,
        SalvageYard,
        DroneFactory,
        // The Frontier completed (2026-09-29): the three reserved pads and the top
        // row's gap got buildings — appended so saved enum values stay put.
        RepairDock,
        JumpGate,
        ClanEmbassy,
        Observatory,
        // The Citadel (2026-09-30), the new district on the north pole — appended.
        RelicVault,
        Academy,
        MissileSilo,
        TradeConsulate,
        Terraformer,
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
        Defense,    // command bastion (railguns + armoured docks at home)
        Salvage,    // salvage yard (resources back from wrecks)
        Drones,     // drone factory (harvester drones for the Wilds' deposits)
        Repair,     // repair dock (hulls lost defending home come back damaged)
        Gate,       // jump gate (faster, cheaper flights; a free jump every 24 h)
        Embassy,    // clan embassy (more wings, supply runs, clan tribute)
        Observatory, // deep space observatory (surveys, radar lead, dreadnought forecast)
        // The Citadel (2026-09-30):
        Vault,       // relic vault (Wilds relics on display, each a small permanent bonus)
        Academy,     // academy (commander XP, cheaper respecs, the commander leads a fleet)
        Silo,        // missile silo (fire on an inbound raid the radar has seen)
        Consulate,   // trade consulate (delivery contracts from other commanders)
        Terraform,   // terraformer (reshape the planet down one of four paths)
    }

    public sealed class BuildingDef
    {
        public string Name = "";
        /// <summary>A shorter name for the building's label on the globe, where a long
        /// one collides with its neighbours' (null = Name).</summary>
        public string? ShortName;
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
        /// <summary>Frontier buildings: the Command Center level that opens them (0 = from the start).</summary>
        public int UnlockCc;
        /// <summary>False for buildings the simulated commanders never build (their
        /// effect runs on the player's battle reports).</summary>
        public bool BotsBuild = true;
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
            BuildingId.CommandBastion,
            BuildingId.SalvageYard,
            BuildingId.DroneFactory,
            BuildingId.RepairDock,
            BuildingId.JumpGate,
            BuildingId.ClanEmbassy,
            BuildingId.Observatory,
            BuildingId.RelicVault,
            BuildingId.Academy,
            BuildingId.MissileSilo,
            BuildingId.TradeConsulate,
            BuildingId.Terraformer,
        };

        /// <summary>The nine buildings every colony starts with room for (the Command district).</summary>
        public static readonly IReadOnlyList<BuildingId> Core = new[]
        {
            BuildingId.CommandCenter, BuildingId.GoldMine, BuildingId.QuartzExtractor, BuildingId.HeliumRefinery,
            BuildingId.PowerPlant, BuildingId.Shipyard, BuildingId.Warehouse, BuildingId.ResearchLab,
            BuildingId.RadarStation,
        };

        /// <summary>Frontier buildings open later (UnlockCc) and sit in the Frontier district.</summary>
        public static bool IsFrontier(BuildingId id) => Defs[id].UnlockCc > 0 && !IsCitadel(id);

        /// <summary>The Citadel's five (2026-09-30), on the north pole.</summary>
        public static bool IsCitadel(BuildingId id) => id is BuildingId.RelicVault or BuildingId.Academy
            or BuildingId.MissileSilo or BuildingId.TradeConsulate or BuildingId.Terraformer;

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
            [BuildingId.CommandBastion] = new BuildingDef
            {
                Name = "Command Bastion",
                Desc = "Railguns and armoured docks. The railguns fire on raiders at your colony every round, " +
                       "aimed at the heaviest hulls and straight through shields, even with no fleet home.",
                Kind = BuildingKind.Defense,
                BaseEnergyUse = 30,
                BaseCost = new ResourceBag(800, 500, 200),
                BaseTimeSec = 300,
                MaxLevel = 30,
                UnlockCc = 6,
            },
            [BuildingId.SalvageYard] = new BuildingDef
            {
                Name = "Salvage Yard",
                Desc = "Crews strip the wrecks after your battles: part of every ship you lose, and of every " +
                       "raider shot down over your colony, comes back as resources to collect here.",
                Kind = BuildingKind.Salvage,
                BaseEnergyUse = 15,
                BaseCost = new ResourceBag(500, 350, 100),
                BaseTimeSec = 240,
                MaxLevel = 30,
                UnlockCc = 8,
                BotsBuild = false,
            },
            [BuildingId.DroneFactory] = new BuildingDef
            {
                Name = "Drone Factory",
                Desc = "Builds harvester drones for the Wilds. They fly out to your charted deposits and bring " +
                       "back what they carry: more drones, and bigger holds, every level.",
                Kind = BuildingKind.Drones,
                BaseEnergyUse = 25,
                BaseCost = new ResourceBag(1200, 900, 300),
                BaseTimeSec = 420,
                MaxLevel = 30,
                UnlockCc = 10,
                BotsBuild = false,
            },
            [BuildingId.RepairDock] = new BuildingDef
            {
                Name = "Repair Dock",
                Desc = "Tugs tow home the ships raiders shoot down over your colony. A share of them come back " +
                       "as damaged hulls you can repair for a fraction of their cost, instead of building anew.",
                Kind = BuildingKind.Repair,
                BaseEnergyUse = 20,
                BaseCost = new ResourceBag(700, 500, 150),
                BaseTimeSec = 300,
                MaxLevel = 30,
                UnlockCc = 9,
                BotsBuild = false,
            },
            [BuildingId.JumpGate] = new BuildingDef
            {
                Name = "Jump Gate",
                Desc = "Folds space for your fleets: faster flights that burn less helium, and a free jump " +
                       "that moves your colony every 24 hours — anywhere you choose from level 10.",
                Kind = BuildingKind.Gate,
                BaseEnergyUse = 30,
                BaseCost = new ResourceBag(900, 700, 400),
                BaseTimeSec = 360,
                MaxLevel = 30,
                UnlockCc = 7,
                BotsBuild = false,
            },
            [BuildingId.ClanEmbassy] = new BuildingDef
            {
                Name = "Clan Embassy",
                ShortName = "Embassy",
                Desc = "Your clan's hall on the colony: more clanmates fly with your strikes, supply runs " +
                       "come more often, and your share of the core's clan tribute grows.",
                Kind = BuildingKind.Embassy,
                BaseEnergyUse = 15,
                BaseCost = new ResourceBag(600, 600, 200),
                BaseTimeSec = 300,
                MaxLevel = 30,
                UnlockCc = 7,
                BotsBuild = false,
            },
            [BuildingId.Observatory] = new BuildingDef
            {
                Name = "Deep Space Observatory",
                ShortName = "Observatory",
                Desc = "Long-range telescopes: Wilds surveys finish sooner, your radar hears raiders earlier, " +
                       "and from level 10 it predicts where and when the next Pirate Dreadnought drops.",
                Kind = BuildingKind.Observatory,
                BaseEnergyUse = 20,
                BaseCost = new ResourceBag(800, 900, 250),
                BaseTimeSec = 360,
                MaxLevel = 30,
                UnlockCc = 11,
                BotsBuild = false,
            },
            // ---- the Citadel (2026-09-30), on the north pole ----
            [BuildingId.Academy] = new BuildingDef
            {
                Name = "Academy",
                Desc = "Where your commander trains: more commander XP, cheaper skill resets, and your commander " +
                       "can lead a fleet into battle for a bonus that grows with their level.",
                Kind = BuildingKind.Academy,
                BaseEnergyUse = 15,
                BaseCost = new ResourceBag(700, 700, 200),
                BaseTimeSec = 300,
                MaxLevel = 30,
                UnlockCc = 4,
                BotsBuild = false,
            },
            [BuildingId.RelicVault] = new BuildingDef
            {
                Name = "Relic Vault",
                Desc = "Puts the relics you find in the Wilds on display. Each kind gives a small permanent bonus " +
                       "for every copy you own, up to the vault's level.",
                Kind = BuildingKind.Vault,
                BaseEnergyUse = 15,
                BaseCost = new ResourceBag(800, 600, 200),
                BaseTimeSec = 300,
                MaxLevel = 30,
                UnlockCc = 5,
                BotsBuild = false,
            },
            [BuildingId.TradeConsulate] = new BuildingDef
            {
                Name = "Trade Consulate",
                ShortName = "Consulate",
                Desc = "Other commanders post delivery contracts here: load Haulers with what they need, fly it " +
                       "to them, and come home with more than you sent.",
                Kind = BuildingKind.Consulate,
                BaseEnergyUse = 15,
                BaseCost = new ResourceBag(900, 600, 300),
                BaseTimeSec = 330,
                MaxLevel = 30,
                UnlockCc = 6,
                BotsBuild = false,
            },
            [BuildingId.MissileSilo] = new BuildingDef
            {
                Name = "Missile Silo",
                ShortName = "Silo",
                Desc = "When your radar sees a raid coming, FIRE: a missile salvo destroys part of the raiding " +
                       "fleet before it lands. It reloads, faster at higher levels.",
                Kind = BuildingKind.Silo,
                BaseEnergyUse = 30,
                BaseCost = new ResourceBag(1000, 700, 500),
                BaseTimeSec = 360,
                MaxLevel = 30,
                UnlockCc = 7,
                BotsBuild = false,
            },
            [BuildingId.Terraformer] = new BuildingDef
            {
                Name = "Terraformer",
                Desc = "Reshapes your planet down one of four paths — Oceanic (helium), Crystalline (quartz), " +
                       "Metallic (gold) or Temperate (faster builds, more energy) — a stage at a time.",
                Kind = BuildingKind.Terraform,
                BaseEnergyUse = 40,
                BaseCost = new ResourceBag(1500, 1200, 600),
                BaseTimeSec = 480,
                MaxLevel = 30,
                UnlockCc = 9,
                BotsBuild = false,
            },
        };
    }
}
