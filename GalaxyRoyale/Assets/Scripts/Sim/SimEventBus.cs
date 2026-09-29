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
    /// <summary>Clan supply runs arrived; they wait in the CLAN panel.</summary>
    public sealed record ClanSuppliesArrived(int Runs) : SimEvent;
    /// <summary>A clan near you invites you to join.</summary>
    public sealed record ClanInviteReceived(int ClanId) : SimEvent;
    /// <summary>A war involving the player's clan began (<paramref name="PlayerClanAttacked"/>: the
    /// other clan declared it).</summary>
    public sealed record ClanWarDeclared(int ClanId, int EnemyClanId, bool PlayerClanAttacked) : SimEvent;
    /// <summary>A war involving the player's clan ended (scores are the final tallies).</summary>
    public sealed record ClanWarEnded(int ClanId, int EnemyClanId, bool Won, bool Draw, int Score, int EnemyScore) : SimEvent;
    /// <summary>Your garrison at a clanmate's colony fought a raid there.</summary>
    public sealed record GarrisonFought(string HostName, string AttackerName, bool Held) : SimEvent;
    /// <summary>Your intercept found nothing: the fleet it hunted changed course or was gone.</summary>
    public sealed record InterceptMissed(string TargetName) : SimEvent;
    /// <summary>Clanmates' garrison wings reached your colony.</summary>
    public sealed record ClanGarrisonArrived(int Wings) : SimEvent;
    /// <summary>The Galactic Core changed hands (HolderId: -1 guardians, 0 you, &gt;0 a bot).</summary>
    public sealed record CoreSeized(int HolderId, int PreviousHolderId) : SimEvent;
    /// <summary>An hour of Core tribute landed (Clan = paid because a clanmate holds it). Milli.</summary>
    public sealed record CoreTributePaid(ResourceBag Resources, int DarkMatter, bool Clan) : SimEvent;
    /// <summary>A commander launched an assault on the core you hold (arriving at ArrivesAtTick).</summary>
    public sealed record CoreUnderAttack(int AttackerId, int ArrivesAtTick) : SimEvent;
    /// <summary>A Pirate Dreadnought dropped in at Tile; it leaves at LeavesTick.</summary>
    public sealed record BossAppeared(TileXY Tile, int LeavesTick) : SimEvent;
    /// <summary>One of your strikes on the dreadnought landed (its report is in the mailbox).</summary>
    public sealed record BossStrikeLanded(BossReport Report) : SimEvent;
    /// <summary>The dreadnought's visit ended: destroyed or escaped; your reward (already paid).</summary>
    public sealed record BossDeparted(bool Killed, long YourDamage, int RewardDM) : SimEvent;
    /// <summary>The commander reached <paramref name="Level"/>, <paramref name="Gained"/> levels
    /// at once (their Dark Matter and milestone items, by shop id, are already paid).</summary>
    public sealed record CommanderLevelUp(int Level, int Gained, int DarkMatter, IReadOnlyList<string> Items) : SimEvent;
    /// <summary>A survey of the Wilds landed: what it found in the sector.</summary>
    public sealed record WildsSurveyed(int Sector, WildsFind Find) : SimEvent;

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
