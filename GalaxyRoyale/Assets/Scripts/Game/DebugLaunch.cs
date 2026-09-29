// Test hooks: open a screen straight after boot, chosen by the GR_OPEN
// environment variable (and set display/audio settings with GR_SETTINGS). The Simulator passes it with
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
            "awards, daily, queues, shop, research, profile, settings, fleet, map, newgame, liveactivity, hail, " +
            "command, mines, frontier, orbit, mapcore";

        public static void Run(GameContext ctx)
        {
            ApplySettings(Environment.GetEnvironmentVariable("GR_SETTINGS"));
            // GR_AI=<proxy URL> points the AI writers at a proxy (a local test server, say).
            if (Environment.GetEnvironmentVariable("GR_AI") is { } ai) AiWriter.ProxyUrl = ai;
            string? open = Environment.GetEnvironmentVariable("GR_OPEN");
            if (string.IsNullOrWhiteSpace(open)) return;
            // After the boot-time panels (the "while you were away" debrief) have had their turn.
            ctx.StartCoroutine(OpenSoon(ctx, open!));
        }

        /// <summary>GR_SETTINGS="colorBlind=1;reducedMotion=1;textSize=2;music=0" — set before a
        /// screenshot or a check, without tapping through Settings.</summary>
        static void ApplySettings(string? spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return;
            foreach (var pair in spec!.Split(';', ','))
            {
                var kv = pair.Split('=');
                if (kv.Length != 2 || !int.TryParse(kv[1].Trim(), out int v)) continue;
                switch (kv[0].Trim())
                {
                    case "colorBlind": Settings.ColorBlind = v != 0; break;
                    case "reducedMotion": Settings.ReducedMotion = v != 0; break;
                    case "textSize": Settings.TextSize = (TextSize)Mathf.Clamp(v, 0, 2); break;
                    case "music": Settings.MusicOn = v != 0; Music.Sync(); break;
                    default: Debug.LogWarning($"[DebugLaunch] GR_SETTINGS: unknown setting '{kv[0]}'"); break;
                }
            }
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
                    case "liveactivity": HomeWidget.Publish(ctx); HomeWidget.ShowSampleRaid(); break;
                    case "hail":
                        if (ctx.Bots?.Bots.Count > 0) UI.HailPanel.Open(ctx, ctx.Bots.Bots[0].Id);
                        break;
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
                    // The galaxy map framed on the Galactic Core and the rivals around it.
                    case "mapcore":
                        ui.CloseModal();
                        ui.SwitchView(UI.ViewId.Map);
                        ctx.GetComponent<MapView>()?.Frame(Sim.Systems.CoreSystem.CoreTile, 520f);
                        break;
                    // The globe base's views (any boot-time panel closed first).
                    case "command": ui.CloseModal(); BaseGlobe.Instance?.Snap(Sim.BaseDistrict.Command); break;
                    case "mines": ui.CloseModal(); BaseGlobe.Instance?.Snap(Sim.BaseDistrict.MiningBelt); break;
                    case "frontier": ui.CloseModal(); BaseGlobe.Instance?.Snap(Sim.BaseDistrict.Frontier); break;
                    case "orbit": ui.CloseModal(); BaseGlobe.Instance?.Snap(Sim.BaseDistrict.Command, orbit: true); break;
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
