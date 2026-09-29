// The home-screen widget and the raid Live Activity (build-all plan,
// 2026-09-28). The game hands a small JSON snapshot to the widget whenever it
// saves (and when it goes to the background). Every timer is an absolute date,
// so the widget keeps counting while the game is closed. A raid the radar has
// picked up starts a Live Activity counting down to impact on the Lock Screen
// and in the Dynamic Island; it ends when the raid lands. The native side is
// Plugins/iOS/GRWidgetBridge.swift and the extension iOSWidget/GalaxyWidget.swift.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game
{
    public static class HomeWidget
    {
        const int MaxTimers = 8;

        /// <summary>Epoch seconds for a sim tick (the sim clock runs at wall-clock speed).</summary>
        static double Epoch(GameState state, int tick) =>
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 + (tick - state.Tick);

        /// <summary>The snapshot the widget draws.</summary>
        public static string Compose(GameState state, Sim.Bots.BotGalaxy? galaxy, IReadOnlyList<RadarContact> threats)
        {
            var timers = new List<(string label, int ends)>();
            foreach (var o in state.BuildQueue)
                timers.Add(($"{Buildings.Defs[o.Building].Name} Lv {o.ToLevel}", o.EndsAtTick));
            foreach (var o in state.ResearchQueue)
                timers.Add(($"{Techs.Defs[o.TechId].Name} Lv {o.ToLevel}", o.EndsAtTick));
            foreach (var o in state.ShipQueue)
                if (o.Remaining > 0)
                    timers.Add(($"{o.Remaining}× {Ships.Defs[o.Hull].Name}",
                        o.NextDoneAtTick + (o.Remaining - 1) * FleetSystem.ShipBuildTime(state, o.Hull)));
            foreach (var m in state.Marches)
                if (m.Phase == MarchPhase.Returning) timers.Add(("Fleet home", m.ArrivesAtTick));
            timers.RemoveAll(t => t.ends <= state.Tick);
            timers.Sort((a, b) => a.ends.CompareTo(b.ends));
            if (timers.Count > MaxTimers) timers.RemoveRange(MaxTimers, timers.Count - MaxTimers);

            var sb = new StringBuilder(512);
            sb.Append('{');
            Num(sb, "at", Epoch(state, state.Tick)).Append(',');
            Str(sb, "name", state.Profile.Name).Append(',');
            Num(sb, "level", state.Commander.Level).Append(',');
            Num(sb, "might", PowerSystem.ComputePower(state)).Append(',');
            Num(sb, "gold", state.Resources.Gold / 1000).Append(',');
            Num(sb, "quartz", state.Resources.Quartz / 1000).Append(',');
            Num(sb, "helium", state.Resources.Helium / 1000).Append(',');
            sb.Append("\"timers\":[");
            for (int i = 0; i < timers.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('{');
                Str(sb, "label", timers[i].label).Append(',');
                Num(sb, "ends", Epoch(state, timers[i].ends));
                sb.Append('}');
            }
            sb.Append("],");
            var raid = NextRaid(threats);
            sb.Append("\"raid\":");
            if (raid == null) sb.Append("null");
            else
            {
                sb.Append('{');
                Str(sb, "attacker", AttackerOf(raid)).Append(',');
                Num(sb, "ends", Epoch(state, raid.ArrivesAtTick));
                sb.Append('}');
            }
            sb.Append(',');
            sb.Append("\"core\":").Append(galaxy != null && CoreSystem.PlayerHolds(galaxy) ? "true" : "false").Append(',');
            sb.Append("\"boss\":");
            if (galaxy == null || !galaxy.Boss.Active) sb.Append("null");
            else
            {
                sb.Append('{');
                Num(sb, "hull", Math.Round(BossSystem.HullShare(galaxy.Boss) * 100)).Append(',');
                Num(sb, "leaves", Epoch(state, galaxy.Boss.LeavesTick));
                sb.Append('}');
            }
            sb.Append('}');
            return sb.ToString();
        }

        static StringBuilder Num(StringBuilder sb, string key, double value) =>
            sb.Append('"').Append(key).Append("\":").Append(value.ToString("0.###", CultureInfo.InvariantCulture));

        static StringBuilder Str(StringBuilder sb, string key, string value)
        {
            sb.Append('"').Append(key).Append("\":\"");
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"');
        }

        /// <summary>The soonest war fleet the radar has picked up (probes don't count).</summary>
        static RadarContact? NextRaid(IReadOnlyList<RadarContact> threats)
        {
            RadarContact? best = null;
            foreach (var c in threats)
                if (c.IsFleet && (best == null || c.ArrivesAtTick < best.ArrivesAtTick)) best = c;
            return best;
        }

        /// <summary>The name the radar can see (tier-gated), or "Unknown contact".</summary>
        static string AttackerOf(RadarContact c) => string.IsNullOrEmpty(c.AttackerName) ? "Unknown contact" : c.AttackerName;

        // ---------- the calls into iOS ----------

        /// <summary>Hand the widget a fresh snapshot (autosave, backgrounding).</summary>
        public static void Publish(GameContext ctx)
        {
            if (ctx.State == null) return;
#if UNITY_IOS && !UNITY_EDITOR
            try { _GRWidgetWrite(Compose(ctx.State, ctx.Bots, RadarService.DetectedThreats)); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
#endif
        }

        /// <summary>The raid the Live Activity shows: -1 none, -2 unknown (a previous run of
        /// the game may have left one on the Lock Screen — the first check ends it).</summary>
        static int s_activityRaidId = -2, s_activityArrives = -1;
        static float s_sampleUntil;

        /// <summary>Keep the raid Live Activity in step with the radar (called about once a second):
        /// start or update it for the soonest detected fleet, end it once none is inbound.</summary>
        public static void SyncRaidActivity(GameContext ctx)
        {
            if (ctx.State == null) return;
            if (UnityEngine.Time.realtimeSinceStartup < s_sampleUntil) return; // the sample raid is showing
            var raid = NextRaid(RadarService.DetectedThreats);
            if (raid != null && raid.ArrivesAtTick > ctx.State.Tick)
            {
                if (raid.Id == s_activityRaidId && raid.ArrivesAtTick == s_activityArrives) return;
                s_activityRaidId = raid.Id;
                s_activityArrives = raid.ArrivesAtTick;
#if UNITY_IOS && !UNITY_EDITOR
                _GRRaidActivityUpdate(AttackerOf(raid), Epoch(ctx.State, raid.ArrivesAtTick), raid.FleetCount);
#endif
                return;
            }
            if (s_activityRaidId == -1) return;
            s_activityRaidId = s_activityArrives = -1;
#if UNITY_IOS && !UNITY_EDITOR
            _GRRaidActivityEnd();
#endif
        }

        /// <summary>Test hook (GR_OPEN=liveactivity): a sample raid five minutes out, to see the
        /// Live Activity on the Lock Screen and in the Dynamic Island.</summary>
        public static void ShowSampleRaid()
        {
            s_sampleUntil = UnityEngine.Time.realtimeSinceStartup + 300;
            s_activityRaidId = -2; // whatever the radar shows next replaces it
#if UNITY_IOS && !UNITY_EDITOR
            _GRRaidActivityUpdate("Sample raider", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 + 300, 42);
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void _GRWidgetWrite(string json);
        [DllImport("__Internal")] static extern void _GRRaidActivityUpdate(string attacker, double arrivesEpoch, int ships);
        [DllImport("__Internal")] static extern void _GRRaidActivityEnd();
#endif
    }
}
