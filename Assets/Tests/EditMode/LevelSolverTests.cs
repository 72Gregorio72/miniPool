using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// Proves a level can be finished: searches shot sequences with the real physics (beam search, cue level, no spin) until the 8 is potted
// without a foul, and keeps a solution only if it still works with every shot 10% softer or harder (no lucky shots: the aim line makes
// the angle precise, the power is pulled by hand).
// Minutes per level, so it runs only when Temp/solve exists, holding the level numbers ("16 17 18");
// solutions are appended to test-results/solutions.txt and replayed by LevelSolutionsTests.
// Run: echo "16" > Temp/solve; bash .claude/scripts/unity-bridge.sh test:LevelSolverTests 3600
public class LevelSolverTests
{
    public struct Shot
    {
        public float angle, speed, wait; // degrees clockwise from the far side, units/s, seconds waited before shooting (timed walls, platforms)
        public override string ToString() => $"{angle} {speed} {wait}";
        public static Shot Parse(string s) { var p = s.Split(' ').Select(float.Parse).ToArray(); return new Shot { angle = p[0], speed = p[1], wait = p[2] }; }
    }

    const string Flag = "Temp/solve";
    static int target = 8; // the ball to pot in the level being solved (Worlds.BallOf)
    static readonly float[] Speeds = { 5, 9, 15, 24, 36 }; // 36: room for +10% under the cue's 40
    const int AngleStep = 2, Beam = 3;

    [Test, Timeout(4 * 3600 * 1000)]
    public void ListedLevels_HaveRobustSolution()
    {
        if (!File.Exists(Flag)) Assert.Ignore("solo con Temp/solve (numeri dei livelli)");
        var failed = new List<string>();
        foreach (var n in File.ReadAllText(Flag).Split(new[] { ' ', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries).Select(int.Parse))
        {
            var name = $"level{n:00}";
            var text = Resources.Load<TextAsset>("Levels/" + name).text;
            int par = LevelBuilder.ParOf(text);
            target = Worlds.BallOf(n);
            var plan = Find(text, Mathf.Max(par, 1) + 1, out string note);
            File.AppendAllText("test-results/solutions.txt", plan != null
                ? $"{name} par {par}: {plan.Count} tiri: {string.Join(" | ", plan)}\n" : $"{name}: NESSUNA SOLUZIONE ({note})\n");
            if (plan == null) failed.Add($"{name}: {note}");
        }
        Assert.IsEmpty(failed, string.Join("\n", failed));
    }

    // debugging a level: Temp/trace holds lines "26 | 90 9 2 | 0 15 0"; writes where every ball is after each shot to test-results/trace.txt
    [Test]
    public void Trace()
    {
        if (!File.Exists("Temp/trace")) Assert.Ignore("solo con Temp/trace");
        var sb = new System.Text.StringBuilder();
        foreach (var line in File.ReadAllLines("Temp/trace").Where(l => l.Trim().Length > 0))
        {
            var parts = line.Split('|').Select(s => s.Trim()).ToArray();
            target = Worlds.BallOf(int.Parse(parts[0]));
            var lv = LevelBuilder.Build(Resources.Load<TextAsset>($"Levels/level{int.Parse(parts[0]):00}").text, "trace");
            sb.Append(line).Append('\n');
            foreach (var s in parts.Skip(1))
            {
                var shot = Shot.Parse(s);
                for (float t = 0; t < shot.wait; t += 0.05f) PoolPhysics.Simulate(lv, 0.05f);
                shot.wait = 0;
                sb.Append($"  after wait t={lv.time:F2}: ").Append(string.Join(" ", lv.balls.Select(b => $"{b.number}@{b.transform.position.x:F2},{b.transform.position.y:F2},{b.transform.position.z:F2}")))
                  .Append(" platforms ").Append(string.Join(" ", lv.platforms.Select(p => $"{p.center} range {p.range}"))).Append('\n');
                var (restart, win) = Play(lv, shot);
                sb.Append($"  {s}: restart={restart} win={win} t={lv.time:F1} | ")
                  .Append(string.Join(" ", lv.balls.Select(b => $"{b.number}@{b.transform.position.x:F2},{b.transform.position.z:F2}"))).Append('\n');
            }
            Object.DestroyImmediate(lv.root.gameObject);
        }
        File.WriteAllText("test-results/trace.txt", sb.ToString());
    }

    // beam search over shot sequences up to maxShots; null if none found (note = how close it got)
    public static List<Shot> Find(string text, int maxShots, out string note)
    {
        var lv = LevelBuilder.Build(text, "solve");
        try
        {
            bool timed = text.Contains('L') || text.Contains('H');
            float[] waits = timed ? new[] { 0f, 1, 2, 3 } : new[] { 0f };
            var shots = (from w in waits from s in Speeds from a in Enumerable.Range(0, 360 / AngleStep)
                         select new Shot { angle = a * AngleStep, speed = s, wait = w }).ToList();
            var start = new Snapshot(lv);
            var beam = new List<(Snapshot s, List<Shot> path)> { (start, new List<Shot>()) };
            float best = float.MaxValue;
            for (int depth = 0; depth < maxShots; depth++)
            {
                var next = new List<(Snapshot s, List<Shot> path, float score)>();
                foreach (var (state, path) in beam)
                    foreach (var shot in shots)
                    {
                        state.Restore(lv);
                        var (restart, win) = Play(lv, shot);
                        if (restart) continue;
                        var p = new List<Shot>(path) { shot };
                        if (win && Robust(lv, start, p)) { note = null; return p; }
                        if (!win) next.Add((new Snapshot(lv), p, Distance8(lv)));
                    }
                // the most promising states, not near-copies of each other
                beam = next.OrderBy(c => c.score).GroupBy(c => c.s.Key).Select(g => g.First()).Take(Beam).Select(c => (c.s, c.path)).ToList();
                if (beam.Count == 0) break;
                best = Mathf.Min(best, next.Min(c => c.score));
            }
            note = $"palla {target} a {best:F1} caselle dalla buca nel migliore dei casi";
            return null;
        }
        finally { Object.DestroyImmediate(lv.root.gameObject); }
    }

    // every shot 10% softer or harder still leads to a win (the last one) or at least no foul (the others)
    static bool Robust(LevelBuilder.Level lv, Snapshot start, List<Shot> plan)
    {
        for (int i = 0; i < plan.Count; i++)
            foreach (var f in new[] { 0.9f, 1.1f })
            {
                start.Restore(lv);
                bool ok = true, won = false;
                for (int k = 0; k < plan.Count && ok; k++)
                {
                    var s = plan[k];
                    if (k == i) s.speed *= f;
                    var (restart, win) = Play(lv, s);
                    ok = !restart;
                    won = win;
                }
                if (!ok || !won) return false;
            }
        return true;
    }

    /// <summary>One shot as GameManager plays it: wait, shoot, simulate until everything stops, then Rules.Evaluate.</summary>
    public static (bool restart, bool win) Play(LevelBuilder.Level lv, Shot shot)
    {
        for (float t = 0; t < shot.wait; t += 0.05f) PoolPhysics.Simulate(lv, 0.05f);
        var cue = lv.balls.First(b => b.number == 0);
        int first = -1;
        cue.onCueHit = b => { if (first < 0) first = b.number; };
        var rad = shot.angle * Mathf.Deg2Rad;
        PoolPhysics.Shoot(cue, new Vector3(Mathf.Sin(rad), 0, Mathf.Cos(rad)), shot.speed, 0, 0);
        var potted = new List<int>();
        var lost = new List<int>();
        for (float t = 0; t < 30; t += 0.05f)
        {
            PoolPhysics.Simulate(lv, 0.05f);
            foreach (var b in lv.balls.Where(b => b.transform.position.y < -0.6f).ToList())
            {
                (b.pocket != null ? potted : lost).Add(b.number);
                lv.balls.Remove(b); // as GameManager: gone from the level
            }
            if (t >= 0.2f && !lv.balls.Any(b => b.IsMoving)) break;
        }
        cue.onCueHit = null;
        var o = Rules.Evaluate(first, potted, lost, target: target);
        return (o.restart, o.win);
    }

    // how far the 8 is from a pocket, in tiles through the table (rails, blocks and closed gates in the way; voids cost more; portals join),
    // plus a little for how far the cue is from the 8: a shot that opens the gates in front of the cue is progress too
    static float Distance8(LevelBuilder.Level lv)
    {
        var black = lv.balls.FirstOrDefault(b => b.number == target);
        var cue = lv.balls.FirstOrDefault(b => b.number == 0);
        if (!black || !cue) return float.MaxValue;
        var toPocket = Paths(lv, lv.pockets.Select(p => p.cell));
        var toBlack = Paths(lv, new[] { LevelBuilder.Level.CellOf(black.transform.position) });
        float At(Dictionary<Vector2Int, float> d, Ball b) => d.TryGetValue(LevelBuilder.Level.CellOf(b.transform.position), out var r) ? r : 1000;
        return At(toPocket, black) + 0.01f * At(toBlack, cue);
    }

    static Dictionary<Vector2Int, float> Paths(LevelBuilder.Level lv, IEnumerable<Vector2Int> from)
    {
        int w = lv.tiles.GetLength(0), h = lv.tiles.GetLength(1);
        string walls = lv.movers.Any(m => !m.timed && !m.up) ? "#P" : "#PG";
        char Tile(Vector2Int c) => c.x < 0 || c.y < 0 || c.x >= w || c.y >= h ? '#' : lv.TileAt(new Vector3(c.x, 0, c.y));
        var dist = new Dictionary<Vector2Int, float>();
        var open = new SortedSet<(float d, int x, int y)>();
        foreach (var c in from) { dist[c] = 0; open.Add((0, c.x, c.y)); }
        while (open.Count > 0)
        {
            var (d, x, y) = open.Min;
            open.Remove(open.Min);
            var c = new Vector2Int(x, y);
            var near = new List<Vector2Int> { c + Vector2Int.up, c + Vector2Int.down, c + Vector2Int.left, c + Vector2Int.right };
            if (lv.portalBlue == c && lv.portalOrange.HasValue) near.Add(lv.portalOrange.Value);
            if (lv.portalOrange == c && lv.portalBlue.HasValue) near.Add(lv.portalBlue.Value);
            foreach (var n in near)
            {
                char t = Tile(n);
                if (walls.IndexOf(t) >= 0) continue;
                float nd = d + (t == ' ' ? 4 : 1);
                if (dist.TryGetValue(n, out var old) && old <= nd) continue;
                dist[n] = nd;
                open.Add((nd, n.x, n.y));
            }
        }
        return dist;
    }

    // everything a shot changes: balls (all fields PoolPhysics.Predict saves, plus which are still on the table) and the clock
    class Snapshot
    {
        readonly List<Ball> balls;
        readonly (Vector3 p, Vector3 v, Vector3 w, bool falling, bool airborne, Vector2Int? cell, bool armed, LevelBuilder.Pocket pocket, float belt, bool onBelt, bool carried)[] state;
        readonly float time;
        public readonly string Key;

        public Snapshot(LevelBuilder.Level lv)
        {
            balls = new List<Ball>(lv.balls);
            state = balls.Select(b => (b.transform.position, b.v, b.w, b.falling, b.airborne, b.cell, b.boostArmed, b.pocket, b.beltSpeed, b.onBelt, b.carried)).ToArray();
            time = lv.time;
            Key = string.Join(";", balls.Select(b => $"{b.number}:{Mathf.RoundToInt(b.transform.position.x * 2)},{Mathf.RoundToInt(b.transform.position.z * 2)}"));
        }

        public void Restore(LevelBuilder.Level lv)
        {
            lv.balls.Clear();
            lv.balls.AddRange(balls);
            for (int i = 0; i < balls.Count; i++)
            {
                var b = balls[i];
                var s = state[i];
                b.transform.position = s.p;
                (b.v, b.w, b.falling, b.airborne, b.cell, b.boostArmed, b.pocket, b.beltSpeed, b.onBelt, b.carried) =
                    (s.v, s.w, s.falling, s.airborne, s.cell, s.armed, s.pocket, s.belt, s.onBelt, s.carried);
            }
            lv.time = time;
            PoolPhysics.UpdateMoving(lv);
        }
    }
}
