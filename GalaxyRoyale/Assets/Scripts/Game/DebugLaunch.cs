// Test hook: open a screen straight after boot, chosen by the GR_OPEN
// environment variable. The Simulator passes it with
//   SIMCTL_CHILD_GR_OPEN=core xcrun simctl launch <device> <bundle>
// so screens can be checked (and App Store screenshots taken) without driving
// the Simulator's laggy taps. A player can't set environment variables, so on
// a phone this does nothing.
using System;
using UnityEngine;

namespace GalaxyRoyale.Game
{
    public static class DebugLaunch
    {
        /// <summary>Names GR_OPEN accepts.</summary>
        public const string Screens = "core, boss, market, commander, clan, rankings, season, mail, news, events, " +
            "awards, daily, queues, shop, research, profile, settings, fleet, map, newgame";

        public static void Run(GameContext ctx)
        {
            string? open = Environment.GetEnvironmentVariable("GR_OPEN");
            if (string.IsNullOrWhiteSpace(open)) return;
            // After the boot-time panels (the "while you were away" debrief) have had their turn.
            ctx.StartCoroutine(OpenSoon(ctx, open!));
        }

        static System.Collections.IEnumerator OpenSoon(GameContext ctx, string open)
        {
            yield return new WaitForSeconds(2f);
            var ui = UI.UIController.Instance;
            if (ui == null) yield break;
            try
            {
                switch (open!.Trim().ToLowerInvariant())
                {
                    case "core": UI.CorePanel.Open(ctx); break;
                    case "commander": ui.OpenCommander(); break;
                    case "boss": ui.OpenBoss(); break;
                    case "market": ui.OpenMarket(); break;
                    case "settings": UI.SettingsPanel.Open(ctx); break;
                    case "clan": ui.OpenClan(); break;
                    case "rankings": ui.OpenRankings(); break;
                    case "season": ui.OpenRankings(season: true); break;
                    case "mail": ui.OpenMailbox(); break;
                    case "news": ui.OpenNews(); break;
                    case "events": ui.OpenEvents(); break;
                    case "awards": ui.OpenAchievements(); break;
                    case "daily": ui.OpenDaily(); break;
                    case "queues": ui.OpenQueues(); break;
                    case "shop": ui.OpenShop(); break;
                    case "research": ui.OpenResearch(); break;
                    case "profile": ui.OpenProfile(); break;
                    case "fleet": ui.SwitchView(UI.ViewId.Fleet); break;
                    case "map": ui.SwitchView(UI.ViewId.Map); break;
                    // The NEW GALAXY setup screen (for a look at it — START there really does reset).
                    case "newgame":
                        UI.NewGamePanel.Open((test, difficulty) => LocalBootstrap.Instance?.ResetEmpire(test, difficulty),
                            ui.CloseModal);
                        break;
                    default: Debug.LogWarning($"[DebugLaunch] GR_OPEN={open}: expected one of {Screens}"); break;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e); // lands in the error log beside the save
            }
        }
    }
}
