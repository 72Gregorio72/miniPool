using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Builds a level from an ASCII grid (format documented in CLAUDE.md). Table geometry lives in LevelBuilder.Table.cs.
public static partial class LevelBuilder
{
    public const float TableHeight = 5.3f; // 76 cm at 1/7 scale
    const string BallChars = "0123456789ABCDEF*"; // * = grey ball (BallArt.Grey)

    // One floor of a level (a "sheet"): a flat table of its own at y = floor * FloorStep, with its own tiles, walls and
    // modules. Floors don't know each other: the same (x, z) can hold something on every floor. The game holds the
    // lowest one (`main`), which shares its balls, root and clock with the others; a ball is on Sheet(its position).
    public class Level
    {
        public int floor;
        public Level main;         // the lowest sheet: the Level the game holds
        public List<Level> sheets; // main only: every floor's sheet, bottom to top (main first)
        public HashSet<Vector2Int> columns = new(); // this floor's cells that are columns down to what's under them (ColumnTag, SolidTag)

        // the floor a ball at p is on: the highest one at or under it that has table (or a platform) there
        public Level Sheet(Vector3 p)
        {
            var all = main.sheets;
            if (all.Count == 1) return main;
            float bottom = p.y - Ball.Radius + FloorStep / 2;
            Level open = null;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var s = all[i];
                if (s.floor * FloorStep > bottom) continue;
                if (!IsVoid(At(s.ground, CellOf(p))) || s.PlatformAt(p) != null) return s;
                open ??= s;
            }
            return open ?? all[0];
        }

        // how far down the sides of the table on cell c go: a slab, or (a column) down to what's under it
        public float SkirtY(Vector2Int c)
        {
            if (!columns.Contains(c)) return BaseY(c) - FloorThickness;
            for (int i = main.sheets.IndexOf(this) - 1; i >= 0; i--)
                if (!IsVoid(At(main.sheets[i].ground, c))) return main.sheets[i].BaseY(c);
            return main.BaseY(c) - FloorThickness;
        }

        public Transform root;
        public char[,] tiles;  // [x, row]: what's on top (a module, or the table itself)
        public char[,] ground; // [x, row]: the table under it (felt, rail...): a module stands on it
        public List<Ball> balls = new();
        public List<Wall> walls = new();
        public readonly Dictionary<Vector2Int, List<Wall>> wallGrid = new(); // walls near each cell, for physics
        public List<Pocket> pockets = new();
        public Bounds bounds;

        public static Vector2Int CellOf(Vector3 p) => new(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.z));

        public char TileAt(Vector3 p) => At(tiles, CellOf(p));

        // plain loop, no LINQ: called for every ball at every 1 ms step (a closure per call was GC hitches on the break)
        public Pocket PocketAt(Vector3 p)
        {
            for (int i = 0; i < pockets.Count; i++)
            {
                var pk = pockets[i];
                float dx = p.x - pk.center.x, dz = p.z - pk.center.y;
                if (dx * dx + dz * dz < pk.radius * pk.radius) return pk;
            }
            return null;
        }

        public Dictionary<Vector2Int, int> angles = new(); // oriented tiles (OrientedChars) from the file; boosters without one use their char
        public float time;                                 // physics clock: timed walls, platforms
        public readonly List<Mover> movers = new();        // gates and timed walls
        public Dictionary<(int f, Vector2Int c), (List<(int f, Vector2Int c)> nodes, bool loop)> trackPaths; // main only: PlatformTracks, sheet coordinates
        public readonly List<TimedSpikesModule> timedSpikes = new();
        public readonly List<Platform> platforms = new();
        public readonly List<(Vector2Int cell, float radius, float pull)> magnets = new();
        public readonly Dictionary<Vector2Int, GameObject> modules = new(); // module prefab instance per cell
        public readonly Dictionary<Vector2Int, List<(Vector3 air, float y)>> wind = new(); // wind fans: acceleration on the cells ahead, at the height of the felt they blow over

        // wind on a ball at p: only the fans blowing at its height (a fan on a deck doesn't blow under it)
        public Vector3 WindAt(Vector3 p)
        {
            if (!wind.TryGetValue(CellOf(p), out var list)) return Vector3.zero;
            var sum = Vector3.zero;
            foreach (var (air, y) in list)
                if (Mathf.Abs(p.y - Ball.Radius - y) < FloorStep / 2) sum += air;
            return sum;
        }

        // the module's settings script on cell c (null = none: physics uses the PoolPhysics default)
        public T ModuleAt<T>(Vector2Int c) where T : Component
        {
            if (stacks.TryGetValue(c, out var st))
                foreach (var l in st)
                    if (l.go && l.go.TryGetComponent<T>(out var s)) return s;
            return modules.TryGetValue(c, out var go) && go ? go.GetComponent<T>() : null;
        }

        // cells with more than one module ("sopra" lines): bottom first, each one standing on the one below (Restack)
        public readonly Dictionary<Vector2Int, List<Layer>> stacks = new();
        readonly List<(char c, float angle)> felt = new();

        // the modules a ball at p feels (the list is reused): on a pile only the flat ones standing at its height
        public List<(char c, float angle)> ModulesAt(Vector3 p)
        {
            felt.Clear();
            var c = CellOf(p);
            var plat = PlatformAt(p);
            if (plat != null) { foreach (var r in plat.riders) felt.Add((r, 0)); } // on a platform: only what rides on it
            else if (!stacks.TryGetValue(c, out var st)) felt.Add((At(tiles, c), AngleAt(c)));
            else
                foreach (var l in st)
                    if (StackHeight(l.c) == 0 && Mathf.Abs(p.y - Ball.Radius - l.y) < FloorStep / 2) felt.Add((l.c, l.angle));
            return felt;
        }

        // the layer of module ch on a pile at c (null = not on a pile)
        public Layer LayerOf(Vector2Int c, char ch) => stacks.TryGetValue(c, out var st) ? st.Find(l => l.c == ch) : null;

        // where the cell's (first) module stands: felt, the top of a rail, a platform's tube under its track
        public float ModuleY(Vector2Int c)
        {
            char g = At(ground, c);
            float y = SurfaceY(c, c);
            return g == '#' ? y + RailHeight : g == ' ' ? y - TrackDrop : y;
        }
        public Vector2Int? portalBlue, portalOrange;

        // felt height of this floor (the same on every cell)
        public float BaseY(Vector2Int c) => floor * FloorStep;

        public float AngleAt(Vector2Int c) => angles.TryGetValue(c, out int a) ? a : Mathf.Max(BoostChars.IndexOf(At(tiles, c)), 0) * 90;

        // surface height of cell c at world point q (x, z): flat, or rising along a ramp toward its angle
        public float SurfaceY(Vector2Int c, Vector2 q)
        {
            float y = BaseY(c);
            if (At(tiles, c) != Ramp) return y;
            var d = AngleDir(AngleAt(c));
            return y + FloorStep * Mathf.Clamp01(Vector2.Dot(q - (Vector2)c, new Vector2(d.x, d.z)) + 0.5f);
        }

        // height of what holds a ball centered over p on this floor; null = it drops (pocket hole, empty cell without a platform).
        // p.y matters on a raised gate / timed wall (a ball it lifted stands on top)
        public float? GroundAt(Vector3 p)
        {
            if (PocketAt(p) != null) return null;
            var plat = PlatformAt(p);
            if (plat != null) return plat.center.y;
            var c = CellOf(p);
            float bottom = p.y - Ball.Radius;
            if (stacks.TryGetValue(c, out var st))
            {
                // the top of the highest solid of the pile the ball is on (or above)
                float top = float.NegativeInfinity;
                foreach (var l in st)
                    if (l.c != 'Q' && l.Top > l.y && bottom > l.Top - FloorStep / 2) top = Mathf.Max(top, l.Top);
                if (top > float.NegativeInfinity) return top;
            }
            else
            {
                foreach (var m in movers)
                    if (m.up && m.cell == c && bottom > m.floor - FloorStep / 2) return m.wall.height;
                // the top of a block is a floor too
                if (At(tiles, c) is Block or WindFan && bottom > ModuleY(c) + BlockHeight - FloorStep / 2) return ModuleY(c) + BlockHeight;
            }
            return IsVoid(At(ground, c)) ? null : SurfaceY(c, new Vector2(p.x, p.z));
        }

        // false = the ball drops here (into a pocket hole or off the table)
        public bool Supports(Vector3 p) => GroundAt(p).HasValue;

        public Platform PlatformAt(Vector3 p)
        {
            for (int i = 0; i < platforms.Count; i++)
            {
                var pl = platforms[i];
                if (!pl.saw && Mathf.Abs(p.x - pl.center.x) <= 0.5f && Mathf.Abs(p.z - pl.center.z) <= 0.5f
                    && Mathf.Abs(p.y - Ball.Radius - pl.center.y) < FloorStep * 0.6f) return pl; // on it, not a floor above or below
            }
            return null;
        }

        // push direction of the booster on this cell, zero if it isn't one
        public Vector3 BoostDirAt(Vector2Int cell) => BoostChars.IndexOf(At(tiles, cell)) < 0 ? Vector3.zero : AngleDir(AngleAt(cell));

        public List<Wall> WallsNear(Vector3 p) => wallGrid.TryGetValue(CellOf(p), out var list) ? list : NoWalls;
        static readonly List<Wall> NoWalls = new();
    }

    static char At(char[,] t, int x, int row) =>
        x >= 0 && row >= 0 && x < t.GetLength(0) && row < t.GetLength(1) ? t[x, row] : ' ';

    // world cell (x, z) -> text grid (rows run opposite to z)
    static char At(char[,] t, Vector2Int c) => At(t, c.x, t.GetLength(1) - 1 - c.y);

    public static char[,] Parse(string text)
    {
        var lines = GridLines(text);
        int w = lines.Max(l => l.Length);
        var t = new char[w, lines.Length];
        for (int r = 0; r < lines.Length; r++)
            for (int x = 0; x < w; x++)
                t[x, r] = x < lines[r].Length ? char.ToUpperInvariant(lines[r][x]) : ' ';
        return t;
    }

    // A level file is the tile grid, then optionally a "---" line followed by
    //   objects:        "<char> <sx> <sz>"        balls and rack on top of the tiles, on a 3x finer grid:
    //                                             world position = (sx, sz) / 3 from the grid's bottom-left tile center (tile (x, z) = sub (3x, 3z))
    //   angles:         "<booster> <x> <z> <deg>" tile coords; degrees clockwise from the far side (0 = '^', 90 = '>');
    //                                             for every oriented tile (OrientedChars), the first char is always a booster char
    // A line "piano <n> [<x> <z>]" starts the grid (and its "---" section) of floor n, its bottom-left tile on (x, z);
    // what comes before the first one is floor 0 (ReadFloors).
    //   old floors:     "= <x> <z> <floor>"       old files: one grid, the cell is raised to that floor (ReadFloors moves it there)
    public const int Sub = 3;
    public const float FloorStep = 0.5f; // one floor = one ball diameter; a ramp climbs it over one tile (~27°)
    const string Separator = "---";

    static string[] AllLines(string text) => text.Replace("\r", "").TrimEnd('\n').Split('\n');

    static string[] GridLines(string text) => AllLines(text).TakeWhile(l => l.Trim() != Separator).ToArray();

    static IEnumerable<string[]> SectionLines(string text) =>
        AllLines(text).SkipWhile(l => l.Trim() != Separator).Skip(1)
            .Select(l => l.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries));

    public static Dictionary<Vector2Int, char> ParseObjects(string text)
    {
        var objs = new Dictionary<Vector2Int, char>();
        foreach (var f in SectionLines(text))
            if (f.Length == 3 && int.TryParse(f[1], out int sx) && int.TryParse(f[2], out int sz))
                objs[new Vector2Int(sx, sz)] = char.ToUpperInvariant(f[0][0]);
        return objs;
    }

    // "tiri N" in the object section: fewest shots the level can be won in (crown), set by hand in the editor; 0 = not set
    public const string ParTag = "tiri";

    public static int ParOf(string text) => NumberLine(text, ParTag);

    // "falli N" in the object section: fouls forgiven per run (each one undoes the shot), set in the editor; 0 = none (a foul restarts)
    public const string FoulsTag = "falli";

    public static int FoulsOf(string text) => NumberLine(text, FoulsTag);

    // "tema N" in the object section: user levels pick the colors of world N (1-based, Worlds.Themes); -1 = no line
    public const string ThemeTag = "tema";

    public static int ThemeLine(string text) => NumberLine(text, ThemeTag) - 1;

    static int NumberLine(string text, string tag) =>
        SectionLines(text).Select(f => f.Length == 2 && f[0] == tag && int.TryParse(f[1], out int n) ? n : 0).FirstOrDefault(n => n > 0);

    public static Dictionary<Vector2Int, int> ParseAngles(string text)
    {
        var angles = new Dictionary<Vector2Int, int>();
        foreach (var f in SectionLines(text))
            if (f.Length == 4 && BoostChars.IndexOf(char.ToUpperInvariant(f[0][0])) >= 0 &&
                int.TryParse(f[1], out int x) && int.TryParse(f[2], out int z) && int.TryParse(f[3], out int deg))
                angles[new Vector2Int(x, z)] = deg;
        return angles;
    }

    // "sotto <x> <z> <c>" in the object section: the table under the module on tile (x, z) when it isn't felt (a rail '#' or a bridge '_')
    public const string UnderTag = "sotto";

    // modules that stand on the table instead of replacing it: on felt, on a rail or on a bridge's deck
    public const string OnTopChars = "X><^VJTYIMWKGLQNZPU=@%!&}{";

    public static bool OnTop(char c) => OnTopChars.IndexOf(c) >= 0;

    // what a module can stand on besides felt (the base is kept, written as an UnderTag line)
    public static bool HoldsModule(char c) => c is '#' or Bridge;

    public static Dictionary<Vector2Int, char> ParseUnder(string text)
    {
        var under = new Dictionary<Vector2Int, char>();
        foreach (var f in SectionLines(text))
            if (f.Length == 4 && f[0] == UnderTag && int.TryParse(f[1], out int x) && int.TryParse(f[2], out int z) && HoldsModule(f[3][0]))
                under[new Vector2Int(x, z)] = f[3][0];
        return under;
    }

    // "sopra <x> <z> <c> [degrees]" in the object section: a module piled on the one in the grid on tile (x, z), bottom to top
    public const string AboveTag = "sopra";

    public static Dictionary<Vector2Int, List<(char c, int angle)>> ParseAbove(string text)
    {
        var above = new Dictionary<Vector2Int, List<(char c, int angle)>>();
        foreach (var f in SectionLines(text))
            if (f.Length >= 4 && f[0] == AboveTag && int.TryParse(f[1], out int x) && int.TryParse(f[2], out int z) && Stackable(char.ToUpperInvariant(f[3][0])))
            {
                char c = char.ToUpperInvariant(f[3][0]);
                int a = f.Length > 4 && int.TryParse(f[4], out int d) ? d : Mathf.Max(BoostChars.IndexOf(c), 0) * 90;
                if (BoostChars.IndexOf(c) >= 0) c = '>';
                var k = new Vector2Int(x, z);
                if (!above.TryGetValue(k, out var pile)) above[k] = pile = new();
                pile.Add((c, a));
            }
        return above;
    }

    public static Dictionary<Vector2Int, List<(char c, int angle)>> CopyAbove(Dictionary<Vector2Int, List<(char c, int angle)>> above) => above.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());

    /// <summary>One floor of a level text: sparse cells keyed by world (x, z), objects on the 3x finer grid.</summary>
    public class FloorPlan
    {
        public Dictionary<Vector2Int, char> cells = new(), objects = new(), under = new();
        public Dictionary<Vector2Int, int> angles = new();
        public Dictionary<Vector2Int, List<(char c, int angle)>> above = new();
        public HashSet<Vector2Int> solid = new(); // cells that are columns: full down to what's under them (ColumnTag)
    }

    public const string FloorTag = "piano";
    // a line with just this word: every cell is a column down to what's under it (how old levels were built)
    public const string SolidTag = "pieno";
    // "colonne <x>,<z> <x>,<z> ..." in a floor's section: only these cells of the floor are columns (editor switch "Colonna piena")
    public const string ColumnTag = "colonne";
    static readonly System.Text.RegularExpressions.Regex FloorLine = new($@"^{FloorTag} (-?\d+)(?: (-?\d+) (-?\d+))?\s*$");

    /// <summary>
    /// The level text split into its floors (origin = world cell of the text's bottom-left). Old files (one grid + "= x z floor" lines) are converted: every cell goes
    /// to its floor, a bridge becomes felt on its floor with felt BridgeClearance floors under it, and every cell is a column (`FloorPlan.solid`).
    /// </summary>
    public static SortedDictionary<int, FloorPlan> ReadFloors(string text, Vector2Int origin = default)
    {
        var plans = new SortedDictionary<int, FloorPlan>();
        FloorPlan P(int f)
        {
            if (!plans.TryGetValue(f, out var p)) plans[f] = p = new FloorPlan();
            return p;
        }
        bool raised = false;
        int floor = 0;
        var at = Vector2Int.zero;
        var chunk = new System.Text.StringBuilder();
        void Flush()
        {
            var objects = new Dictionary<Vector2Int, char>();
            var angles = new Dictionary<Vector2Int, int>();
            var floors = new Dictionary<Vector2Int, int>();
            var under = new Dictionary<Vector2Int, char>();
            var above = new Dictionary<Vector2Int, List<(char c, int angle)>>();
            string part = chunk.ToString();
            var cells = ToCells(part, origin + at, objects, angles, floors, under, above);
            chunk.Clear();
            foreach (var f in SectionLines(part).Where(f => f.Length > 1 && f[0] == ColumnTag))
                foreach (var xz in f.Skip(1).Select(s => s.Split(',')))
                    if (xz.Length == 2 && int.TryParse(xz[0], out int x) && int.TryParse(xz[1], out int z)) P(floor).solid.Add(origin + at + new Vector2Int(x, z));
            var low = new Dictionary<Vector2Int, int>(); // old bridges: the floor of the felt under the deck
            foreach (var kv in cells)
            {
                var k = kv.Key;
                char c = kv.Value;
                int n = floors.GetValueOrDefault(k), f = floor + n;
                raised |= n != 0;
                if (c == Bridge || (OnTop(c) && under.GetValueOrDefault(k) == Bridge))
                {
                    low[k] = floor + Mathf.Max(n - BridgeClearance, 0);
                    if (low[k] != f) P(low[k]).cells[k] = '.';
                    if (c == Bridge) c = '.';
                }
                else if (under.TryGetValue(k, out char u)) P(f).under[k] = u;
                var p = P(f);
                p.cells[k] = c;
                if (angles.TryGetValue(k, out int a)) p.angles[k] = a;
                if (above.TryGetValue(k, out var pile)) p.above[k] = pile;
            }
            foreach (var o in objects) // an old file's ball starts on the felt of its tile (under a bridge)
            {
                var t = new Vector2Int(Mathf.RoundToInt(o.Key.x / (float)Sub), Mathf.RoundToInt(o.Key.y / (float)Sub));
                P(low.TryGetValue(t, out int l) ? l : floor + floors.GetValueOrDefault(t)).objects[o.Key] = o.Value;
            }
        }
        foreach (var line in AllLines(text))
        {
            var m = FloorLine.Match(line);
            if (!m.Success) { chunk.Append(line).Append('\n'); continue; }
            Flush();
            floor = int.Parse(m.Groups[1].Value);
            at = m.Groups[2].Success ? new Vector2Int(int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)) : Vector2Int.zero;
        }
        Flush();
        if (raised || SectionLines(text).Any(f => f.Length == 1 && f[0] == SolidTag))
            foreach (var p in plans.Values) p.solid.UnionWith(p.cells.Keys);
        return plans;
    }

    /// <summary>
    /// The level text of these floors: floor 0 first as a plain grid, the others after their "piano" line.
    /// `min` = world cell of the text's bottom-left; `extra` = lines for the "---" section (tiri, falli, ...).
    /// </summary>
    public static string WriteFloors(SortedDictionary<int, FloorPlan> plans, out Vector2Int min, string extra = "")
    {
        var used = plans.Where(kv => kv.Value.cells.Count > 0).ToList();
        var keys = used.SelectMany(kv => kv.Value.cells.Keys).ToList();
        min = keys.Count == 0 ? Vector2Int.zero : new Vector2Int(keys.Min(k => k.x), keys.Min(k => k.y));
        var sb = new System.Text.StringBuilder();
        // nothing is under the lowest floor, so its cells count only when it is alone; every cell a column = one SolidTag line
        bool solid = used.Count > 0 && used.Skip(used.Count > 1 ? 1 : 0).All(kv => kv.Value.cells.Keys.All(kv.Value.solid.Contains));
        foreach (var kv in used)
        {
            var p = kv.Value;
            string text = ToText(p.cells, out var at, p.objects, p.angles, null, p.under, p.above);
            var cols = p.solid.Where(p.cells.ContainsKey).OrderBy(k => k.y).ThenBy(k => k.x).Select(k => $" {k.x - at.x},{k.y - at.y}").ToList();
            if (!solid && kv.Key != used[0].Key && cols.Count > 0) text = AddLines(text, ColumnTag + string.Concat(cols) + "\n");
            if (kv.Key != 0 || at != min) sb.Append($"{FloorTag} {kv.Key} {at.x - min.x} {at.y - min.y}\n");
            sb.Append(text);
        }
        return AddLines(sb.ToString(), (solid ? SolidTag + "\n" : "") + extra);
    }

    /// <summary>Adds lines to the "---" section of the text's last floor.</summary>
    public static string AddLines(string text, string lines)
    {
        if (lines == "") return text;
        var all = AllLines(text);
        int last = System.Array.FindLastIndex(all, l => FloorLine.IsMatch(l));
        return text + (all.Skip(last + 1).Any(l => l.Trim() == Separator) ? "" : Separator + "\n") + lines;
    }

    // the highest floor with a cell on k: what a player pointing at the table from above means
    static FloorPlan TopAt(SortedDictionary<int, FloorPlan> plans, Vector2Int k) => plans.Values.LastOrDefault(p => p.cells.ContainsKey(k));

    public static Dictionary<Vector2Int, int> ParseFloors(string text)
    {
        var floors = new Dictionary<Vector2Int, int>();
        foreach (var f in SectionLines(text))
            if (f.Length == 4 && f[0] == "=" && int.TryParse(f[1], out int x) && int.TryParse(f[2], out int z) && int.TryParse(f[3], out int n) && n != 0)
                floors[new Vector2Int(x, z)] = n;
        return floors;
    }

    // Level text <-> sparse cells keyed by world (x, z); used by the editor tool. `min` = world cell of the text's bottom-left.
    // With `objects`, the object section is read into it (world sub coords) and objects written in the grid move there too.
    // With `angles`, every oriented tile gets its angle in degrees (boosters all become '>'). With `floors`, the raised cells.
    public static Dictionary<Vector2Int, char> ToCells(string text, Vector2Int min, Dictionary<Vector2Int, char> objects = null, Dictionary<Vector2Int, int> angles = null,
        Dictionary<Vector2Int, int> floors = null, Dictionary<Vector2Int, char> under = null, Dictionary<Vector2Int, List<(char c, int angle)>> above = null)
    {
        var t = Parse(text);
        int h = t.GetLength(1);
        var fileAngles = ParseAngles(text);
        if (floors != null)
            foreach (var f in ParseFloors(text)) floors[min + f.Key] = f.Value;
        var cells = new Dictionary<Vector2Int, char>();
        for (int r = 0; r < h; r++)
            for (int x = 0; x < t.GetLength(0); x++)
            {
                char c = t[x, r];
                if (c == ' ') continue;
                var local = new Vector2Int(x, h - 1 - r);
                var k = min + local;
                if (objects != null && IsObject(c)) { objects[k * Sub] = c; c = '.'; }
                if (angles != null && IsOriented(c))
                {
                    angles[k] = fileAngles.TryGetValue(local, out int a) ? a : Mathf.Max(BoostChars.IndexOf(c), 0) * 90;
                    if (BoostChars.IndexOf(c) >= 0) c = '>';
                }
                cells[k] = c;
            }
        if (objects != null)
            foreach (var o in ParseObjects(text)) objects[o.Key + min * Sub] = o.Value;
        if (under != null)
            foreach (var u in ParseUnder(text))
                if (cells.TryGetValue(min + u.Key, out var top) && OnTop(top)) under[min + u.Key] = u.Value;
        if (above != null)
            foreach (var u in ParseAbove(text))
                if (cells.TryGetValue(min + u.Key, out var bottom) && PileBase(bottom)) above[min + u.Key] = u.Value;
        return cells;
    }

    public static string ToText(Dictionary<Vector2Int, char> cells, out Vector2Int min, Dictionary<Vector2Int, char> objects = null, Dictionary<Vector2Int, int> angles = null,
        Dictionary<Vector2Int, int> floors = null, Dictionary<Vector2Int, char> under = null, Dictionary<Vector2Int, List<(char c, int angle)>> above = null,
        (Vector2Int min, Vector2Int max)? box = null)
    {
        min = Vector2Int.zero;
        if (cells.Count == 0 && box == null) return "";
        // box: the grid covers exactly these cells (every floor of a level on the same grid)
        min = box?.min ?? new Vector2Int(cells.Keys.Min(k => k.x), cells.Keys.Min(k => k.y));
        var max = box?.max ?? new Vector2Int(cells.Keys.Max(k => k.x), cells.Keys.Max(k => k.y));
        var sb = new System.Text.StringBuilder();
        for (int z = max.y; z >= min.y; z--)
        {
            for (int x = min.x; x <= max.x; x++) sb.Append(cells.TryGetValue(new Vector2Int(x, z), out var c) ? c : ' ');
            sb.Append('\n');
        }
        var a = angles?.Where(kv => cells.TryGetValue(kv.Key, out var c) && IsOriented(c)).OrderBy(kv => kv.Key.y).ThenBy(kv => kv.Key.x).ToList();
        var fl = floors?.Where(kv => kv.Value != 0 && cells.ContainsKey(kv.Key)).OrderBy(kv => kv.Key.y).ThenBy(kv => kv.Key.x).ToList();
        var un = under?.Where(kv => cells.TryGetValue(kv.Key, out var c) && OnTop(c)).OrderBy(kv => kv.Key.y).ThenBy(kv => kv.Key.x).ToList();
        var ab = above?.Where(kv => kv.Value.Count > 0 && cells.TryGetValue(kv.Key, out var c) && PileBase(c)).OrderBy(kv => kv.Key.y).ThenBy(kv => kv.Key.x).ToList();
        if ((objects != null && objects.Count > 0) || (a != null && a.Count > 0) || (fl != null && fl.Count > 0) || (un != null && un.Count > 0) || (ab != null && ab.Count > 0))
        {
            sb.Append(Separator).Append('\n');
            if (un != null)
                foreach (var kv in un) sb.Append($"{UnderTag} {kv.Key.x - min.x} {kv.Key.y - min.y} {kv.Value}\n");
            if (ab != null)
                foreach (var kv in ab)
                    foreach (var (c, an) in kv.Value)
                        sb.Append($"{AboveTag} {kv.Key.x - min.x} {kv.Key.y - min.y} {c}{(IsOriented(c) ? " " + an : "")}\n");
            if (fl != null)
                foreach (var kv in fl) sb.Append($"= {kv.Key.x - min.x} {kv.Key.y - min.y} {kv.Value}\n");
            if (a != null)
                foreach (var kv in a) sb.Append($"> {kv.Key.x - min.x} {kv.Key.y - min.y} {kv.Value}\n");
            if (objects != null)
                foreach (var o in objects.OrderBy(o => o.Key.y).ThenBy(o => o.Key.x))
                    sb.Append($"{o.Value} {o.Key.x - min.x * Sub} {o.Key.y - min.y * Sub}\n");
        }
        return sb.ToString();
    }

    // "felt + rails" brush (editor and build phase): felt on the cell, a rail on every empty cell around it.
    // Painting it over a rail turns that rail into felt, so pieces placed side by side merge into one table.
    public const char FeltWithRails = '+';

    public static bool PaintFeltWithRails(Dictionary<Vector2Int, char> cells, Vector2Int cell)
    {
        bool changed = !cells.TryGetValue(cell, out var old) || old != '.';
        cells[cell] = '.';
        for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                var k = cell + new Vector2Int(x, z);
                if (!cells.ContainsKey(k)) { cells[k] = '#'; changed = true; }
            }
        return changed;
    }

    // One tile painted on a cell (editor and build phase): boosters become '>' + angle, ramps and platforms snap to
    // 90°, a portal moves (one blue and one orange per level). Returns whether anything changed.
    // With `under`, a module (OnTop) keeps the rail / bridge it is painted on; anything else replaces the base too.
    // With `stack` (editor "Sovrapposizione"), a module painted on a cell that has one goes on top of its pile (`above`)
    // instead of replacing it; otherwise a replaced module takes its pile with it.
    public static bool SetTile(Dictionary<Vector2Int, char> cells, Dictionary<Vector2Int, int> angles, Vector2Int cell, char c, int angle,
        Dictionary<Vector2Int, char> under = null, Dictionary<Vector2Int, List<(char c, int angle)>> above = null, bool stack = false)
    {
        if (BoostChars.IndexOf(c) >= 0) c = '>';
        if (c is Ramp or PlatformTile or DiagRail or WindFan or Track or Saw) angle = ((Mathf.RoundToInt(angle / 90f) * 90) % 360 + 360) % 360;
        bool oriented = IsOriented(c);
        if (stack && above != null && Stackable(c) && cells.TryGetValue(cell, out var bottom) && (Stackable(bottom) || (bottom == PlatformTile && RidesPlatform(c))))
        {
            if (!above.TryGetValue(cell, out var pile)) pile = new();
            var (topC, topA) = pile.Count > 0 ? pile[^1] : (bottom, angles.GetValueOrDefault(cell));
            if (1 + pile.Count >= MaxStack || (topC == c && (!oriented || topA == angle))) return false; // full, or the same one again (a drag)
            if (c is PortalBlue or PortalOrange) RemovePortal(cells, above, c);
            pile.Add((c, oriented ? angle : 0));
            above[cell] = pile;
            return true;
        }
        if (cells.TryGetValue(cell, out var old) && old == c && (!oriented || (angles.TryGetValue(cell, out int a0) && a0 == angle))) return false;
        above?.Remove(cell);
        if (c is PortalBlue or PortalOrange) RemovePortal(cells, above, c);
        if (under != null)
        {
            char ground = old == default ? '.' : OnTop(old) ? under.GetValueOrDefault(cell, '.') : old;
            if (OnTop(c) && HoldsModule(ground)) under[cell] = ground;
            else under.Remove(cell);
        }
        cells[cell] = c;
        if (oriented) angles[cell] = angle;
        else angles.Remove(cell);
        return true;
    }

    // one blue and one orange portal per level: the old one goes (felt in the grid, off a pile)
    static void RemovePortal(Dictionary<Vector2Int, char> cells, Dictionary<Vector2Int, List<(char c, int angle)>> above, char c)
    {
        foreach (var k in cells.Where(kv => kv.Value == c).Select(kv => kv.Key).ToList()) cells[k] = '.';
        if (above != null) foreach (var pile in above.Values) pile.RemoveAll(e => e.c == c);
    }

    // the eraser with stacking on: only the top module of the cell goes (the last one leaves its base: felt, rail or bridge).
    // False = no module here (the eraser clears the cell as usual).
    public static bool EraseTop(Dictionary<Vector2Int, char> cells, Dictionary<Vector2Int, int> angles, Dictionary<Vector2Int, char> under,
        Dictionary<Vector2Int, List<(char c, int angle)>> above, Vector2Int cell)
    {
        if (above.TryGetValue(cell, out var pile) && pile.Count > 0)
        {
            pile.RemoveAt(pile.Count - 1);
            if (pile.Count == 0) above.Remove(cell);
            return true;
        }
        if (!cells.TryGetValue(cell, out var c) || !OnTop(c)) return false;
        cells[cell] = under.Remove(cell, out char u) ? u : '.';
        angles.Remove(cell);
        return true;
    }

    static readonly Vector2Int[] Sides = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

    // a line with just this word in a piece's "---" section: the piece goes on top of the table (AttachPiece)
    public const string OverlayTag = "sovrapponi";

    public static bool IsOverlay(string piece) => SectionLines(piece).Any(f => f.Length == 1 && f[0] == OverlayTag);

    // the piece's floors with its middle on `at`, turned `rot` clockwise degrees (multiples of 90), keyed by world cell
    static SortedDictionary<int, FloorPlan> PiecePlans(string piece, Vector2Int at, int rot)
    {
        var src = ReadFloors(piece);
        var placed = new SortedDictionary<int, FloorPlan>();
        var keys = src.Values.SelectMany(p => p.cells.Keys).ToList();
        if (keys.Count == 0) return placed;
        var pivot = new Vector2Int((keys.Min(k => k.x) + keys.Max(k => k.x)) / 2, (keys.Min(k => k.y) + keys.Max(k => k.y)) / 2);
        int turns = ((rot / 90) % 4 + 4) % 4;
        Vector2Int W(Vector2Int k)
        {
            var d = k - pivot;
            for (int i = 0; i < turns; i++) d = new Vector2Int(d.y, -d.x); // clockwise: far side (+z) -> right (+x)
            return at + d;
        }
        foreach (var kv in src)
        {
            var p = placed[kv.Key] = new FloorPlan();
            foreach (var c in kv.Value.cells) p.cells[W(c.Key)] = c.Value;
            foreach (var a in kv.Value.angles) p.angles[W(a.Key)] = (a.Value + turns * 90) % 360;
            foreach (var u in kv.Value.under) p.under[W(u.Key)] = u.Value;
            foreach (var k in kv.Value.solid) p.solid.Add(W(k));
            foreach (var ab in kv.Value.above) p.above[W(ab.Key)] = ab.Value.Select(e => (e.c, IsOriented(e.c) ? (e.angle + turns * 90) % 360 : e.angle)).ToList();
        }
        return placed;
    }

    /// <summary>The world cells a piece covers (every floor of it) with its middle on `at`, turned `rot` degrees.</summary>
    public static Dictionary<Vector2Int, char> PieceCells(string piece, Vector2Int at, int rot)
    {
        var cells = new Dictionary<Vector2Int, char>();
        foreach (var p in PiecePlans(piece, at, rot).Values)
            foreach (var c in p.cells) cells[c.Key] = c.Value;
        return cells;
    }

    // a ball or the rack starts on this cell (nothing may be built there)
    static bool UnderObject(Dictionary<Vector2Int, char> objects, Vector2Int cell)
    {
        foreach (var o in objects)
        {
            float dx = o.Key.x / (float)Sub - cell.x, dz = o.Key.y / (float)Sub - cell.y;
            bool under = o.Value == 'R' ? dx > -2.5f && dx < 0.75f && Mathf.Abs(dz) < 1.3f // rack rows grow toward +x from the apex
                                        : Mathf.Abs(dx) < 0.75f && Mathf.Abs(dz) < 0.75f;
            if (under) return true;
        }
        return false;
    }

    // Multiplayer build phase: the level text with a premade field piece (same text format, closed by rails) attached.
    // `at` = world cell (as built: text coords from the bottom-left) of the piece's middle, `rot` = clockwise degrees
    // (multiples of 90). The piece may only cover empty cells or lay its rails over the table's rails; where such an
    // overlapping rail has felt of the table on one side and felt of the piece on the other, it opens up into felt.
    // Null = not allowed: it covers the table, or it doesn't connect anywhere.
    // Overlay pieces (OverlayTag line in the file, e.g. a strip of spikes) go on top of the table instead: see below.
    public static string AttachPiece(string text, string piece, Vector2Int at, int rot)
    {
        var plans = ReadFloors(text);
        var placed = PiecePlans(piece, at, rot);
        if (placed.Count == 0) return null;
        void Put(FloorPlan to, FloorPlan from, Vector2Int k)
        {
            to.cells[k] = from.cells[k];
            if (from.angles.TryGetValue(k, out int a)) to.angles[k] = a; else to.angles.Remove(k);
            if (from.under.TryGetValue(k, out char u)) to.under[k] = u; else to.under.Remove(k);
            if (from.above.TryGetValue(k, out var pile)) to.above[k] = pile; else to.above.Remove(k);
            if (from.solid.Contains(k)) to.solid.Add(k); else to.solid.Remove(k);
        }

        if (IsOverlay(piece))
        {
            // every cell lands on the table, on the highest floor there (rails anywhere but a pocket, the rest on felt /
            // modules), never on a starting ball; it replaces what's there
            foreach (var p in placed.Values)
                foreach (var kv in p.cells)
                    if (TopAt(plans, kv.Key) is not { } top || top.cells[kv.Key] == 'O' || (kv.Value != '#' && !IsFloor(top.cells[kv.Key])) || UnderObject(top.objects, kv.Key)) return null;
            foreach (var p in placed.Values)
                foreach (var k in p.cells.Keys) Put(TopAt(plans, k), p, k);
            return WriteFloors(plans, out _);
        }

        bool door = false;
        foreach (var pk in placed)
        {
            if (!plans.TryGetValue(pk.Key, out var floor)) plans[pk.Key] = floor = new FloorPlan();
            var table = new Dictionary<Vector2Int, char>(floor.cells);
            var overlap = new List<Vector2Int>();
            foreach (var kv in pk.Value.cells)
            {
                var w = kv.Key;
                if (table.TryGetValue(w, out var t))
                {
                    if (t != '#' || kv.Value != '#') return null;
                    overlap.Add(w);
                    continue;
                }
                // columns: nothing goes under or over another floor's cell
                if (plans.Any(o => o.Key != pk.Key && o.Value.cells.ContainsKey(w) && (pk.Value.solid.Contains(w) || o.Value.solid.Contains(w)))) return null;
                Put(floor, pk.Value, w);
            }
            foreach (var w in overlap)
                foreach (var s in Sides)
                    if (table.TryGetValue(w + s, out var t) && IsFloor(t) && pk.Value.cells.TryGetValue(w - s, out var c) && IsFloor(c))
                    {
                        floor.cells[w] = '.';
                        door = true;
                    }
        }
        return door ? WriteFloors(plans, out _) : null;
    }

    // Multiplayer: the level with its only pocket on `cell` of the highest floor there (any old one goes back to rail on
    // the table's edge, felt inside). Allowed on felt (a hole) or on a rail next to felt. Null = not allowed there, or a ball / the rack starts there.
    public static string MovePocket(string text, Vector2Int cell)
    {
        var plans = ReadFloors(text);
        if (TopAt(plans, cell) is not { } p) return null;
        var cells = p.cells;
        bool Felt(Vector2Int k) => cells.TryGetValue(k, out var c) && IsFloor(c);
        char here = cells[cell];
        if (!(here == '.' || here == 'O' || (here == '#' && Around.Any(d => Felt(cell + d))))) return null;
        if (UnderObject(p.objects, cell)) return null;
        foreach (var o in plans.Values) ClearPockets(o.cells);
        cells[cell] = 'O';
        return WriteFloors(plans, out _);
    }

    static readonly Vector2Int[] Around = Enumerable.Range(0, 9).Select(i => new Vector2Int(i % 3 - 1, i / 3 - 1)).Where(d => d != Vector2Int.zero).ToArray();

    // pockets go back to rail on the table's edge, felt inside
    static void ClearPockets(Dictionary<Vector2Int, char> cells)
    {
        foreach (var k in cells.Where(kv => kv.Value == 'O').Select(kv => kv.Key).ToList())
            cells[k] = Around.Any(d => !cells.ContainsKey(k + d)) ? '#' : '.';
    }

    // multiplayer build phase: no pocket while pieces go on (they may cover where it was; the round's winner places it again)
    public static string RemovePocket(string text)
    {
        var plans = ReadFloors(text);
        foreach (var p in plans.Values) ClearPockets(p.cells);
        return WriteFloors(plans, out _);
    }

    // Multiplayer start: only one pocket, the one farthest from the cue's start; none: one on the farthest rail that takes it
    public static string OnePocket(string text)
    {
        var plans = ReadFloors(text).Values;
        var pockets = plans.SelectMany(p => p.cells).Where(kv => kv.Value == 'O').Select(kv => kv.Key).ToList();
        if (pockets.Count == 1) return text;
        // none (the build phase removed it and nobody placed it again): the farthest rail that can take one
        var cue = plans.SelectMany(p => p.objects).Where(o => o.Value == '0').Select(o => (Vector2)o.Key / Sub).DefaultIfEmpty(Vector2.zero).First();
        if (pockets.Count == 0) pockets = plans.SelectMany(p => p.cells).Where(kv => kv.Value == '#').Select(kv => kv.Key).ToList();
        return pockets.OrderByDescending(p => (p - cue).sqrMagnitude).Select(p => MovePocket(text, p)).FirstOrDefault(t => t != null) ?? text;
    }

    // online every player chases an 8: the level's own ball (Worlds.BallOf) becomes the 8
    public static string GoalToEight(string text, int ball) =>
        text == null ? null : System.Text.RegularExpressions.Regex.Replace(text, $@"(?m)^{BallChars[ball]}(?= -?\d+ -?\d+\s*$)", "8");

    // objects stand on a tile instead of being one; still accepted inside the grid (centered on that tile, felt under it)
    public static bool IsObject(char c) => c == 'R' || BallChars.IndexOf(c) >= 0;

    public static bool IsFloor(char c) => c == '.' || IsObject(c) || ModuleChars.IndexOf(c) >= 0;

    // no table here: empty, or the lane of a moving platform
    public static bool IsVoid(char c) => c == ' ' || c == PlatformTile;

    public static bool IsOriented(char c) => OrientedChars.IndexOf(c) >= 0;

    // pos = point on the felt
    static void Place(Level lv, char c, Vector3 pos)
    {
        var ballPos = pos + Vector3.up * Ball.Radius;
        int n = BallChars.IndexOf(c);
        if (n >= 0) lv.balls.Add(SpawnBall(n, ballPos, lv.root));
        else if (c == 'R') Rack(lv, ballPos);
    }

    public static Level Build(string text, string name)
    {
        var plans = ReadFloors(text);
        if (plans.Count == 0) plans[0] = new FloorPlan();
        // every floor on the same grid; the file's bottom-left tile stays the world's (0, 0)
        Vector2Int min = Vector2Int.zero, max = Vector2Int.zero;
        foreach (var k in plans.Values.SelectMany(p => p.cells.Keys))
        {
            min = Vector2Int.Min(min, k);
            max = Vector2Int.Max(max, k);
        }
        Level main = null;
        var tracks = PlatformTracks(plans).ToDictionary(kv => (kv.Key.f, kv.Key.c - min), kv => (kv.Value.nodes.Select(n => (n.f, n.c - min)).ToList(), kv.Value.loop));
        foreach (var kv in plans)
        {
            var p = kv.Value;
            main ??= new Level { root = new GameObject(name).transform, sheets = new(), trackPaths = tracks };
            var sheet = main.sheets.Count == 0 ? main : new Level();
            foreach (var k in p.solid) sheet.columns.Add(k - min);
            BuildSheet(ToText(p.cells, out _, p.objects, p.angles, null, p.under, p.above, (min, max)), kv.Key, sheet, main);
        }
        // a cell is a step for the floor right under it (no room to roll under); a column cell is more:
        // a wall on every floor down to what's under it
        for (int i = 1; i < main.sheets.Count; i++)
        {
            var s = main.sheets[i];
            for (int x = 0; x < s.ground.GetLength(0); x++)
                for (int z = 0; z < s.ground.GetLength(1); z++)
                {
                    var c = new Vector2Int(x, z);
                    if (IsVoid(At(s.ground, c))) continue;
                    bool solid = s.columns.Contains(c);
                    for (int j = i - 1; j >= 0; j--)
                    {
                        var g = main.sheets[j];
                        bool has = !IsVoid(At(g.ground, c));
                        if (g.floor == s.floor - 1 || (solid && !has))
                            g.walls.Add(SquareWall(new Vector3(x, g.BaseY(c), z), s.BaseY(c) - FloorStep * 0.6f)); // a ball rolling off the top doesn't touch it
                        if (has || !solid) break;
                    }
                }
        }
        // a platform is on every floor its track reaches (Sheet / GroundAt find it there); track pieces under a tube hide
        foreach (var s in main.sheets.ToList())
            foreach (var pl in s.platforms.ToList())
                for (int i = 1; i < pl.path.Count; i++)
                {
                    int f = pl.saw ? s.floor : Mathf.RoundToInt(pl.path[i].y / FloorStep);
                    var other = main.sheets.Find(x => x.floor == f);
                    if (other == null) continue;
                    var c = Level.CellOf(pl.path[i]);
                    if (IsTrack(At(other.tiles, c)) && other.modules.TryGetValue(c, out var piece) && piece) piece.SetActive(false);
                    if (!pl.saw && !other.platforms.Contains(pl)) other.platforms.Add(pl);
                }
        // index walls by every cell a ball touching them could be centered in
        foreach (var s in main.sheets)
            foreach (var wall in s.walls)
            {
                var b = wall.bounds;
                for (int x = Mathf.RoundToInt(b.xMin - Ball.Radius); x <= Mathf.RoundToInt(b.xMax + Ball.Radius); x++)
                    for (int z = Mathf.RoundToInt(b.yMin - Ball.Radius); z <= Mathf.RoundToInt(b.yMax + Ball.Radius); z++)
                    {
                        var key = new Vector2Int(x, z);
                        if (!s.wallGrid.TryGetValue(key, out var list)) s.wallGrid[key] = list = new List<Wall>();
                        list.Add(wall);
                    }
            }
        TableBase(main);
        return main;
    }

    // one floor: text = its grid and "---" section (no floor lines), on the grid every floor shares
    static void BuildSheet(string text, int floor, Level lv, Level main)
    {
        var tiles = Parse(text);
        int w = tiles.GetLength(0), h = tiles.GetLength(1);
        lv.tiles = tiles;
        lv.angles = ParseAngles(text);
        lv.floor = floor;
        lv.main = main;
        lv.root = main.root;
        lv.balls = main.balls;
        main.sheets.Add(lv);
        var under = ParseUnder(text);
        var above = ParseAbove(text);
        lv.ground = (char[,])tiles.Clone();
        for (int r = 0; r < h; r++)
            for (int x = 0; x < w; x++)
                if (OnTop(tiles[x, r])) lv.ground[x, r] = under.GetValueOrDefault(new Vector2Int(x, h - 1 - r), '.');
        // a platform's track runs over nothing (a saw's lies on the felt)
        foreach (var t in main.trackPaths.Values)
            foreach (var (f, k) in t.nodes)
                if (f == floor && IsTrack(At(tiles, k))) lv.ground[k.x, h - 1 - k.y] = ' ';
        lv.bounds = new Bounds(new Vector3((w - 1) / 2f, 0, (h - 1) / 2f), new Vector3(w, 0, h));

        for (int r = 0; r < h; r++)
            for (int x = 0; x < w; x++)
            {
                var cell = new Vector2Int(x, h - 1 - r);
                var pos = new Vector3(cell.x, lv.SurfaceY(cell, cell), cell.y);
                Place(lv, tiles[x, r], pos);
                // a module stands on what's under it: felt, the top of a rail
                pos.y = lv.ModuleY(cell);
                if (tiles[x, r] == PlatformTile && above.TryGetValue(cell, out var ride))
                {
                    var plat = Module(lv, PlatformTile, pos);
                    foreach (var (c, a) in ride.Where(l => RidesPlatform(l.c)))
                    {
                        Module(lv, c, pos, a, true).transform.SetParent(plat.transform, true); // rides with it
                        lv.platforms[^1].riders.Add(c);
                    }
                    lv.modules[cell] = plat;
                    continue;
                }
                if (!above.TryGetValue(cell, out var pile) || !Stackable(tiles[x, r]))
                {
                    Module(lv, tiles[x, r], pos);
                    continue;
                }
                var st = new List<Layer> { AddLayer(lv, tiles[x, r], pos, lv.AngleAt(cell), null) };
                var first = lv.modules.GetValueOrDefault(cell);
                foreach (var (c, a) in pile)
                {
                    pos.y = st[^1].Top;
                    st.Add(AddLayer(lv, c, pos, a, st[^1]));
                }
                lv.modules[cell] = first; // juice punches the bottom one, the pile rides it
                lv.stacks[cell] = st;
            }
        foreach (var o in ParseObjects(text))
        {
            var q = new Vector3(o.Key.x / (float)Sub, floor * FloorStep, o.Key.y / (float)Sub);
            q.y = lv.GroundAt(q) ?? q.y;
            Place(lv, o.Value, q);
        }
        BuildTable(lv);
    }

    static void TableBase(Level lv)
    {
        float legTop = FloorThickness + 0.6f;
        int w = lv.tiles.GetLength(0), h = lv.tiles.GetLength(1);
        // under the lowest floor; columns stand on it too
        bool Solid(int x, int z) => lv.sheets.Any(s => (s == lv || s.columns.Contains(new Vector2Int(x, z))) && !IsVoid(At(s.ground, new Vector2Int(x, z))));
        // the apron follows the table's shape: one box per run of tiles in each row
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                if (!Solid(x, z)) continue;
                int start = x;
                while (x + 1 < w && Solid(x + 1, z)) x++;
                Visual("Apron", PrimitiveType.Cube, new Vector3((start + x) / 2f, -(FloorThickness + 0.3f), z), new Vector3(x - start + 1, 0.6f, 1),
                    Quaternion.identity, DarkWood, lv.root);
            }
        // a leg under every outer corner of the shape (both side neighbors toward that corner are empty)
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
                if (Solid(x, z))
                    foreach (var sx in new[] { -1, 1 })
                        foreach (var sz in new[] { -1, 1 })
                            if (!Solid(x + sx, z) && !Solid(x, z + sz))
                                Visual("Leg", PrimitiveType.Cylinder, new Vector3(x - sx * 0.1f, -(legTop + TableHeight) / 2, z - sz * 0.1f),
                                    new Vector3(0.9f, TableHeight - legTop, 0.9f), Quaternion.identity, DarkWood, lv.root);
    }

    // Standard 8-ball rack, apex on `apex`, rows growing toward +x.
    static void Rack(Level lv, Vector3 apex)
    {
        var numbers = RackNumbers(new System.Random());
        float s = Ball.Radius * 2 * 1.002f; // tiny gap so the rack doesn't start overlapping
        int slot = 0;
        for (int k = 0; k < 5; k++)
            for (int i = 0; i <= k; i++)
                lv.balls.Add(SpawnBall(numbers[slot++], apex + new Vector3(k * s * 0.8660254f, 0, (i - k / 2f) * s), lv.root));
    }

    // index = rack slot, row by row from the apex: 8 in the middle (slot 4), one solid and one stripe in the back corners (10, 14).
    public static int[] RackNumbers(System.Random rng)
    {
        var solids = new List<int> { 1, 2, 3, 4, 5, 6, 7 }.OrderBy(_ => rng.Next()).ToList();
        var stripes = new List<int> { 9, 10, 11, 12, 13, 14, 15 }.OrderBy(_ => rng.Next()).ToList();
        var r = new int[15];
        r[4] = 8;
        bool solidLeft = rng.Next(2) == 0;
        r[10] = solidLeft ? solids[0] : stripes[0];
        r[14] = solidLeft ? stripes[0] : solids[0];
        var rest = solids.Skip(1).Concat(stripes.Skip(1)).OrderBy(_ => rng.Next()).ToList();
        for (int i = 0, j = 0; i < 15; i++)
            if (i != 4 && i != 10 && i != 14) r[i] = rest[j++];
        return r;
    }

    // stylized palette: saturated, flat colors (see CLAUDE.md "Stile grafico")
    // table colors come from the world theme (SetTheme); start with the first one
    static Color Felt = Worlds.Themes[0].felt, CushionColor = Worlds.Themes[0].cushion, RoomColor = Worlds.Themes[0].room;
    static Color Wood = Worlds.Themes[0].wood, DarkWood = Worlds.Themes[0].darkWood;
    // module colors live here, not in LevelBuilder.Modules.cs: Palette needs them initialized first (same file = textual order)
    static readonly Color BlockColor = new(0.55f, 0.5f, 0.62f), BlockTopColor = new(0.72f, 0.68f, 0.8f);
    static readonly Color SpikeBaseColor = new(0.45f, 0.1f, 0.12f), MetalColor = new(0.78f, 0.8f, 0.86f);
    static readonly Color FanColor = new(0.2f, 0.6f, 1f), DuctColor = new(0.08f, 0.1f, 0.18f), BladeColor = new(0.85f, 0.88f, 0.95f), HubColor = new(1f, 0.85f, 0.3f);
    static readonly Color AirColor = new(0.85f, 0.95f, 1f);
    public static Material AirMaterial => Mat("Air", AirColor); // fan / wind fan streaks
    static readonly Color BoosterColor = new(1f, 0.55f, 0.12f), ArrowColor = new(1f, 0.93f, 0.55f);
    static readonly Color PortalBlueColor = new(0.15f, 0.55f, 1f), PortalOrangeColor = new(1f, 0.5f, 0.1f), PortalCoreColor = new(0.9f, 0.97f, 1f);
    static readonly Color IceColor = new(0.75f, 0.92f, 1f), SandColor = new(0.93f, 0.8f, 0.5f), GrateColor = new(0.25f, 0.27f, 0.3f);
    static readonly Color ButtonColor = new(0.95f, 0.2f, 0.25f), GateColor = new(0.95f, 0.75f, 0.2f), TimedColor = new(0.6f, 0.35f, 0.9f), TimedInvColor = new(0.3f, 0.82f, 0.4f);
    static readonly Color BumperColor = new(1f, 0.35f, 0.6f), BeltColor = new(0.2f, 0.22f, 0.26f), MagnetColor = new(0.85f, 0.15f, 0.15f);
    static readonly Color PlatformColor = new(0.35f, 0.75f, 0.6f);
    static readonly Color LampShadeColor = new(0.05f, 0.32f, 0.2f), LampGlowColor = new(1f, 0.86f, 0.55f), BrassColor = new(0.85f, 0.65f, 0.25f);

    // Blender material name -> color (Unity's FBX import colors aren't reliable in URP)
    static readonly Dictionary<string, Color> Palette = new()
    {
        ["Wood"] = Wood, ["DarkWood"] = DarkWood, ["Cushion"] = CushionColor, ["Felt"] = Felt,
        ["CueTip"] = new(0.15f, 0.3f, 0.7f), ["CueFerrule"] = new(0.95f, 0.93f, 0.85f), ["CueWood"] = new(0.85f, 0.7f, 0.45f),
        ["CueButt"] = new(0.08f, 0.05f, 0.04f), ["CueWrap"] = new(0.15f, 0.15f, 0.17f),
        ["Block"] = BlockColor, ["BlockTop"] = BlockTopColor, ["SpikeBase"] = SpikeBaseColor, ["TimedSpikes"] = TimedColor, ["TimedSpikesInv"] = TimedInvColor, ["Metal"] = MetalColor,
        ["FanHousing"] = FanColor, ["FanDuct"] = DuctColor, ["FanBlade"] = BladeColor, ["FanHub"] = HubColor, ["BoosterPlate"] = BoosterColor,
        ["PortalBlue"] = PortalBlueColor, ["PortalOrange"] = PortalOrangeColor, ["PortalCore"] = PortalCoreColor, ["Ice"] = IceColor, ["Sand"] = SandColor,
        ["Grate"] = GrateColor, ["ButtonCap"] = ButtonColor, ["Gate"] = GateColor, ["TimedWall"] = TimedColor, ["Bumper"] = BumperColor, ["BumperBase"] = BeltColor,
        ["Belt"] = BeltColor, ["Magnet"] = MagnetColor, ["Platform"] = PlatformColor, ["Stripe"] = new(0.12f, 0.12f, 0.14f),
        ["LampShade"] = LampShadeColor, ["LampGlow"] = LampGlowColor, ["Brass"] = BrassColor,
    };

    // world look for the next Build (models pick their wood/felt through Palette)
    public static void SetTheme(Worlds.Theme t)
    {
        Palette["Felt"] = Felt = t.felt;
        Palette["Cushion"] = CushionColor = t.cushion;
        Palette["Wood"] = Wood = t.wood;
        Palette["DarkWood"] = DarkWood = t.darkWood;
        RoomColor = t.room;
        hall = t.hall;
    }

    public static Ball SpawnBall(int n, Vector3 pos, Transform parent)
    {
        var go = Visual(n == 0 ? "CueBall" : "Ball" + n, PrimitiveType.Sphere, pos, Vector3.one * Ball.Radius * 2, Random.rotation, Color.white, parent, "Ball");
        foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = BallArt.Material(n);
        var ball = go.AddComponent<Ball>();
        ball.number = n;
        return ball;
    }

    /// <summary>
    /// Spawns Resources/Models/&lt;model&gt; (a Blender FBX) or the fallback primitive, scaled so its bounds fill `size`
    /// (in local axes) and centered on `center`. Any authoring scale or pivot works. Visual only: colliders are removed.
    /// </summary>
    public static GameObject Visual(string name, PrimitiveType fallback, Vector3 center, Vector3 size, Quaternion rot, Color color, Transform parent, string model = null)
    {
        var holder = new GameObject(name).transform;
        holder.SetParent(parent, false);
        holder.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var fit = new GameObject("Fit").transform;
        fit.SetParent(holder, false);

        var prefab = Resources.Load<GameObject>("Models/" + (model ?? name));
        GameObject go;
        if (prefab)
        {
            go = Object.Instantiate(prefab, fit);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = r.sharedMaterials.Select(m => m && Palette.TryGetValue(m.name, out var c) ? Mat(m.name, c) : m).ToArray();
        }
        else
        {
            go = GameObject.CreatePrimitive(fallback);
            go.transform.SetParent(fit, false);
            go.GetComponent<Renderer>().sharedMaterial = Mat(color);
        }
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

        var rends = go.GetComponentsInChildren<Renderer>();
        var b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        var s = new Vector3(size.x / Mathf.Max(b.size.x, 1e-4f), size.y / Mathf.Max(b.size.y, 1e-4f), size.z / Mathf.Max(b.size.z, 1e-4f));
        fit.localScale = s;
        fit.localPosition = -Vector3.Scale(b.center, s);
        holder.SetPositionAndRotation(center, rot);
        return holder.gameObject;
    }

    static readonly Dictionary<Color, Material> mats = new();

    // in builds the shader is there thanks to Resources/BuildShaders (BuildGame)
    public static Material Mat(Color c)
    {
        if (!mats.TryGetValue(c, out var m) || !m)
        {
            mats[c] = m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = c };
            m.SetFloat("_Smoothness", 0.15f); // matte, stylized
        }
        return m;
    }

    static readonly Dictionary<(string, Color), Material> namedMats = new();

    // same as Mat(c) but named (Blender material name): module prefabs save it as an asset under that name
    public static Material Mat(string name, Color c)
    {
        if (!namedMats.TryGetValue((name, c), out var m) || !m) namedMats[(name, c)] = m = new Material(Mat(c)) { name = name };
        return m;
    }
}
