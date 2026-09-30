// Galaxy-level progression that needs the player AND the bots: clan politics
// and supply runs, season rollovers, achievements and commander XP. GameContext runs it once per sim
// tick right after the bots advance — live, and at the end of the offline
// catch-up (where the bus holds its events; the debrief reads the results off
// the state instead).
using GalaxyRoyale.Sim.Bots;

namespace GalaxyRoyale.Sim.Systems
{
    public static class ProgressionSystem
    {
        public static void Advance(GameState player, BotGalaxy galaxy, SimEventBus events)
        {
            ClanSystem.Tick(player, galaxy, events);
            BountySystem.Tick(player, galaxy, events); // rival events (2026-09-30)
            NemesisSystem.Tick(player, galaxy, events);
            var season = SeasonSystem.Tick(player, galaxy);
            if (season != null) events.Emit(new SeasonEnded(season));
            foreach (var a in AchievementSystem.CheckNew(player)) events.Emit(new AchievementUnlocked(a));
            CommanderSystem.Tick(player, events); // after the achievements: they're worth XP
        }
    }
}
