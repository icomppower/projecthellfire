using System;
using System.Collections.Generic;
using Hellfire.Sim;
using NUnit.Framework;

namespace Hellfire.Sim.Tests
{
    /// <summary>
    /// Gate 3: spatial hash vs brute force over 300 dense queries — 0 dropped
    /// occupants, 0 phantom extras. Exact set equality, not just counts, because
    /// the Pacific Commander failure mode is silent dropping.
    /// </summary>
    [TestFixture]
    public class SpatialHashTests
    {
        private static int BruteForce(float x, float y, float radius, int self,
                                      float[] posX, float[] posY, int count, int[] results)
        {
            float r2 = radius * radius;
            int n = 0;
            for (int j = 0; j < count; j++)
            {
                if (j == self) continue;
                float dx = posX[j] - x;
                float dy = posY[j] - y;
                if (dx * dx + dy * dy <= r2) results[n++] = j;
            }
            return n;
        }

        [Test]
        public void DenseQueries_ExactMatchWithBruteForce_300Queries()
        {
            const int agentCount = 1500;
            const ulong seed = 777UL;

            // Dense cluster: all agents packed into a quarter of the world so cells overflow-test.
            var posX = new float[agentCount];
            var posY = new float[agentCount];
            for (int i = 0; i < agentCount; i++)
            {
                posX[i] = DetHash.Float01(seed, 0, (ulong)i, 100) * (Simulation.WorldWidth * 0.25f);
                posY[i] = DetHash.Float01(seed, 0, (ulong)i, 101) * (Simulation.WorldHeight * 0.25f);
            }

            var hash = new SpatialHash(Simulation.WorldWidth, Simulation.WorldHeight, 12f, agentCount);
            hash.Build(posX, posY, agentCount);

            var hashResults = new int[agentCount];
            var bruteResults = new int[agentCount];
            int totalNeighborsSeen = 0;

            for (int q = 0; q < 300; q++)
            {
                int self = (int)(DetHash.Hash(seed, 1, (ulong)q, 200) % agentCount);
                // Vary radius across queries, including radii larger than a cell.
                float radius = 4f + DetHash.Float01(seed, 1, (ulong)q, 201) * 28f;

                int nh = hash.QueryRadius(posX[self], posY[self], radius, self, posX, posY, hashResults);
                int nb = BruteForce(posX[self], posY[self], radius, self, posX, posY, agentCount, bruteResults);
                Array.Sort(bruteResults, 0, nb);

                Assert.That(nh, Is.EqualTo(nb), $"query {q}: count mismatch (dropped or phantom occupants)");
                for (int k = 0; k < nb; k++)
                {
                    Assert.That(hashResults[k], Is.EqualTo(bruteResults[k]), $"query {q}: member mismatch at {k}");
                }
                totalNeighborsSeen += nb;
            }

            // Guard against the test itself degenerating into empty queries.
            Assert.That(totalNeighborsSeen, Is.GreaterThan(1000), "queries were not dense enough to be meaningful");
        }

        [Test]
        public void OutOfBoundsPositions_ClampIntoEdgeCells_NotDropped()
        {
            var posX = new float[] { -5f, Simulation.WorldWidth + 5f, 10f };
            var posY = new float[] { -5f, Simulation.WorldHeight + 5f, 10f };
            var hash = new SpatialHash(Simulation.WorldWidth, Simulation.WorldHeight, 12f, 3);
            hash.Build(posX, posY, 3);

            var results = new int[3];
            int n = hash.QueryRadius(0f, 0f, 30f, -1, posX, posY, results);
            var found = new HashSet<int>();
            for (int i = 0; i < n; i++) found.Add(results[i]);
            Assert.That(found, Does.Contain(0), "out-of-bounds occupant was dropped");
            Assert.That(found, Does.Contain(2));
        }

        [Test]
        public void QueryResults_AreSortedAscending()
        {
            const int agentCount = 400;
            var posX = new float[agentCount];
            var posY = new float[agentCount];
            for (int i = 0; i < agentCount; i++)
            {
                posX[i] = DetHash.Float01(3UL, 0, (ulong)i, 300) * 100f;
                posY[i] = DetHash.Float01(3UL, 0, (ulong)i, 301) * 100f;
            }
            var hash = new SpatialHash(Simulation.WorldWidth, Simulation.WorldHeight, 12f, agentCount);
            hash.Build(posX, posY, agentCount);

            var results = new int[agentCount];
            int n = hash.QueryRadius(50f, 50f, 40f, -1, posX, posY, results);
            Assert.That(n, Is.GreaterThan(10));
            for (int i = 1; i < n; i++)
            {
                Assert.That(results[i], Is.GreaterThan(results[i - 1]), "canonical ascending order violated");
            }
        }
    }
}
