// Sim → render event channel. One direction only. Port of v1's `src/sim/events.ts`.
// Events are collected (not dispatched) while Suppressed is true — used during offline
// catch-up so we don't spam the UI with thousands of retroactive notifications.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Combat;

namespace GalaxyRoyale.Sim
{
    public abstract record SimEvent;

    public sealed record BuildingCompleted(BuildingId Building, int Level) : SimEvent;
    public sealed record ShipsCompleted(HullId Hull, int Count) : SimEvent;
    public sealed record MarchPhaseChanged(int MarchId, MarchPhase Phase) : SimEvent;
    public sealed record BattleResolved(BattleReport Report) : SimEvent;
    public sealed record MarchReturned(int MarchId, ResourceBag Cargo, long CargoDm = 0) : SimEvent;
    public sealed record StorageFull(ResourceId Resource) : SimEvent;
    public sealed record SpyReportReceived(SpyReport Report) : SimEvent;
    public sealed record ItemPurchased(string ItemId) : SimEvent;
    public sealed record NodeDepleted(string NodeId) : SimEvent;
    public sealed record NodeRespawned(string NodeId) : SimEvent;
    public sealed record ResearchCompleted(TechId Tech, int Level) : SimEvent;
    /// <summary>A simulated commander's raid landed on the player's colony (report is attacker-perspective).</summary>
    public sealed record ColonyRaided(BattleReport Report, string AttackerName) : SimEvent;
    /// <summary>An achievement unlocked (its Dark Matter is already paid).</summary>
    public sealed record AchievementUnlocked(AchievementDef Achievement) : SimEvent;
    /// <summary>A season ended — the record holds your finish and its (already paid) reward.</summary>
    public sealed record SeasonEnded(SeasonRecord Record) : SimEvent;
    /// <summary>Allied supply runs arrived; they wait in the ALLIES panel.</summary>
    public sealed record AllySuppliesArrived(int Runs) : SimEvent;

    public sealed class SimEventBus
    {
        readonly List<Action<SimEvent>> _listeners = new();
        readonly List<SimEvent> _held = new();

        /// <summary>While true, events are collected instead of dispatched.</summary>
        public bool Suppressed { get; set; }

        /// <summary>Subscribe; returned Action unsubscribes.</summary>
        public Action Subscribe(Action<SimEvent> fn)
        {
            _listeners.Add(fn);
            return () => _listeners.Remove(fn);
        }

        public void Emit(SimEvent e)
        {
            if (Suppressed)
            {
                _held.Add(e);
                return;
            }
            // Snapshot to tolerate a listener unsubscribing during dispatch.
            var snapshot = _listeners.ToArray();
            foreach (var l in snapshot) l(e);
        }

        public IReadOnlyList<SimEvent> DrainSuppressed()
        {
            var drained = _held.ToArray();
            _held.Clear();
            return drained;
        }
    }
}
