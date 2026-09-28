// GameState ↔ v12 JSON. The shape matches v1's TypeScript save EXACTLY —
// same camelCase keys, same enum strings, same optional-field conventions —
// because cloud saves in Supabase are shared across clients and a divergent
// key would silently corrupt an empire. Source of truth for the shape:
// `iGalaxy v1/src/sim/GameState.ts` + `SaveManager.ts` (envelope).
//
// Enum name maps are explicit switches (not reflection) so IL2CPP stripping
// can't break them and a rename on either side is a compile error here.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Map;

namespace GalaxyRoyale.Sim.Save
{
    public static class SaveCodec
    {
        // ---------- public API ----------

        public static string Encode(SaveFile file) => Json.Write(EncodeTree(file));

        /// <summary>
        /// The save as a plain JSON tree: fresh dictionaries, lists, strings and
        /// numbers only — nothing references live state — so the tree can be
        /// turned into text on a worker thread while the sim keeps ticking.
        /// </summary>
        public static Dictionary<string, object?> EncodeTree(SaveFile file)
        {
            var root = new Dictionary<string, object?>
            {
                ["version"] = (long)file.Version,
                ["savedAtMs"] = file.SavedAtMs,
                ["state"] = EncodeState(file.State),
            };
            if (file.Bots != null) root["bots"] = EncodeBots(file.Bots);
            return root;
        }

        /// <summary>Throws FormatException on malformed input. Version gate stays in SaveManager.Unwrap.</summary>
        public static SaveFile Decode(string json)
        {
            var root = AsObj(Json.Parse(json), "root");
            return new SaveFile
            {
                Version = I32(root, "version"),
                SavedAtMs = I64(root, "savedAtMs"),
                State = DecodeState(AsObj(root["state"], "state")),
                Bots = root.TryGetValue("bots", out var b) && b is Dictionary<string, object?> bd
                    ? DecodeBots(bd) : null,
            };
        }

        // ---------- the simulated galaxy (v17: 99 bot commanders) ----------

        public static Dictionary<string, object?> EncodeBots(Bots.BotGalaxy g) => new()
        {
            ["nextAttackId"] = (long)g.NextAttackId,
            ["nextInboundWindowTick"] = (long)g.NextInboundWindowTick,
            ["news"] = Arr(g.News, n => (object?)new Dictionary<string, object?>
            {
                ["tick"] = (long)n.Tick,
                ["atk"] = (long)n.AttackerId,
                ["def"] = (long)n.DefenderId,
                ["won"] = n.AttackerWon,
                ["loot"] = n.LootMilli,
            }),
            ["inbound"] = Arr(g.Inbound, a => (object?)new Dictionary<string, object?>
            {
                ["id"] = (long)a.Id,
                ["botId"] = (long)a.BotId,
                ["isFleet"] = a.IsFleet,
                ["ships"] = Comp(a.Ships),
                ["launchTick"] = (long)a.LaunchTick,
                ["arrivesAtTick"] = (long)a.ArrivesAtTick,
                ["from"] = Tile(a.From),
            }),
            ["marches"] = Arr(g.Marches, m => (object?)new Dictionary<string, object?>
            {
                ["id"] = (long)m.Id,
                ["botId"] = (long)m.BotId,
                ["targetBotId"] = (long)m.TargetBotId,
                ["isSpy"] = m.IsSpy,
                ["ships"] = Comp(m.Ships),
                ["from"] = Tile(m.From),
                ["to"] = Tile(m.To),
                ["launchTick"] = (long)m.LaunchTick,
                ["arrivesAtTick"] = (long)m.ArrivesAtTick,
                ["returnsAtTick"] = (long)m.ReturnsAtTick,
                ["resolved"] = m.Resolved,
                ["loot"] = Bag(m.LootMilli),
            }),
            ["nextMarchId"] = (long)g.NextMarchId,
            ["empires"] = Arr(g.Bots, b => (object?)new Dictionary<string, object?>
            {
                ["id"] = (long)b.Id,
                ["lastThinkTick"] = (long)b.LastThinkTick,
                ["nextAttackRollTick"] = (long)b.NextAttackRollTick,
                ["might"] = b.CachedMight,
                ["lastPortTick"] = (long)b.LastPortTick,
                ["lastHuntJumpTick"] = (long)b.LastHuntJumpTick,
                ["defLossWindowStart"] = (long)b.DefenseLossWindowStart,
                ["defLossCount"] = (long)b.DefenseLossCount,
                ["focusTargetId"] = (long)b.FocusTargetId,
                ["focusSetTick"] = (long)b.FocusSetTick,
                ["spyBackAtTick"] = (long)b.SpyBackAtTick,
                ["seasonStartMight"] = b.SeasonStartMight,
                ["state"] = EncodeState(b.State),
            }),
        };

        public static Bots.BotGalaxy DecodeBots(Dictionary<string, object?> d)
        {
            var g = new Bots.BotGalaxy
            {
                NextAttackId = I32(d, "nextAttackId"),
                NextInboundWindowTick = I32(d, "nextInboundWindowTick"),
            };
            // Optional (added mid-v17) — earlier v17 saves simply lack it.
            if (d.TryGetValue("news", out var rawNews) && rawNews is List<object?> newsRows)
            {
                foreach (var raw in newsRows)
                {
                    if (raw is not Dictionary<string, object?> o) continue;
                    g.News.Add(new Bots.NewsItem
                    {
                        Tick = I32(o, "tick"),
                        AttackerId = I32(o, "atk"),
                        DefenderId = I32(o, "def"),
                        AttackerWon = o.TryGetValue("won", out var w) && w is bool wb && wb,
                        LootMilli = I64(o, "loot"),
                    });
                }
            }
            foreach (var raw in AsArr(d["inbound"], "bots.inbound"))
            {
                var o = AsObj(raw, "inbound[]");
                g.Inbound.Add(new Bots.BotAttack
                {
                    Id = I32(o, "id"),
                    BotId = I32(o, "botId"),
                    IsFleet = o.TryGetValue("isFleet", out var f) && f is bool fb && fb,
                    Ships = DecComp(AsObj(o["ships"], "inbound.ships")),
                    LaunchTick = I32(o, "launchTick"),
                    ArrivesAtTick = I32(o, "arrivesAtTick"),
                    From = DecTile(AsObj(o["from"], "inbound.from")),
                });
            }
            // Optional (added mid-v17) — earlier v17 saves simply lack them.
            if (d.TryGetValue("marches", out var rawMarches) && rawMarches is List<object?> marchRows)
            {
                foreach (var raw in marchRows)
                {
                    if (raw is not Dictionary<string, object?> o) continue;
                    g.Marches.Add(new Bots.BotMarch
                    {
                        Id = I32(o, "id"),
                        BotId = I32(o, "botId"),
                        TargetBotId = I32(o, "targetBotId"),
                        IsSpy = o.TryGetValue("isSpy", out var sp) && sp is bool spb && spb,
                        Ships = DecComp(AsObj(o["ships"], "botmarch.ships")),
                        From = DecTile(AsObj(o["from"], "botmarch.from")),
                        To = DecTile(AsObj(o["to"], "botmarch.to")),
                        LaunchTick = I32(o, "launchTick"),
                        ArrivesAtTick = I32(o, "arrivesAtTick"),
                        ReturnsAtTick = I32(o, "returnsAtTick"),
                        Resolved = o.TryGetValue("resolved", out var rv) && rv is bool rb && rb,
                        LootMilli = o.TryGetValue("loot", out var lt) && lt is Dictionary<string, object?> lb
                            ? DecBag(lb) : new ResourceBag(),
                    });
                }
            }
            if (d.TryGetValue("nextMarchId", out var nmi) && nmi != null)
                g.NextMarchId = ToI32(nmi);

            foreach (var raw in AsArr(d["empires"], "bots.empires"))
            {
                var o = AsObj(raw, "empires[]");
                g.Bots.Add(new Bots.BotEmpire
                {
                    Id = I32(o, "id"),
                    LastThinkTick = I32(o, "lastThinkTick"),
                    NextAttackRollTick = I32(o, "nextAttackRollTick"),
                    CachedMight = I64(o, "might"),
                    LastPortTick = o.TryGetValue("lastPortTick", out var lp) && lp != null ? ToI32(lp) : 0,
                    LastHuntJumpTick = o.TryGetValue("lastHuntJumpTick", out var lh) && lh != null ? ToI32(lh) : 0,
                    DefenseLossWindowStart = o.TryGetValue("defLossWindowStart", out var dw) && dw != null ? ToI32(dw) : 0,
                    DefenseLossCount = o.TryGetValue("defLossCount", out var dc) && dc != null ? ToI32(dc) : 0,
                    // -1 (no focus) is the default — 0 means "focused on the player".
                    FocusTargetId = o.TryGetValue("focusTargetId", out var ft) && ft != null ? ToI32(ft) : -1,
                    FocusSetTick = o.TryGetValue("focusSetTick", out var fs) && fs != null ? ToI32(fs) : 0,
                    SpyBackAtTick = o.TryGetValue("spyBackAtTick", out var sb) && sb != null ? ToI32(sb) : 0,
                    SeasonStartMight = o.TryGetValue("seasonStartMight", out var ssm) && ssm != null ? ToI64(ssm) : 0,
                    State = DecodeState(AsObj(o["state"], "empire.state")),
                });
            }
            return g;
        }

        // ---------- state ----------
        // Public: Supabase's `saves` table stores the BARE state in a jsonb column
        // with save_version alongside (v1 RemoteSave.ts shape) — no envelope.

        public static Dictionary<string, object?> EncodeState(GameState s)
        {
            var buildings = new Dictionary<string, object?>();
            foreach (var id in Buildings.All)
                buildings[Name(id)] = new Dictionary<string, object?>
                {
                    ["level"] = (long)(s.Buildings.TryGetValue(id, out var slot) ? slot.Level : 0),
                };

            var layoutSrc = s.BuildingLayout.Count > 0 ? s.BuildingLayout : GameState.DefaultLayout();
            var layout = new Dictionary<string, object?>();
            foreach (var id in Buildings.All)
                if (layoutSrc.TryGetValue(id, out var t)) layout[Name(id)] = Tile(t);

            var ships = new Dictionary<string, object?>();
            foreach (var h in Ships.All)
                ships[Name(h)] = (long)(s.Ships.TryGetValue(h, out var n) ? n : 0);

            var overrides = new Dictionary<string, object?>();
            foreach (var kv in s.Map.NodeOverrides)
                overrides[kv.Key] = EncodeOverride(kv.Value);

            var research = new Dictionary<string, object?>();
            foreach (var kv in s.Research)
                if (kv.Value != 0) research[Name(kv.Key)] = (long)kv.Value;

            var root = new Dictionary<string, object?>
            {
                ["tick"] = (long)s.Tick,
                ["seed"] = (long)s.Seed,
                ["homeTile"] = Tile(s.HomeTile),
                ["profile"] = new Dictionary<string, object?>
                {
                    ["name"] = s.Profile.Name,
                    ["avatarSeed"] = (long)s.Profile.AvatarSeed,
                },
                ["resources"] = Bag(s.Resources),
                ["premium"] = new Dictionary<string, object?> { ["darkMatter"] = (long)s.Premium.DarkMatter },
                ["buildings"] = buildings,
                ["buildingLayout"] = layout,
                ["buildQueue"] = Arr(s.BuildQueue, o =>
                {
                    var d = new Dictionary<string, object?>
                    {
                        ["building"] = Name(o.Building),
                        ["toLevel"] = (long)o.ToLevel,
                        ["endsAtTick"] = (long)o.EndsAtTick,
                    };
                    if (o.MineId is int mid) d["mineId"] = (long)mid;
                    return d;
                }),
                ["extraMines"] = Arr(s.ExtraMines, m => (object?)new Dictionary<string, object?>
                {
                    ["id"] = (long)m.Id,
                    ["type"] = Name(m.Type),
                    ["level"] = (long)m.Level,
                    ["plot"] = (long)m.Plot,
                }),
                ["nextMineId"] = (long)s.NextMineId,
                ["ships"] = ships,
                ["shipQueue"] = Arr(s.ShipQueue, o => (object?)new Dictionary<string, object?>
                {
                    ["hull"] = Name(o.Hull),
                    ["remaining"] = (long)o.Remaining,
                    ["nextDoneAtTick"] = (long)o.NextDoneAtTick,
                }),
                ["marches"] = Arr(s.Marches, EncodeMarch),
                ["nextMarchId"] = (long)s.NextMarchId,
                ["map"] = new Dictionary<string, object?>
                {
                    ["nodeOverrides"] = overrides,
                    ["dynamicNodes"] = Arr(s.Map.DynamicNodes, EncodeNode),
                    ["nextDynId"] = (long)s.Map.NextDynId,
                },
                ["research"] = research,
                ["researchQueue"] = Arr(s.ResearchQueue, o => (object?)new Dictionary<string, object?>
                {
                    ["techId"] = Name(o.TechId),
                    ["toLevel"] = (long)o.ToLevel,
                    ["endsAtTick"] = (long)o.EndsAtTick,
                }),
                ["inventory"] = Arr(s.Inventory, e => (object?)new Dictionary<string, object?>
                {
                    ["itemId"] = e.ItemId,
                    ["count"] = (long)e.Count,
                }),
                ["buffs"] = new Dictionary<string, object?>
                {
                    ["prodBoostUntilTick"] = (long)s.Buffs.ProdBoostUntilTick,
                    ["extraBuildSlotUntilTick"] = (long)s.Buffs.ExtraBuildSlotUntilTick,
                    ["energyBoostUntilTick"] = (long)s.Buffs.EnergyBoostUntilTick,
                    ["extraResearchSlotUntilTick"] = (long)s.Buffs.ExtraResearchSlotUntilTick,
                    ["shieldUntilTick"] = (long)s.Buffs.ShieldUntilTick,
                },
                ["mailbox"] = Arr(s.Mailbox, EncodeMail),
                ["nextReportId"] = (long)s.NextReportId,
                ["skins"] = new Dictionary<string, object?>
                {
                    ["owned"] = Arr(s.Skins.Owned, x => (object?)x),
                    ["activePlanet"] = s.Skins.ActivePlanet,
                },
                ["stats"] = EncodeStats(s.Stats),
            };
            if (s.BurningUntilTick > 0) root["burningUntilTick"] = (long)s.BurningUntilTick;
            if (s.VisualSeedOffset != 0) root["visualSeedOffset"] = (long)s.VisualSeedOffset;
            root["testMode"] = s.TestMode;
            if (s.QuestStep > 0) root["questStep"] = (long)s.QuestStep;
            if (s.Achievements.Count > 0)
            {
                var ids = new List<string>(s.Achievements);
                ids.Sort(StringComparer.Ordinal);
                root["achievements"] = Arr(ids, x => (object?)x);
            }
            if (s.Title != null) root["title"] = s.Title;
            if (s.EventInstance >= 0)
                root["event"] = new Dictionary<string, object?>
                {
                    ["instance"] = (long)s.EventInstance,
                    ["baseline"] = s.EventBaseline,
                    ["claimed"] = s.EventClaimed,
                };
            if (s.Season > 0)
                root["season"] = new Dictionary<string, object?>
                {
                    ["number"] = (long)s.Season,
                    ["startMight"] = s.SeasonStartMight,
                    ["history"] = Arr(s.SeasonHistory, h => (object?)new Dictionary<string, object?>
                    {
                        ["season"] = (long)h.Season, ["rank"] = (long)h.Rank, ["of"] = (long)h.Of,
                        ["gain"] = h.Gain, ["reward"] = (long)h.RewardDM,
                    }),
                };
            if (s.Allies.Count > 0)
                root["allies"] = Arr(s.Allies, a =>
                {
                    var pact = new Dictionary<string, object?>
                    {
                        ["bot"] = (long)a.BotId, ["since"] = (long)a.SinceTick, ["nextAid"] = (long)a.NextAidTick,
                    };
                    if (a.PendingRuns > 0)
                    {
                        pact["pending"] = Bag(a.Pending);
                        pact["pendingRuns"] = (long)a.PendingRuns;
                    }
                    return (object?)pact;
                });
            return root;
        }

        public static GameState DecodeState(Dictionary<string, object?> d)
        {
            var s = new GameState
            {
                Tick = I32(d, "tick"),
                Seed = I32(d, "seed"),
                HomeTile = DecTile(AsObj(d["homeTile"], "homeTile")),
                NextMineId = I32(d, "nextMineId"),
                NextMarchId = I32(d, "nextMarchId"),
                NextReportId = I32(d, "nextReportId"),
                BurningUntilTick = d.TryGetValue("burningUntilTick", out var burn) && burn != null
                    ? ToI32(burn) : 0,
                VisualSeedOffset = d.TryGetValue("visualSeedOffset", out var vso) && vso != null
                    ? ToI32(vso) : 0,
                // Saves from before the NEW GAME choice were all test games.
                TestMode = !d.TryGetValue("testMode", out var tm) || tm is not bool tmb || tmb,
                QuestStep = d.TryGetValue("questStep", out var qs) && qs != null ? ToI32(qs) : 0,
            };

            var profile = AsObj(d["profile"], "profile");
            s.Profile = new Profile { Name = Str(profile, "name"), AvatarSeed = I32(profile, "avatarSeed") };
            s.Resources = DecBag(AsObj(d["resources"], "resources"));
            s.Premium = new Premium { DarkMatter = I32(AsObj(d["premium"], "premium"), "darkMatter") };

            s.Buildings = new Dictionary<BuildingId, BuildingSlot>();
            var buildings = AsObj(d["buildings"], "buildings");
            foreach (var id in Buildings.All)
                s.Buildings[id] = new BuildingSlot
                {
                    Level = buildings.TryGetValue(Name(id), out var v) && v != null
                        ? I32(AsObj(v, "building"), "level") : 0,
                };

            s.BuildingLayout = new Dictionary<BuildingId, TileXY>();
            if (d.TryGetValue("buildingLayout", out var rawLayout) && rawLayout is Dictionary<string, object?> layout)
                foreach (var id in Buildings.All)
                    if (layout.TryGetValue(Name(id), out var t) && t != null)
                        s.BuildingLayout[id] = DecTile(AsObj(t, "layout tile"));

            s.BuildQueue = new List<BuildOrder>();
            foreach (var raw in AsArr(d["buildQueue"], "buildQueue"))
            {
                var o = AsObj(raw, "buildQueue[]");
                s.BuildQueue.Add(new BuildOrder
                {
                    Building = BuildingFrom(Str(o, "building")),
                    ToLevel = I32(o, "toLevel"),
                    EndsAtTick = I32(o, "endsAtTick"),
                    MineId = o.TryGetValue("mineId", out var m) && m != null ? (int?)ToI32(m) : null,
                });
            }

            s.ExtraMines = new List<ExtraMine>();
            foreach (var raw in AsArr(d["extraMines"], "extraMines"))
            {
                var o = AsObj(raw, "extraMines[]");
                s.ExtraMines.Add(new ExtraMine
                {
                    Id = I32(o, "id"),
                    Type = MineFrom(Str(o, "type")),
                    Level = I32(o, "level"),
                    Plot = I32(o, "plot"),
                });
            }

            s.Ships = new Dictionary<HullId, int>();
            var ships = AsObj(d["ships"], "ships");
            foreach (var h in Ships.All)
                s.Ships[h] = ships.TryGetValue(Name(h), out var n) && n != null ? ToI32(n) : 0;

            s.ShipQueue = new List<ShipOrder>();
            foreach (var raw in AsArr(d["shipQueue"], "shipQueue"))
            {
                var o = AsObj(raw, "shipQueue[]");
                s.ShipQueue.Add(new ShipOrder
                {
                    Hull = HullFrom(Str(o, "hull")),
                    Remaining = I32(o, "remaining"),
                    NextDoneAtTick = I32(o, "nextDoneAtTick"),
                });
            }

            s.Marches = new List<March>();
            foreach (var raw in AsArr(d["marches"], "marches"))
                s.Marches.Add(DecodeMarch(AsObj(raw, "marches[]")));

            var map = AsObj(d["map"], "map");
            s.Map = new MapState { NextDynId = I32(map, "nextDynId") };
            foreach (var kv in AsObj(map["nodeOverrides"], "nodeOverrides"))
                s.Map.NodeOverrides[kv.Key] = DecodeOverride(AsObj(kv.Value, "nodeOverride"));
            foreach (var raw in AsArr(map["dynamicNodes"], "dynamicNodes"))
                s.Map.DynamicNodes.Add(DecodeNode(AsObj(raw, "dynamicNodes[]")));

            s.Research = new Dictionary<TechId, int>();
            foreach (var kv in AsObj(d["research"], "research"))
                if (kv.Value != null) s.Research[TechFrom(kv.Key)] = ToI32(kv.Value);

            s.ResearchQueue = new List<ResearchOrder>();
            foreach (var raw in AsArr(d["researchQueue"], "researchQueue"))
            {
                var o = AsObj(raw, "researchQueue[]");
                s.ResearchQueue.Add(new ResearchOrder
                {
                    TechId = TechFrom(Str(o, "techId")),
                    ToLevel = I32(o, "toLevel"),
                    EndsAtTick = I32(o, "endsAtTick"),
                });
            }

            s.Inventory = new List<InventoryEntry>();
            foreach (var raw in AsArr(d["inventory"], "inventory"))
            {
                var o = AsObj(raw, "inventory[]");
                s.Inventory.Add(new InventoryEntry { ItemId = Str(o, "itemId"), Count = I32(o, "count") });
            }

            var buffs = AsObj(d["buffs"], "buffs");
            s.Buffs = new Buffs
            {
                ProdBoostUntilTick = I32(buffs, "prodBoostUntilTick"),
                ExtraBuildSlotUntilTick = I32(buffs, "extraBuildSlotUntilTick"),
                EnergyBoostUntilTick = I32(buffs, "energyBoostUntilTick"),
                ExtraResearchSlotUntilTick = I32(buffs, "extraResearchSlotUntilTick"),
                // Optional (added mid-v17) — earlier v17 saves simply lack it.
                ShieldUntilTick = buffs.TryGetValue("shieldUntilTick", out var sh) && sh != null
                    ? ToI32(sh) : 0,
            };

            s.Mailbox = new List<MailItem>();
            foreach (var raw in AsArr(d["mailbox"], "mailbox"))
                s.Mailbox.Add(DecodeMail(AsObj(raw, "mailbox[]")));

            var skins = AsObj(d["skins"], "skins");
            s.Skins = new Skins { Owned = new List<string>(), ActivePlanet = Str(skins, "activePlanet") };
            foreach (var raw in AsArr(skins["owned"], "skins.owned"))
                s.Skins.Owned.Add((string)raw!);

            var stats = AsObj(d["stats"], "stats");
            int Opt(string key) => stats.TryGetValue(key, out var v) && v != null ? ToI32(v) : 0;
            s.Stats = new Stats
            {
                BattlesWon = I32(stats, "battlesWon"),
                BattlesLost = I32(stats, "battlesLost"),
                MarchesSent = I32(stats, "marchesSent"),
                CampsCleared = Opt("campsCleared"),
                RaidsWon = Opt("raidsWon"),
                DefensesWon = Opt("defensesWon"),
                ShipsBuilt = Opt("shipsBuilt"),
                UpgradesDone = Opt("upgradesDone"),
                ResearchDone = Opt("researchDone"),
                EventsCompleted = Opt("eventsCompleted"),
                LootMilli = stats.TryGetValue("lootMilli", out var lm) && lm != null ? ToI64(lm) : 0,
                BestSeasonRank = Opt("bestSeasonRank"),
            };

            if (d.TryGetValue("achievements", out var ach) && ach != null)
                foreach (var raw in AsArr(ach, "achievements"))
                    if (raw is string id) s.Achievements.Add(id);
            s.Title = d.TryGetValue("title", out var tt) ? tt as string : null;
            if (d.TryGetValue("event", out var ev) && ev != null)
            {
                var e = AsObj(ev, "event");
                s.EventInstance = I32(e, "instance");
                s.EventBaseline = I64(e, "baseline");
                s.EventClaimed = e.TryGetValue("claimed", out var ec) && ec is bool ecb && ecb;
            }
            if (d.TryGetValue("season", out var se) && se != null)
            {
                var o = AsObj(se, "season");
                s.Season = I32(o, "number");
                s.SeasonStartMight = I64(o, "startMight");
                foreach (var raw in AsArr(o["history"], "season.history"))
                {
                    var h = AsObj(raw, "season.history[]");
                    s.SeasonHistory.Add(new SeasonRecord
                    {
                        Season = I32(h, "season"), Rank = I32(h, "rank"), Of = I32(h, "of"),
                        Gain = I64(h, "gain"), RewardDM = I32(h, "reward"),
                    });
                }
            }
            if (d.TryGetValue("allies", out var al) && al != null)
                foreach (var raw in AsArr(al, "allies"))
                {
                    var a = AsObj(raw, "allies[]");
                    s.Allies.Add(new Alliance
                    {
                        BotId = I32(a, "bot"), SinceTick = I32(a, "since"), NextAidTick = I32(a, "nextAid"),
                        Pending = a.TryGetValue("pending", out var pd) && pd is Dictionary<string, object?> pdd
                            ? DecBag(pdd) : new ResourceBag(),
                        PendingRuns = a.TryGetValue("pendingRuns", out var pn) && pn != null ? ToI32(pn) : 0,
                    });
                }

            return s;
        }

        // ---------- pieces ----------

        static Dictionary<string, object?> EncodeMarch(March m)
        {
            var d = new Dictionary<string, object?>
            {
                ["id"] = (long)m.Id,
                ["phase"] = Name(m.Phase),
                ["ships"] = Comp(m.Ships),
                ["node"] = Tile(m.Node),
                ["legFrom"] = Pos(m.LegFrom),
                ["legTo"] = Pos(m.LegTo),
                ["departedAtTick"] = (long)m.DepartedAtTick,
                ["arrivesAtTick"] = (long)m.ArrivesAtTick,
                ["cargo"] = Bag(m.Cargo),
                ["heliumSpent"] = (long)m.HeliumSpent,
            };
            if (m.CargoDm > 0) d["cargoDm"] = m.CargoDm; // v13 — absent in older saves
            if (m.Recalled) d["recalled"] = true;        // optional — absent in older saves
            d["mission"] = Name(m.Mission);
            return d;
        }

        static March DecodeMarch(Dictionary<string, object?> o) => new()
        {
            Id = I32(o, "id"),
            Phase = PhaseFrom(Str(o, "phase")),
            Ships = DecComp(AsObj(o["ships"], "march.ships")),
            Node = DecTile(AsObj(o["node"], "march.node")),
            LegFrom = DecPos(AsObj(o["legFrom"], "march.legFrom")),
            LegTo = DecPos(AsObj(o["legTo"], "march.legTo")),
            DepartedAtTick = I32(o, "departedAtTick"),
            ArrivesAtTick = I32(o, "arrivesAtTick"),
            Cargo = DecBag(AsObj(o["cargo"], "march.cargo")),
            HeliumSpent = I32(o, "heliumSpent"),
            CargoDm = o.TryGetValue("cargoDm", out var dm) && dm is long dml ? dml : 0,
            Recalled = o.TryGetValue("recalled", out var rc) && rc is bool rcb && rcb,
            Mission = MissionFrom(Str(o, "mission")),
        };

        static Dictionary<string, object?> EncodeOverride(NodeOverride o)
        {
            var d = new Dictionary<string, object?>();
            if (o.Remaining is int r) d["remaining"] = (long)r;
            if (o.Cleared) d["cleared"] = true;
            if (o.RespawnAtTick != 0) d["respawnAtTick"] = (long)o.RespawnAtTick;
            if (o.Retired) d["retired"] = true;
            return d;
        }

        static NodeOverride DecodeOverride(Dictionary<string, object?> d) => new()
        {
            Remaining = d.TryGetValue("remaining", out var r) && r != null ? (int?)ToI32(r) : null,
            Cleared = d.TryGetValue("cleared", out var c) && c is bool cb && cb,
            RespawnAtTick = d.TryGetValue("respawnAtTick", out var t) && t != null ? ToI32(t) : 0,
            Retired = d.TryGetValue("retired", out var x) && x is bool xb && xb,
        };

        static Dictionary<string, object?> EncodeNode(MapNode n)
        {
            var d = new Dictionary<string, object?>
            {
                ["id"] = n.Id,
                ["kind"] = Name(n.Kind),
                ["tile"] = Tile(n.Tile),
                ["tier"] = (long)n.Tier,
                ["amount"] = (long)n.Amount,
                ["ratePerSec"] = (long)n.RatePerSec,
            };
            if (n.Resource is ResourceId res) d["resource"] = Name(res);
            d["campLevel"] = (long)n.CampLevel;
            return d;
        }

        static MapNode DecodeNode(Dictionary<string, object?> d) => new()
        {
            Id = Str(d, "id"),
            Kind = KindFrom(Str(d, "kind")),
            Tile = DecTile(AsObj(d["tile"], "node.tile")),
            Tier = I32(d, "tier"),
            Amount = I32(d, "amount"),
            RatePerSec = I32(d, "ratePerSec"),
            Resource = d.TryGetValue("resource", out var r) && r is string rs ? ResourceFrom(rs) : null,
            CampLevel = d.TryGetValue("campLevel", out var c) && c != null ? ToI32(c) : 0,
        };

        static Dictionary<string, object?> EncodeMail(MailItem item)
        {
            var d = new Dictionary<string, object?>
            {
                ["kind"] = item is BattleMailReport ? "battle" : item is RadarWarning ? "radar" : "spy",
                ["id"] = (long)item.Id,
                ["atTick"] = (long)item.AtTick,
                ["target"] = Tile(item.Target),
                ["subject"] = item.Subject,
            };
            switch (item)
            {
                case RadarWarning radar:
                {
                    var w = new Dictionary<string, object?>
                    {
                        ["arrivesAtTick"] = (long)radar.ArrivesAtTick,
                    };
                    if (radar.IsFleet is bool fleet) w["isFleet"] = fleet;
                    if (radar.AttackerName != null) w["attackerName"] = radar.AttackerName;
                    if (radar.FleetCount is int fc) w["fleetCount"] = (long)fc;
                    if (radar.FleetComp != null) w["fleetComp"] = Comp(radar.FleetComp);
                    d["warning"] = w;
                    break;
                }
                case SpyReport spy:
                {
                    var intel = new Dictionary<string, object?>
                    {
                        ["kind"] = spy.Intel.Kind is NodeKind k ? Name(k) : "empty",
                    };
                    if (spy.Intel.Tier is int tier) intel["tier"] = (long)tier;
                    if (spy.Intel.Remaining is int rem) intel["remaining"] = (long)rem;
                    if (spy.Intel.Garrison != null) intel["garrison"] = Comp(spy.Intel.Garrison);
                    if (spy.Intel.CampLevel is int cl) intel["campLevel"] = (long)cl;
                    if (spy.Intel.Research != null)
                    {
                        var research = new Dictionary<string, object?>();
                        foreach (var kv in spy.Intel.Research)
                            if (kv.Value != 0) research[Name(kv.Key)] = (long)kv.Value;
                        intel["research"] = research;
                    }
                    if (spy.Intel.Buildings != null)
                    {
                        var buildings = new Dictionary<string, object?>();
                        foreach (var kv in spy.Intel.Buildings)
                            if (kv.Value != 0) buildings[Name(kv.Key)] = (long)kv.Value;
                        intel["buildings"] = buildings;
                    }
                    if (spy.Intel.LootableMilli != null) intel["lootable"] = Bag(spy.Intel.LootableMilli);
                    if (spy.Intel.ProtectedMilli != null) intel["protected"] = Bag(spy.Intel.ProtectedMilli);
                    d["intel"] = intel;
                    break;
                }
                case BattleMailReport battle:
                    d["report"] = EncodeReport(battle.Report);
                    if (battle.Defending) d["defending"] = true; // optional: absent = attacker view
                    if (battle.AttackerBotId > 0) d["attackerBotId"] = (long)battle.AttackerBotId;
                    if (battle.AllyShips != null) d["allyShips"] = Comp(battle.AllyShips);
                    if (battle.AllyNames != null) d["allyNames"] = battle.AllyNames;
                    break;
            }
            d["read"] = item.Read;
            d["favorite"] = item.Favorite;
            return d;
        }

        static MailItem DecodeMail(Dictionary<string, object?> d)
        {
            string kind = Str(d, "kind");
            MailItem item;
            if (kind == "battle")
            {
                item = new BattleMailReport { Report = DecodeReport(AsObj(d["report"], "mail.report")) };
            }
            else if (kind == "radar")
            {
                var w = AsObj(d["warning"], "mail.warning");
                item = new RadarWarning
                {
                    ArrivesAtTick = I32(w, "arrivesAtTick"),
                    IsFleet = w.TryGetValue("isFleet", out var fl) && fl is bool flb ? (bool?)flb : null,
                    AttackerName = w.TryGetValue("attackerName", out var an) ? an as string : null,
                    FleetCount = w.TryGetValue("fleetCount", out var fc) && fc != null ? (int?)ToI32(fc) : null,
                    FleetComp = w.TryGetValue("fleetComp", out var comp) && comp is Dictionary<string, object?> cd
                        ? DecComp(cd) : null,
                };
            }
            else
            {
                var intel = AsObj(d["intel"], "mail.intel");
                string ik = Str(intel, "kind");
                Dictionary<TechId, int>? spiedResearch = null;
                if (intel.TryGetValue("research", out var sr) && sr is Dictionary<string, object?> srd)
                {
                    spiedResearch = new Dictionary<TechId, int>();
                    foreach (var kv in srd)
                        if (kv.Value != null) spiedResearch[TechFrom(kv.Key)] = ToI32(kv.Value);
                }
                Dictionary<BuildingId, int>? spiedBuildings = null;
                if (intel.TryGetValue("buildings", out var sb) && sb is Dictionary<string, object?> sbd)
                {
                    spiedBuildings = new Dictionary<BuildingId, int>();
                    foreach (var kv in sbd)
                        if (kv.Value != null) spiedBuildings[BuildingFrom(kv.Key)] = ToI32(kv.Value);
                }
                item = new SpyReport
                {
                    Intel = new SpyIntel
                    {
                        Kind = ik == "empty" ? null : KindFrom(ik),
                        Tier = intel.TryGetValue("tier", out var t) && t != null ? (int?)ToI32(t) : null,
                        Remaining = intel.TryGetValue("remaining", out var r) && r != null ? (int?)ToI32(r) : null,
                        Garrison = intel.TryGetValue("garrison", out var g) && g is Dictionary<string, object?> gd
                            ? DecComp(gd) : null,
                        CampLevel = intel.TryGetValue("campLevel", out var c) && c != null ? (int?)ToI32(c) : null,
                        Research = spiedResearch,
                        Buildings = spiedBuildings,
                        LootableMilli = intel.TryGetValue("lootable", out var lo) && lo is Dictionary<string, object?> lod
                            ? DecBag(lod) : null,
                        ProtectedMilli = intel.TryGetValue("protected", out var pr) && pr is Dictionary<string, object?> prd
                            ? DecBag(prd) : null,
                    },
                };
            }
            item.Id = I32(d, "id");
            item.AtTick = I32(d, "atTick");
            item.Target = DecTile(AsObj(d["target"], "mail.target"));
            item.Subject = Str(d, "subject");
            item.Read = d.TryGetValue("read", out var rd) && rd is bool rb && rb;
            item.Favorite = d.TryGetValue("favorite", out var f) && f is bool fb && fb;
            if (item is BattleMailReport mail)
            {
                mail.Defending = IsDefenseReport(mail,
                    d.TryGetValue("defending", out var df) && df is bool dfb ? dfb : null);
                if (d.TryGetValue("attackerBotId", out var ab) && ab is long abl) mail.AttackerBotId = (int)abl;
                if (d.TryGetValue("allyShips", out var ash) && ash is Dictionary<string, object?> ashd)
                    mail.AllyShips = DecComp(ashd);
                if (d.TryGetValue("allyNames", out var anm)) mail.AllyNames = anm as string;
            }
            return item;
        }

        // Public: the PvP raid wire format (raids.report / .loot / .ship_losses
        // jsonb columns) reuses the exact v12 battle-report shapes.
        /// <summary>
        /// Which side of a battle the player was on. The saved flag decides —
        /// except for "Raid repelled — X", which is ALSO the subject of your own
        /// failed raid on X: saves from before the flag, and builds that guessed
        /// from the subject alone, read those as defenses (a green VICTORY for a
        /// raid you lost). Your raid's defender is X itself; a raid you fought off
        /// names the attacker, with you as the defender.
        /// </summary>
        static bool IsDefenseReport(BattleMailReport mail, bool? flag)
        {
            const string repelled = "Raid repelled — ";
            string subject = mail.Subject;
            if (subject.StartsWith(repelled, StringComparison.Ordinal))
                return subject.Substring(repelled.Length) != (mail.Report.DefenderName ?? "");
            if (flag is bool saved) return saved;
            return subject.StartsWith("Colony raided by", StringComparison.Ordinal)
                || subject.StartsWith("Raid deflected", StringComparison.Ordinal);
        }

        static Dictionary<string, object?> EncodeStats(Stats st)
        {
            var d = new Dictionary<string, object?>
            {
                ["battlesWon"] = (long)st.BattlesWon,
                ["battlesLost"] = (long)st.BattlesLost,
                ["marchesSent"] = (long)st.MarchesSent,
            };
            // Progression counters only when set: 249 bot states carry stats too.
            void Opt(string key, long v) { if (v != 0) d[key] = v; }
            Opt("campsCleared", st.CampsCleared);
            Opt("raidsWon", st.RaidsWon);
            Opt("defensesWon", st.DefensesWon);
            Opt("shipsBuilt", st.ShipsBuilt);
            Opt("upgradesDone", st.UpgradesDone);
            Opt("researchDone", st.ResearchDone);
            Opt("eventsCompleted", st.EventsCompleted);
            Opt("lootMilli", st.LootMilli);
            Opt("bestSeasonRank", st.BestSeasonRank);
            return d;
        }

        public static Dictionary<string, object?> EncodeReport(BattleReport r)
        {
            var d = new Dictionary<string, object?>
            {
                ["attacker"] = Comp(r.Attacker),
                ["defender"] = Comp(r.Defender),
                ["winner"] = Name(r.Winner),
                ["rounds"] = Arr(r.Rounds, rl => (object?)new Dictionary<string, object?>
                {
                    ["round"] = (long)rl.Round,
                    ["attackerLosses"] = Comp(rl.AttackerLosses),
                    ["defenderLosses"] = Comp(rl.DefenderLosses),
                }),
                ["attackerSurvivors"] = Comp(r.AttackerSurvivors),
                ["defenderSurvivors"] = Comp(r.DefenderSurvivors),
            };
            if (r.Loot != null) d["loot"] = Bag(r.Loot);
            if (r.Location is TileXY loc) d["location"] = Tile(loc);
            if (r.DefenderName != null) d["defenderName"] = r.DefenderName;
            if (r.DefenderBattery > 0) d["battery"] = (long)r.DefenderBattery;
            return d;
        }

        public static BattleReport DecodeReport(Dictionary<string, object?> d)
        {
            var r = new BattleReport
            {
                Attacker = DecComp(AsObj(d["attacker"], "report.attacker")),
                Defender = DecComp(AsObj(d["defender"], "report.defender")),
                Winner = WinnerFrom(Str(d, "winner")),
                AttackerSurvivors = DecComp(AsObj(d["attackerSurvivors"], "report.attackerSurvivors")),
                DefenderSurvivors = DecComp(AsObj(d["defenderSurvivors"], "report.defenderSurvivors")),
                Loot = d.TryGetValue("loot", out var l) && l != null ? DecBag(AsObj(l, "report.loot")) : null,
                Location = d.TryGetValue("location", out var loc) && loc != null
                    ? (TileXY?)DecTile(AsObj(loc, "report.location")) : null,
                DefenderName = d.TryGetValue("defenderName", out var n) && n is string ns ? ns : null,
                DefenderBattery = d.TryGetValue("battery", out var bt) && bt is long btl ? (int)btl : 0,
            };
            foreach (var raw in AsArr(d["rounds"], "report.rounds"))
            {
                var o = AsObj(raw, "rounds[]");
                r.Rounds.Add(new RoundLog
                {
                    Round = I32(o, "round"),
                    AttackerLosses = DecComp(AsObj(o["attackerLosses"], "round.attackerLosses")),
                    DefenderLosses = DecComp(AsObj(o["defenderLosses"], "round.defenderLosses")),
                });
            }
            return r;
        }

        // ---------- small shared shapes ----------

        static Dictionary<string, object?> Tile(TileXY t) => new() { ["x"] = (long)t.X, ["y"] = (long)t.Y };
        static TileXY DecTile(Dictionary<string, object?> d) => new(I32(d, "x"), I32(d, "y"));

        static Dictionary<string, object?> Pos(Position p) => new() { ["x"] = p.X, ["y"] = p.Y };
        static Position DecPos(Dictionary<string, object?> d) => new(F64(d, "x"), F64(d, "y"));

        public static Dictionary<string, object?> Bag(ResourceBag b) => new()
        {
            ["gold"] = b.Gold,
            ["quartz"] = b.Quartz,
            ["helium"] = b.Helium,
        };

        public static ResourceBag DecBag(Dictionary<string, object?> d) => new()
        {
            Gold = I64(d, "gold"),
            Quartz = I64(d, "quartz"),
            Helium = I64(d, "helium"),
        };

        /// <summary>Partial hull record — only non-zero entries, like v1's FleetComp.</summary>
        public static Dictionary<string, object?> Comp(Dictionary<HullId, int> comp)
        {
            var d = new Dictionary<string, object?>();
            foreach (var h in Ships.All)
                if (comp.TryGetValue(h, out int n) && n != 0) d[Name(h)] = (long)n;
            return d;
        }

        public static Dictionary<HullId, int> DecComp(Dictionary<string, object?> d)
        {
            var comp = new Dictionary<HullId, int>();
            foreach (var kv in d)
                if (kv.Value != null) comp[HullFrom(kv.Key)] = ToI32(kv.Value);
            return comp;
        }

        static List<object?> Arr<T>(IReadOnlyList<T> items, Func<T, object?> encode)
        {
            var arr = new List<object?>(items.Count);
            foreach (var item in items) arr.Add(encode(item));
            return arr;
        }

        // ---------- enum name maps (v1 string ids — compile-checked switches) ----------

        static string Name(BuildingId id) => id switch
        {
            BuildingId.CommandCenter => "commandCenter",
            BuildingId.GoldMine => "goldMine",
            BuildingId.QuartzExtractor => "quartzExtractor",
            BuildingId.HeliumRefinery => "heliumRefinery",
            BuildingId.PowerPlant => "powerPlant",
            BuildingId.Shipyard => "shipyard",
            BuildingId.Warehouse => "warehouse",
            BuildingId.ResearchLab => "researchLab",
            BuildingId.RadarStation => "radarStation",
            _ => throw new InvalidOperationException($"unknown BuildingId {id}"),
        };

        static BuildingId BuildingFrom(string s) => s switch
        {
            "commandCenter" => BuildingId.CommandCenter,
            "goldMine" => BuildingId.GoldMine,
            "quartzExtractor" => BuildingId.QuartzExtractor,
            "heliumRefinery" => BuildingId.HeliumRefinery,
            "powerPlant" => BuildingId.PowerPlant,
            "shipyard" => BuildingId.Shipyard,
            "warehouse" => BuildingId.Warehouse,
            "researchLab" => BuildingId.ResearchLab,
            "radarStation" => BuildingId.RadarStation,
            _ => throw new FormatException($"unknown building '{s}'"),
        };

        static string Name(HullId id) => id switch
        {
            HullId.Fighter => "fighter",
            HullId.Bomber => "bomber",
            HullId.Cruiser => "cruiser",
            HullId.Hauler => "hauler",
            HullId.Probe => "probe",
            HullId.Talon => "talon",
            HullId.Vanguard => "vanguard",
            HullId.Lancer => "lancer",
            HullId.Leviathan => "leviathan",
            HullId.Reaper => "reaper",
            HullId.Atlas => "atlas",
            HullId.Aegis => "aegis",
            HullId.Scavenger => "scavenger",
            HullId.Sentinel => "sentinel",
            HullId.Harrier => "harrier",
            HullId.Rampart => "rampart",
            HullId.Corsair => "corsair",
            HullId.Bulwark => "bulwark",
            HullId.Javelin => "javelin",
            HullId.Behemoth => "behemoth",
            HullId.Nomad => "nomad",
            HullId.Warden => "warden",
            HullId.Wraith => "wraith",
            _ => throw new InvalidOperationException($"unknown HullId {id}"),
        };

        static HullId HullFrom(string s) => s switch
        {
            "fighter" => HullId.Fighter,
            "bomber" => HullId.Bomber,
            "cruiser" => HullId.Cruiser,
            "hauler" => HullId.Hauler,
            "probe" => HullId.Probe,
            "talon" => HullId.Talon,
            "vanguard" => HullId.Vanguard,
            "lancer" => HullId.Lancer,
            "leviathan" => HullId.Leviathan,
            "reaper" => HullId.Reaper,
            "atlas" => HullId.Atlas,
            "aegis" => HullId.Aegis,
            "scavenger" => HullId.Scavenger,
            "sentinel" => HullId.Sentinel,
            "harrier" => HullId.Harrier,
            "rampart" => HullId.Rampart,
            "corsair" => HullId.Corsair,
            "bulwark" => HullId.Bulwark,
            "javelin" => HullId.Javelin,
            "behemoth" => HullId.Behemoth,
            "nomad" => HullId.Nomad,
            "warden" => HullId.Warden,
            "wraith" => HullId.Wraith,
            _ => throw new FormatException($"unknown hull '{s}'"),
        };

        static string Name(MineType t) => t switch
        {
            MineType.GoldMine => "goldMine",
            MineType.QuartzExtractor => "quartzExtractor",
            MineType.HeliumRefinery => "heliumRefinery",
            _ => throw new InvalidOperationException($"unknown MineType {t}"),
        };

        static MineType MineFrom(string s) => s switch
        {
            "goldMine" => MineType.GoldMine,
            "quartzExtractor" => MineType.QuartzExtractor,
            "heliumRefinery" => MineType.HeliumRefinery,
            _ => throw new FormatException($"unknown mine type '{s}'"),
        };

        static string Name(TechId id) => id switch
        {
            TechId.YieldOptimization => "yieldOptimization",
            TechId.DeepCoreDrilling => "deepCoreDrilling",
            TechId.IonThrusters => "ionThrusters",
            TechId.CargoHolds => "cargoHolds",
            TechId.FuelInjection => "fuelInjection",
            TechId.WeaponsCalibration => "weaponsCalibration",
            TechId.ArmorPlating => "armorPlating",
            TechId.PrefabAssembly => "prefabAssembly",
            TechId.ExtractionAlgorithms => "extractionAlgorithms",
            TechId.DeepVaultProtocols => "deepVaultProtocols",
            TechId.FighterDoctrine => "fighterDoctrine",
            TechId.BomberPayloads => "bomberPayloads",
            TechId.CruiserBroadsides => "cruiserBroadsides",
            TechId.FighterPlating => "fighterPlating",
            TechId.BomberHulls => "bomberHulls",
            TechId.CruiserBulkheads => "cruiserBulkheads",
            TechId.RapidFabrication => "rapidFabrication",
            TechId.AntimatterWarheads => "antimatterWarheads",
            TechId.DeflectorArray => "deflectorArray",
            TechId.ReinforcedHulls => "reinforcedHulls",
            TechId.NaniteRepairSwarms => "naniteRepairSwarms",
            TechId.SwarmFabricators => "swarmFabricators",
            TechId.QuantumExtractors => "quantumExtractors",
            TechId.QuantumComputing => "quantumComputing",
            TechId.SingularityCores => "singularityCores",
            TechId.OrbitalAssembly => "orbitalAssembly",
            TechId.BastionHangars => "bastionHangars",
            TechId.PointDefenseGrid => "pointDefenseGrid",
            TechId.OrbitalBatteries => "orbitalBatteries",
            TechId.PlanetaryDeflectors => "planetaryDeflectors",
            _ => throw new InvalidOperationException($"unknown TechId {id}"),
        };

        static TechId TechFrom(string s) => s switch
        {
            "yieldOptimization" => TechId.YieldOptimization,
            "deepCoreDrilling" => TechId.DeepCoreDrilling,
            "ionThrusters" => TechId.IonThrusters,
            "cargoHolds" => TechId.CargoHolds,
            "fuelInjection" => TechId.FuelInjection,
            "weaponsCalibration" => TechId.WeaponsCalibration,
            "armorPlating" => TechId.ArmorPlating,
            "prefabAssembly" => TechId.PrefabAssembly,
            "extractionAlgorithms" => TechId.ExtractionAlgorithms,
            "deepVaultProtocols" => TechId.DeepVaultProtocols,
            "fighterDoctrine" => TechId.FighterDoctrine,
            "bomberPayloads" => TechId.BomberPayloads,
            "cruiserBroadsides" => TechId.CruiserBroadsides,
            "fighterPlating" => TechId.FighterPlating,
            "bomberHulls" => TechId.BomberHulls,
            "cruiserBulkheads" => TechId.CruiserBulkheads,
            "rapidFabrication" => TechId.RapidFabrication,
            "antimatterWarheads" => TechId.AntimatterWarheads,
            "deflectorArray" => TechId.DeflectorArray,
            "reinforcedHulls" => TechId.ReinforcedHulls,
            "naniteRepairSwarms" => TechId.NaniteRepairSwarms,
            "swarmFabricators" => TechId.SwarmFabricators,
            "quantumExtractors" => TechId.QuantumExtractors,
            "quantumComputing" => TechId.QuantumComputing,
            "singularityCores" => TechId.SingularityCores,
            "orbitalAssembly" => TechId.OrbitalAssembly,
            "bastionHangars" => TechId.BastionHangars,
            "pointDefenseGrid" => TechId.PointDefenseGrid,
            "orbitalBatteries" => TechId.OrbitalBatteries,
            "planetaryDeflectors" => TechId.PlanetaryDeflectors,
            _ => throw new FormatException($"unknown tech '{s}'"),
        };

        static string Name(NodeKind k) => k switch
        {
            NodeKind.Asteroid => "asteroid",
            NodeKind.Nebula => "nebula",
            NodeKind.HeliumCloud => "heliumcloud",
            NodeKind.Derelict => "derelict",
            NodeKind.Camp => "camp",
            NodeKind.DMField => "dmfield",
            _ => throw new InvalidOperationException($"unknown NodeKind {k}"),
        };

        static NodeKind KindFrom(string s) => s switch
        {
            "asteroid" => NodeKind.Asteroid,
            "nebula" => NodeKind.Nebula,
            "heliumcloud" => NodeKind.HeliumCloud,
            "derelict" => NodeKind.Derelict,
            "camp" => NodeKind.Camp,
            "dmfield" => NodeKind.DMField,
            _ => throw new FormatException($"unknown node kind '{s}'"),
        };

        static string Name(ResourceId r) => r switch
        {
            ResourceId.Gold => "gold",
            ResourceId.Quartz => "quartz",
            ResourceId.Helium => "helium",
            _ => throw new InvalidOperationException($"unknown ResourceId {r}"),
        };

        static ResourceId ResourceFrom(string s) => s switch
        {
            "gold" => ResourceId.Gold,
            "quartz" => ResourceId.Quartz,
            "helium" => ResourceId.Helium,
            _ => throw new FormatException($"unknown resource '{s}'"),
        };

        static string Name(MarchPhase p) => p switch
        {
            MarchPhase.Outbound => "outbound",
            MarchPhase.Gathering => "gathering",
            MarchPhase.Returning => "returning",
            _ => throw new InvalidOperationException($"unknown MarchPhase {p}"),
        };

        static MarchPhase PhaseFrom(string s) => s switch
        {
            "outbound" => MarchPhase.Outbound,
            "gathering" => MarchPhase.Gathering,
            "returning" => MarchPhase.Returning,
            _ => throw new FormatException($"unknown phase '{s}'"),
        };

        static string Name(MarchMission m) => m switch
        {
            MarchMission.Gather => "gather",
            MarchMission.Attack => "attack",
            MarchMission.Spy => "spy",
            _ => throw new InvalidOperationException($"unknown MarchMission {m}"),
        };

        static MarchMission MissionFrom(string s) => s switch
        {
            "gather" => MarchMission.Gather,
            "attack" => MarchMission.Attack,
            "spy" => MarchMission.Spy,
            _ => throw new FormatException($"unknown mission '{s}'"),
        };

        static string Name(BattleWinner w) => w switch
        {
            BattleWinner.Attacker => "attacker",
            BattleWinner.Defender => "defender",
            BattleWinner.Draw => "draw",
            _ => throw new InvalidOperationException($"unknown BattleWinner {w}"),
        };

        static BattleWinner WinnerFrom(string s) => s switch
        {
            "attacker" => BattleWinner.Attacker,
            "defender" => BattleWinner.Defender,
            "draw" => BattleWinner.Draw,
            _ => throw new FormatException($"unknown winner '{s}'"),
        };

        // ---------- JSON access helpers ----------

        static Dictionary<string, object?> AsObj(object? v, string what) =>
            v as Dictionary<string, object?> ?? throw new FormatException($"expected object for {what}");

        static List<object?> AsArr(object? v, string what) =>
            v as List<object?> ?? throw new FormatException($"expected array for {what}");

        static string Str(Dictionary<string, object?> d, string key) =>
            d.TryGetValue(key, out var v) && v is string s
                ? s : throw new FormatException($"expected string '{key}'");

        static int I32(Dictionary<string, object?> d, string key) =>
            d.TryGetValue(key, out var v) && v != null
                ? ToI32(v) : throw new FormatException($"expected number '{key}'");

        static long I64(Dictionary<string, object?> d, string key) =>
            d.TryGetValue(key, out var v) && v != null
                ? ToI64(v) : throw new FormatException($"expected number '{key}'");

        static double F64(Dictionary<string, object?> d, string key) =>
            d.TryGetValue(key, out var v) && v != null
                ? v is double dd ? dd : v is long l ? l : throw new FormatException($"expected number '{key}'")
                : throw new FormatException($"expected number '{key}'");

        static int ToI32(object v) => v switch
        {
            long l => checked((int)l),
            double d => checked((int)Math.Round(d)),
            _ => throw new FormatException($"expected int, got {v.GetType()}"),
        };

        static long ToI64(object v) => v switch
        {
            long l => l,
            double d => checked((long)Math.Round(d)),
            _ => throw new FormatException($"expected long, got {v.GetType()}"),
        };
    }
}
