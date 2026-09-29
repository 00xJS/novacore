// Flavour text (2026-09-28): the facts sent to the AI proxy match its contract
// (server/ai-proxy/src/facts.ts), and the game's own Gazette, recaps and hail
// replies — used without the AI — say what actually happened.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Text;

namespace GalaxyRoyale.Sim.Tests
{
    public class FlavorTextTests
    {
        static (GameState player, BotGalaxy galaxy) Setup()
        {
            var galaxy = BotSystem.CreateGalaxy(Spawn.GalaxySeed, Balance.HomeTile, 12);
            var player = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            player.Profile.Name = "Plasma Karen";
            player.Tick = 3 * FlavorText.Day + 3600;
            galaxy.News.Clear();
            galaxy.Clans.Clear();
            foreach (var b in galaxy.Bots) b.ClanId = 0;
            return (player, galaxy);
        }

        static void KeysAre(Dictionary<string, object?> facts, params string[] keys) =>
            CollectionAssert.AreEquivalent(keys, facts.Keys, "the proxy drops unknown fields and needs these");

        [Test]
        public void GazetteFacts_MatchTheProxy_AndCarryTheLastDaysNews()
        {
            var (player, galaxy) = Setup();
            var a = galaxy.Bots[0];
            var d = galaxy.Bots[1];
            galaxy.AddNews(player.Tick - 2 * FlavorText.Day, a.Id, d.Id, true, 5_000_000); // two days ago: old news
            galaxy.AddNews(player.Tick - 600, a.Id, d.Id, true, 12_000_000);
            galaxy.AddNews(player.Tick - 300, d.Id, 0, false, 0);
            galaxy.AddBulletin(player.Tick - 60, "Something big happened");

            var facts = FlavorText.GazetteFacts(player, galaxy);
            KeysAre(facts, "day", "playerName", "headlines", "wars", "core");
            Assert.AreEqual(4L, facts["day"]);
            Assert.AreEqual("Plasma Karen", facts["playerName"]);
            var headlines = ((List<object?>)facts["headlines"]!).Cast<Dictionary<string, object?>>().Select(h => (string)h["text"]!).ToList();
            Assert.AreEqual(3, headlines.Count, "only the last day's news");
            Assert.AreEqual("Something big happened", headlines[0], "newest first");
            Assert.That(headlines[1], Does.Contain(d.Name).And.Contain("beat off a raid"));
            Assert.That(headlines[2], Does.Contain(a.Name).And.Contain("carried off 12K"));
            Assert.That((string)facts["core"]!, Does.Contain("Core Guardians"));

            // It survives the trip to JSON and back (what goes over the wire).
            var back = (Dictionary<string, object?>)Json.Parse(Json.Write(facts))!;
            Assert.AreEqual(4L, back["day"]);
        }

        [Test]
        public void GazetteFallback_HasAHeadlineAndBullets()
        {
            var (player, galaxy) = Setup();
            galaxy.AddNews(player.Tick - 600, galaxy.Bots[0].Id, galaxy.Bots[1].Id, true, 8_000_000);
            string text = FlavorText.GazetteFallback(player, galaxy);
            var lines = text.Split('\n');
            Assert.AreEqual("RAIDERS ON THE PROWL", lines[0]);
            Assert.GreaterOrEqual(lines.Length, 3);
            Assert.IsTrue(lines.Skip(1).All(l => l.StartsWith("• ")), text);
            Assert.That(text, Does.Contain("Galactic Core"));

            galaxy.News.Clear();
            Assert.AreEqual("A QUIET DAY IN THE GALAXY", FlavorText.GazetteFallback(player, galaxy).Split('\n')[0]);
            galaxy.AddBulletin(player.Tick - 10, "[VOID] Hubble Trouble seized the Galactic Core");
            Assert.AreEqual("THE CORE CHANGES HANDS", FlavorText.GazetteFallback(player, galaxy).Split('\n')[0]);
        }

        static BattleMailReport Report(bool defending, BattleWinner winner, int attackerBotId = 0)
        {
            var report = new BattleReport
            {
                Attacker = new Dictionary<HullId, int> { [HullId.Fighter] = 40, [HullId.Cruiser] = 10 },
                Defender = new Dictionary<HullId, int> { [HullId.Bomber] = 30 },
                AttackerSurvivors = new Dictionary<HullId, int> { [HullId.Fighter] = 25, [HullId.Cruiser] = 10 },
                DefenderSurvivors = winner == BattleWinner.Attacker ? new() : new Dictionary<HullId, int> { [HullId.Bomber] = 5 },
                Winner = winner,
                Rounds = new List<RoundLog> { new(), new(), new() },
                Loot = winner == BattleWinner.Attacker ? new ResourceBag(3_000_000, 1_000_000, 500_000) : null,
                DefenderName = defending ? "Plasma Karen" : "Pirate camp Lv 3",
            };
            return new BattleMailReport { Id = 42, Report = report, Defending = defending, AttackerBotId = attackerBotId };
        }

        [Test]
        public void RecapFacts_MatchTheProxy_FromEitherSide()
        {
            var (player, galaxy) = Setup();
            var raid = FlavorText.RecapFacts(player, galaxy, Report(false, BattleWinner.Attacker));
            KeysAre(raid, "attacker", "defender", "winner", "rounds", "attackerShips", "defenderShips",
                "attackerLost", "defenderLost", "loot", "clanSupport", "where");
            Assert.AreEqual("Plasma Karen", raid["attacker"]);
            Assert.AreEqual("Pirate camp Lv 3", raid["defender"]);
            Assert.AreEqual("attacker", raid["winner"]);
            Assert.AreEqual(3L, raid["rounds"]);
            Assert.AreEqual(50L, raid["attackerShips"]);
            Assert.AreEqual(15L, raid["attackerLost"]);
            Assert.AreEqual(30L, raid["defenderLost"]);
            Assert.AreEqual(4500L, raid["loot"]);
            Assert.AreEqual("a pirate camp", raid["where"]);

            var rival = galaxy.Bots[2];
            var mail = Report(true, BattleWinner.Defender, rival.Id);
            mail.Target = player.HomeTile;
            var defence = FlavorText.RecapFacts(player, galaxy, mail);
            Assert.AreEqual(rival.Name, defence["attacker"]);
            Assert.AreEqual("Plasma Karen", defence["defender"]);
            Assert.AreEqual("defender", defence["winner"]);
            Assert.AreEqual("Plasma Karen's colony", defence["where"]);
        }

        [Test]
        public void RecapFallback_TellsTheBattleAsItHappened()
        {
            var (player, galaxy) = Setup();
            var facts = FlavorText.RecapFacts(player, galaxy, Report(false, BattleWinner.Attacker));
            string text = FlavorText.RecapFallback(facts, 42);
            Assert.That(text, Does.Contain("Plasma Karen").And.Contain("lost 15 of 50 ships").And.Contain("4,500"));
            Assert.AreEqual(text, FlavorText.RecapFallback(facts, 42), "the same report always reads the same");
            Assert.IsTrue(char.IsUpper(text[0]));
        }

        [Test]
        public void HailFacts_MatchTheProxy_AndTheFallbackHearsAGrudge()
        {
            var (player, galaxy) = Setup();
            var bot = galaxy.Bots[3];
            var facts = FlavorText.HailFacts(player, galaxy, bot, HailIntent.Peace);
            KeysAre(facts, "commander", "clan", "aggression", "economyFocus", "grudge", "mightRatio", "intent", "playerName");
            Assert.AreEqual(bot.Name, facts["commander"]);
            Assert.AreEqual("peace", facts["intent"]);
            Assert.IsNull(facts["clan"]);
            Assert.AreEqual(false, facts["grudge"]);

            string calm = FlavorText.HailFallback(facts, 7);
            Assert.That(calm, Does.Contain("Plasma Karen"));
            Assert.AreEqual(calm, FlavorText.HailFallback(facts, 7));

            bot.FocusTargetId = 0; // they hold a grudge against the player
            bot.FocusSetTick = player.Tick;
            var angry = FlavorText.HailFacts(player, galaxy, bot, HailIntent.Peace);
            Assert.AreEqual(true, angry["grudge"]);
            Assert.That(FlavorText.HailFallback(angry, 7), Does.Contain("raid").Or.Contain("ships you cost me"));
        }

        [Test]
        public void EveryHailIntent_HasAReply()
        {
            var (player, galaxy) = Setup();
            foreach (HailIntent intent in System.Enum.GetValues(typeof(HailIntent)))
                foreach (var bot in galaxy.Bots)
                {
                    var facts = FlavorText.HailFacts(player, galaxy, bot, intent);
                    Assert.IsNotEmpty(FlavorText.HailFallback(facts, bot.Id), $"{intent} to {bot.Name}");
                }
        }
    }
}
