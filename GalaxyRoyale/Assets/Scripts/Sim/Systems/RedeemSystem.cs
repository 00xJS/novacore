// Redeem codes (Settings › REDEEM A CODE): look a code up by its fingerprint
// (Data/RedeemCodes), pay it once per game, remember that it was used.
using System.Security.Cryptography;
using System.Text;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class RedeemSystem
    {
        /// <summary>Upper case, no spaces or dashes: "galaxy-launch " reads as "GALAXYLAUNCH".</summary>
        public static string Normalize(string code)
        {
            var sb = new StringBuilder(code.Length);
            foreach (char c in code)
                if (!char.IsWhiteSpace(c) && c != '-') sb.Append(char.ToUpperInvariant(c));
            return sb.ToString();
        }

        public static string Fingerprint(string code)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes("galaxyroyale:" + Normalize(code)));
            var hex = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) hex.Append(b.ToString("x2"));
            return hex.ToString();
        }

        public static RedeemCodeDef? Find(string code)
        {
            string hash = Fingerprint(code);
            foreach (var def in RedeemCodes.All)
                if (def.Hash == hash) return def;
            return null;
        }

        /// <summary>Pay a code's reward. Fails on an empty, unknown or already-used code.</summary>
        public static SimResult Redeem(GameState state, string code, out RedeemCodeDef? paid)
        {
            paid = null;
            if (Normalize(code).Length == 0) return SimResult.Fail("Enter a code");
            var def = Find(code);
            if (def == null) return SimResult.Fail("That code isn't valid");
            if (state.RedeemedCodes.Contains(def.Hash)) return SimResult.Fail("You've already redeemed that code in this game");
            state.RedeemedCodes.Add(def.Hash);
            ResourceSystem.Add(state, def.Resources.Milli());
            state.Premium.DarkMatter += def.DarkMatter;
            paid = def;
            return SimResult.Success;
        }
    }
}
