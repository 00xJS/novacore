// The globe base (2026-09-28): every building has a fixed pad on the home
// planet, placed by latitude and longitude. Four districts sit around the
// northern band: Command (the nine core buildings), the Mining Belt to the east
// (a pad for every extra mine the Command Center can unlock: one column per
// unlock, one row per resource, each row in line with that resource's first
// mine), the Frontier to the west (the buildings that open later — the Command
// Bastion, the Salvage Yard and the Drone Factory, with the Exchange Terminal
// as the Market's home — and reserved pads) and, on the far side, the
// Spaceport where the docked fleet parks (2026-09-29). The southern half is
// the Wilds (Sim/Wilds.cs).
//
// Pure data; BuildingMarkers draws it. A mine's pad follows from the order its
// type's mines were built, so saves need no new fields.
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim
{
    public enum BaseDistrict { Command, MiningBelt, Frontier, Spaceport, Wilds, Citadel }

    public enum PadKind
    {
        /// <summary>A building: the nine core ones, and the Frontier's once they're in the game.</summary>
        Building,
        /// <summary>An extra mine's pad in the Mining Belt.</summary>
        Mine,
        /// <summary>A building that isn't in the game yet, shown as a hologram.</summary>
        Planned,
        /// <summary>Kept free for buildings in later updates.</summary>
        Reserved,
    }

    public enum PlannedBuilding { None, ExchangeTerminal }

    public sealed class BasePad
    {
        public string Key = "";
        public PadKind Kind;
        public BaseDistrict District;
        /// <summary>Degrees north of the equator.</summary>
        public double Lat;
        /// <summary>Degrees east of the Command Center's meridian.</summary>
        public double Lon;
        /// <summary>Building pads: which building. Mine pads: the mine's building type.</summary>
        public BuildingId Building;
        public MineType Mine;
        /// <summary>Mine pads: 0..3, the pad for the 2nd..5th mine of its type.</summary>
        public int Tier;
        /// <summary>The Command Center level that opens the pad (0 = from the start).</summary>
        public int UnlockCc;
        public PlannedBuilding Planned;
        public string Name = "";
    }

    public static class BaseLayout
    {
        /// <summary>The three rows every district shares; each resource keeps one.</summary>
        public const double TopRow = 45, MiddleRow = 27, BottomRow = 9;
        /// <summary>How far the Command district's side columns sit from its middle one.</summary>
        public const double CoreSpread = 20;
        /// <summary>The belt's columns, west to east: one per Command Center unlock.</summary>
        public static readonly double[] BeltLon = { 67.5, 82.5, 97.5, 112.5 };

        /// <summary>The longitude a district faces the camera at (the Wilds spin freely).</summary>
        public static double DistrictLon(BaseDistrict d) => d switch
        {
            BaseDistrict.MiningBelt => 90,
            BaseDistrict.Frontier => -90,
            BaseDistrict.Spaceport => 180,
            _ => 0,
        };

        public static string DistrictName(BaseDistrict d) => d switch
        {
            BaseDistrict.MiningBelt => "Mining Belt",
            BaseDistrict.Frontier => "Frontier",
            BaseDistrict.Spaceport => "Spaceport",
            BaseDistrict.Wilds => "The Wilds",
            BaseDistrict.Citadel => "The Citadel",
            _ => "Command District",
        };

        /// <summary>The districts around the northern band, west to east of Command and round the back.</summary>
        public static readonly IReadOnlyList<BaseDistrict> NorthBand = new[]
        {
            BaseDistrict.Command, BaseDistrict.MiningBelt, BaseDistrict.Frontier, BaseDistrict.Spaceport,
        };

        /// <summary>The Citadel (2026-09-30) crowns the north pole: four pads on a ring at
        /// this latitude, and the Terraformer on the pole itself.</summary>
        public const double CitadelRing = 71;

        /// <summary>The Spaceport's landing field: its centre and half its span (degrees).</summary>
        public const double PortLat = MiddleRow, PortLon = 180, PortSpan = 21;

        /// <summary>Gold on the top row, quartz in the middle, helium at the bottom:
        /// the same rows as the first mine of each in the Command district.</summary>
        public static double MineRow(MineType type) => type switch
        {
            MineType.GoldMine => TopRow,
            MineType.QuartzExtractor => MiddleRow,
            _ => BottomRow,
        };

        public static readonly IReadOnlyList<BasePad> Pads = Build();

        static List<BasePad> Build()
        {
            var pads = new List<BasePad>();
            void Core(BuildingId id, double lat, double lon) => pads.Add(new BasePad
            {
                Key = id.ToString(), Kind = PadKind.Building, District = BaseDistrict.Command,
                Lat = lat, Lon = lon, Building = id, Name = Buildings.Defs[id].Name,
            });
            Core(BuildingId.ResearchLab, TopRow, -CoreSpread);
            Core(BuildingId.RadarStation, TopRow, 0);
            Core(BuildingId.GoldMine, TopRow, CoreSpread);
            Core(BuildingId.PowerPlant, MiddleRow, -CoreSpread);
            Core(BuildingId.CommandCenter, MiddleRow, 0);
            Core(BuildingId.QuartzExtractor, MiddleRow, CoreSpread);
            Core(BuildingId.Warehouse, BottomRow, -CoreSpread);
            Core(BuildingId.Shipyard, BottomRow, 0);
            Core(BuildingId.HeliumRefinery, BottomRow, CoreSpread);

            for (int tier = 0; tier < BeltLon.Length; tier++)
                foreach (var type in MineTypes.All)
                {
                    var id = MineTypes.ToBuildingId(type);
                    pads.Add(new BasePad
                    {
                        Key = $"{type}#{tier + 2}", Kind = PadKind.Mine, District = BaseDistrict.MiningBelt,
                        Lat = MineRow(type), Lon = BeltLon[tier], Building = id, Mine = type, Tier = tier,
                        UnlockCc = Balance.MineSlotUnlocks[tier + 1],
                        Name = $"{Buildings.Defs[id].Name} #{tier + 2}",
                    });
                }

            void Plan(PlannedBuilding b, string name, double lat, double lon, int cc) => pads.Add(new BasePad
            {
                Key = b.ToString(), Kind = PadKind.Planned, District = BaseDistrict.Frontier,
                Lat = lat, Lon = lon, Planned = b, UnlockCc = cc, Name = name,
            });
            // Labelled "Exchange" on the globe (its MARKET tag says the rest): the full
            // name ran into the Jump Gate's label. The top row is spread for the gate too.
            Plan(PlannedBuilding.ExchangeTerminal, "Exchange", TopRow, -114, 5);
            void Frontier(BuildingId id, double lat, double lon) => pads.Add(new BasePad
            {
                Key = id.ToString(), Kind = PadKind.Building, District = BaseDistrict.Frontier,
                Lat = lat, Lon = lon, Building = id, Name = Buildings.Defs[id].ShortName ?? Buildings.Defs[id].Name,
                UnlockCc = Buildings.Defs[id].UnlockCc,
            });
            Frontier(BuildingId.CommandBastion, TopRow, -66);
            Frontier(BuildingId.DroneFactory, BottomRow, -106);
            Frontier(BuildingId.SalvageYard, BottomRow, -74);
            // The Frontier completed (2026-09-29): the three reserved pads of the
            // middle row and the top row's gap (App Store rule: nothing "coming soon").
            Frontier(BuildingId.JumpGate, TopRow, -90);
            Frontier(BuildingId.ClanEmbassy, MiddleRow, -112);
            Frontier(BuildingId.Observatory, MiddleRow, -90);
            Frontier(BuildingId.RepairDock, MiddleRow, -69); // in from the screen's edge

            void Citadel(BuildingId id, double lat, double lon) => pads.Add(new BasePad
            {
                Key = id.ToString(), Kind = PadKind.Building, District = BaseDistrict.Citadel,
                Lat = lat, Lon = lon, Building = id, Name = Buildings.Defs[id].ShortName ?? Buildings.Defs[id].Name,
                UnlockCc = Buildings.Defs[id].UnlockCc,
            });
            Citadel(BuildingId.Academy, CitadelRing, 0);
            Citadel(BuildingId.RelicVault, CitadelRing, 90);
            Citadel(BuildingId.TradeConsulate, CitadelRing, 180);
            Citadel(BuildingId.MissileSilo, CitadelRing, -90);
            Citadel(BuildingId.Terraformer, 89.5, 0);
            return pads;
        }

        public static BasePad BuildingPad(BuildingId id)
        {
            foreach (var p in Pads) if (p.Kind == PadKind.Building && p.Building == id) return p;
            throw new KeyNotFoundException(id.ToString());
        }

        public static BasePad MinePad(MineType type, int tier)
        {
            foreach (var p in Pads) if (p.Kind == PadKind.Mine && p.Mine == type && p.Tier == tier) return p;
            throw new KeyNotFoundException($"{type} tier {tier}");
        }

        /// <summary>The belt pad an extra mine stands on: how many of its type were built before it.</summary>
        public static int MineTier(GameState state, ExtraMine mine)
        {
            int tier = 0;
            foreach (var m in state.ExtraMines)
                if (m.Type == mine.Type && m.Id < mine.Id) tier++;
            return tier;
        }

        /// <summary>The extra mine on a belt pad, or null while it's empty.</summary>
        public static ExtraMine? MineOn(GameState state, BasePad pad)
        {
            if (pad.Kind != PadKind.Mine) return null;
            foreach (var m in state.ExtraMines)
                if (m.Type == pad.Mine && MineTier(state, m) == pad.Tier) return m;
            return null;
        }

        public static bool Unlocked(GameState state, BasePad pad) => pad.Kind switch
        {
            PadKind.Reserved => false,
            _ => state.Buildings[BuildingId.CommandCenter].Level >= pad.UnlockCc,
        };

        /// <summary>Belt pads that are unlocked and still empty: where the next mines can go.</summary>
        public static int OpenPads(GameState state, BaseDistrict district)
        {
            int open = 0;
            foreach (var p in Pads)
                if (p.District == district && p.Kind == PadKind.Mine && Unlocked(state, p) && MineOn(state, p) == null)
                    open++;
            return open;
        }

        /// <summary>Built pads and all the pads a district will ever have (reserved ones aside).</summary>
        public static (int built, int total) Count(GameState state, BaseDistrict district)
        {
            int built = 0, total = 0;
            foreach (var p in Pads)
            {
                if (p.District != district || p.Kind == PadKind.Reserved) continue;
                total++;
                bool isBuilt = p.Kind switch
                {
                    PadKind.Building => state.Buildings[p.Building].Level > 0,
                    PadKind.Mine => MineOn(state, p) is { Level: > 0 },
                    PadKind.Planned => PlannedOnline(state, p.Planned),
                    _ => false,
                };
                if (isBuilt) built++;
            }
            return (built, total);
        }

        /// <summary>The one planned building that already works: the Exchange
        /// Terminal is the Market's home from Command Center 5.</summary>
        public static bool PlannedOnline(GameState state, PlannedBuilding b) =>
            b == PlannedBuilding.ExchangeTerminal
            && state.Buildings[BuildingId.CommandCenter].Level >= BuildingPadFor(b).UnlockCc;

        static BasePad BuildingPadFor(PlannedBuilding b)
        {
            foreach (var p in Pads) if (p.Kind == PadKind.Planned && p.Planned == b) return p;
            throw new KeyNotFoundException(b.ToString());
        }

        public static BasePad PlannedPad(PlannedBuilding b) => BuildingPadFor(b);
    }
}
