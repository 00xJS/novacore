# Game Center — achievements to create

Create these in App Store Connect › your app › Game Center › Achievements (and the leaderboard `galaxyroyale.might`: "Might", integer, high to low). The ids must match exactly — the game derives them from `GalaxyRoyale/Assets/Scripts/Data/Achievements.cs` (`galaxyroyale.` + the id with dashes as underscores). Achievements missing in App Store Connect are skipped quietly.

Points are split by each achievement's Dark Matter reward: 925 of Apple's 1,000, leaving room for later ones. Change them freely.

| Achievement ID | Name | Description (earned) | Points |
|---|---|---|---|
| `galaxyroyale.first_blood` | First Blood | Win your first battle | 5 |
| `galaxyroyale.veteran` | Veteran | Win 25 battles | 10 |
| `galaxyroyale.warlord` | Warlord | Win 100 battles | 35 |
| `galaxyroyale.pirate_hunter` | Pirate Hunter | Clear 10 pirate camps | 10 |
| `galaxyroyale.scourge` | Scourge of the Void | Clear 50 pirate camps | 25 |
| `galaxyroyale.raider` | Raider | Win 5 raids on rival colonies | 10 |
| `galaxyroyale.conqueror` | Conqueror | Win 25 raids on rival colonies | 35 |
| `galaxyroyale.iron_wall` | Iron Wall | Repel 5 raids on your colony | 10 |
| `galaxyroyale.unbreakable` | Unbreakable | Repel 25 raids on your colony | 35 |
| `galaxyroyale.shipwright` | Shipwright | Build 100 ships | 5 |
| `galaxyroyale.admiral` | Admiral | Build 1,000 ships | 20 |
| `galaxyroyale.plunderer` | Plunderer | Loot 100,000 resources | 15 |
| `galaxyroyale.architect` | Architect | Raise the Command Center to Lv 10 | 20 |
| `galaxyroyale.scholar` | Scholar | Complete 25 research levels | 15 |
| `galaxyroyale.fortress` | Fortress | Complete 10 defense research levels | 15 |
| `galaxyroyale.pathfinder` | Pathfinder | Finish the first act of the Commander's Path | 15 |
| `galaxyroyale.trailblazer` | Trailblazer | Finish both acts of the Commander's Path | 35 |
| `galaxyroyale.diplomat` | Diplomat | Join or found a clan | 5 |
| `galaxyroyale.full_ranks` | Full Ranks | Be in a clan of 15 commanders | 20 |
| `galaxyroyale.warmaster` | Warmaster | Win 3 clan wars | 35 |
| `galaxyroyale.event_hunter` | Event Hunter | Complete 5 galaxy event goals | 15 |
| `galaxyroyale.contender` | Contender | Finish a season in the top 10 | 20 |
| `galaxyroyale.champion` | Champion | Win a season | 55 |
| `galaxyroyale.rising_power` | Rising Power | Reach 50,000 might | 20 |
| `galaxyroyale.core_breacher` | Core Breacher | Seize the Galactic Core | 25 |
| `galaxyroyale.warden` | Warden of the Core | Collect 24 hours of Core tribute | 55 |
| `galaxyroyale.dreadnought_hunter` | Dreadnought Hunter | Deal 500,000 damage to Pirate Dreadnoughts | 20 |
| `galaxyroyale.final_blow` | Leviathan Slayer | Land the final blow on a Pirate Dreadnought | 35 |
| `galaxyroyale.trader` | Merchant Prince | Make 25 trades on the galactic market | 10 |
| `galaxyroyale.nemesis_slayer` | Nemesis Slayer | Break a nemesis | 20 |
| `galaxyroyale.lord_breaker` | Lord-Breaker | Defeat 3 Pirate Lords | 20 |
| `galaxyroyale.court_breaker` | Court-Breaker | Defeat every Pirate Lord | 55 |
| `galaxyroyale.nightwalker` | Nightwalker | Finish 5 chapters of The Long Night | 25 |
| `galaxyroyale.dawnbringer` | Dawnbringer | Finish The Long Night | 70 |
| `galaxyroyale.wonder_builder` | Wonder-Builder | Complete a mega-project | 35 |
| `galaxyroyale.architect_of_worlds` | Architect of Worlds | Complete all four mega-projects | 70 |
