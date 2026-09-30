// Ship modules, blueprints and fleet presets (Data/Modules, 2026-09-30). A
// fitted module changes its hull class's attack and hull multipliers
// (ResearchSystem.AtkMultFor / HpMultFor), so every battle, forecast and
// defense reads it. Refitting is free and instant: the choice is the game.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public sealed class ModuleState
    {
        /// <summary>Blueprint copies found, by module (the module's Mk is min(copies, 3)).</summary>
        public Dictionary<ModuleKind, int> Blueprints = new();
        /// <summary>Hull class name → the module fitted to it.</summary>
        public Dictionary<string, ModuleKind> Fitted = new();
        /// <summary>Saved fleet line-ups for the attack screen (three slots; empty = unset).</summary>
        public List<Dictionary<HullId, int>> Presets = new() { new(), new(), new() };
    }

    public static class ModuleSystem
    {
        public static int Mark(GameState s, ModuleKind kind) =>
            Math.Min(Modules.MaxMark, s.Modules.Blueprints.TryGetValue(kind, out var n) ? n : 0);

        public static bool Owned(GameState s, ModuleKind kind) => Mark(s, kind) > 0;

        public static ModuleKind? FittedTo(GameState s, string hullClass) =>
            s.Modules.Fitted.TryGetValue(hullClass, out var k) ? k : null;

        public static SimResult Fit(GameState s, string hullClass, ModuleKind? kind)
        {
            bool warship = false;
            foreach (var c in Modules.Classes) if (c == hullClass) warship = true;
            if (!warship) return SimResult.Fail("Modules fit warships only");
            if (kind is not { } k) { s.Modules.Fitted.Remove(hullClass); return SimResult.Success; }
            if (!Owned(s, k)) return SimResult.Fail("Find its blueprint first");
            s.Modules.Fitted[hullClass] = k;
            return SimResult.Success;
        }

        /// <summary>A blueprint found: one more copy (Mk up to III). Returns the new Mk.</summary>
        public static int Grant(GameState s, ModuleKind kind)
        {
            s.Modules.Blueprints[kind] = (s.Modules.Blueprints.TryGetValue(kind, out var n) ? n : 0) + 1;
            return Mark(s, kind);
        }

        static ModuleDef? For(GameState s, HullId hull, out float scale)
        {
            scale = 0f;
            if (s.Modules.Fitted.Count == 0) return null;
            if (!s.Modules.Fitted.TryGetValue(Ships.Defs[hull].Class, out var kind)) return null;
            int mark = Mark(s, kind);
            if (mark == 0) return null;
            scale = Modules.MarkScale(mark);
            return Modules.Def(kind);
        }

        /// <summary>The module's attack change for one hull (joins AtkMultFor).</summary>
        public static float AtkFor(GameState s, HullId hull) => For(s, hull, out var k) is { } d ? d.Atk * k : 0f;

        /// <summary>The module's hull change for one hull (joins HpMultFor).</summary>
        public static float HpFor(GameState s, HullId hull) => For(s, hull, out var k) is { } d ? d.Hp * k : 0f;

        // ---------- fleet presets ----------

        public static void SavePreset(GameState s, int slot, Dictionary<HullId, int> fleet)
        {
            if (slot < 0 || slot >= s.Modules.Presets.Count) return;
            var copy = new Dictionary<HullId, int>();
            foreach (var kv in fleet) if (kv.Value > 0) copy[kv.Key] = kv.Value;
            s.Modules.Presets[slot] = copy;
        }

        /// <summary>A preset, trimmed to what's docked now.</summary>
        public static Dictionary<HullId, int> LoadPreset(GameState s, int slot)
        {
            var fleet = new Dictionary<HullId, int>();
            if (slot < 0 || slot >= s.Modules.Presets.Count) return fleet;
            foreach (var kv in s.Modules.Presets[slot])
            {
                int docked = s.Ships.TryGetValue(kv.Key, out var n) ? n : 0;
                if (Math.Min(docked, kv.Value) > 0) fleet[kv.Key] = Math.Min(docked, kv.Value);
            }
            return fleet;
        }
    }
}
