// The simulated galaxy — 99 AI commanders who play the same game as the
// player (Galaxy Royale's core conceit: one human vs a living single-player
// "server"). Each bot owns a real GameState and progresses through the SAME
// Balance formulas, cost curves, tech gates, and CombatResolver as the player.
//
// Bot limitations (design rules, user-confirmed at the single-player pivot;
// queue rule revised 2026-07-08):
//   1. NO shop, speed-ups, Dark Matter, or buffs — those are player-exclusive.
//      (Bots never touch ShopSystem and their Buffs stay zeroed.)
//   2. Bots run the STANDARD two parallel build/research/ship queues, same as
//      the player (one queue paced the whole game too slow — user spec). Only
//      the shop's THIRD build/research slot stays player-exclusive.
//
// Perf model: bots do NOT run the 1 Hz TickEngine. They advance in coarse
// "think steps" (60 s live, 600 s while catching up) — resources accrue via
// the exact ProducedBetween integral, queued orders complete against absolute
// ticks, and decisions happen once per step. 99 bots stay cheap enough for
// per-frame advancing on a phone and for 8 h offline catch-up at boot.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Bots
{
    /// <summary>Fixed traits derived from (galaxySeed, botId) — never saved, always recomputed.</summary>
    public readonly struct BotPersonality
    {
        /// <summary>0..1 — how often the bot raids anyone.</summary>
        public readonly double Aggression;
        /// <summary>0..1 — mines/economy vs military build bias.</summary>
        public readonly double EconomyFocus;
        /// <summary>0..1 — fraction of hours the bot is "online" making decisions.</summary>
        public readonly double Activity;
        public readonly int AvatarSeed;
        public readonly string Skin;
        /// <summary>Simulated head start ("joined the server earlier") applied at galaxy creation.</summary>
        public readonly int PreSimTicks;

        public BotPersonality(double aggression, double economyFocus, double activity,
            int avatarSeed, string skin, int preSimTicks)
        {
            Aggression = aggression;
            EconomyFocus = economyFocus;
            Activity = activity;
            AvatarSeed = avatarSeed;
            Skin = skin;
            PreSimTicks = preSimTicks;
        }
    }

    /// <summary>One simulated commander: a full GameState plus AI bookkeeping.</summary>
    public sealed class BotEmpire
    {
        public int Id;
        public GameState State = new();
        /// <summary>Last tick (player clock) this bot's think step ran.</summary>
        public int LastThinkTick;
        /// <summary>Next tick this bot considers launching a raid/spy at anyone.</summary>
        public int NextAttackRollTick;
        /// <summary>Might, recomputed once per think step (cheap enough to read every frame).</summary>
        public long CachedMight;
        /// <summary>Last tick this bot panic-ported away from a raid streak (0 = never).</summary>
        public int LastPortTick;
        /// <summary>Last tick this bot precision-jumped toward a hunt target (0 = never).</summary>
        public int LastHuntJumpTick;
        /// <summary>Rolling window for "being attacked too much" — start tick + defenses lost in it.</summary>
        public int DefenseLossWindowStart;
        public int DefenseLossCount;
        /// <summary>The empire this bot is fixated on (-1 = none, 0 = the player,
        /// >0 = bot id): a hunt mark its spy probe confirmed rich, or a grudge
        /// against whoever raided it — settled when it's big enough to act.</summary>
        public int FocusTargetId = -1;
        public int FocusSetTick;
        /// <summary>Pending "spy them back" flight at the PLAYER (0 = none) — set
        /// when the player's probe sweeps this bot and it notices.</summary>
        public int SpyBackAtTick;
        /// <summary>Might at the start of the current season (SeasonSystem).</summary>
        public long SeasonStartMight;
        /// <summary>Clan membership (0 = independent). See ClanSystem.</summary>
        public int ClanId;
        /// <summary>Flies with a clanmate's raid again from this tick (ClanSystem.RaidSupport).</summary>
        public int SupportReadyTick;

        public string Name => BotNames.NameOf(Id);
        public TileXY HomeTile => State.HomeTile;
    }

    /// <summary>Raid: a battle at arrival. Spy: a lone recon probe. Escort: a
    /// clanmate's wing flying with the player's joint strike, intercept or core
    /// assault — it waits at the target until the player's battle settles it.
    /// Garrison: a clanmate's wing standing guard at the player's colony
    /// (StrikeSystem). CoreAssault: a commander's (or its clanmates') fleet
    /// bound for the Galactic Core; wings of one assault share a LinkId (CoreSystem).</summary>
    public enum BotMarchKind { Raid, Spy, Escort, Garrison, CoreAssault }

    /// <summary>
    /// A bot fleet flying between two points in real time (visible on the galaxy
    /// map). A raid resolves AT ARRIVAL against the defender's live state;
    /// survivors + loot fly the return leg home.
    /// </summary>
    public sealed class BotMarch
    {
        public int Id;
        /// <summary>The bot the ships belong to.</summary>
        public int BotId;
        /// <summary>Raid/spy: the defending bot. Escort: the player's target bot (0 for an intercept).</summary>
        public int TargetBotId;
        public BotMarchKind Kind;
        /// <summary>Escort: the player march it flies with. Garrison: the empire it guards (0 = the player).</summary>
        public int LinkId;
        /// <summary>True = a lone recon probe (no combat at arrival — it confirms a
        /// hunt mark and may provoke the scanned bot into scouting back).</summary>
        public bool IsSpy
        {
            get => Kind == BotMarchKind.Spy;
            set { if (value) Kind = BotMarchKind.Spy; else if (Kind == BotMarchKind.Spy) Kind = BotMarchKind.Raid; }
        }
        public Dictionary<HullId, int> Ships = new();
        public TileXY From;
        public TileXY To;
        public int LaunchTick;
        public int ArrivesAtTick;
        /// <summary>Return-leg completion; 0 until the arrival battle resolves.</summary>
        public int ReturnsAtTick;
        /// <summary>True once the arrival battle ran (the march is on its way home).</summary>
        public bool Resolved;
        /// <summary>Plunder riding home with the survivors (milli).</summary>
        public ResourceBag LootMilli = new();
    }

    /// <summary>An inbound bot fleet/probe aimed at the player, flying in real time.</summary>
    public sealed class BotAttack
    {
        public int Id;
        public int BotId;
        /// <summary>True = war fleet; false = spy probe.</summary>
        public bool IsFleet;
        public Dictionary<HullId, int> Ships = new();
        public int LaunchTick;
        public int ArrivesAtTick;
        public TileXY From;
    }

    /// <summary>What an attacker (player) sees of a defender (bot) — computed live, never stale.</summary>
    public sealed class DefenseSnapshot
    {
        public int BotId;
        public string CommanderName = "";
        public int HomeX;
        public int HomeY;
        public long Might;
        public Dictionary<HullId, int> Ships = new();
        public ResourceBag ProtectedMilli = new();
        public ResourceBag LootableMilli = new();
        /// <summary>Completed tech levels — spy reports print the full stack.</summary>
        public Dictionary<TechId, int> Research = new();
        /// <summary>Building levels — spy reports print the whole base.</summary>
        public Dictionary<BuildingId, int> Buildings = new();
    }

    /// <summary>One line of galaxy news — a battle between two empires (id 0 =
    /// the player), or a bulletin (clan founded, war declared or won…) when
    /// <see cref="Text"/> is set.</summary>
    public sealed class NewsItem
    {
        public int Tick;
        public int AttackerId;
        public int DefenderId;
        public bool AttackerWon;
        /// <summary>Milli-resources plundered (0 on a repelled raid).</summary>
        public long LootMilli;
        /// <summary>A bulletin's headline (null for battles).</summary>
        public string? Text;

        public bool IsBulletin => Text != null;
    }

    /// <summary>The 99 simulated commanders + attacks currently aimed at the player.</summary>
    public sealed class BotGalaxy
    {
        public List<BotEmpire> Bots = new();
        public List<BotAttack> Inbound = new();
        /// <summary>Bot-vs-bot raid fleets currently in flight (rendered on the map).</summary>
        public List<BotMarch> Marches = new();
        public int NextMarchId = 1;
        public int NextAttackId = 1;
        /// <summary>Earliest tick the next bot may claim a raid slot against the player
        /// (galaxy-wide cooldown so 99 bots don't dogpile the human).</summary>
        public int NextInboundWindowTick;
        /// <summary>Galaxy news feed (the global chat reborn): every battle report,
        /// newest LAST. Capped at Balance.NewsCap. Read it to spot the serial raiders.</summary>
        public List<NewsItem> News = new();
        /// <summary>Last player tick Advance ran for (transient — not saved).
        /// Lets the per-frame call return in one compare between sim ticks.</summary>
        public int LastAdvanceTick = -1;
        /// <summary>Clans (ClanSystem) — the simulated ones and the player's.</summary>
        public List<Clan> Clans = new();
        public int NextClanId = 1;
        /// <summary>Galaxy time of the next clan-politics pass (ClanSystem.Politics).</summary>
        public int NextPoliticsTick;
        /// <summary>The Galactic Core (CoreSystem).</summary>
        public CoreState Core = new();

        public BotEmpire? Find(int botId) => Bots.Find(b => b.Id == botId);
        public Clan? FindClan(int clanId) => clanId == 0 ? null : Clans.Find(c => c.Id == clanId);

        /// <summary>A bulletin line on the news wire (clan politics, the core…).</summary>
        public void AddBulletin(int tick, string text)
        {
            var item = new NewsItem { Tick = tick, AttackerId = -1, DefenderId = -1, Text = text };
            int at = News.Count;
            while (at > 0 && News[at - 1].Tick > tick) at--;
            News.Insert(at, item);
            if (News.Count > Balance.NewsCap) News.RemoveRange(0, News.Count - Balance.NewsCap);
        }

        public void AddNews(int tick, int attackerId, int defenderId, bool attackerWon, long lootMilli)
        {
            var item = new NewsItem
            {
                Tick = tick, AttackerId = attackerId, DefenderId = defenderId,
                AttackerWon = attackerWon, LootMilli = lootMilli,
            };
            // Ordered insert (newest LAST): catch-up resolves battles with historical
            // arrival stamps, so appends aren't guaranteed chronological anymore.
            int at = News.Count;
            while (at > 0 && News[at - 1].Tick > tick) at--;
            News.Insert(at, item);
            if (News.Count > Balance.NewsCap) News.RemoveRange(0, News.Count - Balance.NewsCap);
        }
    }

    /// <summary>
    /// Deterministic commander handles — bot #N always gets the same name.
    /// Style spec (user request): funny tech/space "racehorse names" — the
    /// absurd-registered-name energy of Hoof Hearted, aimed at nerds. Curated
    /// list of 277 (every rival in a 250-commander galaxy stays unique, with
    /// headroom). ASCII only (the runtime font tofu-boxes emoji and glyphs).
    /// </summary>
    public static class BotNames
    {
        static readonly string[] Names =
        {
            "Ctrl Alt Defeat",   "Zero Gravitas",     "Sir Loots-a-Lot",   "Moon Moon",
            "Sudo Nova",         "Segfault Sally",    "Kernel Panic",      "Warp Speed Chonk",
            "Kessler Syndrome",  "Blue Screen Betty", "Cache Me Outside",  "Infinite Loop Lou",
            "Major Tomfoolery",  "Apollo Gee",        "Halley's Comment",  "Meteor Wrong",
            "Comet Sense",       "Rocket Surgeon",    "Gravity Schmavity", "Event Horizontal",
            "Dark Matter Dan",   "Cosmic Latte",      "Galaxy Brain",      "Orbit Happens",
            "Big Dipper Energy", "Red Dwarf Randy",   "Neutron Norm",      "Quasar Quinn",
            "Pulsar Pete",       "Nebula Nonsense",   "Void Boi",          "Star Schemer",
            "Zero G Whiz",       "Vacuum Cleaner",    "Light Year Larry",  "Parsec Percy",
            "Hyper Drive Thru",  "Terraform Tommy",   "Crater Face",       "Asteroid Boyd",
            "Space Junk Jeff",   "Houston Problem",   "One Small Step",    "Tang Enjoyer",
            "Freeze Dried Fred", "Martian Manny",     "Little Green Man",  "Mostly Harmless",
            "Don't Panic",       "Spaghettified",     "Slingshot Sammy",   "Retrograde Greg",
            "Perihelion Phil",   "Doppler Dave",      "Redshift Rick",     "Blueshift Bill",
            "Tachyon Tina",      "Muon Mike",         "Gluon Gwen",        "Higgs Boson Hank",
            "Quark Snack",       "Ion Maiden",        "Plasma Karen",      "Fusion Confusion",
            "Antimatter Matt",   "Singularity Sue",   "Wormhole Wally",    "Time Dilation Tim",
            "Relativity Rex",    "Schrodinger's Cat", "Heisenberg Maybe",  "Absolute Zero Zoe",
            "Entropy Ed",        "Quantum Leap Year", "Ping Me Later",     "Alt F4tune",
            "CAPTCHA the Flag",  "Spam Folder",       "Airplane Mode",     "Low Battery",
            "Patch Notes",       "Day One DLC",       "Lootbox Lenny",     "Dial-Up Doug",
            "Teapot 418",        "Uptime Andy",       "Hotfix Harry",      "Beta Tester Bob",
            "NPC Energy",        "Definitely Human",  "Not A Bot",         "Lag Spike Lucy",
            "Respawn Ron",       "GG No Re-Entry",    "NoScope Nebula",    "Space Bar Hero",
            "Buffering 99",      "Solar Flair",       "Gas Giant Gary",    "Packet Loss Pam",
            "Firewall Fiona",    "Malware Mallory",   "Zip Bomb Ziggy",    "Glitch Witch",
            "Stack Overflowed",  "Merge Conflict",    "Git Blame Greta",   "Force Push Frank",
            "Rebase Regret",     "Legacy Codey",      "Tech Debt Ted",     "Rubber Duck Debug",
            "Semicolon Sam",     "Camel Case Casey",  "Snake Case Sid",    "Off By One Ollie",
            "Race Condition",    "Deadlock Dora",     "Mutex Max",         "Thread Theo",
            "Memory Leak Mel",   "Null Terminator",   "Boolean Boo",       "Big O Notation",
            "Syntax Terror",     "Logic Bomb Lola",   "Bit Flip Bianca",   "Byte Me Bobby",
            "Kilobyte Kyle",     "Megabyte Mabel",    "Gigabyte Gabe",     "Terabyte Tara",
            "Petabyte Pedro",    "Overclocked Otis",  "Undervolt Ursula",  "RGB Everything",
            "Dead Pixel Dex",    "Screen Burn Bern",  "Jailbroken Jane",   "Rooted Ruth",
            "Factory Reset Fay", "Safe Mode Sadie",   "Incognito Iggy",    "Cookie Banner",
            "Terms Of Service",  "Unsubscribe Link",  "Reply All Rhonda",  "CC Everybody",
            "Out Of Office",     "Password 123",      "Hunter Two",        "Qwerty Uiop",
            "Admin Admin",       "Guest Account",     "Two Factor Tia",    "Verify Email Vern",
            "Skip Tutorial",     "Pay2Win Wanda",     "Microtransaction",  "Loot Crate Lars",
            "Season Pass Sage",  "Battle Pass Buzz",  "Ragequit Rita",     "Spawn Camper Cam",
            "Aimbot Abbot",      "Wallhack Willa",    "Sweaty Tryhard",    "Touch Grass Gus",
            "AFK Farmer Fern",   "Smurf Account",     "Patch Day Panic",   "Server Tick Rate",
            "Ping 999",          "Hitbox Hattie",     "Desync Daisy",      "Rollback Rollo",
            "Netcode Ned",       "Speedrun Strats",   "Frame Perfect",     "Cheese Strat Chet",
            "Meta Slave Mave",   "Nerf This Nessa",   "Buff Me Bruno",     "Balance Patch Bea",
            "Glass Cannon Cate", "Tank Main Tank",    "Heal Please Hal",   "Peel For Me Pearl",
            "One More Game",     "Zero Oxygen Zane",  "Airlock Alice",     "Oort Cloud Otto",
            "Kuiper Belt Kip",   "Van Allen Al",      "Magnetar Meg",      "Blazar Blaze",
            "Supernova Nora",    "Kilonova Kona",     "Dark Energy Dee",   "Exoplanet Enzo",
            "Goldilocks Zone",   "Tidally Locked",    "Roche Limit Rhea",  "Escape Velocity",
            "Delta V Devon",     "Apoapsis Abe",      "Periapsis Pia",     "Hohmann Transfer",
            "Gravity Assist",    "Aerobrake Ava",     "Lithobrake Leon",   "Max Q Mack",
            "Go For Launch",     "Hold Hold Hold",    "Scrubbed Again",    "T Minus Ten",
            "Booster Bertha",    "Fairing Fell Off",  "Payload Paula",     "Orbital Debris",
            "Space Elevator",    "Fermi Paradox",     "Drake Equation",    "Great Filter Gil",
            "Dyson Swarm Dawn",  "SETI Listener",     "Wow Signal Will",   "Oumuamua Omar",
            "Great Red Spot",    "Cassini Cassie",    "Voyager Vicky",     "Golden Record",
            "Pale Blue Dot",     "Hubble Trouble",    "Starlink Steve",    "Space Debris Deb",
            "Geostationary Jo",  "Polar Orbit Paul",  "Retro Rocket Ray",  "Ion Engine Ian",
            "Solar Sail Saul",   "Warp Core Cora",    "Dilithium Dylan",   "Tractor Beam Tam",
            "Cloaking Device",   "Photon Torpedo",    "Shields At Full",   "Red Shirt Ricky",
            "Beam Me Up Benny",  "Live Long Lonnie",  "The Kessel Runt",   "Gravity Well Walt",
            "Lagrange Point Lee", "Comet Chaser Chad", "Meteor Shower Mo",  "Eclipse Elvis",
            "Equinox Enid",      "Solstice Sol",      "Aurora Snorealis",  "Zenith Zed",
            "Nadir Nadia",       "Azimuth Astrid",    "Albedo Alba",       "Perigee Podge",
            "Star Nosed Mole",   "Cosmic Ray Chuck",  "Muon Shower Mona",  "Pion Pioneer",
            "Lepton Leapin",     "Hadron Collider",   "Boson Buddy",       "Fermion Farrah",
            "Spin Half Harpo",   "Charm Quark Cher",  "Strange Quark Sky", "Up Quark Uma",
            "Down Quark Dot",
        };

        public static string NameOf(int botId) =>
            Names[System.Math.Abs(botId - 1) % Names.Length];
    }

    public static class BotSystem
    {
        // 250 commanders total: the player + 249 rivals (user spec 2026-07-07).
        public const int BotCount = 249;
        /// <summary>Live think cadence (seconds of sim time between bot decisions).</summary>
        public const int ThinkIntervalTicks = 60;
        /// <summary>Coarse cadence for catch-up/pre-sim (large gaps).</summary>
        public const int CatchUpIntervalTicks = 600;
        /// <summary>A bot weighs an attack roll this often.</summary>
        public const int AttackRollIntervalTicks = 900;
        /// <summary>Galaxy-wide minimum spacing between raids aimed at the player.
        /// (45 → 35 min at the aggression bump — user: "a tad" hotter.)</summary>
        public const int InboundCooldownTicks = 2100; // 35 min
        /// <summary>Chance-per-roll multiplier on Aggression (raised 0.25 → 0.32).</summary>
        public const double AggressionRollMult = 0.32;
        /// <summary>Fraction of the docked combat fleet a raider commits (was 0.6).</summary>
        public const double RaidCommitFraction = 0.7;
        /// <summary>Raiders only pick fights they expect to WIN: their committed
        /// fleet must out-power the defender's docked fleet by this edge.</summary>
        public const double BeatabilityEdge = 1.15;
        /// <summary>Bots ignore the player below this might — same new-commander shield as PvP had.</summary>
        public const long PlayerShieldMight = 500;
        /// <summary>Raiders won't strike targets over this multiple of their own might
        /// (user spec: bots that are too small don't attack a much higher player).</summary>
        public const double PunchUpLimit = 2.5;
        /// <summary>Longest simulated head start at galaxy creation. Trimmed from 3 days
        /// (user report 2026-07-07: rivals opened at CC 8 / everything 7+, which read
        /// as cheating) — a 36 h-old server still has a leaderboard with teeth.</summary>
        public const int MaxPreSimTicks = 36 * 3600;
        /// <summary>"At the keyboard" action windows: a bot only starts NEW orders during
        /// windows this long that pass the DecideChance roll — real players don't
        /// re-queue the instant a timer pops. This (plus the single queue) is the
        /// player's edge (user spec 2026-07-07: bot wait times must feel realistic).</summary>
        public const int DecideWindowTicks = 1200; // 20 min
        public const double DecideChance = 0.35;
        /// <summary>Panic port: lose this many defenses inside the window → warp away.</summary>
        public const int PortLossThreshold = 3;
        public const int PortWindowTicks = 6 * 3600;
        public const int PortCooldownTicks = 12 * 3600;
        /// <summary>Precision hunt-jump: at most one per day, only toward marks at least
        /// this far away, landing within the given ring of the target.</summary>
        public const int HuntJumpCooldownTicks = 24 * 3600;
        public const int HuntJumpMinTargetDist = 300;
        public const int HuntJumpLandMinR = 40;
        public const int HuntJumpLandMaxR = 120;
        /// <summary>A hunt mark / grudge is dropped if not settled within this long.</summary>
        public const int FocusExpiryTicks = 3 * 24 * 3600;
        /// <summary>Chance a spied empire notices the sweep and scouts back.</summary>
        public const double SpyBackChance = 0.6;

        // ---------- creation ----------

        public static BotPersonality PersonalityOf(int galaxySeed, int botId)
        {
            var rng = Rng.Mulberry32(unchecked((uint)galaxySeed ^ (uint)(botId * 0x9E3779B1)));
            double aggression = 0.05 + rng() * 0.85;
            double economyFocus = 0.30 + rng() * 0.60;
            double activity = 0.25 + rng() * 0.70;
            int avatarSeed = (int)(rng() * 10) % 10;
            // Even thirds of blue/red/green, using the REAL shop skin ids — the old
            // bare "crimson"/"emerald" strings never matched the map's skin table,
            // which is why every rival planet rendered as the untinted purple disc.
            double skinRoll = rng();
            string skin = skinRoll < 1.0 / 3 ? "default"
                : skinRoll < 2.0 / 3 ? "skin-crimson" : "skin-emerald";
            int preSim = (int)(rng() * MaxPreSimTicks);
            return new BotPersonality(aggression, economyFocus, activity, avatarSeed, skin, preSim);
        }

        /// <summary>
        /// Found the simulated server: 99 bots SCATTERED across the whole galaxy
        /// disk (user feedback: the shared rim ring read as non-random), each
        /// pre-simulated by its personality's head start so the galaxy feels
        /// lived-in from the first map open.
        /// </summary>
        public static BotGalaxy CreateGalaxy(int galaxySeed, TileXY playerHome, int count = BotCount)
        {
            var galaxy = new BotGalaxy();
            var taken = new HashSet<string> { playerHome.Key() };
            var homes = new List<TileXY> { playerHome };

            for (int i = 1; i <= count; i++)
            {
                var personality = PersonalityOf(galaxySeed, i);
                var state = GameState.CreateNewGame(galaxySeed, testMode: false);

                // Strip the player-only TestMode grants (no DM/speed-ups/buffs, ever),
                // then seat the bot on its own opening wallet — 250K each (user spec:
                // rivals come out of the gate swinging).
                state.Resources = new ResourceBag(Balance.BotStartResources,
                    Balance.BotStartResources, Balance.BotStartResources).Milli();
                state.Premium.DarkMatter = 0;
                state.Inventory.Clear();

                // Whole-disk scatter (0.22–1.02 of the half-size), re-salting until
                // the tile also keeps a little breathing room from other homes.
                state.HomeTile = Spawn.SpawnTileFor($"bot-{i}", galaxySeed, taken, 0.22, 1.02);
                for (int salt = 0; salt < 8; salt++)
                {
                    bool crowded = false;
                    foreach (var other in homes)
                        if (TileXY.Distance(state.HomeTile, other) < 12) { crowded = true; break; }
                    if (!crowded) break;
                    state.HomeTile = Spawn.SpawnTileFor($"bot-{i}-s{salt}", galaxySeed, taken, 0.22, 1.02);
                }
                taken.Add(state.HomeTile.Key());
                homes.Add(state.HomeTile);
                state.Profile = new Profile { Name = BotNames.NameOf(i), AvatarSeed = personality.AvatarSeed };
                state.Skins.ActivePlanet = personality.Skin;

                var bot = new BotEmpire
                {
                    Id = i,
                    State = state,
                    LastThinkTick = 0,
                    NextAttackRollTick = AttackRollIntervalTicks + (i * 37) % AttackRollIntervalTicks,
                };

                // Head start: simulate the bot having "played" before the player joined.
                // Runs on the bot's own clock offset — decisions only, no player interplay.
                if (personality.PreSimTicks > 0)
                {
                    AdvanceBot(bot, personality, personality.PreSimTicks, galaxySeed);
                    // Rebase to the player's clock: the head start happened "before tick 0".
                    RebaseToTickZero(bot);
                }
                bot.CachedMight = PowerSystem.ComputePower(bot.State);
                galaxy.Bots.Add(bot);
            }
            galaxy.NextInboundWindowTick = InboundCooldownTicks; // quiet first 45 min
            return galaxy;
        }

        /// <summary>Shift a pre-simulated bot's absolute tick stamps back to t=0 on the player clock.</summary>
        static void RebaseToTickZero(BotEmpire bot)
        {
            int offset = bot.State.Tick;
            if (offset == 0) return;
            foreach (var o in bot.State.BuildQueue) o.EndsAtTick = Math.Max(1, o.EndsAtTick - offset);
            foreach (var o in bot.State.ResearchQueue) o.EndsAtTick = Math.Max(1, o.EndsAtTick - offset);
            foreach (var o in bot.State.ShipQueue)
                if (o.NextDoneAtTick > 0) o.NextDoneAtTick = Math.Max(1, o.NextDoneAtTick - offset);
            bot.State.Tick = 0;
            bot.LastThinkTick = 0;
            bot.NextAttackRollTick = Math.Max(1, bot.NextAttackRollTick - offset);
        }

        // ---------- per-frame advance (called from the Game layer after the player sim ticks) ----------

        /// <summary>
        /// Bring every bot up to the player's clock and resolve any inbound attacks
        /// that have landed. Cheap when nothing is due; does the full catch-up loop
        /// after offline gaps. `events` may be suppressed during catch-up.
        /// </summary>
        public static void Advance(GameState player, BotGalaxy galaxy, SimEventBus events)
        {
            int now = player.Tick;
            // Everything below is tick-stamped, so between sim ticks (~59 of every
            // 60 frames) the whole galaxy advance collapses to this one compare.
            if (now == galaxy.LastAdvanceTick) return;
            int prevTick = galaxy.LastAdvanceTick;
            galaxy.LastAdvanceTick = now;
            foreach (var bot in galaxy.Bots)
            {
                // Per-bot cadence jitter breaks the lockstep where all 99 bots
                // became due on the same tick — that one-frame spike every minute
                // read as the whole map hitching ("fleets skipping").
                if (now - bot.LastThinkTick < ThinkIntervalTicks + bot.Id % 13) continue;
                var personality = PersonalityOf(player.Seed, bot.Id);
                AdvanceBot(bot, personality, now, player.Seed);
                bot.CachedMight = PowerSystem.ComputePower(bot.State);
                // Reactive: "you scanned me, I scan you" — the counter-probe the
                // bot queued when the player's spy swept it (RaidArrivals).
                if (bot.SpyBackAtTick > 0 && now >= bot.SpyBackAtTick)
                {
                    bot.SpyBackAtTick = 0;
                    if (!galaxy.Inbound.Exists(a => a.BotId == bot.Id)
                        && !ClanSystem.SameClanAsPlayer(player, bot)) // clanmates don't scout clanmates
                        LaunchAtPlayer(player, galaxy, bot, fleet: null, launchTick: now);
                }
                RollAttacks(player, galaxy, bot, personality, events);
            }
            // Intercepts fight first — always before the raid they were sent to stop —
            // and garrisons change the guard only after the raids of their watch.
            StrikeSystem.BeforeRaids(player, galaxy, events);
            CoreSystem.Tick(player, galaxy, events);
            ResolveBotMarches(player, galaxy, events);
            ResolveInbound(player, galaxy, events);
            StrikeSystem.AfterRaids(player, galaxy, events, prevTick);
        }

        /// <summary>Run think steps until the bot's own state reaches `toTick`.</summary>
        static void AdvanceBot(BotEmpire bot, BotPersonality personality, int toTick, int galaxySeed)
        {
            var state = bot.State;
            while (state.Tick < toTick)
            {
                int gap = toTick - state.Tick;
                int step = gap > 3600 ? CatchUpIntervalTicks : ThinkIntervalTicks;
                int next = Math.Min(toTick, state.Tick + step);
                ThinkStep(bot, personality, next, galaxySeed);
            }
            bot.LastThinkTick = state.Tick;
        }

        /// <summary>One coarse step: accrue economy, complete orders, then (if awake) decide.</summary>
        static void ThinkStep(BotEmpire bot, BotPersonality personality, int toTick, int galaxySeed)
        {
            var state = bot.State;
            int from = state.Tick;

            // Economy — the exact telescoping integral the player's ResourceSystem uses.
            var rates = ResourceSystem.GetRates(state);
            foreach (var res in Data.Resources.All)
            {
                long gain = ResourceSystem.ProducedBetween(rates.Get(res), from, toTick);
                if (gain > 0) state.Resources.Set(res, state.Resources.Get(res) + gain);
            }

            // Gathering abstraction: while awake, bots "fly gather loops" off-screen
            // — a real player's biggest early income. Base rate for scraping nearby
            // nodes with whatever's docked, plus a per-hauler bonus once a hauler
            // fleet exists. (Without this, mine output alone takes days to fund the
            // Command Center curve — no real commander plays mines-only.)
            if (IsAwake(bot, personality, from, galaxySeed))
            {
                int haulers = state.Ships.TryGetValue(HullId.Hauler, out var h) ? h : 0;
                long perHourMilli = (120 + Math.Min(haulers, 30) * 12) * 1000L;
                long bonus = perHourMilli * (toTick - from) / 3600;
                state.Resources.Gold += bonus;
                state.Resources.Quartz += (long)(bonus * 0.6);
                state.Resources.Helium += (long)(bonus * 0.3);
            }

            state.Tick = toTick;
            CompleteOrders(state);

            if (IsAwake(bot, personality, toTick, galaxySeed))
                Decide(bot, personality, galaxySeed);
        }

        /// <summary>Hour-bucketed presence: a bot with Activity 0.6 is "online" ~60% of
        /// hours. Public so the map can show gather flights for awake rivals only.</summary>
        public static bool IsAwake(int galaxySeed, int botId, double activity, int tick)
        {
            int hour = tick / 3600;
            return Rng.Hash2d(unchecked((uint)galaxySeed * 31u + (uint)botId), hour, 7919)
                < activity;
        }

        static bool IsAwake(BotEmpire bot, BotPersonality personality, int tick, int galaxySeed) =>
            IsAwake(galaxySeed, bot.Id, personality.Activity, tick);

        /// <summary>Apply every finished order against absolute ticks (jump-safe, batch-aware).</summary>
        static void CompleteOrders(GameState state)
        {
            // Buildings — the completion sweep tolerates any queue shape (bots run
            // the standard two parallel orders since the 2026-07-08 pacing change).
            for (int i = state.BuildQueue.Count - 1; i >= 0; i--)
            {
                var o = state.BuildQueue[i];
                if (o.EndsAtTick > state.Tick) continue;
                if (o.MineId is int mineId)
                {
                    var mine = state.ExtraMines.Find(m => m.Id == mineId);
                    if (mine != null) mine.Level = o.ToLevel;
                }
                else state.Buildings[o.Building].Level = o.ToLevel;
                state.BuildQueue.RemoveAt(i);
            }

            for (int i = state.ResearchQueue.Count - 1; i >= 0; i--)
            {
                var o = state.ResearchQueue[i];
                if (o.EndsAtTick > state.Tick) continue;
                state.Research[o.TechId] = o.ToLevel;
                state.ResearchQueue.RemoveAt(i);
            }

            // Ships — batch completion with per-ship anchor times across the
            // Balance.ShipQueueSlots parallel lines (FleetSystem.Tick completes one
            // per call per line; this closes whole batches across a jump). Repeats
            // while removals slide new orders into the active window.
            for (int pass = 0; pass < 8; pass++)
            {
                bool removed = false;
                int lines = Math.Min(Balance.ShipQueueSlots, state.ShipQueue.Count);
                for (int i = lines - 1; i >= 0; i--)
                {
                    var o = state.ShipQueue[i];
                    int buildTime = Math.Max(1, FleetSystem.ShipBuildTime(state, o.Hull));
                    if (o.NextDoneAtTick == 0) // slid into the window — anchor now
                    {
                        o.NextDoneAtTick = state.Tick + buildTime;
                        continue;
                    }
                    if (o.NextDoneAtTick > state.Tick) continue;
                    int elapsed = state.Tick - o.NextDoneAtTick;
                    int done = Math.Min(o.Remaining, 1 + elapsed / buildTime);
                    state.Ships[o.Hull] = (state.Ships.TryGetValue(o.Hull, out var n) ? n : 0) + done;
                    o.Remaining -= done;
                    if (o.Remaining <= 0) { state.ShipQueue.RemoveAt(i); removed = true; }
                    else o.NextDoneAtTick += done * buildTime;
                }
                if (!removed) break;
            }
        }

        // ---------- decisions ----------

        /// <summary>
        /// "At the keyboard" gate: new orders only start during hashed 20-min windows
        /// that pass DecideChance. Timers still complete on schedule — this models a
        /// real commander who ISN'T re-queueing the second something finishes, and
        /// it's what keeps a single-queue bot honestly slower than an attentive
        /// double-queue player (user spec).
        /// </summary>
        static bool AtKeyboard(int galaxySeed, int botId, int tick, int salt) =>
            Rng.Hash2d(unchecked((uint)galaxySeed * 131u + (uint)salt), botId, tick / DecideWindowTicks)
                < DecideChance;

        static void Decide(BotEmpire bot, BotPersonality personality, int galaxySeed)
        {
            var state = bot.State;
            var rng = Rng.Mulberry32(unchecked((uint)galaxySeed
                ^ (uint)(bot.Id * 0x85EBCA6B) ^ (uint)state.Tick));

            if (AtKeyboard(galaxySeed, bot.Id, state.Tick, 1)) DecideBuild(state, personality, rng);
            if (AtKeyboard(galaxySeed, bot.Id, state.Tick, 2)) DecideResearch(state, personality, rng);
            if (AtKeyboard(galaxySeed, bot.Id, state.Tick, 3)) DecideShips(state, personality, rng);
        }

        static void DecideBuild(GameState state, BotPersonality personality, Func<double> rng)
        {
            // Standard two parallel queues (bots never hold the shop's third slot,
            // so BuildSlots always returns the base 2 for them).
            if (state.BuildQueue.Count >= BuildingSystem.BuildSlots(state)) return;

            // Energy crunch always comes first — a starved base helps nobody.
            var energy = ResourceSystem.GetEnergyBalance(state);
            if (energy.Factor < 1f
                && BuildingSystem.CheckUpgrade(state, BuildingId.PowerPlant).Ok)
            {
                BuildingSystem.StartUpgrade(state, BuildingId.PowerPlant);
                return;
            }

            // An unlocked, affordable extra mine is near-free economy — grab it.
            foreach (var type in MineTypes.All)
            {
                if (BuildingSystem.MineCountOfType(state, type) >= BuildingSystem.AllowedMinesForType(state))
                    continue;
                if (!BuildingSystem.CheckBuildMine(state, type).Ok) continue;
                int plot = NextFreePlot(state);
                if (plot < 0) break;
                BuildingSystem.BuildMine(state, type, plot, out _);
                return;
            }

            // Self-preservation instinct: a wallet spilling far past the Warehouse
            // shield is exactly what the loot-ranked raiders hunt — smart
            // commanders raise the vault BEFORE the vultures circle.
            if (LootableTotal(state) > ResourceSystem.GetProtected(state).Total * 2
                && BuildingSystem.CheckUpgrade(state, BuildingId.Warehouse).Ok)
            {
                BuildingSystem.StartUpgrade(state, BuildingId.Warehouse);
                return;
            }

            // Personality pick: economy bots lean on the smart economy suggestion,
            // military bots push Shipyard/Lab/Radar/Warehouse. CC breaks cap-locks.
            BuildingId pick;
            if (rng() < personality.EconomyFocus)
            {
                pick = BuildingSystem.NextBestUpgrade(state);
            }
            else
            {
                var military = new[]
                {
                    BuildingId.Shipyard, BuildingId.ResearchLab,
                    BuildingId.Warehouse, BuildingId.RadarStation,
                };
                pick = military[(int)(rng() * military.Length) % military.Length];
            }

            if (BuildingSystem.CheckUpgrade(state, pick).Ok)
            {
                BuildingSystem.StartUpgrade(state, pick);
                return;
            }
            // Blocked (usually the CC cap) → raise the Command Center instead.
            if (BuildingSystem.CheckUpgrade(state, BuildingId.CommandCenter).Ok)
                BuildingSystem.StartUpgrade(state, BuildingId.CommandCenter);
        }

        static int NextFreePlot(GameState state)
        {
            for (int plot = 0; plot < 9; plot++)
            {
                bool used = false;
                foreach (var m in state.ExtraMines) if (m.Plot == plot) { used = true; break; }
                if (!used) return plot;
            }
            return -1;
        }

        static void DecideResearch(GameState state, BotPersonality personality, Func<double> rng)
        {
            // Standard two parallel queues (the shop's third slot is player-only).
            if (state.ResearchQueue.Count >= ResearchSystem.ResearchSlots(state)) return;
            if (state.Buildings[BuildingId.ResearchLab].Level < 1) return;
            // Don't raid the construction budget while the base is tiny: research
            // only spends while a build is running (or once the colony matures).
            if (state.BuildQueue.Count == 0
                && state.Buildings[BuildingId.CommandCenter].Level < 3) return;

            // Walk the tree in a personality-flavored order and start the first
            // tech that passes the real CheckResearch (lab level, prereqs, cost).
            bool economyFirst = rng() < personality.EconomyFocus;
            foreach (var id in Techs.All)
            {
                var def = Techs.Defs[id];
                bool isEconomy = def.Category != TechCategory.Military && def.Category != TechCategory.Defense;
                if (isEconomy != economyFirst) continue;
                if (ResearchSystem.CheckResearch(state, id).Ok)
                {
                    ResearchSystem.StartResearch(state, id);
                    return;
                }
            }
            foreach (var id in Techs.All)
            {
                if (ResearchSystem.CheckResearch(state, id).Ok)
                {
                    ResearchSystem.StartResearch(state, id);
                    return;
                }
            }
        }

        static void DecideShips(GameState state, BotPersonality personality, Func<double> rng)
        {
            // Standard two parallel production lines, same as the player.
            if (state.ShipQueue.Count >= Balance.ShipQueueSlots) return;
            if (state.Buildings[BuildingId.Shipyard].Level < 1) return;

            // Keep a couple of recon probes docked — they fuel the reactive layer
            // (scouting far marks before hunt-jumps, spying back when scanned).
            int probes = state.Ships.TryGetValue(HullId.Probe, out var pr) ? pr : 0;
            if (probes < 2 && FleetSystem.MaxBuildable(state, HullId.Probe) >= 2)
            {
                FleetSystem.QueueShips(state, HullId.Probe, 2);
                AnchorShipQueue(state);
                return;
            }

            // Haulers first (the economy backbone — they raise the gather income,
            // so they're exempt from the construction-savings rule below).
            int haulers = state.Ships.TryGetValue(HullId.Hauler, out var h) ? h : 0;
            int wantHaulers = 4 + state.Buildings[BuildingId.CommandCenter].Level;
            bool buyHaulers = haulers < wantHaulers && rng() > personality.Aggression * 0.5;

            if (buyHaulers && FleetSystem.MaxBuildable(state, HullId.Hauler) >= 2)
            {
                FleetSystem.QueueShips(state, HullId.Hauler,
                    Math.Min(4, FleetSystem.MaxBuildable(state, HullId.Hauler)));
                AnchorShipQueue(state);
                return;
            }

            // SAVE, don't splurge: an idle build queue early on means DecideBuild
            // couldn't afford its next building — spending the savings on combat
            // ships here would starve construction forever (bots would never reach
            // the Command Center costs that unlock everything else).
            if (state.BuildQueue.Count == 0
                && state.Buildings[BuildingId.CommandCenter].Level < 5) return;

            // Best affordable combat hull, scanned top-down (Ships.All is tier-ordered).
            for (int i = Ships.All.Count - 1; i >= 0; i--)
            {
                var hull = Ships.All[i];
                if (hull == HullId.Hauler || hull == HullId.Probe) continue;
                if (FleetSystem.UnlockBlocker(state, hull) != null) continue;
                int buildable = FleetSystem.MaxBuildable(state, hull);
                if (buildable < 3) continue;
                FleetSystem.QueueShips(state, hull, Math.Min(3 + (int)(rng() * 5), buildable));
                AnchorShipQueue(state);
                return;
            }
        }

        /// <summary>Freshly queued batches anchor their first completion immediately
        /// (the 1 Hz FleetSystem.Tick that normally does this doesn't run for bots).
        /// Anchors every unanchored order inside the parallel-line window.</summary>
        static void AnchorShipQueue(GameState state)
        {
            int lines = Math.Min(Balance.ShipQueueSlots, state.ShipQueue.Count);
            for (int i = 0; i < lines; i++)
            {
                var o = state.ShipQueue[i];
                if (o.NextDoneAtTick == 0)
                    o.NextDoneAtTick = state.Tick + Math.Max(1, FleetSystem.ShipBuildTime(state, o.Hull));
            }
        }

        // ---------- attacks ----------

        static void RollAttacks(GameState player, BotGalaxy galaxy, BotEmpire bot,
            BotPersonality personality, SimEventBus events)
        {
            int now = player.Tick;
            if (now < bot.NextAttackRollTick) return;
            // Catch-up spreads the rolls across the gap at their HISTORICAL ticks
            // instead of dumping every decision on the final tick — which was why
            // the whole news feed used to timestamp one simultaneous mega-battle
            // after time away (user report).
            int guard = 0;
            while (bot.NextAttackRollTick <= now && guard++ < 128)
            {
                int rollTick = bot.NextAttackRollTick;
                bot.NextAttackRollTick += AttackRollIntervalTicks;
                RollAttackAt(player, galaxy, bot, personality, rollTick);
            }
            if (bot.NextAttackRollTick <= now) // ancient stamp — re-anchor
                bot.NextAttackRollTick = now + AttackRollIntervalTicks;
        }

        static void RollAttackAt(GameState player, BotGalaxy galaxy, BotEmpire bot,
            BotPersonality personality, int rollTick)
        {
            // One sortie at a time — a bot with a fleet already in flight sits tight.
            if (galaxy.Marches.Exists(m => m.BotId == bot.Id)) return;
            if (galaxy.Inbound.Exists(a => a.BotId == bot.Id)) return;

            var rng = Rng.Mulberry32(unchecked((uint)player.Seed
                ^ (uint)(bot.Id * 0x27D4EB2F) ^ (uint)rollTick));
            // A clan at war rolls hotter ("war fever").
            var myClan = galaxy.FindClan(bot.ClanId);
            double fever = myClan != null && myClan.WarWithClanId != 0 ? ClanSystem.WarFever : 1.0;
            if (rng() >= personality.Aggression * AggressionRollMult * fever) return; // stand down

            var fleet = CombatFleetOf(bot.State, RaidCommitFraction);
            if (CombatResolver.FleetCount(fleet) < 8) return; // no worthwhile fleet yet
            long myPower = EstimateFleetPower(fleet);

            // A fixated bot settles its business first: the hunt mark its probe
            // confirmed rich, or the grudge against whoever raided it. If the
            // score can't be settled yet (target outgrew it), the grudge keeps —
            // "remember to fight another day" (user spec).
            if (bot.FocusTargetId >= 0)
            {
                if (rollTick - bot.FocusSetTick > FocusExpiryTicks) bot.FocusTargetId = -1;
                else if (ActOnFocus(player, galaxy, bot, rollTick, fleet)) return;
            }

            // FREE-FOR-ALL target pick (user spec): every empire — the player
            // included — is ranked by UNSHIELDED loot, and raiders strike near
            // the top of that list. Sitting on a fat unprotected wallet is what
            // paints the target on your back, exactly like a real server.
            // Two intelligence gates per candidate:
            //   - Punch-up limit (might over ~2.5× the attacker's is out of reach);
            //   - Beatability: the defender's DOCKED fleet must be out-powered by
            //     a clear edge or the raider walks away — no suiciding a light
            //     wing into a fortress. A real garrison is now a real deterrent.
            long reachCap = (long)(Math.Max(bot.CachedMight, 1) * PunchUpLimit);
            var candidates = new List<(int botId, long loot)>(); // botId 0 = the player
            foreach (var other in galaxy.Bots)
            {
                if (other.Id == bot.Id) continue;
                if (ClanSystem.SameClan(bot, other)) continue;       // clanmates never raid clanmates
                if (other.CachedMight < PlayerShieldMight) continue; // young empires shielded
                if (other.CachedMight > reachCap) continue;         // too big to bite
                if (myPower < (long)(EstimateDefensePower(other.State) * BeatabilityEdge))
                    continue;                                       // a fight they'd lose
                long loot = LootableTotal(other.State);
                // At war, the enemy clan's members rank as the richest marks.
                if (myClan != null && other.ClanId != 0 && myClan.WarWithClanId == other.ClanId)
                    loot = (long)(loot * ClanSystem.WarTargetWeight);
                candidates.Add((other.Id, loot));
            }
            long playerMight = PowerSystem.ComputePower(player);
            bool playerEligible = rollTick >= galaxy.NextInboundWindowTick
                && !ClanSystem.SameClanAsPlayer(player, bot) // clanmates never raid clanmates
                && playerMight >= PlayerShieldMight
                && playerMight <= reachCap
                && player.Buffs.ShieldUntilTick <= rollTick // Aegis Shield: untargetable
                && myPower >= (long)(EstimateDefensePower(player) * BeatabilityEdge);
            if (playerEligible)
            {
                long loot = LootableTotal(player);
                if (myClan != null && player.ClanId != 0 && myClan.WarWithClanId == player.ClanId)
                    loot = (long)(loot * ClanSystem.WarTargetWeight);
                candidates.Add((0, loot));
            }
            if (candidates.Count == 0) return;

            candidates.Sort((a, b) => b.loot.CompareTo(a.loot));
            // Squared roll biases hard toward the richest few, with some spread.
            int reach = Math.Min(8, candidates.Count);
            int pickIndex = (int)(rng() * rng() * reach) % reach;
            var pick = candidates[pickIndex];

            if (pick.botId == 0)
            {
                galaxy.NextInboundWindowTick = rollTick + InboundCooldownTicks;
                bool spyFirst = rng() < 0.35; // scout the mark before committing the fleet
                if (!spyFirst) AddLootHaulers(bot, fleet, pick.loot);
                LaunchAtPlayer(player, galaxy, bot, spyFirst ? null : fleet, rollTick);
            }
            else
            {
                var defender = galaxy.Find(pick.botId);
                if (defender == null) return;
                // A rich mark far across the galaxy gets SCOUTED first (user spec:
                // "if the bots spy someone farther with a lot of resources, they
                // could port closer to try and fight them"). The probe flies for
                // real; its returning intel sets the Focus, the Focus drives the
                // once-a-day precision jump, and the strike follows from close in.
                double targetDist = TileXY.Distance(bot.HomeTile, defender.HomeTile);
                if (targetDist > HuntJumpMinTargetDist)
                {
                    if (LaunchSpyAtBot(galaxy, bot, defender, rollTick)) return;
                    // no probe docked — commit to the long flight instead
                }
                AddLootHaulers(bot, fleet, pick.loot);
                LaunchAtBot(galaxy, bot, defender, fleet, rollTick);
            }
        }

        /// <summary>Crude battle strength of a fleet — enough signal for raiders to
        /// tell a soft target from a fortress without running a full resolver pass.</summary>
        /// <summary>What a raider has to beat at <paramref name="target"/>'s home: the
        /// docked fleet scaled by the research it defends with, plus the Orbital
        /// Batteries' fire (damage × the rounds they get with no fleet home).</summary>
        public static long EstimateDefensePower(GameState target)
        {
            // Global scalars only: this runs for every candidate on every attack
            // roll, and the per-hull DefenseMods tables made catch-up ~2× slower.
            double scale = (ResearchSystem.AtkMult(target) + ResearchSystem.HpMult(target)
                + ResearchSystem.EffectTotal(target, TechEffectKind.DefAtkMult)
                + ResearchSystem.EffectTotal(target, TechEffectKind.DefHpMult)) * 0.5;
            long battery = (long)ResearchSystem.BatteryLevel(target)
                * Balance.BatteryDamagePerLevel * Balance.BatteryOnlyRounds;
            return (long)(EstimateFleetPower(target.Ships) * scale) + battery;
        }

        public static long EstimateFleetPower(Dictionary<HullId, int> comp)
        {
            long power = 0;
            foreach (var kv in comp)
            {
                if (kv.Value <= 0) continue;
                var def = Ships.Defs[kv.Key];
                power += (long)kv.Value * (def.Atk + def.Shield + def.Hp / 2);
            }
            return power;
        }

        /// <summary>Smart looting: if the combat wing can't CARRY the expected haul,
        /// haulers ride along (up to 70% of those docked). More loot per victory —
        /// and a juicier, more vulnerable raid fleet, same trade a greedy human makes.</summary>
        static void AddLootHaulers(BotEmpire bot, Dictionary<HullId, int> fleet, long expectedLootMilli)
        {
            int haulers = bot.State.Ships.TryGetValue(HullId.Hauler, out var h) ? h : 0;
            if (haulers <= 0 || expectedLootMilli <= 0) return;
            long combatCargo = MarchSystem.FleetCargoCap(fleet);
            long deficit = expectedLootMilli - combatCargo;
            if (deficit <= 0) return;
            int perHauler = Math.Max(1, Ships.Defs[HullId.Hauler].Cargo);
            int wanted = (int)Math.Min(haulers * 7L / 10, (deficit + perHauler - 1) / perHauler);
            if (wanted > 0) fleet[HullId.Hauler] = wanted;
        }

        /// <summary>
        /// Try to settle the bot's Focus (hunt mark or revenge grudge). Returns true
        /// when the roll was consumed (jump or launch); false lets the normal
        /// loot-ranked pick proceed (e.g. the target is still too big to bite).
        /// </summary>
        static bool ActOnFocus(GameState player, BotGalaxy galaxy, BotEmpire bot,
            int rollTick, Dictionary<HullId, int> fleet)
        {
            long reachCap = (long)(Math.Max(bot.CachedMight, 1) * PunchUpLimit);

            long myPower = EstimateFleetPower(fleet);

            if (bot.FocusTargetId == 0) // the player wronged this bot
            {
                if (ClanSystem.SameClanAsPlayer(player, bot)) { bot.FocusTargetId = -1; return false; }
                long playerMight = PowerSystem.ComputePower(player);
                if (playerMight > reachCap) return false;  // not big enough yet — another day
                if (playerMight < PlayerShieldMight) { bot.FocusTargetId = -1; return false; }
                if (rollTick < galaxy.NextInboundWindowTick) return true; // wait for a slot
                if (player.Buffs.ShieldUntilTick > rollTick) return true; // wait out the Aegis
                // Revenge is patient: it waits until the fight is winnable.
                if (myPower < (long)(EstimateDefensePower(player) * BeatabilityEdge))
                    return false;
                galaxy.NextInboundWindowTick = rollTick + InboundCooldownTicks;
                AddLootHaulers(bot, fleet, LootableTotal(player));
                LaunchAtPlayer(player, galaxy, bot, fleet, rollTick); // no scouting — they KNOW
                bot.FocusTargetId = -1;
                return true;
            }

            var mark = galaxy.Find(bot.FocusTargetId);
            if (mark == null || ClanSystem.SameClan(bot, mark)) { bot.FocusTargetId = -1; return false; }
            if (mark.CachedMight > reachCap) return false; // outgrew the grudge — for now
            if (mark.CachedMight < PlayerShieldMight) { bot.FocusTargetId = -1; return false; }
            if (myPower < (long)(EstimateDefensePower(mark.State) * BeatabilityEdge))
                return false; // the mark keeps a strong garrison — bide time

            double dist = TileXY.Distance(bot.HomeTile, mark.HomeTile);
            if (dist > HuntJumpMinTargetDist
                && rollTick - bot.LastHuntJumpTick >= HuntJumpCooldownTicks)
            {
                var pad = Spawn.TileNear($"bot-{bot.Id}-hunt-{rollTick}", bot.State.Seed,
                    mark.HomeTile, HuntJumpLandMinR, HuntJumpLandMaxR, HomeKeys(galaxy, player));
                if (pad is TileXY landing)
                {
                    bot.State.HomeTile = landing;
                    bot.LastHuntJumpTick = rollTick;
                    return true; // strike comes on a later roll, from the forward base
                }
            }
            AddLootHaulers(bot, fleet, LootableTotal(mark.State));
            LaunchAtBot(galaxy, bot, mark, fleet, rollTick);
            bot.FocusTargetId = -1;
            return true;
        }

        /// <summary>Fly one recon probe at a rival colony (a REAL round trip on the
        /// map). Needs a docked probe. Intel lands as the attacker's Focus at
        /// arrival — see ResolveBotMarches.</summary>
        static bool LaunchSpyAtBot(BotGalaxy galaxy, BotEmpire bot, BotEmpire target, int launchTick)
        {
            int probes = bot.State.Ships.TryGetValue(HullId.Probe, out var p) ? p : 0;
            if (probes < 1) return false;
            if (galaxy.Marches.Exists(m => m.BotId == bot.Id)) return false; // one sortie at a time

            var ships = new Dictionary<HullId, int> { [HullId.Probe] = 1 };
            int travelSec = Balance.TravelSeconds(
                TileXY.Distance(bot.HomeTile, target.HomeTile), MarchSystem.FleetSpeed(ships));
            bot.State.Ships[HullId.Probe] = probes - 1; // credited back at return

            galaxy.Marches.Add(new BotMarch
            {
                Id = galaxy.NextMarchId++,
                BotId = bot.Id,
                TargetBotId = target.Id,
                IsSpy = true,
                Ships = ships,
                From = bot.HomeTile,
                To = target.HomeTile,
                LaunchTick = launchTick,
                ArrivesAtTick = launchTick + travelSec,
            });
            return true;
        }

        /// <summary>Every current home tile (player + bots) — relocation collision set.</summary>
        static HashSet<string> HomeKeys(BotGalaxy galaxy, GameState? player)
        {
            var keys = new HashSet<string>();
            if (player != null) keys.Add(player.HomeTile.Key());
            foreach (var b in galaxy.Bots) keys.Add(b.HomeTile.Key());
            return keys;
        }

        /// <summary>Total milli-resources NOT protected by the empire's Warehouse shield.</summary>
        static long LootableTotal(GameState state)
        {
            var shielded = ResourceSystem.GetProtected(state);
            return Math.Max(0, state.Resources.Gold - shielded.Gold)
                 + Math.Max(0, state.Resources.Quartz - shielded.Quartz)
                 + Math.Max(0, state.Resources.Helium - shielded.Helium);
        }

        /// <summary>Up to `fraction` of each docked combat hull.</summary>
        public static Dictionary<HullId, int> CombatFleetOf(GameState state, double fraction)
        {
            var fleet = new Dictionary<HullId, int>();
            foreach (var kv in state.Ships)
            {
                if (kv.Key == HullId.Hauler || kv.Key == HullId.Probe) continue;
                int n = (int)(kv.Value * fraction);
                if (n > 0) fleet[kv.Key] = n;
            }
            return fleet;
        }

        /// <summary>File a real inbound contact — the Radar Station picks it up inside its lead window.</summary>
        static void LaunchAtPlayer(GameState player, BotGalaxy galaxy, BotEmpire bot,
            Dictionary<HullId, int>? fleet, int launchTick)
        {
            bool isFleet = fleet != null;
            var ships = fleet ?? new Dictionary<HullId, int> { [HullId.Probe] = 1 };
            double dist = TileXY.Distance(bot.HomeTile, player.HomeTile);
            int speed = MarchSystem.FleetSpeed(ships);
            if (speed <= 0) return;
            int travelSec = Balance.TravelSeconds(dist, speed);

            galaxy.Inbound.Add(new BotAttack
            {
                Id = galaxy.NextAttackId++,
                BotId = bot.Id,
                IsFleet = isFleet,
                Ships = ships,
                LaunchTick = launchTick,
                ArrivesAtTick = launchTick + travelSec,
                From = bot.HomeTile,
            });
            if (isFleet)
            {
                // The raiding ships leave the bot's dock for the duration (they're
                // credited back, minus losses, when the attack resolves).
                foreach (var kv in ships)
                    bot.State.Ships[kv.Key] = Math.Max(0, bot.State.Ships[kv.Key] - kv.Value);
            }
        }

        /// <summary>Inbound fleets/probes that reached the player's planet resolve here.</summary>
        static void ResolveInbound(GameState player, BotGalaxy galaxy, SimEventBus events)
        {
            for (int i = galaxy.Inbound.Count - 1; i >= 0; i--)
            {
                var atk = galaxy.Inbound[i];
                if (atk.ArrivesAtTick > player.Tick) continue;
                galaxy.Inbound.RemoveAt(i);
                var bot = galaxy.Find(atk.BotId);
                if (bot == null) continue;

                if (!atk.IsFleet)
                {
                    // A spy pass: no combat, but a Radar Station catches the sweep
                    // and files it — so you wake up to "you were scanned" mail
                    // even when the probe flew while you slept. Detail follows
                    // the radar's tier, same as live warnings. No radar, no clue.
                    int radarLevel = RadarSystem.Level(player);
                    if (radarLevel >= 1)
                    {
                        int tier = RadarSystem.DetailTier(radarLevel);
                        InsertMail(player, new RadarWarning
                        {
                            Id = player.NextReportId++,
                            // Stamped at the ARRIVAL tick, not "now" — offline scans
                            // read with their true time-ago in the mailbox.
                            AtTick = atk.ArrivesAtTick,
                            Target = player.HomeTile,
                            // (No "⚠" prefix — it tofu-boxes in the runtime font; the
                            // mailbox paints a warning icon on radar rows instead.)
                            Subject = tier >= 3
                                ? $"RADAR ALERT — {bot.Name}'s probe scanned your colony"
                                : "RADAR ALERT — a spy probe scanned your colony",
                            ArrivesAtTick = atk.ArrivesAtTick,
                            IsFleet = tier >= 2 ? false : null,
                            AttackerName = tier >= 3 ? bot.Name : null,
                        });
                    }
                    continue;
                }

                // Aegis Shield up at IMPACT → the raid deflects: no battle, no loot,
                // the bot's fleet turns for home intact. Buying the shield during
                // the radar warning window is a legitimate (expensive) panic button.
                // Judged at the ARRIVAL tick: during offline catch-up player.Tick is
                // already the END of the window, so a shield that covered the impact
                // but lapsed before you reopened the app used to be ignored.
                if (player.Buffs.ShieldUntilTick > atk.ArrivesAtTick)
                {
                    foreach (var kv in atk.Ships)
                        bot.State.Ships[kv.Key] =
                            (bot.State.Ships.TryGetValue(kv.Key, out var n) ? n : 0) + kv.Value;
                    InsertMail(player, new BattleMailReport
                    {
                        Id = player.NextReportId++,
                        AtTick = atk.ArrivesAtTick,
                        Target = player.HomeTile,
                        Subject = $"Raid deflected — {bot.Name} hit your Aegis Shield",
                        Defending = true,
                        AttackerBotId = bot.Id,
                        Report = new BattleReport
                        {
                            Attacker = new Dictionary<HullId, int>(atk.Ships),
                            AttackerSurvivors = new Dictionary<HullId, int>(atk.Ships),
                            Defender = new Dictionary<HullId, int>(),
                            DefenderSurvivors = new Dictionary<HullId, int>(),
                            Winner = BattleWinner.Defender,
                            Location = player.HomeTile,
                            DefenderName = player.Profile.Name,
                        },
                    });
                    continue;
                }

                // The battle: bot fleet vs whatever the player has DOCKED right now.
                // Ships out on fly-to holds/marches are away — the radar warning is
                // a real dodge window, exactly like the old PvP arrival rule.
                // Clanmates in range send a share of their docked warships; they
                // fight under the colony's defense research (it's your home they hold).
                // A clan garrison standing at the colony (StrikeSystem) fights first;
                // its members don't also scramble as helpers.
                var garrison = StrikeSystem.StationedAtPlayer(galaxy, atk.ArrivesAtTick);
                var onGuard = new HashSet<int>();
                foreach (var g in garrison) onGuard.Add(g.BotId);
                var reinforcements = ClanSystem.DefenseHelpers(player, galaxy, 0, bot.Id, onGuard);
                var lines = new List<Dictionary<HullId, int>> { new Dictionary<HullId, int>(player.Ships) };
                foreach (var g in garrison) lines.Add(new Dictionary<HullId, int>(g.Ships));
                foreach (var (_, sent) in reinforcements) lines.Add(sent);
                var defenders = ClanSystem.Combine(lines);
                Dictionary<HullId, int>? allyShips = lines.Count > 1
                    ? ClanSystem.Combine(lines.GetRange(1, lines.Count - 1)) : null;
                var allyNameList = new List<string>();
                foreach (var g in garrison)
                    if (galaxy.Find(g.BotId) is { } guard && !allyNameList.Contains(guard.Name)) allyNameList.Add(guard.Name);
                foreach (var (ally, _) in reinforcements)
                    if (!allyNameList.Contains(ally.Name)) allyNameList.Add(ally.Name);
                var report = CombatResolver.Resolve(atk.Ships, defenders,
                    ResearchSystem.CombatMods(bot.State), ResearchSystem.DefenseMods(player));
                report.Location = player.HomeTile;
                report.DefenderName = player.Profile.Name;

                var loot = new ResourceBag();
                if (report.Winner == BattleWinner.Attacker)
                {
                    var shielded = ResourceSystem.GetProtected(player);
                    var lootable = new ResourceBag(
                        Math.Max(0, player.Resources.Gold - shielded.Gold),
                        Math.Max(0, player.Resources.Quartz - shielded.Quartz),
                        Math.Max(0, player.Resources.Helium - shielded.Helium));
                    // War Games (galaxy event): raiding fleets haul more.
                    long cap = (long)(MarchSystem.FleetCargoCap(report.AttackerSurvivors) * EventSystem.RaidLootMult(player));
                    long total = lootable.Total;
                    double scale = total > 0 ? Math.Min(1.0, cap / (double)total) : 0;
                    loot.Gold = (long)Math.Floor(lootable.Gold * scale);
                    loot.Quartz = (long)Math.Floor(lootable.Quartz * scale);
                    loot.Helium = (long)Math.Floor(lootable.Helium * scale);
                }
                report.Loot = loot.Clone();

                // Defender side: losses + plunder land now. Each hull's losses are
                // shared in proportion to who put ships in the line.
                var losses = ClanSystem.SplitLosses(lines, report.DefenderSurvivors);
                ClanSystem.Deduct(player.Ships, losses[0]);
                for (int gi = 0; gi < garrison.Count; gi++)
                {
                    ClanSystem.Deduct(garrison[gi].Ships, losses[gi + 1]);
                    if (CombatResolver.FleetCount(garrison[gi].Ships) == 0) galaxy.Marches.Remove(garrison[gi]);
                }
                for (int r = 0; r < reinforcements.Count; r++)
                {
                    ClanSystem.Deduct(reinforcements[r].ally.State.Ships, losses[garrison.Count + r + 1]);
                    reinforcements[r].ally.CachedMight = PowerSystem.ComputePower(reinforcements[r].ally.State);
                }
                player.Resources.Gold = Math.Max(0, player.Resources.Gold - loot.Gold);
                player.Resources.Quartz = Math.Max(0, player.Resources.Quartz - loot.Quartz);
                player.Resources.Helium = Math.Max(0, player.Resources.Helium - loot.Helium);

                bool playerLost = report.Winner == BattleWinner.Attacker;
                if (playerLost)
                {
                    player.Stats.BattlesLost++;
                    // Battle scar: burns 4 h from the ARRIVAL, so a raid that landed
                    // hours into an offline stretch may already be embers. Max keeps
                    // a later-stamped burn from being shortened by an older arrival.
                    player.BurningUntilTick = Math.Max(player.BurningUntilTick,
                        atk.ArrivesAtTick + Balance.BurnDurationSec);
                }
                else
                {
                    player.Stats.BattlesWon++;
                    player.Stats.DefensesWon++;
                }
                ClanSystem.RecordBattle(player, galaxy, bot.Id, 0, playerLost);

                InsertMail(player, new BattleMailReport
                {
                    Id = player.NextReportId++,
                    AtTick = atk.ArrivesAtTick,
                    Target = player.HomeTile,
                    Subject = playerLost
                        ? $"Colony raided by {bot.Name}"
                        : $"Raid repelled — {bot.Name}",
                    Defending = true,
                    AttackerBotId = bot.Id,
                    Report = report,
                    AllyShips = allyShips,
                    AllyNames = allyNameList.Count > 0 ? string.Join(", ", allyNameList) : null,
                });
                events.Emit(new ColonyRaided(report, bot.Name));
                galaxy.AddNews(atk.ArrivesAtTick, bot.Id, 0, playerLost, loot.Total);

                // Attacker (bot) side: survivors + loot come home.
                foreach (var kv in report.AttackerSurvivors)
                    bot.State.Ships[kv.Key] =
                        (bot.State.Ships.TryGetValue(kv.Key, out var n) ? n : 0) + kv.Value;
                bot.State.Resources.Add(loot);
                if (playerLost) bot.State.Stats.BattlesWon++;
                else bot.State.Stats.BattlesLost++;
            }
        }

        /// <summary>Commit a raid fleet to a REAL flight between the two colonies —
        /// visible on the galaxy map — resolving at arrival (ResolveBotMarches).</summary>
        static void LaunchAtBot(BotGalaxy galaxy, BotEmpire attacker, BotEmpire defender,
            Dictionary<HullId, int> fleet, int launchTick)
        {
            int speed = MarchSystem.FleetSpeed(fleet);
            if (speed <= 0) return;
            double dist = TileXY.Distance(attacker.HomeTile, defender.HomeTile);
            int travelSec = Balance.TravelSeconds(dist, speed);

            // The fleet leaves the dock for the round trip.
            foreach (var kv in fleet)
                attacker.State.Ships[kv.Key] = Math.Max(0,
                    (attacker.State.Ships.TryGetValue(kv.Key, out var n) ? n : 0) - kv.Value);

            galaxy.Marches.Add(new BotMarch
            {
                Id = galaxy.NextMarchId++,
                BotId = attacker.Id,
                TargetBotId = defender.Id,
                Ships = new Dictionary<HullId, int>(fleet),
                From = attacker.HomeTile,
                To = defender.HomeTile,
                LaunchTick = launchTick,
                ArrivesAtTick = launchTick + travelSec,
            });
        }

        /// <summary>Bot-vs-bot marches: battle at arrival, survivors + loot fly home,
        /// dock credit at return. The same loot-hunting free-for-all the player lives
        /// in — now flown in real time so the map shows the wars happening.</summary>
        static void ResolveBotMarches(GameState player, BotGalaxy galaxy, SimEventBus events)
        {
            for (int i = galaxy.Marches.Count - 1; i >= 0; i--)
            {
                var march = galaxy.Marches[i];
                var attacker = galaxy.Find(march.BotId);
                if (attacker == null) { galaxy.Marches.RemoveAt(i); continue; }

                // Joint-strike wings wait for your battle; garrison wings stand guard
                // (StrikeSystem and RaidArrivals send them home); core assaults are
                // fought by CoreSystem.
                bool wing = march.Kind == BotMarchKind.Escort || march.Kind == BotMarchKind.Garrison
                    || march.Kind == BotMarchKind.CoreAssault;
                if (!march.Resolved && !wing && player.Tick >= march.ArrivesAtTick)
                {
                    var defender = galaxy.Find(march.TargetBotId);
                    if (defender == null || !defender.HomeTile.Equals(march.To))
                    {
                        // Target ported away mid-flight — the fleet finds empty space
                        // and turns for home with nothing.
                        march.Resolved = true;
                        march.ReturnsAtTick = march.ArrivesAtTick
                            + (march.ArrivesAtTick - march.LaunchTick);
                        continue;
                    }

                    if (march.IsSpy)
                    {
                        // Recon pass: no combat. The probe confirms the mark — the
                        // attacker fixates on it (Focus drives the hunt-jump + the
                        // strike on later rolls). And the SCANNED bot usually
                        // notices and scouts right back (user spec: reactive bots).
                        attacker.FocusTargetId = defender.Id;
                        attacker.FocusSetTick = march.ArrivesAtTick;
                        if (Rng.Hash2d(unchecked((uint)player.Seed * 53u + (uint)defender.Id),
                                attacker.Id, march.ArrivesAtTick) < SpyBackChance)
                            LaunchSpyAtBot(galaxy, defender, attacker, march.ArrivesAtTick);
                        march.Resolved = true;
                        march.ReturnsAtTick = march.ArrivesAtTick + Balance.TravelSeconds(
                            TileXY.Distance(march.To, march.From),
                            Math.Max(1, MarchSystem.FleetSpeed(march.Ships)));
                        continue;
                    }

                    // They joined the same clan while the fleet was in flight: it stands down.
                    if (ClanSystem.SameClan(attacker, defender))
                    {
                        march.Resolved = true;
                        march.ReturnsAtTick = march.ArrivesAtTick + (march.ArrivesAtTick - march.LaunchTick);
                        continue;
                    }

                    // The defender's clanmates in range help hold the line — and any
                    // garrison the player stationed there (StrikeSystem).
                    var helpers = ClanSystem.DefenseHelpers(player, galaxy, defender.Id, attacker.Id);
                    var lines = new List<Dictionary<HullId, int>> { new Dictionary<HullId, int>(defender.State.Ships) };
                    foreach (var (_, sent) in helpers) lines.Add(sent);
                    int clanLines = lines.Count;
                    var guards = StrikeSystem.PlayerGarrisonsAt(player, defender, march.ArrivesAtTick);
                    foreach (var g in guards) lines.Add(new Dictionary<HullId, int>(g.Ships));
                    var defending = ClanSystem.Combine(lines);
                    var report = CombatResolver.Resolve(march.Ships, defending,
                        ResearchSystem.CombatMods(attacker.State), ResearchSystem.DefenseMods(defender.State));
                    report.Location = defender.HomeTile;
                    report.DefenderName = defender.Name;

                    var losses = ClanSystem.SplitLosses(lines, report.DefenderSurvivors);
                    ClanSystem.Deduct(defender.State.Ships, losses[0]);
                    for (int hi = 0; hi < helpers.Count; hi++)
                    {
                        ClanSystem.Deduct(helpers[hi].ally.State.Ships, losses[hi + 1]);
                        helpers[hi].ally.CachedMight = PowerSystem.ComputePower(helpers[hi].ally.State);
                    }
                    if (guards.Count > 0)
                    {
                        var clanNames = new List<string> { defender.Name };
                        foreach (var (ally, _) in helpers) clanNames.Add(ally.Name);
                        StrikeSystem.SettlePlayerGarrisons(player, defender, attacker, report, guards,
                            losses.GetRange(clanLines, guards.Count), ClanSystem.Combine(lines.GetRange(0, clanLines)),
                            string.Join(", ", clanNames), march.ArrivesAtTick, events);
                    }

                    long lootTotal = 0;
                    if (report.Winner == BattleWinner.Attacker)
                    {
                        var shielded = ResourceSystem.GetProtected(defender.State);
                        var lootable = new ResourceBag(
                            Math.Max(0, defender.State.Resources.Gold - shielded.Gold),
                            Math.Max(0, defender.State.Resources.Quartz - shielded.Quartz),
                            Math.Max(0, defender.State.Resources.Helium - shielded.Helium));
                        long cap = (long)(MarchSystem.FleetCargoCap(report.AttackerSurvivors)
                            * EventSystem.RaidLootMult(defender.State));
                        long total = lootable.Total;
                        double scale = total > 0 ? Math.Min(1.0, cap / (double)total) : 0;
                        var loot = new ResourceBag(
                            (long)(lootable.Gold * scale),
                            (long)(lootable.Quartz * scale),
                            (long)(lootable.Helium * scale));
                        defender.State.Resources.Gold -= loot.Gold;
                        defender.State.Resources.Quartz -= loot.Quartz;
                        defender.State.Resources.Helium -= loot.Helium;
                        attacker.State.Stats.BattlesWon++;
                        defender.State.Stats.BattlesLost++;
                        defender.State.BurningUntilTick = Math.Max(defender.State.BurningUntilTick,
                            march.ArrivesAtTick + Balance.BurnDurationSec); // battle scar
                        march.LootMilli = loot;
                        lootTotal = loot.Total;
                        // The loser remembers who did this — and comes back for its
                        // resources once it's grown big enough (user spec: reclaim).
                        defender.FocusTargetId = attacker.Id;
                        defender.FocusSetTick = march.ArrivesAtTick;
                        RecordDefenseLoss(galaxy, defender, march.ArrivesAtTick, player);
                    }
                    else
                    {
                        attacker.State.Stats.BattlesLost++;
                        defender.State.Stats.BattlesWon++;
                    }
                    ClanSystem.RecordBattle(player, galaxy, attacker.Id, defender.Id,
                        report.Winner == BattleWinner.Attacker);
                    defender.CachedMight = PowerSystem.ComputePower(defender.State);
                    galaxy.AddNews(march.ArrivesAtTick, attacker.Id, defender.Id,
                        report.Winner == BattleWinner.Attacker, lootTotal);

                    march.Ships = new Dictionary<HullId, int>(report.AttackerSurvivors);
                    if (CombatResolver.FleetCount(march.Ships) == 0)
                    {
                        galaxy.Marches.RemoveAt(i); // wiped out — nobody flies home
                        continue;
                    }
                    march.Resolved = true;
                    int backSpeed = MarchSystem.FleetSpeed(march.Ships);
                    march.ReturnsAtTick = march.ArrivesAtTick + Balance.TravelSeconds(
                        TileXY.Distance(march.To, march.From), Math.Max(1, backSpeed));
                }

                if (march.Resolved && player.Tick >= march.ReturnsAtTick)
                {
                    foreach (var kv in march.Ships)
                        attacker.State.Ships[kv.Key] =
                            (attacker.State.Ships.TryGetValue(kv.Key, out var n) ? n : 0) + kv.Value;
                    attacker.State.Resources.Add(march.LootMilli);
                    attacker.CachedMight = PowerSystem.ComputePower(attacker.State);
                    galaxy.Marches.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// "Attacked too much" bookkeeping (user spec): a bot that loses several
        /// defenses inside the rolling window random-ports its colony away — the
        /// same escape a hounded human would buy.
        /// </summary>
        static void RecordDefenseLoss(BotGalaxy galaxy, BotEmpire bot, int tick, GameState? player)
        {
            if (tick - bot.DefenseLossWindowStart > PortWindowTicks)
            {
                bot.DefenseLossWindowStart = tick;
                bot.DefenseLossCount = 0;
            }
            bot.DefenseLossCount++;
            if (bot.DefenseLossCount < PortLossThreshold) return;
            if (tick - bot.LastPortTick < PortCooldownTicks && bot.LastPortTick > 0) return;

            var refuge = Spawn.SpawnTileFor($"bot-{bot.Id}-port-{tick}", bot.State.Seed,
                HomeKeys(galaxy, player), 0.22, 1.02);
            bot.State.HomeTile = refuge;
            bot.LastPortTick = tick;
            bot.DefenseLossCount = 0;
            bot.DefenseLossWindowStart = tick;
        }

        // ---------- the player raiding/spying a bot ----------

        /// <summary>Live defense readout — replaces the old published-snapshot fetch.</summary>
        public static DefenseSnapshot SnapshotOf(BotEmpire bot)
        {
            var shielded = ResourceSystem.GetProtected(bot.State);
            var lootable = new ResourceBag(
                Math.Max(0, bot.State.Resources.Gold - shielded.Gold),
                Math.Max(0, bot.State.Resources.Quartz - shielded.Quartz),
                Math.Max(0, bot.State.Resources.Helium - shielded.Helium));
            var buildings = new Dictionary<BuildingId, int>();
            foreach (var kv in bot.State.Buildings)
                if (kv.Value.Level > 0) buildings[kv.Key] = kv.Value.Level;
            return new DefenseSnapshot
            {
                BotId = bot.Id,
                CommanderName = bot.Name,
                HomeX = bot.HomeTile.X,
                HomeY = bot.HomeTile.Y,
                Might = bot.CachedMight > 0 ? bot.CachedMight : PowerSystem.ComputePower(bot.State),
                Ships = new Dictionary<HullId, int>(bot.State.Ships),
                ProtectedMilli = shielded,
                LootableMilli = lootable,
                Research = new Dictionary<TechId, int>(bot.State.Research),
                Buildings = buildings,
            };
        }

        /// <summary>True while this bot means to pay the player back for a raid:
        /// ApplyPlayerRaid marks it, and the mark holds until ActOnFocus settles
        /// it with a counter-raid or it lapses after FocusExpiryTicks.</summary>
        public static bool HoldsGrudge(BotEmpire bot, int nowTick) =>
            bot.FocusTargetId == 0 && nowTick - bot.FocusSetTick <= FocusExpiryTicks;

        /// <summary>Apply a player raid's outcome to the defending bot (+ news + battle
        /// scar). A bot the player keeps farming counts those losses toward its
        /// panic-port threshold too — hounded rivals eventually flee.</summary>
        /// <param name="ownLosses">The target's own losses when its clanmates fought
        /// beside it (ClanSystem.SplitLosses); null = everything in the report was its.</param>
        public static void ApplyPlayerRaid(BotGalaxy galaxy, BotEmpire bot,
            BattleReport report, ResourceBag lootMilli, GameState? player = null,
            Dictionary<HullId, int>? ownLosses = null)
        {
            if (ownLosses != null) ClanSystem.Deduct(bot.State.Ships, ownLosses);
            else
                foreach (var hull in Ships.All)
                {
                    int had = bot.State.Ships.TryGetValue(hull, out var d) ? d : 0;
                    int inFight = report.Defender.TryGetValue(hull, out var f) ? f : 0;
                    int left = report.DefenderSurvivors.TryGetValue(hull, out var s) ? s : 0;
                    int lost = Math.Min(had, inFight - left);
                    if (lost > 0) bot.State.Ships[hull] = had - lost;
                }
            bot.State.Resources.Gold = Math.Max(0, bot.State.Resources.Gold - lootMilli.Gold);
            bot.State.Resources.Quartz = Math.Max(0, bot.State.Resources.Quartz - lootMilli.Quartz);
            bot.State.Resources.Helium = Math.Max(0, bot.State.Resources.Helium - lootMilli.Helium);
            bool playerWon = report.Winner == BattleWinner.Attacker;
            if (player != null) ClanSystem.RecordBattle(player, galaxy, 0, bot.Id, playerWon);
            if (playerWon)
            {
                bot.State.Stats.BattlesLost++;
                bot.State.BurningUntilTick =
                    bot.State.Tick + Balance.BurnDurationSec; // their planet burns too
                // The bot remembers YOU — expect a visit once it's big enough.
                bot.FocusTargetId = 0;
                bot.FocusSetTick = bot.State.Tick;
                RecordDefenseLoss(galaxy, bot, bot.State.Tick, player);
            }
            else bot.State.Stats.BattlesWon++;
            bot.CachedMight = PowerSystem.ComputePower(bot.State);
            galaxy.AddNews(bot.State.Tick, 0, bot.Id, playerWon, lootMilli.Total);
        }

        /// <summary>
        /// Raiding while shielded drops the Aegis (user rule: "if a player attacks
        /// with a shield bubble, their bubble will disappear"). Returns true if a
        /// live shield was broken — callers surface that in the launch message.
        /// </summary>
        public static bool BreakShieldForAggression(GameState state)
        {
            if (state.Buffs.ShieldUntilTick <= state.Tick) return false;
            state.Buffs.ShieldUntilTick = 0;
            return true;
        }

        /// <summary>Mailbox insert with the 50-cap ring (favorites never evict) — MarchSystem's rule.</summary>
        public static void InsertMail(GameState state, MailItem item)
        {
            state.Mailbox.Insert(0, item);
            if (state.Mailbox.Count <= 50) return;
            for (int i = state.Mailbox.Count - 1; i >= 0 && state.Mailbox.Count > 50; i--)
                if (!state.Mailbox[i].Favorite) state.Mailbox.RemoveAt(i);
        }
    }
}
