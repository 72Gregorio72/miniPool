using System.Linq;
using NUnit.Framework;
using UnityEngine;

public class WorldMapTests
{
    // every outline is simple: its ear clipping covers exactly its area (a self-crossing outline would not)
    [Test] public void ContinentOutlines_TriangulateToTheirArea()
    {
        foreach (var p in WorldMap.Outlines())
        {
            var t = WorldMap.Triangulate(p);
            Assert.AreEqual((p.Length - 2) * 3, t.Count);
            float sum = 0;
            for (int i = 0; i < t.Count; i += 3) sum += WorldMap.Area(new[] { p[t[i]], p[t[i + 1]], p[t[i + 2]] });
            Assert.AreEqual(WorldMap.Area(p), sum, 1e-2f, $"outline starting at {p[0]}");
        }
    }

    // every tournament city and its ball sit on the felt of the regulation table
    [Test] public void TournamentPins_SitOnTheFelt()
    {
        foreach (var t in Worlds.Themes)
        {
            var q = WorldMap.Project(t.city);
            Assert.That(q.x is > 0.9f and < 17.1f && q.z is > 0.9f and < 9.1f, t.name);
        }
    }
}
