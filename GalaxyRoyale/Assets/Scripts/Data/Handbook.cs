// The Commander's Handbook (2026-09-30): every system in a few short paragraphs,
// for the player who skipped the training or wants the detail later. Settings ›
// Help › COMMANDER'S HANDBOOK, and the last step of the training, open it.
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public sealed class HandbookTopic
    {
        public string Title = "";
        public string Summary = "";
        public string[] Paragraphs = System.Array.Empty<string>();
    }

    public static class Handbook
    {
        public static readonly IReadOnlyList<HandbookTopic> Topics = new[]
        {
            new HandbookTopic
            {
                Title = "Getting started", Summary = "The loop every commander follows",
                Paragraphs = new[]
                {
                    "Your colony makes resources every hour. Spend them on buildings, research and ships, and the colony makes more.",
                    "The Commander's Path, top left of the base, always shows your next goal and pays a reward when it's done. Its first act covers the basics; its second carries you through the first week. Following it is the quickest way to grow.",
                    "Fleet command drops a supply crate every 4 hours. Up to 3 wait at the Command Center; tap SUPPLY there to open them.",
                    "Out in the galaxy, 249 rival commanders build, raid and form clans on their own schedule, whether or not you're playing. Grow fast enough to hold your own.",
                },
            },
            new HandbookTopic
            {
                Title = "Resources and energy", Summary = "Gold, quartz, helium, energy, Dark Matter",
                Paragraphs = new[]
                {
                    "Gold, quartz and helium pay for everything. The Gold Mine, Quartz Extractor and Helium Refinery make them every hour; upgrade them to make more. Mines ramp up fast through their first six levels.",
                    "Helium is also fuel: every fleet burns it to fly.",
                    "Energy comes from the Power Plant. When your mines need more energy than you make, the percentage in the header drops below 100% and every mine slows down. Upgrade the Power Plant to fix it.",
                    "Dark Matter is rare. It comes from quests, events, achievements, dark-matter fields and holding the Galactic Core, and buys speed-ups and items in the ITEMS shop.",
                },
            },
            new HandbookTopic
            {
                Title = "Buildings", Summary = "Upgrading and the Command Center",
                Paragraphs = new[]
                {
                    "Tap a building to see its quick actions: UPGRADE, INFO and BOOST. INFO opens the full panel with the cost, the time and what the next level does.",
                    "No building can be a higher level than your Command Center. When one is capped, upgrade the Command Center.",
                    "You have two build slots. The ☰ button lists what's under way, and a number on it means a slot is free.",
                    "Speed-Ups cut time off a build; use them from the building's panel (SPEED UP). You can also cancel an upgrade and get the resources back.",
                },
            },
            new HandbookTopic
            {
                Title = "Your planet", Summary = "Command, Mines, Frontier, Port and the Wilds",
                Paragraphs = new[]
                {
                    "Drag the globe to turn it and pinch to zoom out to orbit. The tabs at the bottom fly you to each district.",
                    "COMMAND holds your nine core buildings. MINES holds extra gold, quartz and helium mines; more pads open as the Command Center rises.",
                    "FRONTIER holds the advanced buildings, which unlock at higher Command Center levels. PORT shows your docked fleet.",
                    "The WILDS cover the south of the planet. Survey sectors to clear the fog and find deposits, supply caches and relics; the Drone Factory's drones harvest the deposits for you.",
                },
            },
            new HandbookTopic
            {
                Title = "The Frontier", Summary = "What each advanced building does",
                Paragraphs = new[]
                {
                    "Exchange Terminal: the market, for trading one resource for another. Command Bastion: railguns that fire on raiders every round, and armour for ships at home.",
                    "Salvage Yard: recovers resources from the wrecks after your battles; collect them from its bubble. Drone Factory: harvester drones for the Wilds.",
                    "Repair Dock: ships you lose defending your colony can come back as damaged hulls, repaired for a fraction of their cost within 72 hours.",
                    "Jump Gate: faster fleets that burn less helium, and a free jump that moves your colony every 24 hours. Clan Embassy: more clanmates in your strikes, more supply runs and a bigger share of the Core's clan tribute. Deep Space Observatory: faster surveys, earlier radar warnings, and a forecast of the next Pirate Dreadnought.",
                },
            },
            new HandbookTopic
            {
                Title = "Ships", Summary = "Building a fleet",
                Paragraphs = new[]
                {
                    "Build ships in FLEET. Each hull's card shows its cost, its attack, shields and hull, its speed and how much it carries.",
                    "Fighters are fast and cheap, Bombers hit hard, and Cruisers take a beating. Probes scout, and Haulers carry cargo home. Heavier hulls unlock as the Shipyard levels up.",
                    "A fleet flies at the speed of its slowest ship, and bigger fleets burn more helium.",
                    "Home Guard: warships docked at home raise your production, up to +15% once their might reaches 25 × your Command Center level squared. Ships out flying don't count. FLEET shows where you stand.",
                },
            },
            new HandbookTopic
            {
                Title = "The galaxy map", Summary = "Worlds, camps, colonies and the Core",
                Paragraphs = new[]
                {
                    "Every small world is something to gather: gold moons, quartz ice worlds, helium gas giants, derelicts to salvage and rare dark-matter fields. Red volcanic worlds are pirate camps.",
                    "Colonies have a coloured rim: yours is orange, clanmates teal, anyone attacking you or at war with your clan red.",
                    "FIND flies you to the nearest world of a kind. The ALL, RESOURCES, EMPIRES and HOSTILE buttons filter the map, and ★ saves a location to your favourites.",
                    "The sun at the centre is the Galactic Core. Nothing else can sit inside the dashed Core Zone around it.",
                },
            },
            new HandbookTopic
            {
                Title = "Gathering", Summary = "Filling your stores from the galaxy",
                Paragraphs = new[]
                {
                    "Tap a resource world and GATHER. Pick ships with room for cargo (Haulers carry the most), and they mine it and fly home with what they hold.",
                    "Each world holds only so much. When it runs dry it drifts to a new spot nearby, so there's always more to find.",
                },
            },
            new HandbookTopic
            {
                Title = "Spying and raiding", Summary = "Scouting, attacking and battle reports",
                Paragraphs = new[]
                {
                    "Tap a pirate camp or a rival colony and SPY to send a Probe. Its report, in MAIL, shows the garrison and, for a colony, what you could plunder.",
                    "Tap ATTACK to pick your fleet. Once you've scanned the target, the forecast shows your odds before you launch.",
                    "Every battle files a report in MAIL with your losses and the plunder, and a replay you can watch round by round.",
                    "Pirate camps hold a stockpile that grows with your Command Center. Send Haulers with the attack: on a raid they carry 50% more. The first win at each camp level pays a bonus, sent straight home, plus Dark Matter.",
                },
            },
            new HandbookTopic
            {
                Title = "Defending your colony", Summary = "Raids, radar, shields and vaults",
                Paragraphs = new[]
                {
                    "A new colony starts under beginner protection: no rival can raid or scan it for 48 hours, or until the Command Center reaches level 5, or until you raid another commander. Pirate camps don't count.",
                    "Rival commanders will raid you. Ships at home fight back, and the Command Bastion's railguns join in.",
                    "The Warehouse keeps part of your stockpile out of raiders' reach; the rest can be plundered.",
                    "The Radar Station warns you before raiders land, and higher levels warn earlier and tell you more. With warning, you can send your fleet away or raise a shield.",
                    "An Aegis Shield from ITEMS blocks every raid while it lasts, but launching a raid of your own drops it. DEFENSE research makes your colony's defenders stronger.",
                },
            },
            new HandbookTopic
            {
                Title = "Research", Summary = "Permanent bonuses",
                Paragraphs = new[]
                {
                    "Build a Research Lab, tap it and OPEN RESEARCH. Technologies give permanent bonuses: more production, faster builds, stronger ships and defenders, cheaper flights.",
                    "The ECONOMY, COMBAT and DEFENSE pages group them. Higher Research Lab levels unlock more of them and higher levels of each.",
                    "From Research Lab 10 a third tier opens: Deep-Space Mining, Subspace Navigation, Adaptive Shielding and Command Doctrine (more commander XP).",
                },
            },
            new HandbookTopic
            {
                Title = "Your commander", Summary = "Levels, skills and titles",
                Paragraphs = new[]
                {
                    "Everything you do earns your commander experience: building, research, battles, gathering, surveying the Wilds, events, dailies and more. Every level past the first earns a skill point; spend them in your profile on ranks in three branches of skills.",
                    "Achievements pay Dark Matter, and many unlock a title you can wear on your profile and in the rankings.",
                },
            },
            new HandbookTopic
            {
                Title = "The Citadel", Summary = "The five buildings on the north pole",
                Paragraphs = new[]
                {
                    "Tip your planet north (or tap CITADEL) for the Citadel: four buildings in a ring round the Terraformer, opening from Command Center 4 to 9.",
                    "Academy: more commander XP, cheaper skill resets, and your commander can lead a fleet. Tick COMMANDER LEADS when you launch it and that fleet fights harder; if it's destroyed, your commander needs 6 hours to recover.",
                    "Relic Vault: relics you claim in the Wilds come home as one of six kinds. Each copy adds a small permanent bonus, up to as many copies of a kind as the vault has levels.",
                    "Trade Consulate: commanders nearby post contracts. Accept one and Haulers fly the goods out and come home with more, plus Dark Matter. A new board goes up every 8 hours.",
                    "Missile Silo: when your radar sees a raid coming, open the silo and FIRE to destroy part of the raiding fleet before it lands. Then it reloads.",
                    "Terraformer: pick a path — Oceanic (helium), Crystalline (quartz), Metallic (gold) or Temperate (faster builds, more energy) — and reshape the planet a stage at a time. Switching paths starts over.",
                },
            },
            new HandbookTopic
            {
                Title = "Expeditions", Summary = "Fleets beyond the charted galaxy",
                Paragraphs = new[]
                {
                    "MORE › EXPLORE lists three destinations beyond the map, refreshed every 12 hours. Send a fleet to one; it's away for 2 to 8 hours and off your docks.",
                    "Halfway there, something happens and you make the call. The bold choice pays more than double and can turn up relics — if it comes off. The careful one always pays. If you don't answer, your fleet plays it safe.",
                    "A fleet as strong as the destination calls for, and your commander leading it (the Academy), make a bold call likelier to come off. Two expeditions can be out at once.",
                },
            },
            new HandbookTopic
            {
                Title = "Your nemesis", Summary = "The rival who remembers you",
                Paragraphs = new[]
                {
                    "Trade enough blows with one rival — beating their raids, raiding them back — and they become your nemesis. The NEMESIS chip on the base screen shows who.",
                    "Your nemesis hunts you: they come back for revenge sooner than other rivals, taunt you on the news wire, and every time you beat them they escalate, up to tier III, with fresh ships.",
                    "Beat them three times — raids won on them or raids of theirs repelled — and they break: a big reward, and they leave you alone for a week. Ignore them for five days and they lose interest.",
                },
            },
            new HandbookTopic
            {
                Title = "Clans", Summary = "Allies, strikes and supplies",
                Paragraphs = new[]
                {
                    "Join a clan, or found your own, with up to 15 members. Clanmates fly wings in your joint strikes, send supply runs you collect at the Warehouse, and defend each other.",
                    "Clans can go to war with each other. Find clan commands in the ••• menu under CLAN.",
                },
            },
            new HandbookTopic
            {
                Title = "The Galactic Core", Summary = "Holding the sun at the heart of the galaxy",
                Paragraphs = new[]
                {
                    "Tap the Core on the map to see who holds it. Beat its garrison to seize it; your surviving fleet stays behind as the new garrison.",
                    "Whoever holds the Core earns tribute every hour, and you also get Galactic Command (faster fleets) and the Core Beacon (warning of every assault on it).",
                    "Other commanders will try to take it from you. The Core's HISTORY tab shows every seizure and failed assault.",
                },
            },
            new HandbookTopic
            {
                Title = "Events and seasons", Summary = "Pirate Dreadnought, events, seasons, dailies",
                Paragraphs = new[]
                {
                    "A Pirate Dreadnought drops into the galaxy from time to time. Strike it along with the other commanders before it leaves: your survivors carry off salvage, and its Dark Matter is shared out by damage dealt.",
                    "They take turns: the classic Dreadnought, a Carrier (harder-hitting guns on a lighter hull), a Siege Dreadnought (shells the colonies near it every 4 hours; an Aegis Shield keeps it off) and a Stealth Dreadnought (hidden for its first 8 hours, unless your Observatory is level 3).",
                    "Galaxy events change the rules for a day or two, such as faster research or richer mines, and set a goal with a reward. Daily objectives reset every day and pay Dark Matter.",
                    "Some events put something on the map near you. A Comet Pass brings a comet rich in every resource and Dark Matter; rivals mine it too, so be quick. A Trade Caravan docks at four waystations: ATTACK it for its cargo, or ESCORT it with warships for a fee when it moves on.",
                    "The Bounty Board marks the rim's most-wanted raider: win a raid on them for a big reward, before a rival collects it. In a Core Tournament the Core's holder is thrown out and its guardians fall to half strength; whoever holds it when the tournament ends wins a prize.",
                    "An Ion Storm darkens your region: fleets fly slower through it, radar can't see raids coming, and its camps carry more loot. A Supernova Warning marks a doomed sector whose worlds gather fast and pay more, until the star explodes and takes every fleet still there.",
                    "Seasons last a week. Everyone is ranked by the might they gain during the season, so a young colony can beat the giants, and the final rank pays Dark Matter.",
                    "From the second week, every galaxy week also brings a twist that bends one rule for everyone, rivals included: Low Gravity (faster fleets), Rich Veins (faster gathering), Solar Maximum (more production), Hunter's Moon (richer Pirate Lords) and more. The event card and EVENTS & SEASON show this week's and next week's.",
                },
            },
            new HandbookTopic
            {
                Title = "The Long Night", Summary = "The campaign and the Pirate Lords",
                Paragraphs = new[]
                {
                    "The Long Night is the story of your commander, told by HALCYON, your colony's ship-mind. Its ten chapters open one at a time as your colony grows: each needs a Command Center level and a number of days since you founded the colony. Open it from MORE › STORY.",
                    "Each chapter sets three objectives, counted from the moment it opens, and ends with a Pirate Lord in their lair. When all three are done, claim the chapter for resources and Dark Matter. After the Commander's Path, the quest card follows the chapter.",
                    "Lairs are marked with a skull on the map. Each lord has a doctrine, the kind of fleet they fly, and their fleet grows with yours, so read it before you attack. Beating a lord pays well, and the first time also brings a relic home.",
                    "Beaten lords don't stay down: every few days one of them returns with a bigger fleet, for another fight and another reward.",
                    "From Command Center 15, MORE › PROJECTS opens the mega-projects: the Dyson Swarm, the Stargate, the Planetary Shield Array and the Orbital Foundry. Each is built in five stages, one at a time, and every stage adds a bonus for good. They're big: a stage costs a day's worth of your mines' output and more.",
                },
            },
            new HandbookTopic
            {
                Title = "Items and settings", Summary = "The shop, speed-ups, codes and help",
                Paragraphs = new[]
                {
                    "ITEMS sells speed-ups, resource packs, shields and relocation for Dark Matter. What you own is in your inventory.",
                    "Settings holds sound, notifications, text size and colour-blind colours, iCloud backup, Game Center, and the privacy policy, support and credits.",
                    "The training can be started again from Settings, under Help.",
                },
            },
        };
    }
}
