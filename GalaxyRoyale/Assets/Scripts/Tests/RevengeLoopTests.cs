// Revenge loop: raids on you name their attacker (for STRIKE BACK), a won raid
// leaves the loser with a visible grudge that lapses, and a report keeps the
// right perspective through a save/load — "Raid repelled — X" is both a raid
// you fought off and your own failed raid on X.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Save;

namespace GalaxyRoyale.Sim.Tests
{
    public class RevengeLoopTests
    {
        static BattleMailReport Reload(GameState state, BattleMailReport mail)
        {
            state.Mailbox.Clear();
            state.Mailbox.Add(mail);
            var back = SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(state, 1000))).State;
            return (BattleMailReport)back.Mailbox[0];
        }

        [Test]
        public void InboundRaidReport_NamesItsAttacker()
        {
            var galaxy = BotSystem.CreateGalaxy(Spawn.GalaxySeed, Balance.HomeTile, 3);
            var player = GameState.CreateNewGame(Spawn.GalaxySeed);
            var bot = galaxy.Bots[0];
            galaxy.Inbound.Add(new BotAttack
            {
                Id = galaxy.NextAttackId++,
                BotId = bot.Id,
                IsFleet = true,
                Ships = new Dictionary<HullId, int> { [HullId.Cruiser] = 40 },
                LaunchTick = 0,
                ArrivesAtTick = 50,
                From = bot.HomeTile,
            });

            player.Tick = 60;
            BotSystem.Advance(player, galaxy, new SimEventBus());

            var report = player.Mailbox.OfType<BattleMailReport>().Single(m => m.Defending);
            Assert.AreEqual(bot.Id, report.AttackerBotId, "STRIKE BACK knows who to hit");
            Assert.AreEqual(bot.Id, Reload(player, report).AttackerBotId, "and remembers after a reload");
        }

        [Test]
        public void WonRaid_LeavesAGrudge_ThatLapses()
        {
            var galaxy = BotSystem.CreateGalaxy(Spawn.GalaxySeed, Balance.HomeTile, 3);
            var bot = galaxy.Bots[0];
            Assert.IsFalse(BotSystem.HoldsGrudge(bot, bot.State.Tick));

            BotSystem.ApplyPlayerRaid(galaxy, bot, new BattleReport { Winner = BattleWinner.Attacker },
                new ResourceBag());

            Assert.IsTrue(BotSystem.HoldsGrudge(bot, bot.State.Tick), "the raided rival wants revenge");
            Assert.IsFalse(BotSystem.HoldsGrudge(bot, bot.State.Tick + BotSystem.FocusExpiryTicks + 1),
                "an unsettled grudge lapses");
        }

        [Test]
        public void YourOwnRepelledRaid_StaysYourRaid_AfterReload()
        {
            var state = GameState.CreateNewGame(42);
            var own = new BattleMailReport
            {
                Id = 1,
                Subject = "Raid repelled — Moon Moon",
                Report = new BattleReport { Winner = BattleWinner.Defender, DefenderName = "Moon Moon" },
            };
            Assert.IsFalse(Reload(state, own).Defending, "your failed raid stays a DEFEAT");

            own.Defending = true; // how builds that guessed from the subject re-saved it
            Assert.IsFalse(Reload(state, own).Defending, "the misread is repaired on load");
        }

        [Test]
        public void RaidYouRepelled_StaysADefense_AfterReload()
        {
            var state = GameState.CreateNewGame(42);
            var defense = new BattleMailReport
            {
                Id = 1,
                Subject = "Raid repelled — Moon Moon",
                Defending = true,
                Report = new BattleReport { Winner = BattleWinner.Defender, DefenderName = "Commander Vega" },
            };
            Assert.IsTrue(Reload(state, defense).Defending);

            defense.Defending = false; // a save from before the flag existed
            Assert.IsTrue(Reload(state, defense).Defending, "the subject names the attacker, not you");
        }
    }
}
