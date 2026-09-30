// Nemesis rivals (2026-09-30): grudges, the hunt, escalation, taunts, breaking them.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class NemesisTests
    {
        const int Hour = 3600;

        static (GameState s, BotGalaxy galaxy, BotEmpire rival) Setup()
        {
            var galaxy = BotSystem.CreateGalaxy(Spawn.GalaxySeed, Balance.HomeTile, 20);
            var s = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            foreach (var b in galaxy.Bots) b.ClanId = 0;
            galaxy.Clans.Clear();
            var rival = galaxy.Bots[2];
            rival.State.Ships[HullId.Cruiser] = 50;
            return (s, galaxy, rival);
        }

        static List<NemesisNews> Drain(GameState s, BotGalaxy galaxy)
        {
            var bus = new SimEventBus();
            var news = new List<NemesisNews>();
            bus.Subscribe(e => { if (e is NemesisEvent n) news.Add(n.News); });
            NemesisSystem.Tick(s, galaxy, bus);
            return news;
        }

        [Test]
        public void TradingBlows_BuildsAGrudge_ThenANemesis()
        {
            var (s, galaxy, rival) = Setup();
            NemesisSystem.OnRaidedYou(s, galaxy, rival, youLost: true, s.Tick);
            Assert.IsFalse(NemesisSystem.Active(s), "one raid is just a raid");
            Assert.AreEqual(1, s.Nemesis.Grudges[rival.Id]);
            NemesisSystem.OnYouRaided(s, galaxy, rival, youWon: true, s.Tick);
            NemesisSystem.OnRaidedYou(s, galaxy, rival, youLost: true, s.Tick);
            Assert.IsTrue(NemesisSystem.Is(s, rival.Id));
            Assert.AreEqual(1, s.Nemesis.Tier);
            Assert.AreEqual(0, rival.FocusTargetId, "they're hunting you");
            var news = Drain(s, galaxy);
            Assert.AreEqual("sworn", news.Single().Kind);
            Assert.IsTrue(galaxy.News.Any(n => n.Text != null && n.Text.Contains("swore vengeance")), "it's on the news wire");

            var other = galaxy.Bots[5];
            NemesisSystem.OnYouRaided(s, galaxy, other, true, s.Tick);
            NemesisSystem.OnYouRaided(s, galaxy, other, true, s.Tick);
            Assert.IsTrue(NemesisSystem.Is(s, rival.Id), "one nemesis at a time");
        }

        [Test]
        public void BeatingYourNemesis_MakesThemEscalate_ThenBreaks_Them()
        {
            var (s, galaxy, rival) = Setup();
            NemesisSystem.OnYouRaided(s, galaxy, rival, true, s.Tick);
            NemesisSystem.OnYouRaided(s, galaxy, rival, true, s.Tick);
            Assert.IsTrue(NemesisSystem.Is(s, rival.Id));
            int cruisers = rival.State.Ships[HullId.Cruiser];

            NemesisSystem.OnRaidedYou(s, galaxy, rival, youLost: false, s.Tick); // you repel them
            Assert.AreEqual(2, s.Nemesis.Tier);
            Assert.Greater(rival.State.Ships[HullId.Cruiser], cruisers, "they rearm");
            NemesisSystem.OnYouRaided(s, galaxy, rival, youWon: false, s.Tick);
            Assert.AreEqual(1, s.Nemesis.TheirWins);
            NemesisSystem.OnYouRaided(s, galaxy, rival, youWon: true, s.Tick);
            Assert.AreEqual(3, s.Nemesis.Tier);
            Assert.AreEqual(s.Nemesis.BotId, RoundTrip(s).Nemesis.BotId, "the save remembers");

            int dm = s.Premium.DarkMatter;
            long before = s.Resources.Total;
            NemesisSystem.OnRaidedYou(s, galaxy, rival, youLost: false, s.Tick); // the third beating
            Assert.IsFalse(NemesisSystem.Active(s), "broken");
            Assert.AreEqual(dm + 150 * 3, s.Premium.DarkMatter);
            Assert.Greater(s.Resources.Total, before);
            Assert.AreEqual(1, s.Stats.NemesesDefeated);
            Assert.IsTrue(s.Nemesis.Humbled.ContainsKey(rival.Id));

            // Humbled: no fresh grudge for a week.
            NemesisSystem.OnYouRaided(s, galaxy, rival, true, s.Tick);
            NemesisSystem.OnYouRaided(s, galaxy, rival, true, s.Tick);
            Assert.IsFalse(NemesisSystem.Active(s));
        }

        [Test]
        public void YourNemesis_Taunts_AndLosesInterestWhenIgnored()
        {
            var (s, galaxy, rival) = Setup();
            NemesisSystem.OnYouRaided(s, galaxy, rival, true, s.Tick);
            NemesisSystem.OnYouRaided(s, galaxy, rival, true, s.Tick);
            Drain(s, galaxy);
            s.Tick = s.Nemesis.NextTauntTick;
            Assert.AreEqual("taunt", Drain(s, galaxy).Single().Kind);
            s.Tick = s.Nemesis.LastClashTick + NemesisSystem.QuietSec + 1;
            Assert.AreEqual("bored", Drain(s, galaxy).Single().Kind);
            Assert.IsFalse(NemesisSystem.Active(s));
        }

        static GameState RoundTrip(GameState s) => SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(s, 1000))).State;
    }
}
