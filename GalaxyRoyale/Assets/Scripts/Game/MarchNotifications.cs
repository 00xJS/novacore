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
                    Title = "GalaxyRoyale",
                    Body = body,
                    ShowInForeground = false,
                    Trigger = new iOSNotificationTimeIntervalTrigger
                    {
                        TimeInterval = TimeSpan.FromSeconds(inSeconds),
                        Repeats = false,
                    },
                });
            }

            foreach (var m in state.Marches)
            {
                string body = m.Phase switch
                {
                    MarchPhase.Outbound => m.Mission switch
                    {
                        MarchMission.Attack => "Your fleet has reached the target — battle underway",
                        MarchMission.Spy => "Your probe is on station — recon incoming",
                        _ => "Your fleet has arrived and started gathering",
                    },
                    MarchPhase.Gathering => "Gathering complete — your fleet is heading home",
                    _ => "Your fleet is back home",
                };
                Schedule($"march-{m.Id}", m.ArrivesAtTick - now, body);
            }

            foreach (var o in state.BuildQueue)
                if (o.EndsAtTick > 0)
                    Schedule($"build-{o.Building}-{o.MineId ?? 0}", o.EndsAtTick - now,
                        $"{Buildings.Defs[o.Building].Name} upgrade to Lv {o.ToLevel} complete");

            foreach (var o in state.ResearchQueue)
                Schedule($"research-{o.TechId}", o.EndsAtTick - now,
                    $"{Techs.Defs[o.TechId].Name} research complete");
        }

        static void ClearAll()
        {
            iOSNotificationCenter.RemoveAllScheduledNotifications();
            iOSNotificationCenter.RemoveAllDeliveredNotifications();
        }
#endif
    }
}
