// Redeem codes (user request 2026-09-29): codes the developer hands out — a
// launch gift, an apology, a stream giveaway. Settings › REDEEM A CODE. Each
// code pays once per game (a new game can use it again).
//
// Only a fingerprint of each code ships (SHA-256 of "galaxyroyale:" + the code,
// upper-cased, spaces and dashes dropped), so the public repo never shows the
// codes themselves. scripts/redeem_code.py <CODE> prints a ready-made entry.
// A new code needs an app update to go live.
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public sealed class RedeemCodeDef
    {
        /// <summary>SHA-256 hex (lower case) of "galaxyroyale:" + the normalised code.</summary>
        public string Hash = "";
        /// <summary>Shown when it's redeemed ("Launch gift").</summary>
        public string Title = "";
        /// <summary>Whole units (not milli).</summary>
        public ResourceBag Resources = new();
        public int DarkMatter;
    }

    public static class RedeemCodes
    {
        public static readonly IReadOnlyList<RedeemCodeDef> All = new[]
        {
            new RedeemCodeDef
            {
                Hash = "822585fa70c2589e4946f0f12897692c1770f7e92c0a921ff56cdbf711e96de6",
                Title = "Launch gift",
                Resources = new ResourceBag(20_000, 20_000, 10_000),
                DarkMatter = 200,
            },
        };
    }
}
