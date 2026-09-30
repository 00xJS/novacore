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
                    "The Commander's Path, top left of the base, always shows your next goal and pays a reward when it's done. Following it is the quickest way to grow.",
                    "Out in the galaxy, 249 rival commanders build, raid and form clans on their own schedule, whether or not you're playing. Grow fast enough to hold your own.",
                },
            },
            new HandbookTopic
            {
                Title = "Resources and energy", Summary = "Gold, quartz, helium, energy, Dark Matter",
                Paragraphs = new[]
                {
                    "Gold, quartz and helium pay for everything. The Gold Mine, Quartz Extractor and Helium Refinery make them every hour; upgrade them to make more.",
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
                },
            },
            new HandbookTopic
            {
                Title = "Defending your colony", Summary = "Raids, radar, shields and vaults",
                Paragraphs = new[]
                {
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
                },
            },
            new HandbookTopic
            {
                Title = "Your commander", Summary = "Levels, skills and titles",
                Paragraphs = new[]
                {
                    "Everything you do earns your commander experience. Every level past the first earns a skill point; spend them in your profile on ranks in three branches of skills.",
                    "Achievements pay Dark Matter, and many unlock a title you can wear on your profile and in the rankings.",
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
                    "Galaxy events change the rules for a day or two, such as faster research or richer mines, and set a goal with a reward. Daily objectives reset every day and pay Dark Matter.",
                    "Seasons last a week. Everyone is ranked by the might they gain during the season, so a young colony can beat the giants, and the final rank pays Dark Matter.",
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
