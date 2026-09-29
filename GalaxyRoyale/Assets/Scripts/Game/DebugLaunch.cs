// Test hooks: open a screen straight after boot, chosen by the GR_OPEN
// environment variable (and set display/audio settings with GR_SETTINGS). The Simulator passes it with
//   SIMCTL_CHILD_GR_OPEN=core xcrun simctl launch <device> <bundle>
// so screens can be checked (and App Store screenshots taken) without driving
// the Simulator's laggy taps. A player can't set environment variables, so on
// a phone this does nothing.
using System;
using System.Collections.Generic;
using UnityEngine;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Combat;

namespace GalaxyRoyale.Game
{
    public static class DebugLaunch
    {
        /// <summary>Names GR_OPEN accepts.</summary>
        public const string Screens = "core, boss, market, commander, clan, rankings, season, mail, news, events, " +
            "awards, daily, queues, shop, research, profile, settings, fleet, map, newgame, liveactivity, hail, " +
            "command, mines, frontier, port, wilds, sector, find, orbit, orbitsouth, mapcore, report, replay, demoreport, demoreplay, " +
            "tour, fxdemo";

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

        /// <summary>GR_SETTINGS="colorBlind=1;reducedMotion=1;textSize=2;music=0;replaySpeed=1" — set
        /// before a screenshot or a check, without tapping through Settings.</summary>
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
                    case "replaySpeed": PlayerPrefs.SetInt(UI.BattleReplayPanel.SpeedKey, v); break;
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
                    // The newest battle report and its replay; the demo pair files
                    // nothing and shows a sample raid, for a look at the art.
                    case "report":
                        if (NewestBattle(ctx) is { } report) UI.MailboxPanel.OpenReport(ctx, report);
                        break;
                    case "replay":
                        if (NewestBattle(ctx) is { } fight && UI.BattleReplayPanel.CanReplay(fight))
                            UI.BattleReplayPanel.Open(ctx, fight);
                        break;
                    case "demoreport": UI.MailboxPanel.OpenReport(ctx, DemoBattle(ctx)); break;
                    case "demoreplay": UI.BattleReplayPanel.Open(ctx, DemoBattle(ctx)); break;
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
                    case "port": ui.CloseModal(); BaseGlobe.Instance?.Snap(Sim.BaseDistrict.Spaceport); break;
                    // The Wilds, turned to the survey under way (or the Command Center's meridian).
                    case "wilds":
                        ui.CloseModal();
                        BaseGlobe.Instance?.Snap(Sim.BaseDistrict.Wilds, lon: WildsFront(ctx));
                        break;
                    // A sector's panel: the survey under way, else the newest find.
                    case "sector":
                    {
                        var wilds = ctx.State?.Wilds;
                        int index = wilds == null ? -1 : wilds.Surveying;
                        if (index < 0 && wilds != null)
                            foreach (var s in wilds.Sectors.Values)
                                if (s.Find != WildsFind.None && s.Index > index) index = s.Index;
                        BaseGlobe.Instance?.Snap(Sim.BaseDistrict.Wilds, lon: WildsFront(ctx));
                        if (index >= 0) UI.SectorPanel.Open(ctx, index);
                        break;
                    }
                    // A find's panel: an unclaimed cache or relic first, else the newest deposit.
                    case "find":
                    {
                        var wilds = ctx.State?.Wilds;
                        int index = -1;
                        if (wilds != null)
                        {
                            foreach (var s in wilds.Sectors.Values)
                                if (s.Find is WildsFind.Cache or WildsFind.Relic && !s.Claimed) { index = s.Index; break; }
                            if (index < 0)
                                foreach (var s in wilds.Sectors.Values)
                                    if (s.Find == WildsFind.Deposit && s.Index > index) index = s.Index;
                        }
                        BaseGlobe.Instance?.Snap(Sim.BaseDistrict.Wilds, lon: WildsFront(ctx));
                        if (index >= 0) UI.SectorPanel.Open(ctx, index);
                        break;
                    }
                    case "tour": UI.GlobeTour.Start(); break;
                    // The globe's effects on a loop, for a look (and screenshots): level-ups at the
                    // Command Center, a raid, a fleet lifting off and landing at the Spaceport.
                    case "fxdemo":
                        ui.CloseModal();
                        if (GlobeFx.Instance != null) GlobeFx.Instance.StartCoroutine(GlobeFx.Instance.Demo());
                        break;
                    case "orbitsouth":
                        ui.CloseModal();
                        BaseGlobe.Instance?.Snap(Sim.BaseDistrict.Wilds, orbit: true, lon: WildsFront(ctx));
                        break;
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

        static double WildsFront(GameContext ctx)
        {
            var wilds = ctx.State?.Wilds;
            if (wilds == null) return 0;
            if (wilds.Surveying >= 0) return WildsLayout.Place(wilds.Surveying).Lon;
            int newest = -1;
            foreach (var s in wilds.Sectors.Values)
                if (s.Find != WildsFind.None && s.Index > newest) newest = s.Index;
            return newest >= 0 ? WildsLayout.Place(newest).Lon : 0;
        }

        static BattleMailReport? NewestBattle(GameContext ctx) =>
            ctx.State?.Mailbox.Find(m => m is BattleMailReport) as BattleMailReport;

        /// <summary>A sample raid (never filed): a mixed fleet against a rival's
        /// colony, its Orbital Batteries firing back.</summary>
        static BattleMailReport DemoBattle(GameContext ctx)
        {
            var rival = ctx.Bots?.Bots.Count > 0 ? ctx.Bots.Bots[0] : null;
            var attack = new Dictionary<HullId, int>
            {
                [HullId.Fighter] = 60, [HullId.Bomber] = 32, [HullId.Corsair] = 8, [HullId.Vanguard] = 12,
            };
            var defend = new Dictionary<HullId, int>
            {
                [HullId.Fighter] = 40, [HullId.Bomber] = 26, [HullId.Reaper] = 3, [HullId.Warden] = 2,
            };
            var report = CombatResolver.Resolve(attack, defend, new FleetMods(1.3f, 1.2f), new FleetMods(1f, batteryLevel: 3));
            report.DefenderName = rival?.Name ?? "Hubble Trouble";
            if (report.Winner == BattleWinner.Attacker) report.Loot = new ResourceBag(9_800_000, 6_120_000, 2_500_000);
            return new BattleMailReport
            {
                Id = 424_242,
                AtTick = (ctx.State?.Tick ?? 240) - 240,
                Target = rival?.HomeTile ?? default,
                Subject = $"Raid on {report.DefenderName}",
                Report = report,
                Salvaged = new ResourceBag(1_240_000, 520_000, 90_000),
            };
        }
    }
}
