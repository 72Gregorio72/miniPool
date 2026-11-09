using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Level modules: tiles with felt underneath and a special effect, applied by PoolPhysics when a ball's center is on them.
//   X spikes (the ball dies = off the table)   > < ^ V booster   J fan (jump)   P block (solid cube, whole tile)
//   S ramp (one floor up toward its angle)   T / Y blue / orange portal   I ice   M sand   W manhole (stops the ball)
//   K button (holds the gates G down)   G gate   L timed wall   Q bumper   N conveyor belt   Z magnet
//   H moving platform (no table there: it shuttles along its angle over the empty cells ahead)
// Look: one prefab per module in Resources/Modules (editable in Unity); without it, the procedural look below.
public static partial class LevelBuilder
{
    public const string ModuleChars = "X><^VJSTYIMWKGLQNZ"; // floor modules (felt under them)
    public const char Block = 'P', Ramp = 'S', PortalBlue = 'T', PortalOrange = 'Y', PlatformTile = 'H', Gate = 'G', TimedWall = 'L';
    public const char DiagRail = '/'; // diagonal rail: rail half toward its angle + 45° (a rail, not a floor module)
    public const float BlockHeight = 1f, BumperRadius = 0.3f, BumperHeight = 0.5f;

    // booster chars; without an angle line in the file they point 0/90/180/270 degrees (clockwise from the far side, +z)
    public const string BoostChars = "^>V<";
    // tiles with an angle (file angle lines, R/Q in the editor); ramps, platforms and diagonal rails only use multiples of 90
    public const string OrientedChars = BoostChars + "STYNH/";

    // prefab name per module (Resources/Modules/<name>.prefab, made by MiniPool > Crea prefab moduli).
    // Pivot = the tile's felt point; the prefab is turned to the tile's angle. Physics stays in code (Module, PoolPhysics).
    public static readonly Dictionary<char, string> ModuleNames = new()
    {
        [Block] = "Block", ['X'] = "Spikes", ['J'] = "Fan", ['>'] = "Booster", ['<'] = "Booster", ['^'] = "Booster", ['V'] = "Booster",
        [PortalBlue] = "PortalBlue", [PortalOrange] = "PortalOrange", ['I'] = "Ice", ['M'] = "Sand", ['W'] = "Manhole", ['K'] = "Button",
        [Gate] = "Gate", [TimedWall] = "TimedWall", ['Q'] = "Bumper", ['N'] = "Belt", ['Z'] = "Magnet", [PlatformTile] = "Platform",
    };

    public static Vector3 AngleDir(float deg) => Quaternion.Euler(0, deg, 0) * Vector3.forward;

    // a wall whose height PoolPhysics changes: gates (down while a button is pressed) and timed walls
    public class Mover
    {
        public Wall wall;
        public Transform visual; // pivot on the felt
        public float floor; // felt height under it
        public bool timed, up = true, shownUp = true; // shownUp: last state ShowMoving played a sound for
        public float period = PoolPhysics.TimedPeriod; // timed walls: seconds up, then as long down
    }

    // shuttles from its start cell along dir over `range` empty cells and back; balls on it ride along
    public class Platform
    {
        public Vector3 start, dir, center, velocity; // center: top middle
        public int range;
        public float speed = PoolPhysics.PlatformSpeed; // tiles/s
        public Transform visual; // pivot = center
    }

    static Wall SquareWall(Vector3 cell, float height)
    {
        var pts = new[] { new Vector2(cell.x - 0.5f, cell.z - 0.5f), new Vector2(cell.x + 0.5f, cell.z - 0.5f), new Vector2(cell.x + 0.5f, cell.z + 0.5f), new Vector2(cell.x - 0.5f, cell.z + 0.5f) };
        return new Wall { pts = pts, bounds = new Rect(cell.x - 0.5f, cell.z - 0.5f, 1, 1), height = height };
    }

    // chevron pointing along rot's forward (built in Unity so the direction is exact)
    static void Chevron(Vector3 pos, Quaternion rot, float size, Color color, Transform parent)
    {
        foreach (var s in new[] { -1, 1 })
            Visual("Arrow", PrimitiveType.Cube, pos + rot * new Vector3(s * 0.16f * size, 0, 0), new Vector3(0.1f, 0.02f, 0.45f) * size,
                rot * Quaternion.Euler(0, -s * 45, 0), color, parent);
    }

    static void Module(Level lv, char c, Vector3 cell)
    {
        if (!ModuleNames.ContainsKey(c)) return;
        var key = Level.CellOf(cell);
        var rot = BoostChars.IndexOf(c) >= 0 ? Quaternion.LookRotation(lv.BoostDirAt(key))
                : c is PortalBlue or PortalOrange or 'N' ? Quaternion.Euler(0, lv.AngleAt(key), 0) : Quaternion.identity;
        var go = ModuleObject(c);
        go.transform.SetParent(lv.root, false);
        go.transform.SetPositionAndRotation(cell, rot);
        lv.modules[key] = go;
        var timedSet = go.GetComponent<TimedWallModule>();
        var bumperSet = go.GetComponent<BumperModule>();
        var magnetSet = go.GetComponent<MagnetModule>();
        var platformSet = go.GetComponent<PlatformModule>();
        switch (c)
        {
            case Block:
                lv.walls.Add(SquareWall(cell, cell.y + BlockHeight));
                break;
            case PortalBlue: lv.portalBlue = key; break;
            case PortalOrange: lv.portalOrange = key; break;
            case Gate: case TimedWall:
                var wall = SquareWall(cell, cell.y + BlockHeight);
                lv.walls.Add(wall);
                lv.movers.Add(new Mover { wall = wall, visual = go.transform, floor = cell.y, timed = c == TimedWall, period = timedSet ? timedSet.period : PoolPhysics.TimedPeriod });
                break;
            case 'Q':
                var ring = Poly.Circle(new Vector2(cell.x, cell.z), BumperRadius, 8).ToArray();
                lv.walls.Add(new Wall { pts = ring, bounds = new Rect(cell.x - BumperRadius, cell.z - BumperRadius, 2 * BumperRadius, 2 * BumperRadius),
                    height = cell.y + BumperHeight, kick = bumperSet ? bumperSet.kick : PoolPhysics.BumperKick });
                break;
            case 'Z':
                lv.magnets.Add((key, magnetSet ? magnetSet.radius : PoolPhysics.MagnetRadius, magnetSet ? magnetSet.pull : PoolPhysics.MagnetPull));
                break;
            case PlatformTile:
                var dir = AngleDir(Mathf.Round(lv.AngleAt(key) / 90) * 90);
                var step = new Vector2Int(Mathf.RoundToInt(dir.x), Mathf.RoundToInt(dir.z));
                int range = 0;
                while (range < 12 && At(lv.tiles, key + step * (range + 1)) == ' ') range++;
                lv.platforms.Add(new Platform { start = cell, center = cell, dir = new Vector3(step.x, 0, step.y), range = range, visual = go.transform,
                    speed = platformSet ? platformSet.speed : PoolPhysics.PlatformSpeed });
                break;
        }
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
            case Gate: case TimedWall:
                Visual(c == Gate ? "Gate" : "TimedWall", PrimitiveType.Cube, up * BlockHeight / 2, new Vector3(1, BlockHeight, 1), id,
                    c == Gate ? GateColor : TimedColor, root);
                if (c == TimedWall) root.gameObject.AddComponent<TimedWallModule>();
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
            case PlatformTile:
                Visual("Platform", PrimitiveType.Cube, Vector3.down * 0.1f, new Vector3(0.98f, 0.2f, 0.98f), id, PlatformColor, root);
                root.gameObject.AddComponent<PlatformModule>();
                break;
        }
        return root.gameObject;
    }
}
