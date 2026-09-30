// Dark Matter store catalogue. Direct port of v1's `src/data/shop.ts` —
// ids, names, prices and grants must match EXACTLY: inventory entries in
// the shared v12 save format reference these ids, so any drift breaks
// cross-client saves. The only structural change is the typed ShopEffect
// enum replacing v1's stringly-typed effect dispatch in ShopSystem.
using System;
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public enum ShopCategory
    {
        Resources,
        Speedups,
        Buffs,
        Skins,
    }

    public enum ShopEffect
    {
        None,
        ResourceGrant,          // adds resources to state on use
        Speedup,                // shaves N seconds off the most relevant timer
        FinishAny,              // Chrono Catalyst — completes soonest queue item
        FinishBuild,            // finishes current building/mine upgrade
        FinishShips,            // finishes the head ship order
        FinishResearch,         // finishes current research
        ProdBoost,              // timed production multiplier
        EnergyBoost,            // timed energy supply multiplier
        ExtraBuildSlot,         // timed extra concurrent building slot
        ExtraResearchSlot,      // timed extra concurrent research slot
        Skin,                   // planet skin (buy unlocks; never in inventory)
        RelocateRandom,         // Blind Jump — warp home to a random empty tile
        RelocateTarget,         // Precision Warp — applied via map targeting, not UseItem
        ChangeCommanderName,    // rename yourself for a fee
        PlanetShield,           // Aegis Shield — timed raid immunity (breaks if you raid)
        RerollPlanetLook,       // Planetary Resurfacing — reroll the base-view planet surface
    }

    public sealed class ShopItemDef
    {
        public string Id = "";
        public string Name = "";
        public string Description = "";
        public ShopCategory Category;
        /// <summary>Dark Matter price (whole units).</summary>
        public int PriceDM;
        public ShopEffect Effect;
        /// <summary>For ResourceGrant: whole units credited on use.</summary>
        public ResourceBag? ResourceGrant;
        /// <summary>For Speedup: seconds to shave.</summary>
        public int? SpeedupSec;
        /// <summary>For PlanetShield: protection duration in seconds.</summary>
        public int? ShieldSec;

        /// <summary>Skins unlock permanently on buy; everything else lands in the inventory.</summary>
        public bool IsSkin => Effect == ShopEffect.Skin;
    }

    public static class Shop
    {
        /// <summary>Quick-select resource pack amounts (whole units). All resource
        /// buys are generated from these at the DMPerK rate so pricing is
        /// consistent and ascending (the old fixed "caches" were flat-priced and
        /// ended up costing MORE DM for LESS resource than the packs).</summary>
        public static readonly int[] ResourcePresetAmounts = { 1_000, 10_000, 100_000, 1_000_000, 10_000_000 };

        /// <summary>Dark Matter per 1,000 whole units, by resource (helium priciest) — v1 DM_PER_K.</summary>
        public static int DMPerK(ResourceId res) => res switch
        {
            ResourceId.Gold   => 2,
            ResourceId.Quartz => 3,
            ResourceId.Helium     => 4,
            _ => 2,
        };

        public static int ResourcePresetPrice(ResourceId res, int amount) =>
            (int)Math.Ceiling(amount / 1000.0 * DMPerK(res));

        /// <summary>Item id for a preset resource pack — v1 resourcePackId.</summary>
        public static string ResourcePackId(ResourceId res, int amount) =>
            $"res-{res.ToString().ToLowerInvariant()}-{amount}";

        public static readonly IReadOnlyList<ShopItemDef> Items = BuildItems();

        /// <summary>Catalogue keyed by id — v1 SHOP_ITEMS.</summary>
        public static readonly IReadOnlyDictionary<string, ShopItemDef> ById = BuildIndex();

        static IReadOnlyDictionary<string, ShopItemDef> BuildIndex()
        {
            var map = new Dictionary<string, ShopItemDef>();
            foreach (var item in Items) map[item.Id] = item;
            return map;
        }

        static IReadOnlyList<ShopItemDef> BuildItems()
        {
            var items = new List<ShopItemDef>
            {
                // Resource buys are ALL generated below (consistent DMPerK pricing,
                // ascending). The old flat-priced "caches" were removed — they cost
                // more DM for less resource than the packs, which made no sense.

                // ---- finishers ----
                new() { Id = "finish-build", Name = "Nano Crew",
                    Description = "Instantly completes the current building upgrade",
                    Category = ShopCategory.Speedups, PriceDM = 30, Effect = ShopEffect.FinishBuild },
                new() { Id = "finish-ships", Name = "Assembly Surge",
                    Description = "Instantly completes the current ship order",
                    Category = ShopCategory.Speedups, PriceDM = 25, Effect = ShopEffect.FinishShips },
                new() { Id = "finish-research", Name = "Insight Burst",
                    Description = "Instantly completes the current research",
                    Category = ShopCategory.Speedups, PriceDM = 35, Effect = ShopEffect.FinishResearch },
                new() { Id = "finish-any", Name = "Chrono Catalyst",
                    Description = "Instantly completes your most imminent building, research or ship",
                    Category = ShopCategory.Speedups, PriceDM = 50, Effect = ShopEffect.FinishAny },

                // ---- buffs ----
                new() { Id = "boost-prod", Name = "Overclock Matrix", Description = "+25% production for 1 hour",
                    Category = ShopCategory.Buffs, PriceDM = 80, Effect = ShopEffect.ProdBoost },
                new() { Id = "buff-buildslot", Name = "Construction Overdrive",
                    Description = "+1 concurrent build slot for 24h (a THIRD parallel build line)",
                    Category = ShopCategory.Buffs, PriceDM = 120, Effect = ShopEffect.ExtraBuildSlot },
                new() { Id = "boost-energy", Name = "Power Surge",
                    Description = "+50% energy output for 24h (eases production shortfalls)",
                    Category = ShopCategory.Buffs, PriceDM = 100, Effect = ShopEffect.EnergyBoost },
                new() { Id = "buff-researchslot", Name = "Research Overclock",
                    Description = "+1 concurrent research slot for 24h (a THIRD parallel tech line)",
                    Category = ShopCategory.Buffs, PriceDM = 140, Effect = ShopEffect.ExtraResearchSlot },
                new() { Id = "name-change", Name = "Commander Rename",
                    Description = "Permanently change your commander name",
                    Category = ShopCategory.Buffs, PriceDM = 100, Effect = ShopEffect.ChangeCommanderName },
                new() { Id = "relocate-random", Name = "Blind Jump",
                    Description = "Warp your home planet to a random empty location in the sector",
                    Category = ShopCategory.Buffs, PriceDM = 150, Effect = ShopEffect.RelocateRandom },
                new() { Id = "relocate-target", Name = "Precision Warp",
                    Description = "Pick a spot on the map to relocate your home planet (preview + confirm)",
                    Category = ShopCategory.Buffs, PriceDM = 400, Effect = ShopEffect.RelocateTarget },
                new() { Id = "shield-8h", Name = "Aegis Shield · 8h",
                    Description = "A shield bubble blocks ALL raids on your planet for 8 hours. Launching a raid drops it.",
                    Category = ShopCategory.Buffs, PriceDM = 250, Effect = ShopEffect.PlanetShield,
                    ShieldSec = Balance.ShieldShortSec },
                new() { Id = "shield-24h", Name = "Aegis Shield · 24h",
                    Description = "A shield bubble blocks ALL raids on your planet for 24 hours. Launching a raid drops it.",
                    Category = ShopCategory.Buffs, PriceDM = 600, Effect = ShopEffect.PlanetShield,
                    ShieldSec = Balance.ShieldLongSec },
                // ---- skins ----
                // Planetary Resurfacing sells from the SKINS tab (user spec 2026-07-07)
                // — it's a cosmetic, even though it's consumable rather than a
                // switchable skin (buys land in inventory; USE rerolls the surface).
                new() { Id = "reroll-planet", Name = "Planetary Resurfacing",
                    // Odds stated (App Store 3.1.1: random items bought with currency that can
                    // be purchased must disclose them): the seed is uniform over 2^32 surfaces.
                    Description = "Regenerates your home planet's SURFACE (the base view) from a fresh " +
                        "random seed. Odds: every surface is equally likely, and it's purely cosmetic. " +
                        "One-way: the old look can't be rolled back. Map skin colors are a separate, " +
                        "switchable cosmetic.",
                    Category = ShopCategory.Skins, PriceDM = 150, Effect = ShopEffect.RerollPlanetLook },
                new() { Id = "default", Name = "Default World",
                    Description = "The classic blue homeworld — always available",
                    Category = ShopCategory.Skins, PriceDM = 0, Effect = ShopEffect.Skin },
                new() { Id = "skin-crimson", Name = "Crimson World",
                    Description = "A volcanic planet skin for your home colony",
                    Category = ShopCategory.Skins, PriceDM = 200, Effect = ShopEffect.Skin },
                new() { Id = "skin-emerald", Name = "Emerald World",
                    Description = "A lush terraformed skin for your home colony",
                    Category = ShopCategory.Skins, PriceDM = 200, Effect = ShopEffect.Skin },
            };

            // ---- speed-up tokens (v1 prices) ----
            foreach (var (label, sec, price) in new[]
            {
                ("1 min", 60, 2), ("5 min", 300, 8), ("15 min", 900, 20), ("30 min", 1800, 36),
                ("1 hr", 3600, 60), ("6 hr", 21600, 300), ("12 hr", 43200, 540), ("24 hr", 86400, 960),
            })
            {
                string idLabel = label.Replace(" min", "m").Replace(" hr", "h");
                // Spelled out from the seconds. The old chained Replace() turned
                // "1 min" into "1 minute", then matched " min" inside THAT —
                // "Cuts 1 minutesute off the active timer" (user screenshot).
                string spelled = sec >= 3600
                    ? (sec == 3600 ? "1 hour" : $"{sec / 3600} hours")
                    : (sec == 60 ? "1 minute" : $"{sec / 60} minutes");
                items.Add(new ShopItemDef
                {
                    Id = $"speed-{idLabel}",
                    Name = $"Speed-Up · {label}",
                    Description = $"Cuts {spelled} off the active timer",
                    Category = ShopCategory.Speedups,
                    PriceDM = price,
                    Effect = ShopEffect.Speedup,
                    SpeedupSec = sec,
                });
            }

            // ---- generated quick-select resource packs (10k / 100k / 1M / 10M) ----
            foreach (var res in Resources.All)
            {
                string name = res == ResourceId.Gold ? "Gold" : res == ResourceId.Quartz ? "Quartz" : "Helium";
                foreach (int amount in ResourcePresetAmounts)
                {
                    items.Add(new ShopItemDef
                    {
                        Id = ResourcePackId(res, amount),
                        Name = $"{name} +{ShortAmt(amount)}",
                        Description = $"+{amount:N0} {name} on use",
                        Category = ShopCategory.Resources,
                        PriceDM = ResourcePresetPrice(res, amount),
                        Effect = ShopEffect.ResourceGrant,
                        ResourceGrant = res == ResourceId.Gold ? new ResourceBag(amount, 0, 0)
                                      : res == ResourceId.Quartz ? new ResourceBag(0, amount, 0)
                                      : new ResourceBag(0, 0, amount),
                    });
                }
            }

            return items;
        }

        static string ShortAmt(int n) =>
            n >= 1_000_000 ? $"{n / 1_000_000}M"
            : n >= 1000 ? $"{n / 1000}k"
            : n.ToString();
    }
}
