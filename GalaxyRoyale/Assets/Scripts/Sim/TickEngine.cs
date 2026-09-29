// Drives the simulation. 1 tick = 1 second of game time; state.Tick is the sim's
// only clock. Wall time exists solely at the AdvanceToWallClock boundary.
// Port of v1's `src/sim/TickEngine.ts`.
using System;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim
{
    public sealed class TickEngine
    {
        readonly GameState _state;
        readonly SimEventBus _events;

        long? _lastWallMs;
        long _accMs;

        public TickEngine(GameState state, SimEventBus events)
        {
            _state = state;
            _events = events;
        }

        public SimEventBus Events => _events;

        /// <summary>Advance n whole ticks. System order inside Step is fixed and load-bearing.</summary>
        public void Advance(int n)
        {
            for (int i = 0; i < n; i++) Step();
        }

        void Step()
        {
            _state.Tick++;

            // Fixed system order — Buildings → Research → Fleet queue → Resources → Marches → Map.
            // Order is load-bearing; changes require an explicit design note.
            EventSystem.Tick(_state); // capture a new galaxy event's goal baseline at its real start
            BuildingSystem.Tick(_state, _events);
            ResearchSystem.Tick(_state, _events);
            FleetSystem.Tick(_state, _events);
            ResourceSystem.Tick(_state, _events);
            MarchSystem.Tick(_state, _events);
            MapSystem.Tick(_state, _events);
            // The Wilds (2026-09-29) go last: surveys and the drones' haul read the
            // resources and buildings as this tick left them, and nothing above
            // depends on them.
            WildsSystem.Tick(_state, _events);
        }

        /// <summary>
        /// Map wall clock → whole ticks (sub-second remainder carries in _accMs).
        /// Backwards clock jumps advance 0 ticks and re-anchor — no punishment, no exploit.
        /// Returns the number of ticks advanced on this call.
        /// </summary>
        public int AdvanceToWallClock(long nowMs)
        {
            if (_lastWallMs is null)
            {
                _lastWallMs = nowMs;
                return 0;
            }
            long elapsed = Math.Max(0, nowMs - _lastWallMs.Value);
            _lastWallMs = nowMs;
            _accMs += elapsed;
            long tickMs = Balance.TickSeconds * 1000L;
            int ticks = (int)(_accMs / tickMs);
            _accMs -= ticks * tickMs;
            if (ticks > 0) Advance(ticks);
            return ticks;
        }

        /// <summary>0..1 progress through the current tick — for render interpolation only.</summary>
        public double TickFraction() => _accMs / (double)(Balance.TickSeconds * 1000);
    }
}
