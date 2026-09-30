// March/build/research completions → iOS local notifications, the retention
// hook v1 flagged but couldn't build under WKWebView. Timers are absolute tick
// stamps, so scheduling is trivial: on backgrounding, schedule one notification
// per pending completion at (endsAtTick - now) seconds; on foregrounding,
// clear everything (the live game shows its own toasts).
//
// Android gets the same treatment when the Android build lands (B.6).
using System;
using UnityEngine;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;
#if UNITY_IOS
using Unity.Notifications.iOS;
#endif

namespace GalaxyRoyale.Game
{
    [AddComponentMenu("GalaxyRoyale/March Notifications")]
    [RequireComponent(typeof(GameContext))]
    public sealed class MarchNotifications : MonoBehaviour
    {
        GameContext _ctx = null!;

        void Awake() => _ctx = GetComponent<GameContext>();

#if UNITY_IOS && !UNITY_EDITOR
        void Start() => StartCoroutine(RequestAuthorization());

        static System.Collections.IEnumerator RequestAuthorization()
        {
            using var request = new AuthorizationRequest(
                AuthorizationOption.Alert | AuthorizationOption.Sound | AuthorizationOption.Badge,
                registerForRemoteNotifications: false);
            while (!request.IsFinished) yield return null;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) ScheduleAll();
            else ClearAll();
        }

        void OnApplicationQuit() => ScheduleAll();

        void ScheduleAll()
        {
            ClearAll();
            var state = _ctx.State;
            if (state == null) return;
            int now = state.Tick;

            void Schedule(string id, long inSeconds, string body)
            {
                if (inSeconds < 5) return; // about to land anyway — skip the noise
                iOSNotificationCenter.ScheduleNotification(new iOSNotification
                {
                    Identifier = id,
                    Title = "Galaxy Royale",
                    Body = body,
                    ShowInForeground = false,
                    Trigger = new iOSNotificationTimeIntervalTrigger
                    {
                        TimeInterval = TimeSpan.FromSeconds(inSeconds),
                        Repeats = false,
                    },
                });
            }

            // Each group can be switched off in Settings › Notifications.
            bool timers = Settings.NotifyOn(Settings.Notify.Timers);
            bool raids = Settings.NotifyOn(Settings.Notify.Raids);
            bool galaxyNews = Settings.NotifyOn(Settings.Notify.Galaxy);
            bool clan = Settings.NotifyOn(Settings.Notify.Clan);

            if (timers)
            foreach (var m in state.Marches)
            {
                // Holding in place (a fly-to, a garrison on guard): nothing is due.
                if (m.ArrivesAtTick == int.MaxValue) continue;
                // Probes sent at a rival fly as Attack marches (RaidArrivals files the
                // intel) — word them as the spy run they are, not "battle underway".
                bool probeOnly = m.Ships.Count == 1 && m.Ships.ContainsKey(HullId.Probe);
                string body = m.Phase switch
                {
                    MarchPhase.Outbound => m.Mission switch
                    {
                        MarchMission.Attack when probeOnly => "Your probe has reached the target — intel incoming",
                        MarchMission.Attack => "Your fleet has reached the target — battle underway",
                        MarchMission.Spy => "Your probe is on station — recon incoming",
                        MarchMission.Intercept => "Your fleet has caught its target — intercept underway",
                        MarchMission.Garrison => "Your garrison has taken up its post",
                        MarchMission.Core => CoreSystem.PlayerGarrison(state) != null ? "Reinforcements have joined your core garrison"
                            : "Your fleet has reached the Galactic Core — assault underway",
                        MarchMission.Boss => "Your fleet has reached the Pirate Dreadnought — strike underway",
                        _ => "Your fleet has arrived and started gathering",
                    },
                    MarchPhase.Gathering => "Gathering complete — your fleet is heading home",
                    _ => "Your fleet is back home",
                };
                Schedule($"march-{m.Id}", m.ArrivesAtTick - now, body);
            }

            if (timers)
            foreach (var o in state.BuildQueue)
                if (o.EndsAtTick > 0)
                    Schedule($"build-{o.Building}-{o.MineId ?? 0}", o.EndsAtTick - now,
                        $"{Buildings.Defs[o.Building].Name} upgrade to Lv {o.ToLevel} complete");

            if (timers)
            foreach (var o in state.ResearchQueue)
                Schedule($"research-{o.TechId}", o.EndsAtTick - now,
                    $"{Techs.Defs[o.TechId].Name} research complete");

            // Hostiles ALREADY flying at the colony, announced exactly when the
            // Radar Station would warn in-game (lead = RadarLeadPerLevelSec × level,
            // detail per tier) — so the dodge / Aegis panic button works with the
            // app closed. No radar, no warning, same as live. (Raids bots decide
            // during the offline catch-up can't be forecast, so they aren't.)
            var galaxy = _ctx.Bots;
            int radarLevel = RadarSystem.Level(state);
            int lead = RadarSystem.WarnLeadSeconds(state);
            if (raids && galaxy != null && lead > 0)
            {
                int tier = RadarSystem.DetailTier(radarLevel);
                foreach (var atk in galaxy.Inbound)
                {
                    string what = tier >= 2 ? (atk.IsFleet ? "Hostile fleet" : "Spy probe") : "Unknown contact";
                    Schedule($"inbound-{atk.Id}", atk.ArrivesAtTick - lead - now,
                        $"Radar: {what} inbound — impact in {UI.UiTheme.FmtDuration(lead)}");
                }
            }

            // Assaults already flying at the Galactic Core you hold.
            if (raids && galaxy != null && galaxy.Core.HolderId == 0)
                foreach (var m in galaxy.Marches)
                    if (m.Kind == GalaxyRoyale.Sim.Bots.BotMarchKind.CoreAssault && !m.Resolved && m.LinkId == m.Id)
                        Schedule($"core-assault-{m.Id}", m.ArrivesAtTick - now,
                            $"{galaxy.Find(m.BotId)?.Name ?? "A commander"}'s assault has reached the Galactic Core — check your garrison");

            // Aegis Shield about to lapse.
            long shieldLeft = state.Buffs.ShieldUntilTick - now;
            if (raids && shieldLeft > 600)
                Schedule("shield-lapse", shieldLeft - 600, "Your Aegis Shield drops in 10 minutes");
            else if (raids && shieldLeft > 0)
                Schedule("shield-lapse", shieldLeft, "Your Aegis Shield is down");

            if (galaxyNews)
            {
                // The next galaxy event (they run on a fixed calendar).
                var next = EventSystem.Next(now);
                if (EventSystem.IsQuiet(next)) next = EventSystem.Next(next.StartTick); // skip a quiet stretch
                if (!EventSystem.IsQuiet(next) && next.StartTick > now)
                    Schedule("galaxy-event", next.StartTick - now, $"{next.Def.Name} has begun: {next.Def.Effect}");
                // The season's last hour.
                int seasonEnd = SeasonSystem.EndTick(SeasonSystem.SeasonAt(now));
                if (seasonEnd - now > 3600)
                    Schedule("season-end", seasonEnd - 3600 - now, "The season ends in an hour — one last push up the rankings");
                // The Pirate Dreadnought: its arrival, or its last hour if you've struck it.
                if (galaxy != null)
                {
                    var boss = galaxy.Boss;
                    if (!boss.Active && boss.NextVisitTick > now)
                        Schedule("boss-arrives", boss.NextVisitTick - now,
                            "A Pirate Dreadnought dropped out of hyperspace — strike it for a share of the Dark Matter");
                    else if (boss.Active && boss.Damage.ContainsKey(0) && boss.LeavesTick - now > 3600)
                        Schedule("boss-leaves", boss.LeavesTick - 3600 - now,
                            "The Pirate Dreadnought jumps away in an hour — one more strike?");
                }
            }

            // Your clan's daily supply run.
            if (clan && state.ClanId != 0 && state.ClanSupplyNextTick > now)
                Schedule("clan-supply", state.ClanSupplyNextTick - now, "Your clan's supply run has arrived — collect it in MORE › CLAN");
        }

        static void ClearAll()
        {
            iOSNotificationCenter.RemoveAllScheduledNotifications();
            iOSNotificationCenter.RemoveAllDeliveredNotifications();
        }
#endif
    }
}
