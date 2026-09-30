// Nemesis rivals (2026-09-30): a simulated commander who remembers you.
//   - Grudges build as you and a rival trade blows: beating them in a raid
//     counts 2, repelling their raid 2, losing to them 1. The first rival past
//     GrudgeToNemesis becomes your Nemesis (one at a time).
//   - Your Nemesis hunts you: their revenge focus stays on you (BotSystem's
//     FocusTargetId), coming round sooner at each tier. Every exchange they
//     lose, they escalate — a tier up (to III) and fresh ships.
//   - They taunt you on the galaxy's news wire.
//   - Beat them BeatsToDefeat times (raids won on them, raids of theirs
//     repelled) and they're broken: a big reward, and they're humbled for a
//     week. Leave them alone for QuietSec and they lose interest.
// Messages for the player queue on NemesisState.Pending and go out as events
// on the next progression step (the raid resolvers have no event bus).
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;

namespace GalaxyRoyale.Sim.Systems
{
    public sealed class NemesisState
    {
        public int BotId;
        public string Name = "";
        public int Tier;
        public int SinceTick, LastClashTick, NextTauntTick;
        public int YourWins, TheirWins;
        /// <summary>Grudge points by rival (not yet a nemesis).</summary>
        public Dictionary<int, int> Grudges = new();
        /// <summary>Rivals you broke, humbled until a tick.</summary>
        public Dictionary<int, int> Humbled = new();
        /// <summary>Not saved: messages waiting to go out as events.</summary>
        public List<NemesisNews> Pending = new();
    }

    public sealed record NemesisNews(string Kind, string Name, int Tier, string Line, ResourceBag? Reward = null, int DarkMatter = 0);

    public static class NemesisSystem
    {
        public const int GrudgeToNemesis = 4;
        public const int BeatsToDefeat = 3;
        public const int MaxTier = 3;
        public const int QuietSec = 5 * 24 * 3600;
        public const int HumbledSec = 7 * 24 * 3600;
        public const double RearmShare = 0.2;

        public static bool Active(GameState s) => s.Nemesis.BotId != 0;
        public static bool Is(GameState s, int botId) => botId != 0 && s.Nemesis.BotId == botId;

        public static string TierName(int tier) => tier switch { 1 => "I", 2 => "II", _ => "III" };

        /// <summary>What breaking them pays: 150 DM a tier and a day of your mines' output.</summary>
        public static (ResourceBag pay, int darkMatter) Bounty(GameState s)
        {
            long total = ResourceSystem.MineOutputPerHour(s).Total * 24;
            return (new ResourceBag(total * 40 / 100, total * 35 / 100, total * 25 / 100), 150 * Math.Max(1, s.Nemesis.Tier));
        }

        // ---------- the exchanges ----------

        /// <summary>You raided a rival (StrikeSystem, at the battle).</summary>
        public static void OnYouRaided(GameState s, BotGalaxy galaxy, BotEmpire bot, bool youWon, int at)
        {
            if (Is(s, bot.Id))
            {
                s.Nemesis.LastClashTick = at;
                if (youWon) Beaten(s, galaxy, bot, at);
                else Gloat(s, galaxy, bot, at);
                return;
            }
            if (youWon) AddGrudge(s, galaxy, bot, 2, at);
        }

        /// <summary>A rival's raid on you landed (BotSystem, at the battle).</summary>
        public static void OnRaidedYou(GameState s, BotGalaxy galaxy, BotEmpire bot, bool youLost, int at)
        {
            if (Is(s, bot.Id))
            {
                s.Nemesis.LastClashTick = at;
                if (youLost) Gloat(s, galaxy, bot, at);
                else Beaten(s, galaxy, bot, at);
                return;
            }
            AddGrudge(s, galaxy, bot, youLost ? 1 : 2, at);
        }

        static void AddGrudge(GameState s, BotGalaxy galaxy, BotEmpire bot, int points, int at)
        {
            var n = s.Nemesis;
            if (n.Humbled.TryGetValue(bot.Id, out var until) && until > at) return;
            if (ClanSystem.SameClanAsPlayer(s, bot)) return;
            n.Grudges[bot.Id] = (n.Grudges.TryGetValue(bot.Id, out var g) ? g : 0) + points;
            if (Active(s) || n.Grudges[bot.Id] < GrudgeToNemesis) return;
            n.BotId = bot.Id;
            n.Name = bot.Name;
            n.Tier = 1;
            n.SinceTick = n.LastClashTick = at;
            n.NextTauntTick = at + TauntGap(1);
            n.YourWins = n.TheirWins = 0;
            n.Grudges.Remove(bot.Id);
            string line = Line(s, "sworn", bot.Name, at);
            galaxy.AddBulletin(at, $"{bot.Name} swore vengeance on {s.Profile.Name}: \"{line}\"");
            n.Pending.Add(new NemesisNews("sworn", bot.Name, 1, line));
            Hunt(bot, at);
        }

        /// <summary>You beat your nemesis: one closer to breaking them; otherwise they escalate.</summary>
        static void Beaten(GameState s, BotGalaxy galaxy, BotEmpire bot, int at)
        {
            var n = s.Nemesis;
            n.YourWins++;
            if (n.YourWins >= BeatsToDefeat)
            {
                var (pay, dm) = Bounty(s);
                ResourceSystem.Add(s, pay);
                s.Premium.DarkMatter += dm;
                s.Stats.NemesesDefeated++;
                string line = Line(s, "broken", bot.Name, at);
                galaxy.AddBulletin(at, $"{s.Profile.Name} broke {bot.Name}: \"{line}\"");
                n.Pending.Add(new NemesisNews("broken", bot.Name, n.Tier, line, pay, dm));
                n.Humbled[bot.Id] = at + HumbledSec;
                if (bot.FocusTargetId == 0) bot.FocusTargetId = -1;
                Clear(n);
                return;
            }
            if (n.Tier < MaxTier)
            {
                n.Tier++;
                Rearm(bot);
            }
            string esc = Line(s, "escalate", bot.Name, at);
            galaxy.AddBulletin(at, $"{bot.Name}: \"{esc}\"");
            n.Pending.Add(new NemesisNews("escalate", bot.Name, n.Tier, esc));
            Hunt(bot, at);
        }

        static void Gloat(GameState s, BotGalaxy galaxy, BotEmpire bot, int at)
        {
            s.Nemesis.TheirWins++;
            string line = Line(s, "gloat", bot.Name, at);
            galaxy.AddBulletin(at, $"{bot.Name}: \"{line}\"");
            s.Nemesis.Pending.Add(new NemesisNews("gloat", bot.Name, s.Nemesis.Tier, line));
            Hunt(bot, at);
        }

        /// <summary>Fresh ships for an escalating nemesis: a fifth again of each hull (at least 5 of their main one).</summary>
        static void Rearm(BotEmpire bot)
        {
            HullId? main = null;
            foreach (var kv in new List<KeyValuePair<HullId, int>>(bot.State.Ships))
            {
                if (kv.Key == HullId.Hauler || kv.Key == HullId.Probe || kv.Value <= 0) continue;
                bot.State.Ships[kv.Key] = kv.Value + (int)Math.Ceiling(kv.Value * RearmShare);
                if (main == null || kv.Value > bot.State.Ships[main.Value]) main = kv.Key;
            }
            var hull = main ?? HullId.Fighter;
            bot.State.Ships[hull] = (bot.State.Ships.TryGetValue(hull, out var n) ? n : 0) + 5;
            bot.CachedMight = PowerSystem.ComputePower(bot.State);
        }

        /// <summary>Point their revenge at you again.</summary>
        static void Hunt(BotEmpire bot, int at)
        {
            bot.FocusTargetId = 0;
            bot.FocusSetTick = at;
        }

        static int TauntGap(int tier) => (14 - 3 * tier) * 3600;

        static void Clear(NemesisState n)
        {
            n.BotId = 0;
            n.Name = "";
            n.Tier = 0;
            n.YourWins = n.TheirWins = 0;
        }

        /// <summary>Per galaxy step (ProgressionSystem): keep the hunt on, taunt, lose interest,
        /// and send the queued messages out.</summary>
        public static void Tick(GameState s, BotGalaxy galaxy, SimEventBus events)
        {
            var n = s.Nemesis;
            if (Active(s))
            {
                var bot = galaxy.Find(n.BotId);
                if (bot != null) n.Name = bot.Name;
                if (bot == null || ClanSystem.SameClanAsPlayer(s, bot)) Clear(n);
                else if (s.Tick - n.LastClashTick > QuietSec)
                {
                    string line = Line(s, "bored", bot.Name, s.Tick);
                    galaxy.AddBulletin(s.Tick, $"{bot.Name}: \"{line}\"");
                    n.Pending.Add(new NemesisNews("bored", bot.Name, n.Tier, line));
                    if (bot.FocusTargetId == 0) bot.FocusTargetId = -1;
                    Clear(n);
                }
                else
                {
                    // The hunt stays on: a fresh revenge focus as the old one expires.
                    if (bot.FocusTargetId != 0 && s.Tick - bot.FocusSetTick > TauntGap(n.Tier)) Hunt(bot, s.Tick);
                    if (s.Tick >= n.NextTauntTick)
                    {
                        n.NextTauntTick = s.Tick + TauntGap(n.Tier);
                        string line = Line(s, "taunt", bot.Name, s.Tick);
                        galaxy.AddBulletin(s.Tick, $"{bot.Name}: \"{line}\"");
                        n.Pending.Add(new NemesisNews("taunt", bot.Name, n.Tier, line));
                    }
                }
            }
            foreach (var news in n.Pending) events.Emit(new NemesisEvent(news));
            n.Pending.Clear();
        }

        // ---------- what they say ----------

        static readonly Dictionary<string, string[]> Lines = new()
        {
            ["sworn"] = new[]
            {
                "Remember my name, {you}. You'll hear it again.",
                "You made an enemy today, {you}. I don't forget.",
                "Every ship you took from me, I'll take back twice.",
            },
            ["gloat"] = new[]
            {
                "Did that hurt, {you}? It was meant to.",
                "Your vaults were lighter than I expected.",
                "Tell your crews I said hello.",
            },
            ["escalate"] = new[]
            {
                "Lucky. Next time I bring the whole fleet.",
                "You've only made me angrier, {you}.",
                "I'm building ships faster than you can burn them.",
            },
            ["taunt"] = new[]
            {
                "Sleep with your shields up, {you}.",
                "I can see your colony from here.",
                "My scouts know every pad on your planet.",
                "Count your ships tonight, {you}. I am.",
            },
            ["broken"] = new[]
            {
                "Enough. You win this one, {you}.",
                "My fleet is gone. So is my appetite for this.",
                "I yield. The rim is yours — for now.",
            },
            ["bored"] = new[]
            {
                "You're not worth the fuel, {you}.",
                "I have bigger fish to fry. Stay out of my way.",
            },
        };

        static string Line(GameState s, string kind, string botName, int at)
        {
            var options = Lines[kind];
            int i = (int)(Rng.Hash2d(unchecked((uint)s.Seed ^ 0x4E3Eu), at / 60, botName.Length + kind.Length) * options.Length);
            return options[Math.Min(options.Length - 1, i)].Replace("{you}", s.Profile.Name);
        }
    }
}
