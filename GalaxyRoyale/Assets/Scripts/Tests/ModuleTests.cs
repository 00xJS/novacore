// Ship modules, blueprints and fleet presets (2026-09-30).
using System.Collections.Generic;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class ModuleTests
    {
        [Test]
        public void AModule_NeedsItsBlueprint_AndChangesItsClassInBattle()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            Assert.IsFalse(ModuleSystem.Fit(s, "Strike Craft", ModuleKind.PlasmaLances).Ok, "no blueprint yet");
            float atk = ResearchSystem.AtkMultFor(s, HullId.Fighter);
            float hp = ResearchSystem.HpMultFor(s, HullId.Fighter);
            ModuleSystem.Grant(s, ModuleKind.PlasmaLances);
            Assert.IsTrue(ModuleSystem.Fit(s, "Strike Craft", ModuleKind.PlasmaLances).Ok);
            Assert.AreEqual(atk + 0.20f, ResearchSystem.AtkMultFor(s, HullId.Fighter), 1e-4);
            Assert.AreEqual(hp - 0.10f, ResearchSystem.HpMultFor(s, HullId.Fighter), 1e-4);
            Assert.AreEqual(atk, ResearchSystem.AtkMultFor(s, HullId.Vanguard), 1e-4, "only its own class");
            Assert.IsFalse(ModuleSystem.Fit(s, "Support", ModuleKind.PlasmaLances).Ok, "warships only");
        }

        [Test]
        public void MoreCopies_RaiseTheMark_UpToIII()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            Assert.AreEqual(1, ModuleSystem.Grant(s, ModuleKind.TwinLinkedGuns));
            Assert.AreEqual(2, ModuleSystem.Grant(s, ModuleKind.TwinLinkedGuns));
            ModuleSystem.Fit(s, "Battleships", ModuleKind.TwinLinkedGuns);
            Assert.AreEqual(0.15f, ModuleSystem.AtkFor(s, HullId.Vanguard), 1e-4, "Mk II: ×1.5");
            ModuleSystem.Grant(s, ModuleKind.TwinLinkedGuns);
            Assert.AreEqual(3, ModuleSystem.Grant(s, ModuleKind.TwinLinkedGuns), "capped at Mk III");
            Assert.AreEqual(0.20f, ModuleSystem.AtkFor(s, HullId.Vanguard), 1e-4);
        }

        [Test]
        public void BeatingALord_BringsTheirBlueprint()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            var events = new SimEventBus();
            var got = new List<BlueprintFound>();
            events.Subscribe(e => { if (e is BlueprintFound b) got.Add(b); });
            var lair = LairSystem.Spawn(s, 4, 0, events);
            LairSystem.OnDefeated(s, lair, events);
            Assert.AreEqual(1, got.Count);
            Assert.AreEqual(ModuleKind.BulwarkFrames, got[0].Kind, "lord 4 carries module 4 % 6");
            Assert.IsTrue(ModuleSystem.Owned(s, ModuleKind.BulwarkFrames));
        }

        [Test]
        public void Presets_Save_LoadTrimmedToWhatsDocked_AndSurviveASave()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            s.Ships[HullId.Fighter] = 100;
            s.Ships[HullId.Cruiser] = 10;
            ModuleSystem.SavePreset(s, 1, new Dictionary<HullId, int> { [HullId.Fighter] = 80, [HullId.Cruiser] = 20, [HullId.Bomber] = 0 });
            var load = ModuleSystem.LoadPreset(s, 1);
            Assert.AreEqual(80, load[HullId.Fighter]);
            Assert.AreEqual(10, load[HullId.Cruiser], "only what's docked");
            Assert.IsFalse(load.ContainsKey(HullId.Bomber));
            ModuleSystem.Grant(s, ModuleKind.BulwarkFrames);
            ModuleSystem.Fit(s, "Destroyers", ModuleKind.BulwarkFrames);
            var back = SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(s, 1000))).State;
            Assert.AreEqual(80, back.Modules.Presets[1][HullId.Fighter]);
            Assert.AreEqual(ModuleKind.BulwarkFrames, ModuleSystem.FittedTo(back, "Destroyers"));
            Assert.AreEqual(1, ModuleSystem.Mark(back, ModuleKind.BulwarkFrames));
            Assert.AreEqual(3, back.Modules.Presets.Count);
        }
    }
}
