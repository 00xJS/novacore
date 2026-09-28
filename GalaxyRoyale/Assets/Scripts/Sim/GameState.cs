// The whole world state. Direct port of v1's `src/sim/GameState.ts`.
// Mutable classes throughout — the sim mutates fields in place each tick, same as v1.
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Map;

namespace GalaxyRoyale.Sim
{
    public enum MarchPhase { Outbound, Gathering, Returning }
    public enum MarchMission { Gather, Attack, Spy }

    public sealed class March
    {
        public int Id;
        public MarchPhase Phase;
        public Dictionary<HullId, int> Ships = new();
        /// <summary>The destination node's tile.</summary>
        public TileXY Node;
        /// <summary>Current leg endpoints, sub-tile precision (recalls start mid-flight).</summary>
        public Position LegFrom;
        public Position LegTo;
        public int DepartedAtTick;
        public int ArrivesAtTick;
        /// <summary>Milli-units on board.</summary>
        public ResourceBag Cargo = new();
        /// <summary>Total milli-helium charged at launch (for recall refund math).</summary>
        public int HeliumSpent;
        /// <summary>Dark Matter on board in milli (v13; DM Field gathering).</summary>
        public long CargoDm;
        public MarchMission Mission;
        /// <summary>True once this march was RECALLED before reaching its target — it
        /// never landed, so no raid/spy resolves against the destination (the
        /// aborted-attack bug: a recalled fleet used to still file a victory).</summary>
        public bool Recalled;
    }

    /// <summary>Production building types that can have multiple instances.</summary>
    public enum MineType { GoldMine, QuartzExtractor, HeliumRefinery }

    public static class MineTypes
    {
        public static readonly IReadOnlyList<MineType> All =
            new[] { MineType.GoldMine, MineType.QuartzExtractor, MineType.HeliumRefinery };

        public static BuildingId ToBuildingId(MineType t) => t switch
        {
            MineType.GoldMine        => BuildingId.GoldMine,
            MineType.QuartzExtractor => BuildingId.QuartzExtractor,
            MineType.HeliumRefinery      => BuildingId.HeliumRefinery,
            _ => BuildingId.GoldMine,
        };
    }

    /// <summary>An additional mine/extractor/refinery beyond the singleton (instance #1).</summary>
    public sealed class ExtraMine
    {
        public int Id;
        public MineType Type;
        public int Level;
        /// <summary>Which expansion plot it occupies on the base.</summary>
        public int Plot;
    }

    /// <summary>Persistent changes to a generated map node (the static map derives from seed).</summary>
    public sealed class NodeOverride
    {
        /// <summary>Remaining gatherable in milli-units. Null = untouched.</summary>
        public int? Remaining;
        public bool Cleared;
        /// <summary>Tick at which a depleted/cleared node refreshes/relocates. 0 = n/a.</summary>
        public int RespawnAtTick;
        /// <summary>A static node that has relocated away — permanently gone from its seed tile.</summary>
        public bool Retired;
    }

    /// <summary>Intel and battle results kept in the player's mailbox.</summary>
    public abstract class MailItem
    {
        public int Id;
        public int AtTick;
        public TileXY Target;
        public string Subject = "";
        public bool Read;
        /// <summary>Favorited reports are protected from the 50-item ring buffer + delete.</summary>
        public bool Favorite;
    }

    public sealed class SpyIntel
    {
        /// <summary>NodeKind or "empty".</summary>
        public NodeKind? Kind;
        public int? Tier;
        /// <summary>Milli-units left at a resource node.</summary>
        public int? Remaining;
        public Dictionary<HullId, int>? Garrison;
        public int? CampLevel;
        /// <summary>Rival-commander recon only: their full tech stack (completed levels).</summary>
        public Dictionary<TechId, int>? Research;
        /// <summary>Rival-commander recon only: their building levels.</summary>
        public Dictionary<BuildingId, int>? Buildings;
        /// <summary>Rival-commander recon only: unshielded (raidable) wallet at scan time, milli.</summary>
        public ResourceBag? LootableMilli;
        /// <summary>Rival-commander recon only: warehouse-shielded portion at scan time, milli.</summary>
        public ResourceBag? ProtectedMilli;
    }

    public sealed class SpyReport : MailItem
    {
        public SpyIntel Intel = new();
    }

    public sealed class BattleMailReport : MailItem
    {
        public BattleReport Report = new();
        /// <summary>True when the PLAYER was the defender (a rival raided the
        /// colony). The report's Attacker side is then the rival, so the mailbox
        /// must flip perspective — it used to show a lost raid as a green
        /// "VICTORY" with the raider's ships under YOUR FLEET.</summary>
        public bool Defending;
        /// <summary>Defense reports: the raiding bot's id, for STRIKE BACK
        /// (0 = unknown — reports filed before the field existed).</summary>
        public int AttackerBotId;
    }

    /// <summary>
    /// Radar Station early warning: an inbound spy probe or war fleet was detected.
    /// Detail fields are filled per the radar's DetailTier at detection time —
    /// missing fields simply weren't visible to that radar level.
    /// </summary>
    public sealed class RadarWarning : MailItem
    {
        /// <summary>True = war fleet inbound; false = spy probe (only known at DetailTier ≥ 2).</summary>
        public bool? IsFleet;
        /// <summary>Sim tick when the contact reaches the player's base.</summary>
        public int ArrivesAtTick;
        /// <summary>Attacker commander name (DetailTier ≥ 3).</summary>
        public string? AttackerName;
        /// <summary>Total inbound ship count (DetailTier ≥ 3).</summary>
        public int? FleetCount;
        /// <summary>Full hull composition (DetailTier ≥ 4).</summary>
        public Dictionary<HullId, int>? FleetComp;
    }

    public sealed class BuildOrder
    {
        public BuildingId Building;
        public int ToLevel;
        public int EndsAtTick;
        /// <summary>When present, the order targets an extra-mine instance rather than the singleton.</summary>
        public int? MineId;
    }

    public sealed class ShipOrder
    {
        public HullId Hull;
        public int Remaining;
        public int NextDoneAtTick;
    }

    public sealed class ResearchOrder
    {
        public TechId TechId;
        public int ToLevel;
        public int EndsAtTick;
    }

    public sealed class InventoryEntry
    {
        public string ItemId = "";
        public int Count;
    }

    public sealed class BuildingSlot
    {
        public int Level;
    }

    public sealed class MapState
    {
        public Dictionary<string, NodeOverride> NodeOverrides = new();
        /// <summary>Nodes that respawned to a new tile (off the seed grid); ids are `dyn-N`.</summary>
        public List<MapNode> DynamicNodes = new();
        public int NextDynId = 1;
    }

    public sealed class Profile
    {
        public string Name = "Commander";
        public int AvatarSeed;
    }

    public sealed class Premium
    {
        /// <summary>Premium currency — whole units, no milli.</summary>
        public int DarkMatter;
    }

    /// <summary>Timed effects — absolute tick stamps, 0 = inactive.</summary>
    public sealed class Buffs
    {
        public int ProdBoostUntilTick;
        public int ExtraBuildSlotUntilTick;
        public int EnergyBoostUntilTick;
        public int ExtraResearchSlotUntilTick;
        /// <summary>Aegis Shield: while active nobody can target this planet with a
        /// raid, and inbound fleets deflect at arrival. Launching a raid drops it.</summary>
        public int ShieldUntilTick;
    }

    public sealed class Skins
    {
        public List<string> Owned = new() { "default" };
        public string ActivePlanet = "default";
    }

    public sealed class Stats
    {
        public int BattlesWon;
        public int BattlesLost;
        public int MarchesSent;
    }

    public sealed class GameState
    {
        public int Tick;
        public int Seed;
        /// <summary>Current home-planet tile — movable via relocation items.</summary>
        public TileXY HomeTile;
        /// <summary>The planet burns on the map until this tick after LOSING a defense
        /// (Balance.BurnDurationSec) — battle scars visible to the whole galaxy.</summary>
        public int BurningUntilTick;
        /// <summary>Mixed into the base-view planet's visual seed. 0 = the natural
        /// look; the Planetary Resurfacing item rerolls it (one-way — rolls are
        /// random and the old surface can't be rolled back).</summary>
        public int VisualSeedOffset;
        /// <summary>Playtest economy, chosen at NEW GAME (user request 2026-09-27):
        /// 500K of every resource, 1M Dark Matter, speed-ups, free warps. Saves
        /// from before the choice existed were all test games.</summary>
        public bool TestMode;
        /// <summary>Commander's Path progress — index of the next quest in
        /// Quests.Chain (runs in both modes).</summary>
        public int QuestStep;
        public Profile Profile = new();
        /// <summary>Milli-units.</summary>
        public ResourceBag Resources = new();
        public Premium Premium = new();
        public Dictionary<BuildingId, BuildingSlot> Buildings = new();
        /// <summary>Where each building sits on the base planet scene (logical px).</summary>
        public Dictionary<BuildingId, TileXY> BuildingLayout = new();
        /// <summary>Concurrent build slots (base 2 — the 2026-07-08 standard; +1
        /// while the ExtraBuildSlot buff unlocks the third line).</summary>
        public List<BuildOrder> BuildQueue = new();
        public List<ExtraMine> ExtraMines = new();
        public int NextMineId = 1;
        public Dictionary<HullId, int> Ships = new();
        public List<ShipOrder> ShipQueue = new();
        public List<March> Marches = new();
        public int NextMarchId = 1;
        public MapState Map = new();
        /// <summary>Tech levels by id (absent/0 = not researched).</summary>
        public Dictionary<TechId, int> Research = new();
        public List<ResearchOrder> ResearchQueue = new();
        public List<InventoryEntry> Inventory = new();
        public Buffs Buffs = new();
        public List<MailItem> Mailbox = new();
        public int NextReportId = 1;
        public Skins Skins = new();
        public Stats Stats = new();

        /// <summary>Default plot positions on the base planet scene (390×844 logical space).</summary>
        public static Dictionary<BuildingId, TileXY> DefaultLayout() => new()
        {
            [BuildingId.CommandCenter]    = new TileXY(195, 400),
            [BuildingId.GoldMine]        = new TileXY( 90, 480),
            [BuildingId.QuartzExtractor] = new TileXY(300, 480),
            [BuildingId.HeliumRefinery]      = new TileXY( 75, 590),
            [BuildingId.PowerPlant]       = new TileXY(315, 590),
            [BuildingId.Shipyard]         = new TileXY(140, 675),
            [BuildingId.Warehouse]        = new TileXY(255, 675),
            [BuildingId.ResearchLab]      = new TileXY(195, 300),
            [BuildingId.RadarStation]     = new TileXY(105, 300),
        };

        /// <summary>Factory for a fresh save. Matches v1's createNewGame.</summary>
        public static GameState CreateNewGame(int seed, bool? testMode = null)
        {
            bool test = testMode ?? Balance.TestModeDefault;
            var startLevels = new Dictionary<BuildingId, int>
            {
                [BuildingId.CommandCenter] = 1,
                [BuildingId.GoldMine]     = 1,
                [BuildingId.PowerPlant]    = 1,
            };

            var buildings = new Dictionary<BuildingId, BuildingSlot>();
            foreach (var id in Data.Buildings.All)
                buildings[id] = new BuildingSlot { Level = startLevels.TryGetValue(id, out var l) ? l : 0 };

            var ships = new Dictionary<HullId, int>();
            foreach (var h in Data.Ships.All)
                ships[h] = 0;

            var state = new GameState
            {
                Tick = 0,
                Seed = seed,
                HomeTile = Balance.HomeTile,
                // AvatarSeed 0 = the default style; players restyle via the
                // profile avatar picker (was seed-derived — user wants a stable default).
                Profile = new Profile { Name = "Commander", AvatarSeed = 0 },
                Resources = Balance.StartResources().Milli(),
                Premium = new Premium
                {
                    DarkMatter = test ? Balance.TestModeDarkMatter : 0,
                },
                TestMode = test,
                Buildings = buildings,
                BuildingLayout = DefaultLayout(),
                Ships = ships,
            };

            if (test)
            {
                // Testing economy: fat wallet + a stack of every speed-up token.
                state.Resources = new ResourceBag(
                    Balance.TestModeResources, Balance.TestModeResources, Balance.TestModeResources).Milli();
                foreach (var item in Shop.Items)
                    if (item.Effect == ShopEffect.Speedup)
                        state.Inventory.Add(new InventoryEntry
                            { ItemId = item.Id, Count = Balance.TestModeSpeedupCount });
            }
            return state;
        }
    }
}
