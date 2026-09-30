// Crediting App Store purchases (2026-09-30). The store hands over each verified
// transaction until the game says it's done with it (it may hand the same one
// over again after a crash or a relaunch), so crediting is idempotent: the
// transaction id is remembered in the save, and a repeat is only acknowledged.
// The caller saves before telling the store the transaction is finished.
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class PurchaseSystem
    {
        public enum Outcome { Credited, AlreadyCredited, UnknownProduct }

        public static Outcome Credit(GameState state, string productId, string transactionId, out int darkMatter)
        {
            darkMatter = 0;
            var pack = DarkMatterPacks.Find(productId);
            if (pack == null) return Outcome.UnknownProduct;
            if (!state.CreditedTransactions.Add(transactionId)) return Outcome.AlreadyCredited;
            state.Premium.DarkMatter += pack.DarkMatter;
            darkMatter = pack.DarkMatter;
            return Outcome.Credited;
        }
    }
}
