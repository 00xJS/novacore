// Raid orchestration vs the simulated commanders — the async-raid model kept
// from the online era, minus the wire: the fleet flies for real and the battle
// resolves AT ARRIVAL against the bot's LIVE state (RaidArrivals), so the
// radar-dodge design survives the single-player pivot intact. The defense
// snapshot is computed on demand — it can never be stale.
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Systems;
using GalaxyRoyale.Game.UI;

namespace GalaxyRoyale.Game
{
    public static class RaidService
    {
        /// <summary>Empires below this might are new-commander shielded (applies to bots too).</summary>
        public const long NewPlayerShieldMight = BotSystem.PlayerShieldMight;

        public static bool IsShielded(long might) => might < NewPlayerShieldMight;

        /// <summary>
        /// Launch only — the battle resolves AT ARRIVAL against the bot's live
        /// defense (RaidArrivals). `ships` must already be validated against
        /// docked counts (PreviewMarch re-checks).
        /// </summary>
        public static (bool ok, string message) LaunchRaid(
            GameContext ctx, DefenseSnapshot target, Dictionary<HullId, int> ships)
        {
            var state = ctx.State!;
            if (IsShielded(target.Might))
                return (false, "That commander is under a new-commander shield");

            // Full fleet flies out; arrival at the node-less tile turns it around,
            // which is the moment RaidArrivals resolves the fight.
            var tile = new TileXY(target.HomeX, target.HomeY);
            var res = MarchSystem.SendRaidMarch(state, ships, tile, out int marchId);
            if (!res.Ok) return (false, res.Reason ?? "Cannot launch");

            // Aggression drops your own Aegis Shield (user rule) — attack OR defend, not both.
            bool shieldBroke = BotSystem.BreakShieldForAggression(state);

            var march = state.Marches.Find(m => m.Id == marchId)!;
            int arrivesInSec = march.ArrivesAtTick - state.Tick;
            RaidArrivals.Register(marchId, target.BotId, target.CommanderName,
                isRaid: true, ships, tile);

            return (true,
                $"Fleet away — battle on arrival in {UiTheme.FmtDuration(arrivesInSec)}"
                + (shieldBroke ? " · your Aegis Shield dropped" : ""));
        }

        /// <summary>
        /// Recon a rival colony: one probe flies out and the intel (garrison read
        /// AT ARRIVAL from the live bot) files as a Camp-kind spy report — which is
        /// exactly what MarchSystem.HasSpyIntel checks, so the raid panel's defense
        /// readout unlocks.
        /// </summary>
        public static (bool ok, string message) SpyBot(GameContext ctx, DefenseSnapshot target)
        {
            var state = ctx.State!;
            int probes = state.Ships.TryGetValue(HullId.Probe, out var p) ? p : 0;
            if (probes < 1) return (false, "No spy probes docked — build one in Fleet Command");

            var tile = new TileXY(target.HomeX, target.HomeY);
            var probeComp = new Dictionary<HullId, int> { [HullId.Probe] = 1 };
            var res = MarchSystem.SendRaidMarch(state, probeComp, tile, out int probeMarchId);
            if (!res.Ok) return (false, res.Reason ?? "Cannot launch");

            var probeMarch = state.Marches.Find(m => m.Id == probeMarchId)!;
            int arrivesInSec = probeMarch.ArrivesAtTick - state.Tick;
            RaidArrivals.Register(probeMarchId, target.BotId, target.CommanderName,
                isRaid: false, probeComp, tile);

            return (true, $"Probe away — intel on arrival in {UiTheme.FmtDuration(arrivesInSec)}");
        }

        /// <summary>Mailbox insert with the 50-cap ring (favorites never evict).</summary>
        public static void InsertMail(GameState state, MailItem item) =>
            BotSystem.InsertMail(state, item);
    }
}
