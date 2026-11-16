using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// The world select map: continents printed on the menu table's felt in a lighter tone (the felt is the sea),
/// built from coarse (longitude, latitude) outlines. Each world is a tournament in a city (Worlds.Theme.city).
/// </summary>
public static class WorldMap
{
    public const float LandHeight = 0.004f;

    // (lon, lat) outlines, a few points each: low poly on purpose. Antarctica, Baltic, Black and Caspian seas left out.
    public static readonly Vector2[][] Land =
    {
        // North and Central America
        P(-165, 68, -157, 71, -140, 70, -120, 74, -95, 72, -80, 70, -65, 62, -55, 52, -66, 44, -76, 36, -81, 30, -80, 25, -84, 30, -90, 29,
          -97, 26, -97, 21, -91, 19, -87, 21, -88, 15, -83, 9, -78, 8, -85, 11, -92, 15, -105, 20, -110, 24, -117, 32, -124, 40, -124, 48,
          -135, 58, -150, 60, -160, 58, -165, 62),
        // Greenland
        P(-45, 60, -22, 70, -18, 80, -40, 83, -65, 81, -58, 75, -52, 68),
        // Iceland
        P(-24, 64, -14, 64, -14, 66, -22, 66.5),
        // Cuba
        P(-85, 21.9, -80, 23.1, -74.2, 20.2, -77.5, 19.9, -81, 22),
        // South America
        P(-77, 8, -60, 10, -50, 0, -35, -5, -38, -14, -41, -22, -48, -26, -58, -38, -65, -42, -68, -55, -73, -50, -74, -40, -71, -30,
          -70, -18, -76, -14, -81, -5, -80, 1),
        // Great Britain, Ireland
        P(-5.5, 50, 1.5, 51, 1.7, 53, -1.5, 55.5, -2, 57.7, -5, 58.6, -6, 56.5, -4.8, 54.8, -3, 53.5, -4.8, 52, -3, 51.5),
        P(-10, 52, -6, 52, -6, 55, -8, 55.3, -10, 54),
        // Eurasia
        P(-9, 37, -9, 43, -2, 43.5, -1.5, 46.5, -4.5, 48.5, -1, 49.5, 2, 51, 5, 53.5, 8.5, 55, 8, 57.5, 5, 58.5, 5, 62, 13, 67, 20, 70,
          28, 71, 40, 67, 60, 69, 70, 73, 80, 73, 100, 77, 115, 73, 130, 71, 140, 72, 160, 70, 180, 68, 180, 65, 178, 62, 165, 60, 163, 56,
          156, 51, 155, 57, 142, 59, 137, 54, 141, 48, 135, 43, 129, 41, 129, 35, 126, 35, 126, 38, 122, 40, 119, 38, 122, 31, 120, 26,
          114, 22, 108, 21, 106, 17, 109, 12, 105, 9, 100, 13, 103, 1, 98, 8, 98, 16, 94, 18, 91, 22, 87, 21, 80, 15, 77, 8, 73, 17,
          72, 21, 67, 24, 59, 22.5, 55, 17, 52, 16, 45, 13, 43, 13, 39, 21, 35, 28, 34.5, 31.5, 35.5, 33, 36, 36.5, 30, 36.3, 27, 37,
          26.5, 39.5, 26, 40.5, 23, 40.5, 24, 38, 22, 36.5, 21, 38.5, 20, 40, 19.5, 42, 14, 45.5, 13, 44, 16, 41.5, 18.5, 40, 16, 38,
          15.5, 40, 12, 42, 9, 44, 4, 43, 3, 42, 0, 39, -2, 37, -6, 36),
        // Japan
        P(130, 31, 131, 34, 136, 36, 140, 40, 140, 45.5, 145.5, 43.5, 142, 42, 141.5, 38, 140.5, 35, 137, 34.5, 135, 33.5, 132, 33),
        // Sumatra, Borneo, New Guinea
        P(95, 5.5, 99, 3, 106, -3, 105.8, -6, 101, -3, 96, 3),
        P(109, 1, 114, 4.5, 119, 6, 118, 1, 116, -4, 111, -3, 109, -1),
        P(131, -1, 141, -2.5, 150, -10, 143, -9, 138, -8, 133, -4),
        // Africa, Madagascar
        P(-17, 21, -17, 15, -15, 11, -8, 4.5, -2, 5, 5, 6, 9, 4, 9.5, 1, 12, -5, 13, -12, 12, -17, 15, -27, 18, -34, 20, -35, 26, -34,
          32, -29, 35, -24, 40, -15, 40, -10, 39, -5, 42, 0, 51, 11, 43, 12, 39, 18, 35, 24, 32, 30, 29, 31, 20, 32, 19, 30, 11, 33,
          10, 37, 3, 36.8, -2, 35.5, -6, 36, -10, 30, -13, 27),
        P(44, -25, 47, -25, 50, -15, 49, -12, 44, -17),
        // Australia, New Zealand
        P(114, -22, 114, -34, 118, -35, 125, -32, 131, -31.5, 138, -35, 141, -38, 147, -38, 150, -37, 153, -30, 153, -25, 146, -19,
          142, -11, 141, -17, 136, -12, 131, -11, 126, -14, 122, -18),
        P(172, -34, 178, -38, 175, -41, 171, -46, 167, -46, 172, -40),
    };

    static Vector2[] P(params double[] v) => Enumerable.Range(0, v.Length / 2).Select(i => new Vector2((float)v[2 * i], (float)v[2 * i + 1])).ToArray();

    /// <summary>Where (lon, lat) falls on the regulation menu table (PoolTable.txt, felt from 0.5 to 17.5 × 0.5 to 9.5), north = far side.</summary>
    public static Vector3 Project(Vector2 lonLat) => new(9 + (lonLat.x - 5) / 21f, 0, 5 + (lonLat.y - 17) / 16f);

    /// <summary>The outlines on the table (x, z), rounded with two Chaikin passes, counter-clockwise.</summary>
    public static Vector2[][] Outlines() => Land.Select(outline =>
    {
        var pts = Smooth(outline, 2).Select(p => { var q = Project(p); return new Vector2(q.x, q.z); }).ToArray();
        if (Area(pts) < 0) System.Array.Reverse(pts);
        return pts;
    }).ToArray();

    // Chaikin corner cutting: each edge keeps its 1/4 and 3/4 points, so coasts come out rounded
    static Vector2[] Smooth(Vector2[] p, int times)
    {
        for (int k = 0; k < times; k++)
        {
            var q = new Vector2[p.Length * 2];
            for (int i = 0; i < p.Length; i++)
            {
                Vector2 a = p[i], b = p[(i + 1) % p.Length];
                q[2 * i] = Vector2.Lerp(a, b, 0.25f);
                q[2 * i + 1] = Vector2.Lerp(a, b, 0.75f);
            }
            p = q;
        }
        return p;
    }

    static Mesh mesh; // triangulated once per session

    /// <summary>The continents printed on the felt: one flat mesh just above it (no sides, so no outline), child of `parent`.</summary>
    public static GameObject Build(Transform parent, Color land)
    {
        if (!mesh)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (var pts in Outlines())
            {
                int s0 = verts.Count;
                verts.AddRange(pts.Select(V));
                foreach (int i in Triangulate(pts)) tris.Add(s0 + i);
            }
            mesh = new Mesh { name = "WorldMap", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetNormals(verts.Select(_ => Vector3.up).ToList());
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }
        var go = new GameObject("WorldMap", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = LevelBuilder.Mat("MapLand", land);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    static Vector3 V(Vector2 p) => new(p.x, LandHeight, p.y);

    /// <summary>Signed area (positive = counter-clockwise).</summary>
    public static float Area(IList<Vector2> p)
    {
        float a = 0;
        for (int i = 0; i < p.Count; i++) a += p[i].x * p[(i + 1) % p.Count].y - p[(i + 1) % p.Count].x * p[i].y;
        return a / 2;
    }

    /// <summary>Ear clipping of a simple counter-clockwise polygon: index triples, n - 2 triangles.</summary>
    public static List<int> Triangulate(IList<Vector2> p)
    {
        var idx = Enumerable.Range(0, p.Count).ToList();
        var tris = new List<int>();
        while (idx.Count > 3)
        {
            int ear = -1;
            for (int i = 0; i < idx.Count && ear < 0; i++)
            {
                Vector2 a = p[idx[(i + idx.Count - 1) % idx.Count]], b = p[idx[i]], c = p[idx[(i + 1) % idx.Count]];
                if (Cross(b - a, c - b) <= 0) continue; // reflex corner
                bool inside = false;
                foreach (int j in idx)
                    if (p[j] != a && p[j] != b && p[j] != c && Cross(b - a, p[j] - a) >= 0 && Cross(c - b, p[j] - b) >= 0 && Cross(a - c, p[j] - c) >= 0) { inside = true; break; }
                if (!inside) ear = i;
            }
            if (ear < 0) ear = 0; // a bad outline (self-crossing): clip anyway rather than loop forever (WorldMapTests catches it)
            tris.Add(idx[(ear + idx.Count - 1) % idx.Count]);
            tris.Add(idx[ear]);
            tris.Add(idx[(ear + 1) % idx.Count]);
            idx.RemoveAt(ear);
        }
        tris.AddRange(idx);
        return tris;
    }

    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
}
