# Assets/Scripts — Galaxy Royale

Single-player 4X: the player versus 99 simulated commanders. Originally a C#
port of the iGalaxy v1 TypeScript sim (archived at `../../Novacore/iGalaxy v1/`),
then pivoted offline on 2026-07-07 — the online layer lives in
`../../Novacore/removed-online-code/`.

## Layout

```
Scripts/
├── Data/         — pure data (constants, definitions, enums). No Unity refs.
│   ├── Balance.cs         (BALANCE — every tunable)
│   ├── Buildings.cs       (BuildingId + defs — GoldMine/QuartzExtractor/HeliumRefinery/…)
│   ├── Ships.cs           (HullId + the 23-hull roster)
│   ├── Nodes.cs           (NodeKind + tier weights + camp templates)
│   ├── Techs.cs           (TechId + the 26-tech tree)
│   └── Shop.cs            (shop items + prices — PLAYER-ONLY; bots never shop)
├── Sim/          — the deterministic simulation (no UnityEngine, enforced by asmdef)
│   ├── GameState.cs / TickEngine.cs / SimEventBus.cs / SaveManager.cs
│   ├── Spawn.cs            (rim spawn placement — player AND bots)
│   ├── Systems/            (Building/Fleet/March/Research/Resource/Map/Power/Shop/Radar)
│   ├── Map/                (MapGenerator — deterministic 2500×2500)
│   ├── Combat/             (CombatResolver — pure, shields + counters)
│   ├── Bots/               (THE SIMULATED GALAXY — 99 AI commanders, free-for-all raiding)
│   └── Save/               (v17 JSON codec: player state + bot galaxy envelope)
├── Local/        — on-device persistence (atomic save + rolling .bak backup)
├── Game/         — Unity bridge: GameContext, LocalBootstrap, cameras, map/base views,
│   │               RadarService / RaidService / RaidArrivals (all vs bots)
│   └── UI/       — UI Toolkit controllers (pure C#, no UXML/USS)
└── Tests/        — EditMode NUnit (sim invariants, codec, bots, raids, spawn)
```

## Purity rule

**`Sim/` and `Data/` never reference `UnityEngine.*`** (asmdef-enforced with
`noEngineReferences`). This is why the whole bot galaxy is unit-testable and
why offline catch-up is deterministic.

## Bot rules (design invariants — see BotGalaxyTests)

1. Bots never touch the shop, speed-ups, Dark Matter, or buffs.
2. Bots run ONE build order + ONE research order (no double queues).
3. Bots progress through the same Balance formulas as the player — no cheats,
   just personalities (aggression / economy focus / activity hours).

## Naming conventions

- **C# `PascalCase`** for public types and methods.
- **Enums** replace v1's string-union types (`BuildingId`, `HullId`, `NodeKind`,
  `TechId`, `ShopCategory`, `ShopEffect`).
- **Namespaces:** `GalaxyRoyale.Data`, `GalaxyRoyale.Sim`,
  `GalaxyRoyale.Sim.Bots`, `GalaxyRoyale.Local`, `GalaxyRoyale.Game`, etc.
- **Resources:** Gold / Quartz / Helium — renamed end-to-end at the pivot
  (code, save wire ids, assets). The archive still uses metal/crystal/gas.
