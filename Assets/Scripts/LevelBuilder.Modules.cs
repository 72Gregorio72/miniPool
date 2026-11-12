using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Level modules: tiles with felt underneath and a special effect, applied by PoolPhysics when a ball's center is on them.
//   X spikes (the ball dies = off the table)   > < ^ V booster   J fan (jump)   P block (solid cube, whole tile)
//   S ramp (one floor up toward its angle)   T / Y blue / orange portal   I ice   M sand   W manhole (stops the ball)
//   K button (holds the gates G down)   G gate   L timed wall   % timed wall, opposite phase (down while L is up)   Q bumper   N conveyor belt   Z magnet
//   H moving platform (no table there: it shuttles along its angle over the empty cells ahead)
//   _ bridge (deck on its floor, felt BridgeClearance floors lower underneath: an underpass)   U wind fan (blows along its angle)
//   ! timed spikes (up and down like a timed wall)   & timed spikes, opposite phase
//   } / { vertical track piece (up / down): leads to the track piece on the same cell one floor up / down
//   @ saw (kills like spikes, rides its track)   = track piece: platforms and saws follow a chain of them (TrackPath)
// Look: one prefab per module in Resources/Modules (editable in Unity); without it, the procedural look below.
public static partial class LevelBuilder
{
    public const string ModuleChars = "X><^VJSTYIMWKGLQNZ_=@%!&}{"; // floor modules (felt under them)
    public const char Block = 'P', Ramp = 'S', PortalBlue = 'T', PortalOrange = 'Y', PlatformTile = 'H', Gate = 'G', TimedWall = 'L';
    public const char Bridge = '_', WindFan = 'U', Track = '=', Saw = '@', TimedWallInv = '%', TimedSpikes = '!', TimedSpikesInv = '&', TrackUp = '}', TrackDown = '{';

    /// <summary>A track piece: flat (=, along its angle) or vertical (}, { = one floor up / down).</summary>
    public static bool IsTrack(char c) => c is Track or TrackUp or TrackDown;
    public const int BridgeClearance = 2; // old files: floors between a bridge's deck and the felt under it (ReadFloors)
    public const char DiagRail = '/'; // diagonal rail: rail half toward its angle + 45° (a rail, not a floor module)
    public const float BlockHeight = 1f, BumperRadius = 0.3f, BumperHeight = 0.5f;
    public const float SawRadius = 0.35f, SawBladeHeight = 0.12f; // blade center above the felt (most of it sticks out)

    // booster chars; without an angle line in the file they point 0/90/180/270 degrees (clockwise from the far side, +z)
    public const string BoostChars = "^>V<";
    // tiles with an angle (file angle lines, R/Q in the editor); ramps, platforms and diagonal rails only use multiples of 90
    public const string OrientedChars = BoostChars + "STYNH/U=@";

    // prefab name per module (Resources/Modules/<name>.prefab, made by MiniPool > Crea prefab moduli).
    // Pivot = the tile's felt point; the prefab is turned to the tile's angle. Physics stays in code (Module, PoolPhysics).
    public static readonly Dictionary<char, string> ModuleNames = new()
    {
        [Block] = "Block", ['X'] = "Spikes", ['J'] = "Fan", ['>'] = "Booster", ['<'] = "Booster", ['^'] = "Booster", ['V'] = "Booster",
        [PortalBlue] = "PortalBlue", [PortalOrange] = "PortalOrange", ['I'] = "Ice", ['M'] = "Sand", ['W'] = "Manhole", ['K'] = "Button",
        [Gate] = "Gate", [TimedWall] = "TimedWall", [TimedWallInv] = "TimedWallInv", ['Q'] = "Bumper", ['N'] = "Belt", ['Z'] = "Magnet", [PlatformTile] = "Platform",
        [Bridge] = "Bridge", [WindFan] = "WindFan", [Track] = "Track", [Saw] = "Saw",
        [TimedSpikes] = "TimedSpikes", [TimedSpikesInv] = "TimedSpikesInv", [TrackUp] = "TrackUp", [TrackDown] = "TrackDown",
    };

    public static Vector3 AngleDir(float deg) => Quaternion.Euler(0, deg, 0) * Vector3.forward;

    // a wall whose height PoolPhysics changes: gates (down while a button is pressed) and timed walls
    public class Mover
    {
        public Wall wall;
        public Transform visual; // pivot on the felt
        public float floor; // felt height under it
        public Vector2Int cell;
        public bool rose; // went up on the last UpdateMoving: lifts the balls standing on it
        public bool timed, inverted, up = true, shownUp = true; // inverted: timed wall that is down while the plain ones are up // shownUp: last state ShowMoving played a sound for
        public float period = PoolPhysics.TimedPeriod; // timed walls: seconds up, then as long down
    }

    // one module of a pile (Level.stacks): it stands on y, the top of the one below (the cell's base for the first)
    public class Layer
    {
        public char c;
        public float angle, y;
        public GameObject go;
        public Mover mover;  // a gate / timed wall: its top goes up and down, and what's on it with it
        public Wall wall;    // a solid's sides (block, bumper, wind fan)
        public Platform saw; // a saw on a pile spins where it is
        public float Top => mover != null ? (mover.up ? y + BlockHeight : y) : y + StackHeight(c);
    }

    public const int MaxStack = 4; // modules per cell
    public const float SpikesDown = 0.05f; // timed spikes' tips height scale when down

    // how tall a module is in a pile: what's put on it stands this much higher (0 = flat)
    public static float StackHeight(char c) => c is Block or WindFan or Gate or TimedWall or TimedWallInv ? BlockHeight : c == 'Q' ? BumperHeight : 0;

    // modules that go in a pile: the ones standing on the table, but not a track piece (tracks run flat)
    public static bool Stackable(char c) => OnTop(c) && !IsTrack(c);

    // surfaces a moving platform can carry on its pile (they ride with it): spikes, sand, ice
    public static bool RidesPlatform(char c) => c is 'X' or 'M' or 'I';

    // what a pile can stand on: a stackable module, or a platform (only RidesPlatform ones on it)
    public static bool PileBase(char c) => Stackable(c) || c == PlatformTile;

    // a module of a pile; a flat one or a solid rides what it stands on (its look follows a moving wall below)
    static Layer AddLayer(Level lv, char c, Vector3 pos, float angle, Layer below)
    {
        int walls = lv.walls.Count, movers = lv.movers.Count, plats = lv.platforms.Count;
        var l = new Layer { c = c, angle = angle, y = pos.y };
        l.go = Module(lv, c, pos, angle, below != null);
        if (lv.movers.Count > movers) l.mover = lv.movers[^1];
        else if (lv.walls.Count > walls) l.wall = lv.walls[^1];
        if (lv.platforms.Count > plats) l.saw = lv.platforms[^1];
        if (below?.go && l.go && l.mover == null && l.saw == null)
        {
            // on a wall that starts down its look is down too: on its top
            if (below.mover != null) l.go.transform.position = new Vector3(pos.x, below.go.transform.position.y + BlockHeight, pos.z);
            l.go.transform.SetParent(below.go.transform, true);
        }
        return l;
    }

    // piles follow what they stand on (a gate / timed wall going up or down): heights, walls, saws
    public static void Restack(Level lv)
    {
        foreach (var st in lv.stacks)
            for (int i = 1; i < st.Value.Count; i++)
            {
                var l = st.Value[i];
                l.y = st.Value[i - 1].Top;
                if (l.mover != null) { l.mover.floor = l.y; l.mover.wall.bottom = l.y - 0.05f; }
                if (l.wall != null) { l.wall.bottom = l.y - 0.05f; l.wall.height = l.y + StackHeight(l.c); }
                if (l.saw != null) l.saw.path[0] = l.saw.center = new Vector3(st.Key.x, l.y, st.Key.y);
                // shortcut: a wind fan on a moving pile keeps blowing at its starting height; move lv.wind with it if levels need it
            }
    }

    // a platform (balls on it ride along) or a saw (kills the balls it touches) going along its track (TrackPath):
    // back and forth, or round and round when the track closes on itself
    public class Platform
    {
        public List<Vector3> path; // cell centers, on the start cell's height (a platform: its top middle)
        public bool loop, saw;
        public Vector3 center, velocity;
        public int range => path.Count - 1;
        public float speed = PoolPhysics.PlatformSpeed; // tiles/s
        public Transform visual; // pivot = center
        public readonly List<char> riders = new(); // spikes / sand / ice piled on a platform, carried with it
    }

    static Wall SquareWall(Vector3 cell, float height)
    {
        var pts = new[] { new Vector2(cell.x - 0.5f, cell.z - 0.5f), new Vector2(cell.x + 0.5f, cell.z - 0.5f), new Vector2(cell.x + 0.5f, cell.z + 0.5f), new Vector2(cell.x - 0.5f, cell.z + 0.5f) };
        return new Wall { pts = pts, bounds = new Rect(cell.x - 0.5f, cell.z - 0.5f, 1, 1), height = height, bottom = cell.y - 0.05f };
    }

    // chevron pointing along rot's forward (built in Unity so the direction is exact)
    static void Chevron(Vector3 pos, Quaternion rot, float size, Color color, Transform parent)
    {
        foreach (var s in new[] { -1, 1 })
            Visual("Arrow", PrimitiveType.Cube, pos + rot * new Vector3(s * 0.16f * size, 0, 0), new Vector3(0.1f, 0.02f, 0.45f) * size,
                rot * Quaternion.Euler(0, -s * 45, 0), color, parent);
    }

    // angle: a module on a pile has its own (else the cell's); stacked: it stands on another module, not on the table
    static GameObject Module(Level lv, char c, Vector3 cell, float? angle = null, bool stacked = false)
    {
        if (!ModuleNames.ContainsKey(c)) return null;
        var key = Level.CellOf(cell);
        float ang = angle ?? lv.AngleAt(key);
        var rot = BoostChars.IndexOf(c) >= 0 ? Quaternion.LookRotation(AngleDir(ang))
                : c is PortalBlue or PortalOrange or 'N' or WindFan or Track or Saw ? Quaternion.Euler(0, ang, 0) : Quaternion.identity;
        var go = ModuleObject(c);
        go.transform.SetParent(lv.root, false);
        go.transform.SetPositionAndRotation(cell, rot);
        lv.modules[key] = go;
        var timedSet = go.GetComponent<TimedWallModule>();
        var bumperSet = go.GetComponent<BumperModule>();
        var magnetSet = go.GetComponent<MagnetModule>();
        var platformSet = go.GetComponent<PlatformModule>();
        var windSet = go.GetComponent<WindModule>();
        var sawSet = go.GetComponent<SawModule>();
        switch (c)
        {
            case TimedSpikes: case TimedSpikesInv:
                var ts = go.GetComponent<TimedSpikesModule>();
                if (ts) { ts.shownUp = !ts.inverted; lv.timedSpikes.Add(ts); }
                break;
            case Block:
                lv.walls.Add(SquareWall(cell, cell.y + BlockHeight));
                break;
            case PortalBlue: lv.portalBlue = key; break;
            case PortalOrange: lv.portalOrange = key; break;
            case Gate: case TimedWall: case TimedWallInv:
                bool inv = c == TimedWallInv; // starts down
                var wall = SquareWall(cell, inv ? float.NegativeInfinity : cell.y + BlockHeight);
                lv.walls.Add(wall);
                lv.movers.Add(new Mover { wall = wall, visual = go.transform, floor = cell.y, cell = key, timed = c != Gate, inverted = inv, up = !inv, shownUp = !inv,
                    period = timedSet ? timedSet.period : PoolPhysics.TimedPeriod });
                if (inv) go.transform.position += Vector3.up * (0.03f - BlockHeight); // already down, no slide at the start
                break;
            case 'Q':
                var ring = Poly.Circle(new Vector2(cell.x, cell.z), BumperRadius, 8).ToArray();
                lv.walls.Add(new Wall { pts = ring, bounds = new Rect(cell.x - BumperRadius, cell.z - BumperRadius, 2 * BumperRadius, 2 * BumperRadius),
                    height = cell.y + BumperHeight, bottom = cell.y - 0.05f, kick = bumperSet ? bumperSet.kick : PoolPhysics.BumperKick });
                break;
            case WindFan:
                lv.walls.Add(SquareWall(cell, cell.y + BlockHeight));
                // air on the cells ahead until a rail or a block: 1 tile away full push, then less and less
                var wd = AngleDir(Mathf.Round(ang / 90) * 90);
                var ws = new Vector2Int(Mathf.RoundToInt(wd.x), Mathf.RoundToInt(wd.z));
                int reach = windSet ? windSet.reach : PoolPhysics.WindReach;
                float push = windSet ? windSet.push : PoolPhysics.WindPush;
                float airY = !stacked && At(lv.ground, key) == '#' ? cell.y - RailHeight : cell.y; // on a rail it blows over the felt at its foot
                int k = 1;
                for (; k <= reach && At(lv.ground, key + ws * k) is not ('#' or DiagRail) && At(lv.tiles, key + ws * k) is not (Block or WindFan); k++)
                {
                    if (!lv.wind.TryGetValue(key + ws * k, out var list)) lv.wind[key + ws * k] = list = new();
                    list.Add((wd * (push * (reach + 1 - k) / reach), airY));
                }
                if (windSet) { windSet.range = k - 1; windSet.drop = cell.y - airY; } // the streaks show where it really blows
                break;
            case 'Z':
                lv.magnets.Add((key, magnetSet ? magnetSet.radius : PoolPhysics.MagnetRadius, magnetSet ? magnetSet.pull : PoolPhysics.MagnetPull));
                break;
            case PlatformTile: case Saw:
                bool saw = c == Saw;
                bool loop = false;
                List<Vector3> path;
                bool tracked;
                if (!saw && !stacked && lv.main.trackPaths != null && lv.main.trackPaths.TryGetValue((lv.floor, key), out var t)) // across floors (PlatformTracks)
                {
                    path = t.nodes.Select(n => new Vector3(n.c.x, cell.y + (n.f - lv.floor) * FloorStep, n.c.y)).ToList();
                    loop = t.loop;
                    tracked = true;
                }
                else
                {
                    var cells = stacked ? new List<Vector2Int> { key } : TrackPath(lv, key, !saw, out loop); // on a pile it spins where it is
                    path = cells.Select(q => new Vector3(q.x, cell.y, q.y)).ToList();
                    tracked = cells.Count > 1 && At(lv.tiles, cells[1]) == Track;
                }
                lv.platforms.Add(new Platform { path = path, loop = loop, saw = saw, center = cell, visual = go.transform,
                    speed = saw ? (sawSet ? sawSet.speed : PoolPhysics.SawSpeed) : platformSet ? platformSet.speed : PoolPhysics.PlatformSpeed });
                // the tube it runs on, one straight piece center to center (under a platform, lying on the felt for a saw), a ring at each end
                var drop = Vector3.up * (saw ? -0.02f : -TrackDrop);
                for (int i = 0; tracked && i < path.Count - (loop ? 0 : 1); i++)
                {
                    Vector3 a = path[i], b = path[(i + 1) % path.Count];
                    Visual("Tube", PrimitiveType.Cylinder, (a + b) / 2 + drop, new Vector3(0.12f, (b - a).magnitude + 0.12f, 0.12f), Quaternion.FromToRotation(Vector3.up, b - a), MetalColor, lv.root);
                }
                if (tracked && !loop)
                    foreach (var e in new[] { path[0], path[^1] })
                        Visual("TrackJoint", PrimitiveType.Cylinder, e + drop + Vector3.up * 0.02f, new Vector3(0.24f, 0.04f, 0.24f), Quaternion.identity, MetalColor, lv.root);
                break;
        }
        return go;
    }

    public const float TrackDrop = 0.37f; // a platform's tube: this far below its top

    // Cells a platform or a saw travels: its own, then along its angle through track pieces (=), each turning it to its own angle,
    // up to the last one. loop = the last one leads back to the start. A platform without a track piece ahead runs straight
    // over the empty cells ahead (up to 12), like before tracks existed.
    // one cell along an angle (rounded to 90)
    static Vector2Int Step(float deg)
    {
        var d = AngleDir(Mathf.Round(deg / 90) * 90);
        return new Vector2Int(Mathf.RoundToInt(d.x), Mathf.RoundToInt(d.z));
    }

    /// <summary>Platforms' tracks across floors (plan coordinates): from each platform along its angle through track pieces, each
    /// turning it to its own angle; a vertical piece (TrackUp / TrackDown) leads to the track piece on the same cell one floor
    /// up / down. Only platforms with a track piece ahead; loop = the last piece leads back to the platform.</summary>
    public static Dictionary<(int f, Vector2Int c), (List<(int f, Vector2Int c)> nodes, bool loop)> PlatformTracks(SortedDictionary<int, FloorPlan> plans)
    {
        char Tile(int f, Vector2Int c) => plans.TryGetValue(f, out var p) && p.cells.TryGetValue(c, out var ch) ? ch : ' ';
        var res = new Dictionary<(int f, Vector2Int c), (List<(int f, Vector2Int c)> nodes, bool loop)>();
        foreach (var (f0, plan) in plans)
            foreach (var (c0, ch0) in plan.cells)
            {
                if (ch0 != PlatformTile) continue;
                var start = (f: f0, c: c0);
                var nodes = new List<(int f, Vector2Int c)> { start };
                bool loop = false;
                while (nodes.Count < 64)
                {
                    var cur = nodes[^1];
                    char t = Tile(cur.f, cur.c);
                    var next = t == TrackUp ? (cur.f + 1, cur.c) : t == TrackDown ? (cur.f - 1, cur.c)
                        : (cur.f, cur.c + Step(plans[cur.f].angles.GetValueOrDefault(cur.c)));
                    if (next == start && nodes.Count > 2) { loop = true; break; }
                    if (!IsTrack(Tile(next.Item1, next.Item2)) || nodes.Contains(next)) break;
                    nodes.Add(next);
                }
                if (nodes.Count > 1) res[start] = (nodes, loop);
            }
        return res;
    }

    public static List<Vector2Int> TrackPath(Level lv, Vector2Int start, bool platform, out bool loop)
    {
        var path = new List<Vector2Int> { start };
        var step = Step(lv.AngleAt(start));
        loop = false;
        if (platform && At(lv.tiles, start + step) != Track)
        {
            while (path.Count <= 12 && At(lv.tiles, path[^1] + step) == ' ') path.Add(path[^1] + step);
            return path;
        }
        while (path.Count < 64)
        {
            var next = path[^1] + step;
            if (next == start && path.Count > 2) { loop = true; break; }
            if (At(lv.tiles, next) != Track || path.Contains(next)) break;
            path.Add(next);
            step = Step(lv.AngleAt(next));
        }
        return path;
    }

    static readonly string[] ThemeMats = { "Felt", "Cushion", "Wood", "DarkWood" };

    // the module's prefab if there is one (its theme materials follow the world), else the procedural look
    public static GameObject ModuleObject(char c)
    {
        var prefab = Resources.Load<GameObject>("Modules/" + ModuleNames[c]);
        if (!prefab) return ModuleVisual(c);
        var go = Object.Instantiate(prefab);
        go.name = prefab.name;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            r.sharedMaterials = r.sharedMaterials.Select(m => m && ThemeMats.Contains(m.name) ? Mat(m.name, Palette[m.name]) : m).ToArray();
        return go;
    }

    // procedural look of a module at the origin, unrotated: the source the prefabs are made from
    public static GameObject ModuleVisual(char c)
    {
        var root = new GameObject(ModuleNames[c]).transform;
        var up = Vector3.up;
        var id = Quaternion.identity;
        switch (c)
        {
            case Block:
                Visual("Pillar", PrimitiveType.Cube, up * BlockHeight / 2, new Vector3(1, BlockHeight, 1), id, BlockColor, root);
                break;
            case 'X':
                Visual("Spikes", PrimitiveType.Cube, up * 0.1f, new Vector3(0.96f, 0.2f, 0.96f), id, SpikeBaseColor, root);
                break;
            case TimedSpikes: case TimedSpikesInv:
                bool green = c == TimedSpikesInv;
                Visual("TimedSpikesBase", PrimitiveType.Cube, up * 0.016f, new Vector3(0.96f, 0.032f, 0.96f), id, green ? TimedInvColor : TimedColor, root);
                var tips = new GameObject("Tips").transform; // pivot on the base: scaled flat when down
                tips.SetParent(root, false);
                tips.localPosition = up * 0.03f;
                Visual("SpikeTips", PrimitiveType.Cube, up * (0.03f + 0.085f), new Vector3(0.88f, 0.17f, 0.88f), id, MetalColor, tips);
                if (green) // same model, green
                    foreach (var r in root.GetComponentsInChildren<Renderer>())
                        r.sharedMaterials = r.sharedMaterials.Select(m => m && m.name.StartsWith("TimedSpikes") ? Mat("TimedSpikesInv", TimedInvColor) : m).ToArray();
                var tsm = root.gameObject.AddComponent<TimedSpikesModule>();
                tsm.tips = tips;
                tsm.inverted = green;
                if (green) tips.localScale = new Vector3(1, SpikesDown, 1); // starts down
                break;
            case 'J':
                var fan = Visual("Fan", PrimitiveType.Cylinder, up * 0.05f, new Vector3(0.9f, 0.1f, 0.9f), id, FanColor, root);
                var blades = Visual("FanBlades", PrimitiveType.Cube, up * 0.05f, new Vector3(0.72f, 0.04f, 0.72f), id, BladeColor, fan.transform);
                var f = root.gameObject.AddComponent<Fan>();
                f.blades = blades.transform;
                f.airMat = Mat("Air", AirColor);
                break;
            case '>': case '<': case '^': case 'V':
                Visual("Booster", PrimitiveType.Cube, up * 0.01f, new Vector3(0.92f, 0.02f, 0.92f), id, BoosterColor, root);
                Chevron(up * 0.03f, id, 1, ArrowColor, root);
                root.gameObject.AddComponent<BoosterModule>();
                break;
            case PortalBlue: case PortalOrange:
                bool blue = c == PortalBlue;
                Visual(blue ? "PortalBlue" : "PortalOrange", PrimitiveType.Cylinder, up * 0.03f, new Vector3(0.9f, 0.06f, 0.9f), id,
                    blue ? PortalBlueColor : PortalOrangeColor, root);
                Chevron(up * 0.07f, id, 0.6f, Color.white, root); // its facing: entry and exit turn by the difference
                break;
            case 'I':
                Visual("Ice", PrimitiveType.Cube, up * 0.01f, new Vector3(0.98f, 0.02f, 0.98f), id, IceColor, root);
                break;
            case 'M':
                Visual("Sand", PrimitiveType.Cube, up * 0.01f, new Vector3(0.98f, 0.02f, 0.98f), id, SandColor, root);
                break;
            case 'W':
                Visual("Manhole", PrimitiveType.Cylinder, up * 0.015f, new Vector3(0.8f, 0.03f, 0.8f), id, GrateColor, root);
                break;
            case 'K':
                Visual("Button", PrimitiveType.Cylinder, up * 0.04f, new Vector3(0.6f, 0.08f, 0.6f), id, ButtonColor, root);
                break;
            case Gate: case TimedWall: case TimedWallInv:
                var mw = Visual(c == Gate ? "Gate" : c == TimedWall ? "TimedWall" : "TimedWallInv", PrimitiveType.Cube, up * BlockHeight / 2, new Vector3(1, BlockHeight, 1), id,
                    c == Gate ? GateColor : c == TimedWall ? TimedColor : TimedInvColor, root, c == TimedWallInv ? "TimedWall" : null);
                if (c == TimedWallInv) // same model, green
                    foreach (var r in mw.GetComponentsInChildren<Renderer>()) r.sharedMaterials = r.sharedMaterials.Select(_ => Mat("TimedWallInv", TimedInvColor)).ToArray();
                if (c != Gate) root.gameObject.AddComponent<TimedWallModule>();
                break;
            case 'Q':
                Visual("Bumper", PrimitiveType.Cylinder, up * BumperHeight / 2, new Vector3(BumperRadius * 2, BumperHeight, BumperRadius * 2), id, BumperColor, root);
                root.gameObject.AddComponent<BumperModule>();
                break;
            case 'N':
                var belt = Visual("Belt", PrimitiveType.Cube, up * 0.01f, new Vector3(0.96f, 0.02f, 0.96f), id, BeltColor, root);
                var chevrons = new List<Transform>();
                for (int i = 0; i < 3; i++)
                {
                    var holder = new GameObject("Chevron").transform;
                    holder.SetParent(belt.transform, false);
                    Chevron(holder.position, holder.rotation, 0.5f, ArrowColor, holder);
                    chevrons.Add(holder);
                }
                root.gameObject.AddComponent<Belt>().chevrons = chevrons;
                break;
            case 'Z':
                Visual("Magnet", PrimitiveType.Cube, up * 0.04f, new Vector3(0.7f, 0.08f, 0.7f), id, MagnetColor, root);
                root.gameObject.AddComponent<MagnetModule>();
                break;
            case Bridge: // pivot = top of the deck
                var deck = Visual("Bridge", PrimitiveType.Cube, Vector3.down * 0.03f, new Vector3(1, 0.06f, 1), id, Felt, root);
                foreach (var r in deck.GetComponentsInChildren<Renderer>()) r.sharedMaterial = Mat("Felt", Felt); // follows the world theme
                var beam = Visual("BridgeBeam", PrimitiveType.Cube, Vector3.down * 0.15f, new Vector3(0.98f, 0.18f, 0.98f), id, DarkWood, root);
                foreach (var r in beam.GetComponentsInChildren<Renderer>()) r.sharedMaterial = Mat("DarkWood", DarkWood);
                break;
            case WindFan: // blows toward +z (its angle)
                float hub = 0.45f;
                Visual("WindFan", PrimitiveType.Cube, up * hub, new Vector3(0.96f, 0.9f, 0.9f), id, FanColor, root);
                var face = Quaternion.Euler(90, 0, 0); // cylinder axis along z
                Visual("WindDuct", PrimitiveType.Cylinder, new Vector3(0, hub, 0.455f), new Vector3(0.78f, 0.02f, 0.78f), face, DuctColor, root);
                var bladesT = new GameObject("Blades").transform;
                bladesT.SetParent(root, false);
                bladesT.localPosition = new Vector3(0, hub, 0.48f);
                Visual("WindBlade", PrimitiveType.Cube, bladesT.position, new Vector3(0.7f, 0.1f, 0.02f), id, BladeColor, bladesT);
                Visual("WindBlade", PrimitiveType.Cube, bladesT.position, new Vector3(0.1f, 0.7f, 0.02f), id, BladeColor, bladesT);
                Visual("WindHub", PrimitiveType.Cylinder, bladesT.position + Vector3.forward * 0.02f, new Vector3(0.16f, 0.03f, 0.16f), face, HubColor, bladesT);
                var wm = root.gameObject.AddComponent<WindModule>();
                wm.blades = bladesT;
                wm.airMat = Mat("Air", AirColor);
                break;
            case PlatformTile:
                Visual("Platform", PrimitiveType.Cube, Vector3.down * 0.1f, new Vector3(0.98f, 0.2f, 0.98f), id, PlatformColor, root);
                root.gameObject.AddComponent<PlatformModule>();
                break;
            case Track: case TrackUp: case TrackDown: // half a tube toward where the next piece goes (hidden on a platform's / saw's track: its tube shows)
                var dir = c == TrackUp ? up : c == TrackDown ? Vector3.down : Vector3.forward;
                float len = c == Track ? 0.5f : FloorStep / 2;
                Visual("Tube", PrimitiveType.Cylinder, up * 0.06f + dir * (len / 2), new Vector3(0.12f, len + 0.06f, 0.12f), Quaternion.FromToRotation(up, dir), MetalColor, root);
                Visual("TrackJoint", PrimitiveType.Cylinder, up * 0.02f, new Vector3(0.24f, 0.04f, 0.24f), id, MetalColor, root);
                break;
            case Saw: // vertical blade along its way (+z), half sunk in a slot
                Visual("SawBase", PrimitiveType.Cube, up * 0.01f, new Vector3(0.2f, 0.02f, 0.8f), id, DuctColor, root);
                var blade = new GameObject("Blade").transform;
                blade.SetParent(root, false);
                blade.localPosition = up * SawBladeHeight;
                var side = Quaternion.Euler(0, 0, 90); // cylinder axis along x: the disc stands in the y-z plane
                Visual("SawBlade", PrimitiveType.Cylinder, blade.position, new Vector3(SawRadius * 2, 0.03f, SawRadius * 2), side, MetalColor, blade);
                Visual("SawHub", PrimitiveType.Cylinder, blade.position, new Vector3(0.14f, 0.06f, 0.14f), side, SpikeBaseColor, blade);
                for (int i = 0; i < 8; i++) // teeth
                {
                    var tr = Quaternion.Euler(i * 45, 0, 0);
                    Visual("SawTooth", PrimitiveType.Cube, blade.position + tr * (up * SawRadius), new Vector3(0.03f, 0.08f, 0.08f), tr * Quaternion.Euler(45, 0, 0), MetalColor, blade);
                }
                root.gameObject.AddComponent<SawModule>().blade = blade;
                break;
        }
        return root.gameObject;
    }
}
