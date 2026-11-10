using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// A level being edited: sparse cells keyed by world (x, z) plus the objects on the 3x finer grid, and every brush / tool
// that changes them. Shared by the Unity editor window (LevelEditorWindow) and the in-game editor (LevelEditor).
// The level text (LevelBuilder format) stays the only saved form: SetText / GetText convert.
public class LevelDraft
{
    /// <summary>One button of the palette: label, tile / object char, tint, in-game editor category.</summary>
    public readonly struct Brush
    {
        public readonly string label;
        public readonly char c;
        public readonly Color color;
        public readonly int category;
        public Brush(string label, char c, Color color, int category) { this.label = label; this.c = c; this.color = color; this.category = category; }
    }

    public static readonly string[] Categories = { "Tavolo", "Palle", "Ostacoli", "Effetti", "Mobili" };

    public static readonly Brush[] Brushes =
    {
        new("Panno", '.', new Color(0.2f, 0.7f, 0.35f), 0),
        new("Panno+sponde", LevelBuilder.FeltWithRails, new Color(0.3f, 0.75f, 0.45f), 0),
        new("Sponda", '#', new Color(0.75f, 0.45f, 0.25f), 0),
        new("Sponda diag. (R)", LevelBuilder.DiagRail, new Color(0.75f, 0.45f, 0.25f), 0),
        new("Buca", 'O', new Color(0.25f, 0.25f, 0.25f), 0),
        new("Rampa (R)", LevelBuilder.Ramp, new Color(0.3f, 0.75f, 0.45f), 0),
        new("Ponte", LevelBuilder.Bridge, new Color(0.45f, 0.7f, 0.5f), 0),             // deck on the current floor, underpass 2 floors below
        new("Bianca", '0', Color.white, 1),
        new("Grigia", '*', new Color(0.55f, 0.55f, 0.57f), 1), // grey balls: as many as you like (BallArt.Grey)
        new("Triangolo", 'R', new Color(1f, 0.85f, 0.3f), 1),
        new("Blocco", 'P', new Color(0.6f, 0.55f, 0.7f), 2),
        new("Spine", 'X', new Color(1f, 0.3f, 0.3f), 2),
        new("Respingente", 'Q', new Color(1f, 0.45f, 0.7f), 2),
        new("Pulsante", 'K', new Color(1f, 0.35f, 0.35f), 2),
        new("Cancello", LevelBuilder.Gate, new Color(1f, 0.8f, 0.3f), 2),
        new("Muro a tempo", LevelBuilder.TimedWall, new Color(0.7f, 0.5f, 1f), 2),
        new("Muro a tempo verde", LevelBuilder.TimedWallInv, new Color(0.4f, 0.85f, 0.5f), 2), // up while the purple ones are down
        new("Ventola", 'J', new Color(0.4f, 0.6f, 1f), 3),
        new("Spinta (R)", '>', new Color(1f, 0.7f, 0.3f), 3),
        new("Portale blu", LevelBuilder.PortalBlue, new Color(0.3f, 0.6f, 1f), 3),
        new("Portale aranc.", LevelBuilder.PortalOrange, new Color(1f, 0.6f, 0.2f), 3),
        new("Ghiaccio", 'I', new Color(0.75f, 0.9f, 1f), 3),
        new("Sabbia", 'M', new Color(0.95f, 0.8f, 0.5f), 3),
        new("Tombino", 'W', new Color(0.5f, 0.5f, 0.55f), 3),
        new("Calamita", 'Z', new Color(1f, 0.3f, 0.3f), 3),
        new("Ventilatore (R)", LevelBuilder.WindFan, new Color(0.55f, 0.8f, 1f), 3),
        new("Nastro (R)", 'N', new Color(0.5f, 0.5f, 0.55f), 4),
        new("Piattaforma", LevelBuilder.PlatformTile, new Color(0.4f, 0.8f, 0.65f), 4),
        new("Sega (R)", LevelBuilder.Saw, new Color(0.85f, 0.85f, 0.9f), 4),           // rides its track like a platform
        new("Estensore (R)", LevelBuilder.Track, new Color(0.7f, 0.72f, 0.78f), 4),    // track piece: platforms and saws turn to its angle
        new("Gomma", ' ', new Color(1f, 0.4f, 0.4f), -1),
    };

    public const string BallChars = "123456789ABCDEF";
    public const char MoveTool = '\u0001', CopyTool = '\u0002'; // brush values that aren't tiles
    public const int MaxFloor = 6, RotateStep = 15;

    public static bool IsTool(char c) => c is MoveTool or CopyTool;

    public const string DefaultTable =
        "O########O########O\n#.................#\n#.................#\n#.................#\n#.................#\n" +
        "#.................#\n#.................#\n#.................#\n#.................#\n#.................#\nO########O########O\n---\n0 12 15\n"; // cue ball on tile (4, 5)

    public Dictionary<Vector2Int, char> cells = new();
    public Dictionary<Vector2Int, char> objects = new(); // key = world sub cell (LevelBuilder.Sub per tile)
    public Dictionary<Vector2Int, int> angles = new();   // oriented tiles: degrees clockwise from the far side
    public Dictionary<Vector2Int, int> floors = new();   // raised cells
    public Dictionary<Vector2Int, char> under = new();   // rail / bridge under a module (missing = felt)
    public Dictionary<Vector2Int, List<(char c, int angle)>> above = new(); // modules piled on the grid's one, bottom to top
    public int par;    // fewest shots to win the level (crown); 0 = not set
    public int fouls;  // fouls forgiven per run ("falli N"); 0 = a foul restarts
    public int theme = -1; // user levels: world theme ("tema N"), -1 = none (the level's own world)

    public void SetText(string text, Vector2Int min)
    {
        objects = new();
        angles = new();
        floors = new();
        under = new();
        above = new();
        cells = LevelBuilder.ToCells(text ?? "", min, objects, angles, floors, under, above);
        par = LevelBuilder.ParOf(text ?? "");
        fouls = LevelBuilder.FoulsOf(text ?? "");
        theme = LevelBuilder.ThemeLine(text ?? "");
    }

    /// <summary>The level text; extras = the "tiri" / "falli" / "tema" lines (levels, not online pieces).</summary>
    public string GetText(out Vector2Int min, bool extras = true)
    {
        var text = LevelBuilder.ToText(cells, out min, objects, angles, floors, under, above);
        if (!extras) return text;
        var extra = (par > 0 ? $"{LevelBuilder.ParTag} {par}\n" : "") + (fouls > 0 ? $"{LevelBuilder.FoulsTag} {fouls}\n" : "")
            + (theme >= 0 ? $"{LevelBuilder.ThemeTag} {theme + 1}\n" : "");
        if (extra == "") return text;
        return text + (text.Split('\n').Contains("---") ? "" : "---\n") + extra;
    }

    // ---------- Sposta / Copia-Incolla ----------

    /// <summary>A grabbed piece of level, keyed relative to its anchor tile (objects: relative sub cells).</summary>
    public class Clip
    {
        public readonly Dictionary<Vector2Int, char> cells = new(), objects = new(), under = new();
        public readonly Dictionary<Vector2Int, int> angles = new(), floors = new();
        public readonly Dictionary<Vector2Int, List<(char c, int angle)>> above = new();
        public Vector2Int min, max; // tile footprint, relative
        public bool fine;   // just one object: follows the fine grid
        public bool single; // one tile picked up: it takes the floor of where it's dropped
    }

    public Clip clip;                                   // kept across levels and pieces: copy here, paste there
    (Vector2Int min, Vector2Int max)? cut;              // Sposta: source tiles cleared when dropped
    Vector2Int? cutObject;                              // Sposta: source object removed when dropped
    public bool Moving => cut.HasValue || cutObject.HasValue;

    // what Sposta leaves behind: obstacles become felt, the table itself (felt, rails, pockets) stays
    static bool Obstacle(char c) => c is not ('.' or '#' or 'O' or LevelBuilder.DiagRail);

    public static Vector2Int TileOf(Vector2Int sub) => new(Mathf.RoundToInt(sub.x / (float)LevelBuilder.Sub), Mathf.RoundToInt(sub.y / (float)LevelBuilder.Sub));

    public void CancelClip()
    {
        clip = null;
        cut = null;
        cutObject = null;
    }

    Clip Grab(Vector2Int a, Vector2Int b)
    {
        var min = Vector2Int.Min(a, b);
        var max = Vector2Int.Max(a, b);
        var center = min + (max - min) / 2;
        var c = new Clip { min = min - center, max = max - center };
        for (int x = min.x; x <= max.x; x++)
            for (int z = min.y; z <= max.y; z++)
            {
                var k = new Vector2Int(x, z);
                if (!cells.TryGetValue(k, out var t)) continue;
                c.cells[k - center] = t;
                if (angles.TryGetValue(k, out int an)) c.angles[k - center] = an;
                if (floors.TryGetValue(k, out int f)) c.floors[k - center] = f;
                if (under.TryGetValue(k, out char u)) c.under[k - center] = u;
                if (above.TryGetValue(k, out var pile)) c.above[k - center] = pile.ToList();
            }
        foreach (var o in objects)
        {
            var t = TileOf(o.Key);
            if (t.x >= min.x && t.x <= max.x && t.y >= min.y && t.y <= max.y) c.objects[o.Key - center * LevelBuilder.Sub] = o.Value;
        }
        return c;
    }

    /// <summary>End of a selection drag: Copia copies the area; Sposta picks up the object / obstacle clicked, or the obstacles of the area.</summary>
    public void Select(char tool, Vector2Int a, Vector2Int b, Vector2Int sub)
    {
        CancelClip();
        if (tool == CopyTool) { clip = Grab(a, b); return; }
        if (a == b)
        {
            foreach (var o in objects)
                if (Dist(o.Key, sub) < 0.4f)
                {
                    clip = new Clip { fine = true };
                    clip.objects[Vector2Int.zero] = o.Value;
                    cutObject = o.Key;
                    return;
                }
            if (!cells.TryGetValue(a, out var t) || !Obstacle(t)) return; // nothing to move here
        }
        clip = Grab(a, b);
        clip.single = a == b;
        cut = (Vector2Int.Min(a, b), Vector2Int.Max(a, b));
    }

    /// <summary>Drops / pastes the clip at the mouse (Sposta also clears where it came from).</summary>
    public bool Paste(Vector2Int at, Vector2Int sub)
    {
        if (clip == null) return false;
        const int S = LevelBuilder.Sub;
        if (cut.HasValue)
        {
            var (cmin, cmax) = cut.Value;
            for (int x = cmin.x; x <= cmax.x; x++)
                for (int z = cmin.y; z <= cmax.y; z++)
                {
                    var k = new Vector2Int(x, z);
                    if (cells.TryGetValue(k, out var t) && Obstacle(t))
                    {
                        cells[k] = LevelBuilder.OnTop(t) && under.Remove(k, out char u) ? u : '.';
                        angles.Remove(k);
                        above.Remove(k);
                    }
                }
            RemoveObjects(o => { var t = TileOf(o.Key); return t.x >= cmin.x && t.x <= cmax.x && t.y >= cmin.y && t.y <= cmax.y; });
        }
        if (cutObject is Vector2Int co) objects.Remove(co);
        if (clip.fine)
        {
            foreach (var o in clip.objects) PlaceObject(sub + o.Key, o.Value);
            return true;
        }
        foreach (var kv in clip.cells)
        {
            var k = at + kv.Key;
            if (kv.Value is LevelBuilder.PortalBlue or LevelBuilder.PortalOrange) // one of each per level: the old one goes
                foreach (var old in cells.Where(c => c.Value == kv.Value && c.Key != k).Select(c => c.Key).ToList()) cells[old] = '.';
            if (clip.single) // one module dropped: it stands on what's there, on that floor
            {
                LevelBuilder.SetTile(cells, angles, k, kv.Value, clip.angles.GetValueOrDefault(kv.Key), under, above);
                if (clip.above.TryGetValue(kv.Key, out var pile)) above[k] = pile.ToList(); // its pile goes with it
                continue;
            }
            cells[k] = kv.Value;
            if (clip.angles.TryGetValue(kv.Key, out int an)) angles[k] = an; else angles.Remove(k);
            if (clip.under.TryGetValue(kv.Key, out char u)) under[k] = u; else under.Remove(k);
            if (clip.above.TryGetValue(kv.Key, out var ab)) above[k] = ab.ToList(); else above.Remove(k);
            if (clip.floors.TryGetValue(kv.Key, out int f)) floors[k] = f; else floors.Remove(k);
        }
        RemoveObjects(o => clip.cells.ContainsKey(TileOf(o.Key) - at));
        foreach (var o in clip.objects) objects[at * S + o.Key] = o.Value;
        return true;
    }

    // ---------- brushes ----------

    /// <summary>
    /// The brush applied at the hovered cell / sub cell (or the rectangle from rectStart); returns whether anything changed.
    /// Tools paste the clip; erase = the eraser whatever the brush.
    /// </summary>
    public bool Apply(char brush, Vector2Int cell, Vector2Int sub, Vector2Int? rectStart, int angle, int floor, bool stack, bool erase)
    {
        if (IsTool(brush)) return !rectStart.HasValue && Paste(cell, sub);
        char c = erase ? ' ' : brush;
        if (!LevelBuilder.IsObject(brush)) return Paint(rectStart ?? cell, cell, c, angle, floor, stack);
        if (erase) return RemoveObjects(o => Dist(o.Key, sub) < 0.4f);
        return PlaceObject(sub, c);
    }

    public static float Dist(Vector2Int a, Vector2Int b) => ((Vector2)(a - b)).magnitude / LevelBuilder.Sub;

    public bool PlaceObject(Vector2Int sub, char c)
    {
        if (objects.TryGetValue(sub, out var old) && old == c) return false;
        // overlapping objects make way; balls and the rack exist once, so placing one moves it
        RemoveObjects(o => Dist(o.Key, sub) < 2 * Ball.Radius - 1e-3f || (o.Value == c && c != '*'));
        objects[sub] = c;
        return true;
    }

    public bool RemoveObjects(System.Func<KeyValuePair<Vector2Int, char>, bool> match)
    {
        var gone = objects.Where(match).Select(o => o.Key).ToList();
        foreach (var k in gone) objects.Remove(k);
        return gone.Count > 0;
    }

    /// <summary>Tiles only; objects stay on top of whatever tile is painted under them, erasing a tile removes them too.</summary>
    public bool Paint(Vector2Int a, Vector2Int b, char c, int angle, int floor, bool stack)
    {
        var min = Vector2Int.Min(a, b);
        var max = Vector2Int.Max(a, b);
        bool changed = false;
        for (int x = min.x; x <= max.x; x++)
            for (int z = min.y; z <= max.y; z++)
            {
                var k = new Vector2Int(x, z);
                if (c == LevelBuilder.FeltWithRails)
                {
                    var before = new HashSet<Vector2Int>(cells.Keys);
                    changed |= LevelBuilder.PaintFeltWithRails(cells, k);
                    angles.Remove(k);
                    under.Remove(k);
                    above.Remove(k);
                    changed |= SetFloor(k, floor);
                    foreach (var n in cells.Keys.Where(n => !before.Contains(n)).ToList()) SetFloor(n, floor); // new rails: same floor
                    continue;
                }
                if (c == ' ' && stack && LevelBuilder.EraseTop(cells, angles, under, above, k)) { changed = true; continue; } // only the top module goes
                if (c == ' ')
                {
                    above.Remove(k);
                    changed |= cells.Remove(k);
                    angles.Remove(k);
                    floors.Remove(k);
                    under.Remove(k);
                    continue;
                }
                bool existed = cells.ContainsKey(k);
                changed |= LevelBuilder.SetTile(cells, angles, k, c, angle, under, above, stack);
                if (!(existed && LevelBuilder.OnTop(c))) changed |= SetFloor(k, floor); // a module stands on the cell as it is, floor included
            }
        if (c == ' ')
            changed |= RemoveObjects(o =>
            {
                var t = TileOf(o.Key);
                return t.x >= min.x && t.x <= max.x && t.y >= min.y && t.y <= max.y;
            });
        return changed;
    }

    bool SetFloor(Vector2Int k, int floor)
    {
        if (floors.TryGetValue(k, out int f) ? f == floor : floor == 0) return false;
        if (floor == 0) floors.Remove(k); else floors[k] = floor;
        return true;
    }

    /// <summary>What's wrong with the level; goal = the ball to pot (0 = an online piece: no balls needed).</summary>
    public IEnumerable<string> Validate(int goal)
    {
        var all = cells.Values.Concat(objects.Values).ToList();
        int cues = all.Count(c => c == '0');
        if (goal > 0 && cues != 1) yield return $"Serve esattamente una bianca (ora: {cues}).";
        bool rack = all.Contains('R');
        foreach (var g in all.Where(c => BallChars.IndexOf(c) >= 0).GroupBy(c => c))
            if (g.Count() > 1 || rack) yield return $"Palla {System.Convert.ToInt32(g.Key.ToString(), 16)} presente più di una volta" + (rack ? " (il triangolo le contiene già)" : "");
        if (goal > 0 && !rack && !all.Contains(BallChars[goal - 1])) yield return $"Manca la palla del livello: la {goal} (obiettivo da mandare in buca).";
        if (!cells.Values.Contains('O')) yield return "Nessuna buca.";
        if (cells.Values.Contains(LevelBuilder.PortalBlue) != cells.Values.Contains(LevelBuilder.PortalOrange)) yield return "Portale senza il suo compagno: servono il blu e l'arancione.";
        if (cells.Values.Contains('G') && !cells.Values.Contains('K')) yield return "Cancello senza pulsante: resta sempre chiuso.";
    }
}
