// Raid orchestration vs the simulated commanders — the async-raid model kept
// from the online era, minus the wire: the fleet flies for real and the battle
// resolves AT ARRIVAL against the bot's LIVE state (RaidArrivals), so the
// radar-dodge design survives the single-player pivot intact. The defense
// snapshot is computed on demand — it can never be stale.
using System;
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
        /// docked counts (PreviewMarch re-checks). <paramref name="joint"/>: a
        /// JOINT STRIKE — clanmates' wings fly from their colonies and land with
        /// your fleet (StrikeSystem.StrikeWings).
        /// </summary>
        public static (bool ok, string message) LaunchRaid(
            GameContext ctx, DefenseSnapshot target, Dictionary<HullId, int> ships, bool joint = false)
        {
            var state = ctx.State!;
            var galaxy = ctx.Bots;
            if (IsShielded(target.Might))
                return (false, "That commander is under a new-commander shield");
            if (galaxy?.Find(target.BotId) is { } targetBot && ClanSystem.SameClanAsPlayer(state, targetBot))
                return (false, $"{target.CommanderName} is your clanmate — clanmates never raid each other");

            // Full fleet flies out; arrival at the node-less tile turns it around,
            // which is the moment RaidArrivals resolves the fight.
            var tile = new TileXY(target.HomeX, target.HomeY);
            var res = MarchSystem.SendRaidMarch(state, ships, tile, out int marchId);
            if (!res.Ok) return (false, res.Reason ?? "Cannot launch");

            // Aggression drops your own Aegis Shield (user rule) — attack OR defend, not both.
            bool shieldBroke = BotSystem.BreakShieldForAggression(state);

            var march = state.Marches.Find(m => m.Id == marchId)!;
            int arrivesInSec = march.ArrivesAtTick - state.Tick;

            // Joint strike: clanmates' wings leave their docks now (or when their
            // shorter flight has to start) and land with your fleet.
            int wings = 0;
            if (joint && galaxy != null)
                wings = StrikeSystem.LaunchWings(state, galaxy,
                    StrikeSystem.StrikeWings(state, galaxy, tile, march.ArrivesAtTick, target.BotId),
                    BotMarchKind.Escort, marchId, target.BotId, tile, march.ArrivesAtTick,
                    ClanSystem.RaidSupportCooldownSec).Count;
            RaidArrivals.Register(marchId, target.BotId, target.CommanderName, isRaid: true, ships, tile);
            // Save now: the pending raid (PlayerPrefs) and the ships that left
            // your docks and your clanmates' (save file) must agree if the app
            // dies before the next autosave — or the fight would hand the
            // clanmates' survivors back to docks they never left.
            LocalBootstrap.RequestSync();

            GameAudio.Feedback(Sfx.Launch, Haptic.Medium);
            return (true,
                $"Fleet away — battle on arrival in {UiTheme.FmtDuration(arrivesInSec)}"
                + (wings > 0 ? $" · {wings} clanmate{(wings == 1 ? "" : "s")} flying with you" : "")
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

            GameAudio.Play(Sfx.Launch, 0.6f, 1.25f);
            GameAudio.Buzz(Haptic.Light);
            return (true, $"Probe away — intel on arrival in {UiTheme.FmtDuration(arrivesInSec)}");
        }

        /// <summary>Mailbox insert with the 50-cap ring (favorites never evict).</summary>
        public static void InsertMail(GameState state, MailItem item) =>
            BotSystem.InsertMail(state, item);
    }
}
