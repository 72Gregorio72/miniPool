using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// What a polygon edge borders, so the mesh builder knows which side faces to emit.
public enum Edge { Internal, Outer, Hole, WoodFace }

// Convex polygon on the table plane (Vector2 = world x, z), counter-clockwise; tags[i] describes edge pts[i] -> pts[i+1].
public class TPoly
{
    public readonly List<Vector2> pts = new();
    public readonly List<Edge> tags = new();

    public void Add(Vector2 p, Edge e) { pts.Add(p); tags.Add(e); }

    public float Area
    {
        get
        {
            float a = 0;
            for (int i = 0; i < pts.Count; i++) a += Poly.Cross(pts[i], pts[(i + 1) % pts.Count]);
            return a / 2;
        }
    }

    public bool IsEmpty => pts.Count < 3 || Area < 1e-6f;
}

public static class Poly
{
    public static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    // unit square around c; side(d) tags the edge facing direction d
    public static TPoly Square(Vector2 c, System.Func<Vector2Int, Edge> side)
    {
        var p = new TPoly();
        p.Add(c + new Vector2(-0.5f, -0.5f), side(Vector2Int.down));
        p.Add(c + new Vector2(0.5f, -0.5f), side(Vector2Int.right));
        p.Add(c + new Vector2(0.5f, 0.5f), side(Vector2Int.up));
        p.Add(c + new Vector2(-0.5f, 0.5f), side(Vector2Int.left));
        return p;
    }

    public static List<Vector2> Circle(Vector2 c, float r, int segments = 32) =>
        Enumerable.Range(0, segments).Select(i => c + r * new Vector2(Mathf.Cos(i * Mathf.PI * 2 / segments), Mathf.Sin(i * Mathf.PI * 2 / segments))).ToList();

    public static List<Vector2> CCW(List<Vector2> p)
    {
        float a = 0;
        for (int i = 0; i < p.Count; i++) a += Cross(p[i], p[(i + 1) % p.Count]);
        if (a < 0) p.Reverse();
        return p;
    }

    public static Vector2 Intersect(Vector2 p, Vector2 r, Vector2 q, Vector2 s) => p + r * (Cross(q - p, s) / Cross(r, s));

    // keeps the part where Dot(x - point, normal) >= 0; the new edge on the cut line gets `cutTag`
    public static TPoly Clip(TPoly poly, Vector2 point, Vector2 normal, Edge cutTag)
    {
        var res = new TPoly();
        int n = poly.pts.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = poly.pts[i], b = poly.pts[(i + 1) % n];
            float da = Vector2.Dot(a - point, normal), db = Vector2.Dot(b - point, normal);
            if (da >= 0) res.Add(a, poly.tags[i]);
            if (da >= 0 != db >= 0) res.Add(a + (b - a) * (da / (da - db)), da >= 0 ? cutTag : poly.tags[i]);
        }
        return res;
    }

    // s minus convex counter-clockwise `hole`, as convex pieces. Edges along the hole get `holeTag`.
    public static List<TPoly> Subtract(TPoly s, List<Vector2> hole, Edge holeTag)
    {
        var res = new List<TPoly>();
        if (!Overlap(s.pts, hole)) { res.Add(s); return res; }
        var rest = s;
        for (int i = 0; i < hole.Count; i++)
        {
            Vector2 p = hole[i], e = hole[(i + 1) % hole.Count] - p;
            var inward = new Vector2(-e.y, e.x).normalized;
            var outside = Clip(rest, p, -inward, holeTag);
            if (!outside.IsEmpty) res.Add(outside);
            rest = Clip(rest, p, inward, Edge.Internal);
            if (rest.IsEmpty) break;
        }
        return res; // what is left in `rest` is inside the hole
    }

    public static List<TPoly> Subtract(List<TPoly> pieces, List<Vector2> hole, Edge holeTag) =>
        pieces.SelectMany(p => Subtract(p, hole, holeTag)).ToList();

    static bool Overlap(List<Vector2> a, List<Vector2> b) =>
        a.Max(p => p.x) > b.Min(p => p.x) && b.Max(p => p.x) > a.Min(p => p.x) &&
        a.Max(p => p.y) > b.Min(p => p.y) && b.Max(p => p.y) > a.Min(p => p.y);

    // Parameter range [t0, t1] of segment a->b inside a convex CCW polygon (empty if t0 >= t1).
    public static Vector2 Inside(Vector2 a, Vector2 b, List<Vector2> poly)
    {
        float t0 = 0, t1 = 1;
        for (int i = 0; i < poly.Count; i++)
        {
            Vector2 p = poly[i], e = poly[(i + 1) % poly.Count] - p;
            var inward = new Vector2(-e.y, e.x);
            float num = Vector2.Dot(a - p, inward), den = Vector2.Dot(b - a, inward);
            if (Mathf.Abs(den) < 1e-9f) { if (num < 0) return new Vector2(1, 0); continue; }
            float t = -num / den;
            if (den > 0) t0 = Mathf.Max(t0, t); else t1 = Mathf.Min(t1, t);
        }
        return new Vector2(t0, t1);
    }

    public static Vector2 Closest(Vector2[] poly, Vector2 p, out bool inside)
    {
        inside = true;
        var best = poly[0];
        float bestD = float.MaxValue;
        for (int i = 0; i < poly.Length; i++)
        {
            Vector2 a = poly[i], e = poly[(i + 1) % poly.Length] - a;
            var q = a + e * Mathf.Clamp01(Vector2.Dot(p - a, e) / e.sqrMagnitude);
            float d = (p - q).sqrMagnitude;
            if (d < bestD) { bestD = d; best = q; }
            if (Cross(e, p - a) < 0) inside = false;
        }
        return best;
    }
}

// Collects flat-shaded geometry into one mesh, so adjacent tiles have no seams.
public class MeshBuilder
{
    readonly List<Vector3> verts = new();
    readonly List<int> tris = new();

    static Vector3 V(Vector2 p, float y) => new(p.x, y, p.y);

    // convex planar face, wound so it is visible from the `facing` side
    public void Face(Vector3[] p, Vector3 facing)
    {
        // whole-polygon normal: a clip through a corner can leave two near-equal points, so the first triangle alone may flip
        var normal = Vector3.zero;
        for (int i = 1; i < p.Length - 1; i++) normal += Vector3.Cross(p[i] - p[0], p[i + 1] - p[0]);
        if (Vector3.Dot(normal, facing) < 0) System.Array.Reverse(p);
        int s = verts.Count;
        verts.AddRange(p);
        for (int i = 1; i < p.Length - 1; i++) { tris.Add(s); tris.Add(s + i); tris.Add(s + i + 1); }
    }

    // vertical wall along a->b, visible from the right of a->b (outside of a counter-clockwise polygon)
    public void Wall(Vector2 a, Vector2 b, float y0, float y1)
    {
        var e = b - a;
        Face(new[] { V(a, y0), V(a, y1), V(b, y1), V(b, y0) }, new Vector3(e.y, 0, -e.x));
    }

    public void Top(TPoly p, float y) => Face(p.pts.Select(q => V(q, y)).ToArray(), Vector3.up);

    public void Build(string name, Material mat, Transform parent)
    {
        if (verts.Count == 0) return;
        var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }
}
