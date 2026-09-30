# Game Center — achievements to create

Create these in App Store Connect › your app › Game Center › Achievements (and the leaderboard `galaxyroyale.might`: "Might", integer, high to low). The ids must match exactly — the game derives them from `GalaxyRoyale/Assets/Scripts/Data/Achievements.cs` (`galaxyroyale.` + the id with dashes as underscores). Achievements missing in App Store Connect are skipped quietly.

Points are split by each achievement's Dark Matter reward, 1000 of Apple's 1,000 in total. Change them freely.

| Achievement ID | Name | Description (earned) | Points |
|---|---|---|---|
| `galaxyroyale.first_blood` | First Blood | Win your first battle | 5 |
| `galaxyroyale.veteran` | Veteran | Win 25 battles | 15 |
| `galaxyroyale.warlord` | Warlord | Win 100 battles | 50 |
| `galaxyroyale.pirate_hunter` | Pirate Hunter | Clear 10 pirate camps | 15 |
| `galaxyroyale.scourge` | Scourge of the Void | Clear 50 pirate camps | 40 |
| `galaxyroyale.raider` | Raider | Win 5 raids on rival colonies | 15 |
| `galaxyroyale.conqueror` | Conqueror | Win 25 raids on rival colonies | 50 |
| `galaxyroyale.iron_wall` | Iron Wall | Repel 5 raids on your colony | 15 |
| `galaxyroyale.unbreakable` | Unbreakable | Repel 25 raids on your colony | 50 |
| `galaxyroyale.shipwright` | Shipwright | Build 100 ships | 10 |
| `galaxyroyale.admiral` | Admiral | Build 1,000 ships | 35 |
| `galaxyroyale.plunderer` | Plunderer | Loot 100,000 resources | 25 |
| `galaxyroyale.architect` | Architect | Raise the Command Center to Lv 10 | 35 |
| `galaxyroyale.scholar` | Scholar | Complete 25 research levels | 25 |
| `galaxyroyale.fortress` | Fortress | Complete 10 defense research levels | 25 |
| `galaxyroyale.pathfinder` | Pathfinder | Finish the first act of the Commander's Path | 25 |
| `galaxyroyale.trailblazer` | Trailblazer | Finish both acts of the Commander's Path | 50 |
| `galaxyroyale.diplomat` | Diplomat | Join or found a clan | 10 |
| `galaxyroyale.full_ranks` | Full Ranks | Be in a clan of 15 commanders | 35 |
| `galaxyroyale.warmaster` | Warmaster | Win 3 clan wars | 50 |
| `galaxyroyale.event_hunter` | Event Hunter | Complete 5 galaxy event goals | 25 |
| `galaxyroyale.contender` | Contender | Finish a season in the top 10 | 35 |
| `galaxyroyale.champion` | Champion | Win a season | 75 |
| `galaxyroyale.rising_power` | Rising Power | Reach 50,000 might | 35 |
| `galaxyroyale.core_breacher` | Core Breacher | Seize the Galactic Core | 40 |
| `galaxyroyale.warden` | Warden of the Core | Collect 24 hours of Core tribute | 75 |
| `galaxyroyale.dreadnought_hunter` | Dreadnought Hunter | Deal 500,000 damage to Pirate Dreadnoughts | 35 |
| `galaxyroyale.final_blow` | Leviathan Slayer | Land the final blow on a Pirate Dreadnought | 50 |
| `galaxyroyale.trader` | Merchant Prince | Make 25 trades on the galactic market | 15 |
| `galaxyroyale.nemesis_slayer` | Nemesis Slayer | Break a nemesis | 35 |
