// Cold-start timing (2026-09-29): what each boot step costs, logged once when
// the first frame is on screen —
//   [Boot] init 0.01 s · ui 0.32 s · save 0.03 s · catch-up 0.01 s · planet 0.00 s · globe 0.03 s · … → first frame 0.53 s
// "first frame" counts from Time.realtimeSinceStartup's zero, which on iOS is
// after the engine has started and loaded Boot.unity.
using System.Collections.Generic;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using Time = UnityEngine.Time;

namespace GalaxyRoyale.Game
{
    public static class BootTrace
    {
        static readonly List<string> s_parts = new();
        static bool s_done;

        /// <summary>Time one boot step: <c>using (BootTrace.Step("save")) { … }</c>.</summary>
        public static Scope Step(string name) => new(name);

        public readonly struct Scope : System.IDisposable
        {
            readonly string _name;
            readonly long _start;

            public Scope(string name)
            {
                _name = name;
                _start = s_done ? 0 : Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                if (s_done) return;
                Add(_name, (Stopwatch.GetTimestamp() - _start) / (double)Stopwatch.Frequency);
            }
        }

        /// <summary>The first frame has rendered: log the line and stop timing.</summary>
        public static void FirstFrame()
        {
            if (s_done) return;
            s_done = true;
            Debug.Log($"[Boot] {string.Join(" · ", s_parts)} → first frame {Time.realtimeSinceStartup:F2} s");
        }

        static void Add(string name, double seconds) => s_parts.Add($"{name} {seconds:F2} s");
    }
}
