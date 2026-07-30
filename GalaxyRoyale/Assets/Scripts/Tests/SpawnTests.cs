// Spawn placement tests: deterministic per id, lands on the outer rim, and
// never on a static node or an already-taken home tile (the bot galaxy seats
// 99 empires through the same function).
using System.Collections.Generic;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Map;

namespace GalaxyRoyale.Sim.Tests
{
    public class SpawnTests
    {
        [Test]
        public void SpawnTile_IsDeterministicPerUserId()
        {
            var a1 = Spawn.SpawnTileForUser("7f9c24e5-1c37-4c1e-9d2a-000000000001");
            var a2 = Spawn.SpawnTileForUser("7f9c24e5-1c37-4c1e-9d2a-000000000001");
            var b = Spawn.SpawnTileForUser("7f9c24e5-1c37-4c1e-9d2a-000000000002");
            Assert.AreEqual(a1, a2, "same user id must always spawn at the same tile");
            Assert.AreNotEqual(a1, b, "different users should (virtually always) spread apart");
        }

        [Test]
        public void SpawnTile_LandsOnOuterRim_OffStaticNodes()
        {
            var sector = MapGenerator.GenerateSector(Spawn.GalaxySeed);
            int center = Balance.SectorSize / 2;
            for (int i = 0; i < 8; i++)
            {
                var tile = Spawn.SpawnTileForUser($"user-{i}");
                Assert.IsFalse(sector.Nodes.ContainsKey(tile.Key()), "spawn must not sit on a node");
                double dist = System.Math.Sqrt(
                    (tile.X - center) * (double)(tile.X - center) +
                    (tile.Y - center) * (double)(tile.Y - center));
                Assert.That(dist, Is.GreaterThan(center * 0.9), "spawn should be far out on the rim");
                Assert.That(tile.X, Is.InRange(4, Balance.SectorSize - 5));
                Assert.That(tile.Y, Is.InRange(4, Balance.SectorSize - 5));
            }
        }

        [Test]
        public void SpawnTile_AvoidsTakenTiles()
        {
            var taken = new HashSet<string>();
            for (int i = 0; i < 32; i++)
            {
                var tile = Spawn.SpawnTileFor($"bot-{i}", Spawn.GalaxySeed, taken);
                Assert.IsFalse(taken.Contains(tile.Key()), "no two empires share a home tile");
                taken.Add(tile.Key());
            }
        }
    }
}
