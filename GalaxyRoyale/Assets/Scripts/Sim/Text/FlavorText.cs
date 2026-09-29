// Flavour text: the Galactic Gazette (the galaxy's daily paper), a war
// correspondent's battle recaps, and commanders' replies when the player hails
// them. Each kind has two halves, both here and deterministic:
//   - the FACTS, plain game data in the shape the AI proxy expects
//     (server/ai-proxy: POST /v1/generate {kind, facts, seed});
//   - the game's own text for the same facts, used when no proxy is set up,
//     the phone is offline or the proxy fails — so every feature works
//     without the AI, and the AI only ever retells what the game decided.
using System;
using System.Collections.Generic;
using System.Text;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Text
{
    public enum HailIntent { Taunt, Peace, Clan, Trade }

    public static class FlavorText
    {
        public const int Day = 24 * 3600;
        const int MaxHeadlines = 12;

        public static string IntentName(HailIntent i) => i switch
        {
            HailIntent.Taunt => "taunt",
            HailIntent.Peace => "peace",
            HailIntent.Clan => "clan",
            _ => "trade",
        };

        static string Name(GameState player, BotGalaxy galaxy, int empireId) =>
            empireId == 0 ? ClanSystem.Tagged(player, galaxy, 0, player.Profile.Name)
            : galaxy.Find(empireId) is { } bot ? ClanSystem.Tagged(player, galaxy, bot.Id, bot.Name)
            : BotNames.NameOf(empireId);

        static string Whole(long milli)
        {
            long n = milli / 1000;
            return n >= 1_000_000 ? $"{n / 1_000_000.0:0.#}M" : n >= 10_000 ? $"{n / 1000.0:0.#}K" : n.ToString("N0");
        }

        static int Ships(Dictionary<HullId, int> fleet)
        {
            int n = 0;
            foreach (var v in fleet.Values) n += v;
            return n;
        }

        // ---------- the Galactic Gazette ----------

        /// <summary>The paper's day (1 = the galaxy's first day).</summary>
        public static int GazetteDay(GameState player) => Math.Max(0, player.Tick) / Day + 1;

        /// <summary>Today's news as lines: the last day's battles and bulletins (newest first),
        /// and the dreadnought if one is in the galaxy.</summary>
        public static List<string> Headlines(GameState player, BotGalaxy galaxy)
        {
            var lines = new List<string>();
            var boss = galaxy.Boss;
            if (boss.Active)
                lines.Add($"A Pirate Dreadnought is in the galaxy with {Math.Round(BossSystem.HullShare(boss) * 100):0}% of its hull left");
            for (int i = galaxy.News.Count - 1; i >= 0 && lines.Count < MaxHeadlines; i--)
            {
                var n = galaxy.News[i];
                if (player.Tick - n.Tick > Day) break;
                if (n.IsBulletin) { lines.Add(n.Text!); continue; }
                string a = Name(player, galaxy, n.AttackerId), d = Name(player, galaxy, n.DefenderId);
                lines.Add(n.AttackerWon
                    ? (n.LootMilli > 0 ? $"{a} raided {d} and carried off {Whole(n.LootMilli)}" : $"{a} broke {d}'s defences")
                    : $"{d} beat off a raid by {a}");
            }
            return lines;
        }

        public static List<string> Wars(GameState player, BotGalaxy galaxy)
        {
            var wars = new List<string>();
            var seen = new HashSet<int>();
            foreach (var clan in galaxy.Clans)
            {
                if (clan.WarWithClanId == 0 || seen.Contains(clan.Id)) continue;
                var enemy = galaxy.FindClan(clan.WarWithClanId);
                if (enemy == null) continue;
                seen.Add(clan.Id);
                seen.Add(enemy.Id);
                wars.Add($"[{clan.Tag}] vs [{enemy.Tag}]");
            }
            return wars;
        }

        public static string CoreLine(GameState player, BotGalaxy galaxy)
        {
            var core = galaxy.Core;
            string holder = CoreSystem.HolderName(player, galaxy);
            if (core.HolderId == CoreSystem.GuardiansId) return "The Core Guardians still hold the Galactic Core";
            int days = Math.Max(0, player.Tick - core.HeldSinceTick) / Day;
            return days >= 1 ? $"{holder} has held the Galactic Core for {days} day{(days == 1 ? "" : "s")}"
                : $"{holder} holds the Galactic Core";
        }

        public static Dictionary<string, object?> GazetteFacts(GameState player, BotGalaxy galaxy)
        {
            var headlines = new List<object?>();
            foreach (var h in Headlines(player, galaxy))
                headlines.Add(new Dictionary<string, object?> { ["text"] = h });
            var wars = new List<object?>();
            foreach (var w in Wars(player, galaxy)) wars.Add(w);
            return new Dictionary<string, object?>
            {
                ["day"] = (long)GazetteDay(player),
                ["playerName"] = player.Profile.Name,
                ["headlines"] = headlines,
                ["wars"] = wars,
                ["core"] = CoreLine(player, galaxy),
            };
        }

        /// <summary>The front page without the AI: a headline for the biggest story, then up to
        /// four items, each on its own line starting with "• " (the AI's format).</summary>
        public static string GazetteFallback(GameState player, BotGalaxy galaxy)
        {
            var headlines = Headlines(player, galaxy);
            var wars = Wars(player, galaxy);
            string lead =
                headlines.Exists(h => h.Contains("seized the Galactic Core")) ? "THE CORE CHANGES HANDS"
                : headlines.Exists(h => h.Contains("Pirate Dreadnought was destroyed")) ? "DREADNOUGHT DOWN!"
                : headlines.Exists(h => h.Contains("dropped out of hyperspace")) ? "PIRATE DREADNOUGHT SIGHTED"
                : wars.Count > 0 ? $"WAR: {wars[0]}"
                : headlines.Count > 0 ? "RAIDERS ON THE PROWL"
                : "A QUIET DAY IN THE GALAXY";
            var sb = new StringBuilder(lead);
            int items = 0;
            foreach (var h in headlines)
            {
                if (items >= 4) break;
                sb.Append("\n• ").Append(h).Append('.');
                items++;
            }
            if (items < 4) { sb.Append("\n• ").Append(CoreLine(player, galaxy)).Append('.'); items++; }
            foreach (var w in wars)
            {
                if (items >= 4) break;
                sb.Append($"\n• The clans {w} are at war.");
                items++;
            }
            if (items == 1) sb.Append("\n• No battles in the last day. The commanders are building, and waiting.");
            return sb.ToString();
        }

        // ---------- battle recaps ----------

        /// <summary>Who fought, for a report in the player's mailbox.</summary>
        public static (string attacker, string defender) Sides(GameState player, BotGalaxy? galaxy, BattleMailReport mail)
        {
            string you = galaxy != null ? ClanSystem.Tagged(player, galaxy, 0, player.Profile.Name) : player.Profile.Name;
            if (!mail.Defending) return (you, mail.Report.DefenderName ?? "the defenders");
            string raider = galaxy?.Find(mail.AttackerBotId) is { } bot
                ? ClanSystem.Tagged(player, galaxy, bot.Id, bot.Name) : "the raiders";
            string defender = mail.GuardedBotId == CoreSystem.CoreGuardId ? $"{you}'s Galactic Core garrison"
                : mail.GuardedBotId > 0 && galaxy?.Find(mail.GuardedBotId) is { } host ? $"{host.Name}'s garrison, {you}'s ships"
                : you;
            return (raider, defender);
        }

        static string Where(GameState player, BotGalaxy? galaxy, BattleMailReport mail)
        {
            var t = mail.Target;
            if (t.Equals(CoreSystem.CoreTile)) return "the Galactic Core";
            if (t.Equals(player.HomeTile)) return $"{player.Profile.Name}'s colony";
            if (galaxy != null)
                foreach (var bot in galaxy.Bots)
                    if (bot.HomeTile.Equals(t)) return $"{bot.Name}'s colony";
            string? d = mail.Report.DefenderName;
            if (d != null && d.StartsWith("Pirate camp", StringComparison.Ordinal)) return "a pirate camp";
            return $"deep space at {t.X}, {t.Y}";
        }

        public static Dictionary<string, object?> RecapFacts(GameState player, BotGalaxy? galaxy, BattleMailReport mail)
        {
            var r = mail.Report;
            var (attacker, defender) = Sides(player, galaxy, mail);
            int aShips = Ships(r.Attacker), dShips = Ships(r.Defender);
            string? support = mail.AllyNames != null ? $"{mail.AllyNames} fought alongside {(mail.Defending ? defender : attacker)}"
                : mail.EnemyAllyNames != null ? $"{mail.EnemyAllyNames} fought alongside {(mail.Defending ? attacker : defender)}"
                : null;
            return new Dictionary<string, object?>
            {
                ["attacker"] = attacker,
                ["defender"] = defender,
                ["winner"] = r.Winner == BattleWinner.Attacker ? "attacker" : r.Winner == BattleWinner.Defender ? "defender" : "draw",
                ["rounds"] = (long)r.Rounds.Count,
                ["attackerShips"] = (long)aShips,
                ["defenderShips"] = (long)dShips,
                ["attackerLost"] = (long)Math.Max(0, aShips - Ships(r.AttackerSurvivors)),
                ["defenderLost"] = (long)Math.Max(0, dShips - Ships(r.DefenderSurvivors)),
                ["loot"] = (r.Loot?.Total ?? 0) / 1000,
                ["clanSupport"] = support,
                ["where"] = Where(player, galaxy, mail),
            };
        }

        /// <summary>The recap without the AI: two or three sentences from the same facts.</summary>
        public static string RecapFallback(Dictionary<string, object?> f, int seed)
        {
            var rng = Rng.Mulberry32(unchecked((uint)seed * 0x9E3779B1u + 17u));
            string Pick(params string[] options) => options[(int)(rng() * options.Length) % options.Length];
            string a = (string)f["attacker"]!, d = (string)f["defender"]!, where = (string)f["where"]!;
            string winner = (string)f["winner"]!;
            long rounds = (long)f["rounds"]!, aLost = (long)f["attackerLost"]!, dLost = (long)f["defenderLost"]!;
            long aShips = (long)f["attackerShips"]!, dShips = (long)f["defenderShips"]!, loot = (long)f["loot"]!;
            string roundsText = rounds == 1 ? "a single round" : rounds > 1 ? $"{rounds} rounds" : "no fighting at all";

            var sb = new StringBuilder();
            sb.Append(winner switch
            {
                "attacker" => Pick($"{a} tore into {where} and won in {roundsText}.",
                                   $"It took {a} {roundsText} to break {d} at {where}.",
                                   $"{d} never stood a chance: {a} carried the day at {where} in {roundsText}."),
                "defender" => Pick($"{d} held {where} against {a} after {roundsText}.",
                                   $"{a}'s attack on {where} broke apart after {roundsText}.",
                                   $"{roundsText} of fire at {where}, and {d} was still standing."),
                _ => Pick($"Neither side gave way at {where}: {a} and {d} fought {roundsText} to a standstill.",
                          $"{roundsText} at {where} settled nothing between {a} and {d}."),
            });
            sb.Append(' ');
            sb.Append($"{a} lost {aLost:N0} of {aShips:N0} ships; {d} lost {dLost:N0} of {dShips:N0}.");
            if (loot > 0) sb.Append($" The raiders hauled off {loot:N0} in plunder.");
            else if (f.TryGetValue("clanSupport", out var cs) && cs is string support) sb.Append($" {support}.");
            // Sentences may start with a lower-case name like "the raiders".
            return char.ToUpperInvariant(sb[0]) + sb.ToString(1, sb.Length - 1);
        }

        // ---------- hailing a commander ----------

        public static Dictionary<string, object?> HailFacts(GameState player, BotGalaxy galaxy, BotEmpire bot, HailIntent intent)
        {
            var p = BotSystem.PersonalityOf(player.Seed, bot.Id);
            var clan = galaxy.FindClan(bot.ClanId);
            long mine = Math.Max(1, PowerSystem.ComputePower(player));
            return new Dictionary<string, object?>
            {
                ["commander"] = bot.Name,
                ["clan"] = clan != null ? ClanSystem.Label(clan) : null,
                ["aggression"] = Math.Round(p.Aggression, 2),
                ["economyFocus"] = Math.Round(p.EconomyFocus, 2),
                ["grudge"] = BotSystem.HoldsGrudge(bot, player.Tick),
                ["mightRatio"] = Math.Round(bot.CachedMight / (double)mine, 2),
                ["intent"] = IntentName(intent),
                ["playerName"] = player.Profile.Name,
            };
        }

        /// <summary>The commander's reply without the AI, from the same facts: temper,
        /// grudge and relative strength decide the tone. Flavour only.</summary>
        public static string HailFallback(Dictionary<string, object?> f, int seed)
        {
            var rng = Rng.Mulberry32(unchecked((uint)seed * 0x85EBCA6Bu + 29u));
            string Pick(params string[] options) => options[(int)(rng() * options.Length) % options.Length];
            string you = (string)f["playerName"]!;
            string intent = (string)f["intent"]!;
            bool grudge = f["grudge"] is bool g && g;
            double aggression = Convert.ToDouble(f["aggression"]), ratio = Convert.ToDouble(f["mightRatio"]);
            bool fierce = aggression >= 0.55, stronger = ratio >= 1.25, weaker = ratio <= 0.8;

            if (grudge)
                return Pick($"You have a nerve hailing me after that raid, {you}. I haven't forgotten, and I will be paying you back.",
                            $"{you}. I still count the ships you cost me. Say what you like; my answer is coming by fleet.");
            return intent switch
            {
                "taunt" => fierce
                    ? (weaker ? Pick($"Big words, {you}. Come closer and see how long they last.",
                                     $"Keep talking, {you}. Every word makes the victory sweeter.")
                              : Pick($"Cute, {you}. I've swatted bigger fleets before breakfast.",
                                     $"I'll frame that message, {you}. Right next to the wreck of your fleet."))
                    : Pick($"Shouting across the void, {you}? Some of us have mines to run.",
                           $"Noted, {you}. I'll add it to the pile of things I'm not worried about."),
                "peace" => fierce
                    ? Pick($"Peace? From you, {you}? I'll believe it when your guns go quiet.",
                           $"Maybe, {you}. Peace is cheaper than a war, but only just.")
                    : Pick($"A quiet border suits me, {you}. My refineries prefer it.",
                           $"I'm listening, {you}. Fewer raids means more time for building."),
                "clan" => stronger
                    ? Pick($"Your clan wants my ships, {you}? Everyone does. What's in it for me?",
                           $"I'm flattered, {you}. Show me your clan can hold its own first.")
                    : Pick($"A clan with you, {you}? Interesting. Strength in numbers has its charms.",
                           $"Tell me more about your clan, {you}. I'm tired of fighting alone."),
                _ => aggression < 0.4 || !fierce
                    ? Pick($"Trade? Now you're speaking my language, {you}. My quartz for your helium, perhaps.",
                           $"Always happy to talk business, {you}. Everything has a price.")
                    : Pick($"Trade, {you}? I usually just take what I want. But go on.",
                           $"Why trade, {you}, when raiding is so much quicker? Still, I'm curious."),
            };
        }
    }
}
