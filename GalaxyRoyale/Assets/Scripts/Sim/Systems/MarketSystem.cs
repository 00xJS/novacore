// The galactic market (build-all plan, 2026-09-28): trade one resource for
// another. Prices are in credits per unit:
//   price = base value × galaxy drift × your impact
// - Base values reflect scarcity (helium is dearest).
// - The galaxy drift is a slow, seeded wander between 0.8 and 1.25 (a new
//   target every six hours, blended smoothly), the same for everyone who
//   plays the same galaxy at the same time.
// - Your impact is how far your own trades pushed the price: selling a lot of
//   something cheapens it, buying makes it dearer — exponentially in the
//   amount over the market's depth (which grows with your production), and
//   priced along the way, so a trade split in pieces costs the same as one
//   and a round trip can only lose (the fee, twice). It wears off with a
//   six-hour half-life.
// Every trade pays a 5% fee. Quotes are exact: trading applies the quote.
using System;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class MarketSystem
    {
        public const double Fee = 0.05;
        public const int DriftStepSec = 6 * 3600;
        public const double DriftMin = 0.8, DriftMax = 1.25;
        public const int ImpactHalfLifeSec = 6 * 3600;
        /// <summary>Depth: base units, plus this many hours of your own production.</summary>
        public const long DepthBase = 50_000;
        public const double DepthHours = 12;
        /// <summary>Trading a full depth moves the price by e^±Steepness (×0.70 / ×1.42). 0.5 read as
        /// punishing in play: a MAX trade crashed the price 39% and cost a third of its value.</summary>
        public const double Steepness = 0.35;
        /// <summary>One trade moves at most a full depth.</summary>
        public const double MaxTradeDepths = 1.0;

        public static double BaseValue(ResourceId r) => r switch
        {
            ResourceId.Quartz => 1.25,
            ResourceId.Helium => 1.6,
            _ => 1.0,
        };

        /// <summary>The galaxy's drift for a resource at a tick: seeded targets every
        /// DriftStepSec, blended smoothly between them.</summary>
        public static double Drift(int seed, ResourceId r, int tick)
        {
            int step = Math.Max(0, tick) / DriftStepSec;
            double f = (Math.Max(0, tick) % DriftStepSec) / (double)DriftStepSec;
            double a = Target(seed, r, step), b = Target(seed, r, step + 1);
            double smooth = f * f * (3 - 2 * f);
            return a + (b - a) * smooth;
        }

        static double Target(int seed, ResourceId r, int step)
        {
            var rng = Rng.Mulberry32(unchecked((uint)seed * 0x2545F491u ^ (uint)((int)r + 1) * 0x9E3779B9u ^ (uint)step * 0x85EBCA77u));
            rng();
            return DriftMin + rng() * (DriftMax - DriftMin);
        }

        /// <summary>Your impact on a resource's price now (decayed toward 1).</summary>
        public static double Impact(GameState state, ResourceId r)
        {
            var m = state.Market;
            double stored = r switch
            {
                ResourceId.Quartz => m.QuartzImpact,
                ResourceId.Helium => m.HeliumImpact,
                _ => m.GoldImpact,
            };
            int dt = Math.Max(0, state.Tick - m.ImpactTick);
            return 1 + (stored - 1) * Math.Pow(0.5, dt / (double)ImpactHalfLifeSec);
        }

        public static double Price(GameState state, ResourceId r) =>
            BaseValue(r) * Drift(state.Seed, r, state.Tick) * Impact(state, r);

        /// <summary>The market's depth in <paramref name="r"/> (whole units): a base plus hours
        /// of your own production. Trading this much moves the price by e^±Steepness.</summary>
        public static double Depth(GameState state, ResourceId r)
        {
            long perHour = ResourceSystem.GetRates(state).Get(r) / 1000;
            return DepthBase + DepthHours * Math.Max(0, perHour);
        }

        /// <summary>1 <paramref name="sell"/> buys this much <paramref name="buy"/> at the
        /// posted prices (before the fee and your trade's own impact).</summary>
        public static double Rate(GameState state, ResourceId sell, ResourceId buy) =>
            Price(state, sell) / Price(state, buy) * TwistSystem.MarketMult(state); // Trade Winds

        public readonly struct Quote
        {
            public readonly bool Ok;
            public readonly string? Reason;
            public readonly long SellWhole, BuyWhole;
            public readonly double SellImpactAfter, BuyImpactAfter;

            public Quote(long sell, long buy, double sellImpact, double buyImpact)
            {
                Ok = true; Reason = null; SellWhole = sell; BuyWhole = buy;
                SellImpactAfter = sellImpact; BuyImpactAfter = buyImpact;
            }

            public Quote(string reason)
            {
                Ok = false; Reason = reason; SellWhole = 0; BuyWhole = 0;
                SellImpactAfter = 1; BuyImpactAfter = 1;
            }
        }

        /// <summary>The most of <paramref name="sell"/> one trade can move: your wallet or a full depth.</summary>
        public static long MaxSell(GameState state, ResourceId sell) =>
            Math.Min(state.Resources.Get(sell) / 1000, (long)(Depth(state, sell) * MaxTradeDepths));

        /// <summary>What selling <paramref name="amountWhole"/> of one resource fetches in
        /// another, priced unit by unit as your trade moves both prices, less the fee.</summary>
        public static Quote GetQuote(GameState state, ResourceId sell, ResourceId buy, long amountWhole)
        {
            if (sell == buy) return new Quote("Pick two different resources");
            if (amountWhole <= 0) return new Quote("Choose an amount to sell");
            if (state.Resources.Get(sell) < amountWhole * 1000) return new Quote($"Not enough {Name(sell)}");
            double sellDepth = Depth(state, sell), buyDepth = Depth(state, buy);
            if (amountWhole > sellDepth * MaxTradeDepths)
                return new Quote($"The market takes at most {(long)(sellDepth * MaxTradeDepths):N0} {Name(sell)} in one trade");

            // Selling A: the price falls as e^(−kA/D); credits are its integral.
            double sellPrice = Price(state, sell);
            double fall = Math.Exp(-Steepness * amountWhole / sellDepth);
            double credits = sellPrice * sellDepth / Steepness * (1 - fall) * (1 - Fee);
            // Buying with those credits: the price rises as e^(kB/D); solve its integral for B.
            double buyPrice = Price(state, buy);
            double got = buyDepth / Steepness * Math.Log(1 + credits * Steepness / (buyDepth * buyPrice));
            long whole = (long)Math.Floor(got);
            if (whole <= 0) return new Quote("Too small a trade");
            return new Quote(amountWhole, whole, Impact(state, sell) * fall,
                Impact(state, buy) * Math.Exp(Steepness * whole / buyDepth));
        }

        public static SimResult Trade(GameState state, ResourceId sell, ResourceId buy, long amountWhole)
        {
            var q = GetQuote(state, sell, buy, amountWhole);
            if (!q.Ok) return SimResult.Fail(q.Reason ?? "Can't trade");
            // Bank the decayed impacts at today's tick before stamping the new ones.
            var m = state.Market;
            double g = Impact(state, ResourceId.Gold), qz = Impact(state, ResourceId.Quartz), he = Impact(state, ResourceId.Helium);
            m.GoldImpact = g; m.QuartzImpact = qz; m.HeliumImpact = he;
            m.ImpactTick = state.Tick;
            Set(m, sell, q.SellImpactAfter);
            Set(m, buy, q.BuyImpactAfter);

            state.Resources.Set(sell, state.Resources.Get(sell) - q.SellWhole * 1000);
            state.Resources.Set(buy, state.Resources.Get(buy) + q.BuyWhole * 1000);
            state.Stats.MarketTrades++;
            return SimResult.Success;
        }

        static void Set(MarketState m, ResourceId r, double v)
        {
            switch (r)
            {
                case ResourceId.Quartz: m.QuartzImpact = v; break;
                case ResourceId.Helium: m.HeliumImpact = v; break;
                default: m.GoldImpact = v; break;
            }
        }

        public static string Name(ResourceId r) => r switch
        {
            ResourceId.Quartz => "quartz",
            ResourceId.Helium => "helium",
            _ => "gold",
        };
    }
}
