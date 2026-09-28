// Save envelope + offline catch-up. Direct port of v1's `src/sim/SaveManager.ts`
// with one key scope difference: JSON serialization + storage backend live OUTSIDE
// the Sim assembly (they're a Phase B.5 concern, plumbed via Game/ + Net/). This
// file owns the *pure* parts:
//
//   1. SaveFile — the envelope { Version, SavedAtMs, State }.
//   2. Version validation (C# port baselines at v12; older Phaser-era saves never
//      travel from v1 to Unity, so v1→v11 migrations are omitted deliberately).
//   3. ApplyOfflineProgress — fast-forward through the same TickEngine.Advance
//      loop as live play, with events suppressed so the mailbox doesn't get
//      spammed with hours of retroactive notifications.
using System;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim
{
    /// <summary>Envelope that wraps a GameState with version + timestamp metadata.
    /// v17+ also carries the simulated galaxy (the 99 bot commanders).</summary>
    public sealed class SaveFile
    {
        public int Version;
        public long SavedAtMs;
        public GameState State = new();
        public Bots.BotGalaxy? Bots;
    }

    public sealed class OfflineSummary
    {
        public int ElapsedSec;
        public ResourceBag Gained = new();
        /// <summary>Events collected during the fast-forward (bus was Suppressed).</summary>
        public System.Collections.Generic.IReadOnlyList<SimEvent> Events =
            System.Array.Empty<SimEvent>();
    }

    public static class SaveManager
    {
        // v17 (2026-07-07): the Galaxy Royale single-player pivot — CLEAN BREAK.
        // Wire ids renamed (gold/quartz/helium, goldMine/quartzExtractor/
        // heliumRefinery, heliumcloud) and the envelope gained the simulated
        // galaxy ("bots": 99 AI commanders). Online-era saves (v12–v16) are not
        // readable — the server was already nuked and no live saves existed at
        // the pivot, so no migration chain is carried.
        public const int CurrentVersion = 17;
        public const int OldestReadableVersion = 17;

        /// <summary>Wrap a live state (+ the simulated galaxy) into a versioned envelope.</summary>
        public static SaveFile Wrap(GameState state, long nowMs, Bots.BotGalaxy? bots = null) => new()
        {
            Version = CurrentVersion,
            SavedAtMs = nowMs,
            State = state,
            Bots = bots,
        };

        /// <summary>
        /// Validate a loaded envelope. Returns null if the version doesn't match the
        /// current baseline (v17 is a clean break — see the version notes above).
        /// </summary>
        public static (GameState state, long savedAtMs, Bots.BotGalaxy? bots)? Unwrap(SaveFile? file)
        {
            if (file == null) return null;
            if (file.Version < OldestReadableVersion || file.Version > CurrentVersion) return null;
            if (!LooksLikeState(file.State)) return null;
            return (file.State, file.SavedAtMs, file.Bots);
        }

        /// <summary>
        /// Fast-forward elapsed real time through the SAME advance() loop as live play (no
        /// second economy code path). Events are suppressed and drained for the summary so
        /// callers can present "you gained X while away" without spamming per-event UI.
        /// Backwards clocks clamp to zero; total offline time caps at Balance.OfflineCapHours.
        /// </summary>
        public static OfflineSummary ApplyOfflineProgress(
            GameState state,
            TickEngine engine,
            SimEventBus events,
            long savedAtMs,
            long nowMs)
        {
            int capSec = Balance.OfflineCapHours * 3600;
            long deltaMs = Math.Max(0, nowMs - savedAtMs);
            int elapsedSec = (int)Math.Min(capSec, deltaMs / 1000);

            var before = state.Resources.Clone();
            events.Suppressed = true;
            // finally: a throw mid catch-up must not leave the bus muted forever.
            try { engine.Advance(elapsedSec); }
            finally { events.Suppressed = false; }

            var gained = new ResourceBag();
            foreach (var res in Resources.All)
                gained.Set(res, state.Resources.Get(res) - before.Get(res));

            return new OfflineSummary
            {
                ElapsedSec = elapsedSec,
                Gained = gained,
                Events = events.DrainSuppressed(),
            };
        }

        static bool LooksLikeState(GameState? s) =>
            s != null && s.Resources != null && s.Buildings != null && s.Map != null;
    }
}
