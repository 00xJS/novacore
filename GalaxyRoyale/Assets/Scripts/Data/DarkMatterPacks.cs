// Dark Matter for real money (App Store in-app purchases, 2026-09-30). Each pack
// is a consumable product in App Store Connect with exactly this product id; its
// price is set there (the shop shows the App Store's localized price, never one
// written here). SuggestedUsd is what store/README.md recommends.
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public sealed class DarkMatterPack
    {
        public string ProductId = "";
        public string Name = "";
        public int DarkMatter;
        /// <summary>Share of bonus Dark Matter over the smallest pack's rate (0.2 = +20%).</summary>
        public float Bonus;
        public string SuggestedUsd = "";
    }

    public static class DarkMatterPacks
    {
        public static readonly IReadOnlyList<DarkMatterPack> All = new[]
        {
            new DarkMatterPack { ProductId = "galaxyroyale.darkmatter.120", Name = "Pinch of Dark Matter", DarkMatter = 120, Bonus = 0f, SuggestedUsd = "0.99" },
            new DarkMatterPack { ProductId = "galaxyroyale.darkmatter.650", Name = "Pouch of Dark Matter", DarkMatter = 650, Bonus = 0.08f, SuggestedUsd = "4.99" },
            new DarkMatterPack { ProductId = "galaxyroyale.darkmatter.1400", Name = "Crate of Dark Matter", DarkMatter = 1400, Bonus = 0.16f, SuggestedUsd = "9.99" },
            new DarkMatterPack { ProductId = "galaxyroyale.darkmatter.3000", Name = "Vault of Dark Matter", DarkMatter = 3000, Bonus = 0.24f, SuggestedUsd = "19.99" },
            new DarkMatterPack { ProductId = "galaxyroyale.darkmatter.8000", Name = "Hoard of Dark Matter", DarkMatter = 8000, Bonus = 0.32f, SuggestedUsd = "49.99" },
            new DarkMatterPack { ProductId = "galaxyroyale.darkmatter.17500", Name = "Trove of Dark Matter", DarkMatter = 17500, Bonus = 0.45f, SuggestedUsd = "99.99" },
        };

        public static DarkMatterPack? Find(string productId)
        {
            foreach (var p in All) if (p.ProductId == productId) return p;
            return null;
        }
    }
}
