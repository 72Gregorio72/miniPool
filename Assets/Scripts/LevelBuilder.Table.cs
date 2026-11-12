using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Table geometry: felt, rails, cushions and pockets, generated from the tile grid as seamless meshes.
// Each pocket defines an "opening" (funnel between the jaws + round hole); rails, felt and physics are all cut by
// the same openings, so the pieces always line up.
public static partial class LevelBuilder
{
    public const float RailHeight = 0.38f, CushionHeight = 0.32f, CushionDepth = 0.2f, FloorThickness = 0.2f, PocketDepth = 0.8f;

    // Regulation pockets (WPA, ball = 0.5): side mouth 5"–5.125" ≈ 2.3 balls, jaws 14° off square (104° facing);
    // corner mouth 4.5"–4.625" ≈ 2 balls (CornerMouth·√2 between the nose points), jaws 38° off the rail (142° facing).
    const float SideMouthHalf = 0.57f, SideJawDeg = 14f, SideHoleDepth = 0.52f, SideHoleRadius = 0.47f;
    const float CornerMouth = 0.71f, CornerJawDeg = 38f, CornerHoleOffset = 0.25f, CornerHoleRadius = 0.47f;
    const float OpenHoleRadius = 0.45f, FunnelFront = 0.3f, FunnelLength = 0.8f;

    public class Wall
    {
        public Vector2[] pts; // convex, counter-clockwise, x/z
        public Rect bounds;
        public float height = RailHeight; // balls flying higher than this pass over it (moving walls change it)
        public float bottom = float.NegativeInfinity; // balls whose top is below this pass under it (a module on a bridge's deck)
        public float kick;                // bumper: extra speed it gives on every hit
    }

    public class Pocket
    {
        public Vector2Int cell;
        public Vector2 center;
        public float radius;
    }

    class Opening
    {
        public Pocket pk;
        public List<Vector2> circle, funnel, funnelWood; // funnel = null: plain hole in the felt
        public readonly List<Nose> jaws = new();
    }

    // a cushion nose segment; n points from the felt into the rail; y = felt height under it
    struct Nose
    {
        public Vector2 a, b, n;
        public float y;
    }

    static readonly Vector2Int[] Dirs = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
    static readonly Vector2Int[] Diags = { new(1, 1), new(1, -1), new(-1, 1), new(-1, -1) };

    static void BuildTable(Level lv)
    {
        var t = lv.ground; // the table under the modules
        int w = t.GetLength(0), h = t.GetLength(1);
        var openings = new List<Opening>();
        for (int x = 0; x < w; x++)
            for (int z = 0; z < h; z++)
                if (At(t, new Vector2Int(x, z)) == 'O')
                {
                    var op = MakeOpening(t, new Vector2Int(x, z));
                    float py = lv.BaseY(op.pk.cell);
                    for (int j = 0; j < op.jaws.Count; j++) { var n = op.jaws[j]; n.y = py; op.jaws[j] = n; }
                    openings.Add(op);
                    lv.pockets.Add(op.pk);
                    Visual("PocketBottom", PrimitiveType.Cylinder, new Vector3(op.pk.center.x, py - PocketDepth, op.pk.center.y),
                        new Vector3(op.pk.radius * 2, 0.02f, op.pk.radius * 2), Quaternion.identity, Color.black, lv.root);
                }

        var felt = new MeshBuilder();
        var rubber = new MeshBuilder();
        var wood = new MeshBuilder();
        var liner = new MeshBuilder();
        var inlay = new MeshBuilder(); // sight diamonds on the rail tops
        var noses = new List<Nose>();
        var woodTops = new List<(TPoly p, float y, float skirt)>();

        // cushion nose a->b (n into the rail), minus the pocket openings
        void AddNose(Vector2 a, Vector2 b, Vector2 n, float y)
        {
            var keep = new List<Vector2> { new(0, 1) };
            foreach (var o in openings)
            {
                if (o.funnel != null) keep = Cut(keep, Poly.Inside(a, b, o.funnel));
                keep = Cut(keep, Poly.Inside(a, b, o.circle));
            }
            foreach (var k in keep) noses.Add(new Nose { a = Vector2.Lerp(a, b, k.x), b = Vector2.Lerp(a, b, k.y), n = n, y = y });
        }

        for (int x = 0; x < w; x++)
            for (int z = 0; z < h; z++)
            {
                var cell = new Vector2Int(x, z);
                char c = At(t, cell);
                if (IsVoid(c)) continue;
                if (c == Ramp) { RampMesh(lv, cell, felt); continue; }
                float y = lv.BaseY(cell);
                var op = openings.FirstOrDefault(o => o.pk.cell == cell);
                bool diag = c == DiagRail;
                var g = diag ? DiagDir(lv, cell) : Vector2.zero; // diagonal rail: unit vector toward its rail corner
                bool rail = c == '#' || diag || (op != null && op.funnel != null);
                // outer side: nothing there, or the neighbor is lower somewhere along this edge (a step down)
                Edge Side(Vector2Int d) => IsVoid(At(t, cell + d)) || EdgeMinY(lv, cell, d) < y - 1e-3f ? Edge.Outer : Edge.Internal;

                // felt everywhere on the table (hidden under the rails), with the holes cut out; a diagonal rail only has its felt
                // half, extended under the cushion up to the wood face (the cushion overhangs, the felt must show beneath it)
                var feltSquare = Poly.Square(cell, Side);
                var feltPieces = new List<TPoly> { diag ? Poly.Clip(feltSquare, cell + g * CushionDepth, -g, Edge.Internal) : feltSquare };
                foreach (var o in openings) feltPieces = Poly.Subtract(feltPieces, o.circle, Edge.Hole);
                foreach (var p in feltPieces)
                {
                    felt.Top(p, y);
                    EdgeWalls(p, Edge.Outer, rail && !diag ? null : felt, lv.SkirtY(cell), y);
                    EdgeWalls(p, Edge.Hole, liner, y - PocketDepth, y);
                }
                if (!rail)
                {
                    StepWalls(lv, cell, y);
                    continue;
                }

                // physics: the tile (the rail half for a diagonal) minus every opening
                var square = Poly.Square(cell, _ => Edge.Internal);
                var solid = new List<TPoly> { diag ? Poly.Clip(square, cell, g, Edge.Internal) : square };
                foreach (var o in openings)
                {
                    if (o.funnel != null) solid = Poly.Subtract(solid, o.funnel, Edge.Internal);
                    solid = Poly.Subtract(solid, o.circle, Edge.Internal);
                }
                foreach (var p in solid)
                {
                    var pts = p.pts.ToArray();
                    lv.walls.Add(new Wall { pts = pts, bounds = Rect.MinMaxRect(pts.Min(q => q.x), pts.Min(q => q.y), pts.Max(q => q.x), pts.Max(q => q.y)), height = y + RailHeight });
                }

                // wood: pulled back by the cushion depth from the felt (on the same floor) and from the jaws
                var top = Poly.Square(cell, Side);
                if (diag) top = Poly.Clip(top, cell + g * CushionDepth, g, Edge.WoodFace);
                // a diagonal rail only borders felt on its rail-side edges
                var feltSides = Dirs.Where(d => FeltToward(lv, cell, d) && (!diag || Vector2.Dot(d, g) > 0) && Mathf.Abs(lv.BaseY(cell + d) - y) < 1e-3f).ToList();
                foreach (var d in feltSides) top = Poly.Clip(top, (Vector2)cell + (Vector2)d * (0.5f - CushionDepth), -(Vector2)d, Edge.WoodFace);
                var woodPieces = top.IsEmpty ? new List<TPoly>() : new List<TPoly> { top };
                foreach (var o in openings)
                {
                    if (o.funnelWood != null) woodPieces = Poly.Subtract(woodPieces, o.funnelWood, Edge.WoodFace);
                    woodPieces = Poly.Subtract(woodPieces, o.circle, Edge.Hole);
                }
                foreach (var p in woodPieces) woodTops.Add((p, y, lv.SkirtY(cell)));

                // sight diamond on a straight rail every 2 tiles (even coordinate along it), none right beside a pocket
                if (!diag && op == null && feltSides.Count == 1)
                {
                    Vector2 dv = feltSides[0], along = new(-dv.y, dv.x);
                    bool even = (dv.x != 0 ? cell.y : cell.x) % 2 == 0;
                    bool byPocket = Dirs.Concat(Diags).Any(n => At(t, cell + n) == 'O');
                    if (even && !byPocket)
                    {
                        var m = (Vector2)cell - dv * (CushionDepth / 2);
                        float dy = y + RailHeight + 0.004f;
                        inlay.Face(new[] { V(m + dv * 0.09f, dy), V(m + along * 0.045f, dy), V(m - dv * 0.09f, dy), V(m - along * 0.045f, dy) }, Vector3.up);
                    }
                }

                // cushion noses along the felt-facing sides (and the diagonal)
                foreach (var d in feltSides)
                {
                    Vector2 dv = d, along = new(-dv.y, dv.x);
                    var a = (Vector2)cell + dv * 0.5f - along * 0.5f;
                    AddNose(a, a + along, -dv, y);
                }
                if (diag)
                {
                    var half = new Vector2(-g.y, g.x) * Mathf.Sqrt(0.5f); // center to the corners on the diagonal
                    AddNose((Vector2)cell - half, (Vector2)cell + half, g, y);
                }
            }

        foreach (var o in openings) noses.AddRange(o.jaws);
        var cushionTops = new List<(List<Vector2> pts, float y)>();
        Cushions(noses, rubber, cushionTops);

        // wood never covers a cushion: the mitered corner of two cushions reaches into the rail tile beside them
        foreach (var (p0, y, skirt) in woodTops)
        {
            var pieces = new List<TPoly> { p0 };
            foreach (var ct in cushionTops)
                if (Mathf.Abs(ct.y - y) < 1e-3f) pieces = Poly.Subtract(pieces, ct.pts, Edge.WoodFace);
            foreach (var p in pieces)
            {
                wood.Top(p, y + RailHeight);
                EdgeWalls(p, Edge.Outer, wood, skirt, y + RailHeight);
                EdgeWalls(p, Edge.WoodFace, wood, y, y + RailHeight);
                EdgeWalls(p, Edge.Hole, liner, y - PocketDepth, y + RailHeight);
            }
        }

        felt.Build("Felt", Mat(Felt), lv.root);
        rubber.Build("Cushions", Mat(CushionColor), lv.root);
        wood.Build("Rails", Mat(Wood), lv.root);
        liner.Build("PocketLiners", Mat(new Color(0.02f, 0.02f, 0.02f)), lv.root);
        inlay.Build("Diamonds", Mat(new Color(0.95f, 0.92f, 0.82f)), lv.root);
    }

    // diagonal rail: unit vector from its center toward the rail corner (angle 0 = far-right corner, multiples of 90)
    static Vector2 DiagDir(Level lv, Vector2Int cell)
    {
        var d = AngleDir(Mathf.Round(lv.AngleAt(cell) / 90) * 90 + 45);
        return new Vector2(d.x, d.z);
    }

    // the neighbor in direction d shows felt along the shared edge (a diagonal rail only on its felt half)
    static bool FeltToward(Level lv, Vector2Int cell, Vector2Int d)
    {
        char n = At(lv.ground, cell + d);
        return IsFloor(n) || (n == DiagRail && Vector2.Dot(d, DiagDir(lv, cell + d)) > 0);
    }

    // lowest point of the neighbor's surface along the edge shared with `cell` in direction d
    static float EdgeMinY(Level lv, Vector2Int cell, Vector2Int d)
    {
        Vector2 mid = (Vector2)cell + (Vector2)d * 0.5f, along = new Vector2(-d.y, d.x) * 0.5f;
        return Mathf.Min(lv.SurfaceY(cell + d, mid - along), lv.SurfaceY(cell + d, mid + along));
    }

    // physics of a step down: a thin wall along each edge of this flat cell that has a lower neighbor. A ball down there
    // bounces off it; a ball rolling off the top doesn't touch it (its height is well below the top, see the drop in PoolPhysics).
    static void StepWalls(Level lv, Vector2Int cell, float y)
    {
        foreach (var d in Dirs)
        {
            if (IsVoid(At(lv.ground, cell + d)) || EdgeMinY(lv, cell, d) >= y - 1e-3f) continue;
            Vector2 dv = d, along = new(-dv.y, dv.x), mid = (Vector2)cell + dv * 0.5f;
            var pts = Poly.CCW(new List<Vector2> { mid - along * 0.5f, mid + along * 0.5f, mid + along * 0.5f - dv * 0.06f, mid - along * 0.5f - dv * 0.06f }).ToArray();
            lv.walls.Add(new Wall { pts = pts, bounds = Rect.MinMaxRect(pts.Min(q => q.x), pts.Min(q => q.y), pts.Max(q => q.x), pts.Max(q => q.y)), height = y - FloorStep * 0.6f });
        }
    }

    // ramp: sloped felt from its floor up one FloorStep toward its angle, with sides down to the table base where needed
    // shortcut: ramps have no step walls, a ball beside a ramp's low side climbs onto it; add walls if levels need it
    static void RampMesh(Level lv, Vector2Int cell, MeshBuilder felt)
    {
        Vector3 C(float dx, float dz) { var q = new Vector2(cell.x + dx, cell.y + dz); return V(q, lv.SurfaceY(cell, q)); }
        felt.Face(new[] { C(-0.5f, -0.5f), C(0.5f, -0.5f), C(0.5f, 0.5f), C(-0.5f, 0.5f) }, Vector3.up);
        foreach (var d in Dirs)
        {
            Vector2 dv = d, along = new Vector2(-dv.y, dv.x) * 0.5f, mid = (Vector2)cell + dv * 0.5f;
            Vector2 a = mid - along, b = mid + along;
            float ya = lv.SurfaceY(cell, a), yb = lv.SurfaceY(cell, b);
            bool open = IsVoid(At(lv.ground, cell + d)) || Mathf.Min(lv.SurfaceY(cell + d, a), lv.SurfaceY(cell + d, b)) < Mathf.Max(ya, yb) - 1e-3f;
            if (open) felt.Face(new[] { V(a, lv.SkirtY(cell)), V(a, ya), V(b, yb), V(b, lv.SkirtY(cell)) }, new Vector3(dv.x, 0, dv.y));
        }
    }

    static void EdgeWalls(TPoly p, Edge tag, MeshBuilder mb, float y0, float y1)
    {
        if (mb == null) return;
        for (int i = 0; i < p.pts.Count; i++)
            if (p.tags[i] == tag) mb.Wall(p.pts[i], p.pts[(i + 1) % p.pts.Count], y0, y1);
    }

    // removes the [t0, t1] range from a list of kept ranges
    static List<Vector2> Cut(List<Vector2> keep, Vector2 cut)
    {
        if (cut.x >= cut.y - 1e-6f) return keep;
        var res = new List<Vector2>();
        foreach (var k in keep)
        {
            if (cut.x > k.x + 1e-4f) res.Add(new Vector2(k.x, Mathf.Min(k.y, cut.x)));
            if (cut.y < k.y - 1e-4f) res.Add(new Vector2(Mathf.Max(k.x, cut.y), k.y));
        }
        return res.Where(k => k.y - k.x > 1e-4f).ToList();
    }

    static Opening MakeOpening(char[,] t, Vector2Int cell)
    {
        var op = new Opening { pk = new Pocket { cell = cell, center = cell, radius = OpenHoleRadius } };
        var feltDirs = Dirs.Where(d => IsFloor(At(t, cell + d))).ToList();
        var corner = Diags.FirstOrDefault(d => IsFloor(At(t, cell + d)));

        if (feltDirs.Count == 1) // side pocket
        {
            Vector2 f = feltDirs[0], u = new(-f.y, f.x), nose = (Vector2)cell + f * 0.5f;
            Vector2 P(float across, float depth) => nose + u * across - f * depth;
            float tan = Mathf.Tan(SideJawDeg * Mathf.Deg2Rad);
            List<Vector2> Funnel(float half) => Poly.CCW(new List<Vector2>
            {
                P(-(half + FunnelFront * tan), -FunnelFront), P(half + FunnelFront * tan, -FunnelFront),
                P(half - FunnelLength * tan, FunnelLength), P(-(half - FunnelLength * tan), FunnelLength),
            });
            op.pk.center = P(0, SideHoleDepth);
            op.pk.radius = SideHoleRadius;
            op.funnel = Funnel(SideMouthHalf);
            op.funnelWood = Funnel(SideMouthHalf + CushionDepth / Mathf.Cos(SideJawDeg * Mathf.Deg2Rad));
            foreach (var s in new[] { -1, 1 })
                AddJaw(op, P(s * SideMouthHalf, 0), (P(s * (SideMouthHalf - tan), 1) - P(s * SideMouthHalf, 0)).normalized);
        }
        else if (feltDirs.Count == 0 && corner != Vector2Int.zero) // corner pocket
        {
            Vector2 g = corner, k = (Vector2)cell + g * 0.5f;
            float cos = Mathf.Cos(CornerJawDeg * Mathf.Deg2Rad), sin = Mathf.Sin(CornerJawDeg * Mathf.Deg2Rad);
            var mouthA = k + new Vector2(g.x * CornerMouth, 0);
            var mouthB = k + new Vector2(0, g.y * CornerMouth);
            var dirA = new Vector2(-g.x * cos, -g.y * sin);
            var dirB = new Vector2(-g.x * sin, -g.y * cos);
            op.pk.center = k - g * CornerHoleOffset;
            op.pk.radius = CornerHoleRadius;
            var mid = (mouthA + mouthB) / 2 - g * 0.3f; // a point inside the funnel
            Vector2 Out(Vector2 m, Vector2 dir) { var n = new Vector2(-dir.y, dir.x); return Vector2.Dot(n, mid - m) > 0 ? -n : n; }
            List<Vector2> Funnel(float offset)
            {
                Vector2 a = mouthA + Out(mouthA, dirA) * offset, b = mouthB + Out(mouthB, dirB) * offset;
                return Poly.CCW(new List<Vector2> { a - dirA * FunnelFront, b - dirB * FunnelFront, b + dirB * FunnelLength, a + dirA * FunnelLength });
            }
            op.funnel = Funnel(0);
            op.funnelWood = Funnel(CushionDepth);
            AddJaw(op, mouthA, dirA);
            AddJaw(op, mouthB, dirB);
        }
        op.circle = Poly.Circle(op.pk.center, op.pk.radius);
        return op;
    }

    // jaw cushion from the mouth point along `dir` until it reaches the hole
    static void AddJaw(Opening op, Vector2 mouth, Vector2 dir)
    {
        var oc = mouth - op.pk.center;
        float bq = Vector2.Dot(oc, dir), cq = oc.sqrMagnitude - op.pk.radius * op.pk.radius, disc = bq * bq - cq;
        float len = disc > 0 ? -bq - Mathf.Sqrt(disc) : FunnelLength;
        var n = new Vector2(-dir.y, dir.x);
        if (Vector2.Dot(n, op.pk.center - mouth) > 0) n = -n; // away from the hole axis, into the rail
        op.jaws.Add(new Nose { a = mouth, b = mouth + dir * len, n = n });
    }

    // Cushion cross-section is a right triangle: one leg against the rail, one leg on top, hypotenuse facing the table.
    // tops: each cushion's footprint (convex, counter-clockwise) and its felt height
    static void Cushions(List<Nose> noses, MeshBuilder mb, List<(List<Vector2> pts, float y)> tops)
    {
        foreach (var s in noses)
        {
            var dir = (s.b - s.a).normalized;
            var ia = Inner(noses, s, s.a, dir, out bool freeA);
            var ib = Inner(noses, s, s.b, dir, out bool freeB);
            tops.Add((Poly.CCW(new List<Vector2> { s.a, s.b, ib, ia }), s.y));
            float top = s.y + CushionHeight;
            Vector3 A = V(s.a, top), B = V(s.b, top), Ai = V(ia, top), Bi = V(ib, top), A0 = V(ia, s.y), B0 = V(ib, s.y);
            var toFelt = new Vector3(-s.n.x, 0, -s.n.y);
            mb.Face(new[] { A, B, Bi, Ai }, Vector3.up);
            mb.Face(new[] { A, B, B0, A0 }, toFelt + Vector3.down);
            if (freeA) mb.Face(new[] { A, Ai, A0 }, V(-dir, 0));
            if (freeB) mb.Face(new[] { B, Bi, B0 }, V(dir, 0));
        }
    }

    static Vector3 V(Vector2 p, float y) => new(p.x, y, p.y);

    // inner (rail-side) end of a nose at endpoint p: mitered with a nose meeting it at an angle, square otherwise
    static Vector2 Inner(List<Nose> noses, Nose s, Vector2 p, Vector2 dir, out bool free)
    {
        free = true;
        foreach (var o in noses)
        {
            if (o.a == s.a && o.b == s.b) continue;
            if ((o.a - p).sqrMagnitude > 1e-6f && (o.b - p).sqrMagnitude > 1e-6f) continue;
            free = false;
            var odir = (o.b - o.a).normalized;
            if (Mathf.Abs(Poly.Cross(dir, odir)) > 1e-3f)
                return Poly.Intersect(p + s.n * CushionDepth, dir, p + o.n * CushionDepth, odir);
        }
        return p + s.n * CushionDepth;
    }
}
