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
        new("Bianca", '0', Color.white, 1),
        new("Grigia", '*', new Color(0.55f, 0.55f, 0.57f), 1), // grey balls: as many as you like (BallArt.Grey)
        new("Triangolo", 'R', new Color(1f, 0.85f, 0.3f), 1),
        new("Blocco", 'P', new Color(0.6f, 0.55f, 0.7f), 2),
        new("Spine", 'X', new Color(1f, 0.3f, 0.3f), 2),
        new("Spine a tempo", LevelBuilder.TimedSpikes, new Color(0.7f, 0.5f, 1f), 2),
        new("Spine a tempo verdi", LevelBuilder.TimedSpikesInv, new Color(0.4f, 0.85f, 0.5f), 2), // up while the purple ones are down
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

    // what each brush does, one line for the editor HUD (HelpOf)
    static readonly Dictionary<char, string> Help = new()
    {
        ['.'] = "Panno: il tavolo su cui rotolano le palle.",
        [LevelBuilder.FeltWithRails] = "Panno con sponde automatiche intorno; dipinto su una sponda la apre, così i pezzi si uniscono.",
        ['#'] = "Sponda: la palla ci rimbalza.",
        [LevelBuilder.DiagRail] = "Sponda diagonale: metà sponda, metà panno, cuscino in diagonale (gira di 90°).",
        ['O'] = "Buca: la palla ci cade dentro. Prende la forma da sola: laterale, d'angolo o buco nel panno.",
        [LevelBuilder.Ramp] = "Rampa: sale di un piano verso la freccia. Se arriva in cima veloce, la palla vola.",
        ['0'] = "Bianca: la palla che colpisci con la stecca. Ce n'è una sola.",
        ['*'] = "Grigia: palla senza numero, quante vuoi. Serve da colpire per spostarsi senza fallo.",
        ['R'] = "Triangolo: le 15 palle del biliardo, vertice qui e file verso destra.",
        ['P'] = "Blocco: cubo pieno alto 1, non si scavalca nemmeno saltando.",
        ['X'] = "Spine: la palla che ci entra muore (come fuori dal tavolo).",
        [LevelBuilder.TimedSpikes] = "Spine a tempo: 2 secondi su (la palla muore), 2 giù (si passa). Partono su.",
        [LevelBuilder.TimedSpikesInv] = "Spine a tempo verdi: al contrario delle viola (giù quando loro sono su). Partono giù.",
        ['Q'] = "Respingente: palo tondo che spinge via la palla a ogni urto.",
        ['K'] = "Pulsante: finché una palla ci sta sopra, tutti i cancelli sono abbassati.",
        [LevelBuilder.Gate] = "Cancello: muro alto 1, giù solo col pulsante premuto. Se si alza sotto una palla la lancia in alto.",
        [LevelBuilder.TimedWall] = "Muro a tempo: 2 secondi su, 2 giù. Parte su.",
        [LevelBuilder.TimedWallInv] = "Muro a tempo verde: al contrario dei viola (giù quando loro sono su). Parte giù.",
        ['J'] = "Ventola: la palla che ci passa sopra salta e scavalca vuoti e palle.",
        ['>'] = "Spinta: la palla che arriva al centro riparte nella direzione della freccia (R / Q la girano di 15°).",
        [LevelBuilder.PortalBlue] = "Portale blu: entrando si esce dall'arancione, girati della differenza fra i due angoli. Uno per livello.",
        [LevelBuilder.PortalOrange] = "Portale arancione: entrando si esce dal blu. Uno per livello.",
        ['I'] = "Ghiaccio: niente attrito, la palla scivola quasi senza rallentare.",
        ['M'] = "Sabbia: frena molto.",
        ['W'] = "Tombino: la palla che ci entra si ferma al centro.",
        ['Z'] = "Calamita: attira le palle in movimento vicine; se rallentano molto le lascia fermare.",
        [LevelBuilder.WindFan] = "Ventilatore: blocco solido che soffia per 3 tile verso la freccia, fermato da sponde e blocchi.",
        ['N'] = "Nastro: trasporta la palla verso la freccia.",
        [LevelBuilder.PlatformTile] = "Piattaforma mobile: niente tavolo sotto, porta chi ci sta sopra. Segue gli estensori; senza, va dritta sul vuoto.",
        [LevelBuilder.Saw] = "Sega: lama che gira e segue gli estensori; la palla che la tocca muore.",
        [LevelBuilder.Track] = "Estensore: pezzo di percorso per piattaforme e seghe; chi ci arriva gira verso la sua direzione. W / S lo girano in su / giù: la piattaforma sale (scende) all'estensore sopra (sotto).",
        [' '] = "Gomma: cancella tile, moduli e palle (con «Sovrapposizione: sì» solo il modulo in cima).",
    };

    /// <summary>What the brush does, for the editor HUD ("" for the tools).</summary>
    public static string HelpOf(char c)
    {
        if (Help.TryGetValue(c, out var s)) return s;
        int n = BallChars.IndexOf(c) + 1;
        return n > 0 ? $"Palla {n}: si può imbucare o perdere liberamente" + (n == LevelEditor.Goal ? " (nei tuoi livelli è l'obiettivo)." : ".") : "";
    }

    public const string BallChars = "123456789ABCDEF";
    public const char MoveTool = '\u0001', CopyTool = '\u0002'; // brush values that aren't tiles
    public const int MaxFloor = 6, RotateStep = 15;

    public static bool IsTool(char c) => c is MoveTool or CopyTool;

    public const string DefaultTable =
        "O########O########O\n#.................#\n#.................#\n#.................#\n#.................#\n" +
        "#.................#\n#.................#\n#.................#\n#.................#\n#.................#\nO########O########O\n---\n0 12 15\n"; // cue ball on tile (4, 5)

    // Every floor is a level of its own: the brushes and tools work on the one being edited (`floor`, set by Apply / Paint)
    // and never touch the others. A cell on floor 2 over one on floor 0 is a bridge.
    public SortedDictionary<int, LevelBuilder.FloorPlan> plans = new();
    public int floor;
    /// <summary>Editor switch "Colonna piena": what gets painted is a column, full down to what's under it; off = a slab, nothing under it.</summary>
    public bool column;
    /// <summary>Every raised cell is a column (old levels); setting it makes every cell a column, or none.</summary>
    public bool solid
    {
        get { var up = plans.Values.Where(p => p.cells.Count > 0).Skip(1).ToList(); return up.Count > 0 && up.All(p => p.cells.Keys.All(p.solid.Contains)); }
        set { foreach (var p in plans.Values) { p.solid.Clear(); if (value) p.solid.UnionWith(p.cells.Keys); } }
    }
    LevelBuilder.FloorPlan Plan => plans.TryGetValue(floor, out var p) ? p : plans[floor] = new LevelBuilder.FloorPlan();
    public Dictionary<Vector2Int, char> cells => Plan.cells;
    public Dictionary<Vector2Int, char> objects => Plan.objects; // key = world sub cell (LevelBuilder.Sub per tile)
    public Dictionary<Vector2Int, int> angles => Plan.angles;    // oriented tiles: degrees clockwise from the far side
    public Dictionary<Vector2Int, char> under => Plan.under;     // rail under a module (missing = felt)
    public Dictionary<Vector2Int, List<(char c, int angle)>> above => Plan.above; // modules piled on the grid's one, bottom to top
    /// <summary>No tile on any floor.</summary>
    public bool Empty => plans.Values.All(p => p.cells.Count == 0);
    /// <summary>Every tile and object of every floor.</summary>
    public IEnumerable<char> All => plans.Values.SelectMany(p => p.cells.Values.Concat(p.objects.Values));
    /// <summary>Every floor's cells (for framing the whole level).</summary>
    public IEnumerable<Vector2Int> AllCells => plans.Values.SelectMany(p => p.cells.Keys);
    public int par;    // fewest shots to win the level (crown); 0 = not set
    public int fouls;  // fouls forgiven per run ("falli N"); 0 = a foul restarts
    public int theme = -1; // user levels: world theme ("tema N"), -1 = none (the level's own world)

    public void SetText(string text, Vector2Int min)
    {
        plans = LevelBuilder.ReadFloors(text ?? "", min);
        par = LevelBuilder.ParOf(text ?? "");
        fouls = LevelBuilder.FoulsOf(text ?? "");
        theme = LevelBuilder.ThemeLine(text ?? "");
    }

    /// <summary>The level text; extras = the "tiri" / "falli" / "tema" lines (levels, not online pieces).</summary>
    public string GetText(out Vector2Int min, bool extras = true)
    {
        var extra = (par > 0 ? $"{LevelBuilder.ParTag} {par}\n" : "") + (fouls > 0 ? $"{LevelBuilder.FoulsTag} {fouls}\n" : "")
            + (theme >= 0 ? $"{LevelBuilder.ThemeTag} {theme + 1}\n" : "");
        return LevelBuilder.WriteFloors(plans, out min, extras ? extra : "");
    }

    /// <summary>Makes `goal` (1..15) the level's goal ball: swaps it with the 8 (user levels' goal) when present, or renumbers the highest ball.</summary>
    public void SetGoal(int goal)
    {
        if (goal <= 0 || All.Contains('R')) return;
        var balls = plans.Values.SelectMany(p => p.objects.Values).ToList();
        char target = BallChars[goal - 1];
        char from = balls.Contains('8') ? '8'
            : !balls.Contains(target) ? balls.Where(c => BallChars.IndexOf(c) >= 0).OrderBy(c => BallChars.IndexOf(c)).LastOrDefault() : default;
        if (from == default || from == target) return;
        foreach (var objs in plans.Values.Select(p => p.objects))
            foreach (var k in objs.Keys.ToList())
                if (objs[k] == from) objs[k] = target;
                else if (objs[k] == target) objs[k] = from;
    }

    // ---------- Sposta / Copia-Incolla ----------

    /// <summary>A grabbed piece of one floor, keyed relative to its anchor tile (objects: relative sub cells).</summary>
    public class Clip
    {
        public readonly Dictionary<Vector2Int, char> cells = new(), objects = new(), under = new();
        public readonly Dictionary<Vector2Int, int> angles = new();
        public readonly Dictionary<Vector2Int, List<(char c, int angle)>> above = new();
        public readonly HashSet<Vector2Int> solid = new();
        public Vector2Int min, max; // tile footprint, relative
        public bool fine;   // just one object: follows the fine grid
        public bool single; // one tile picked up: dropped on a module's base it keeps that base
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
                if (under.TryGetValue(k, out char u)) c.under[k - center] = u;
                if (above.TryGetValue(k, out var pile)) c.above[k - center] = pile.ToList();
                if (Plan.solid.Contains(k)) c.solid.Add(k - center);
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

    // 90° clockwise around the clip's anchor: far side (+z) -> right (+x), like LevelBuilder.PieceCells
    static Vector2Int Turn(Vector2Int k) => new(k.y, -k.x);

    /// <summary>Turns the clip in hand by 90° steps (clockwise for positive turns); oriented tiles turn with it.</summary>
    // shortcut: a rack keeps growing toward +x after a turn (its rows are fixed), rotate it in Rack if level makers ask
    public void RotateClip(int turns)
    {
        if (clip == null || clip.fine) return;
        for (turns = (turns % 4 + 4) % 4; turns > 0; turns--)
        {
            var r = new Clip { single = clip.single, min = new Vector2Int(clip.min.y, -clip.max.x), max = new Vector2Int(clip.max.y, -clip.min.x) };
            foreach (var kv in clip.cells) r.cells[Turn(kv.Key)] = kv.Value;
            foreach (var kv in clip.objects) r.objects[Turn(kv.Key)] = kv.Value;
            foreach (var kv in clip.under) r.under[Turn(kv.Key)] = kv.Value;
            foreach (var k in clip.solid) r.solid.Add(Turn(k));
            foreach (var kv in clip.angles) r.angles[Turn(kv.Key)] = (kv.Value + 90) % 360;
            foreach (var kv in clip.above) r.above[Turn(kv.Key)] = kv.Value.Select(e => (e.c, LevelBuilder.IsOriented(e.c) ? (e.angle + 90) % 360 : e.angle)).ToList();
            clip = r;
        }
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
            if (clip.solid.Contains(kv.Key)) Plan.solid.Add(k); else Plan.solid.Remove(k);
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
        this.floor = floor;
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
        // overlapping objects make way; balls and the rack exist once (on any floor), so placing one moves it
        RemoveObjects(o => Dist(o.Key, sub) < 2 * Ball.Radius - 1e-3f);
        if (c != '*')
            foreach (var p in plans.Values)
                foreach (var k in p.objects.Where(o => o.Value == c).Select(o => o.Key).ToList()) p.objects.Remove(k);
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
        this.floor = floor;
        var min = Vector2Int.Min(a, b);
        var max = Vector2Int.Max(a, b);
        bool changed = false;
        bool Column(Vector2Int k) => column ? Plan.solid.Add(k) : Plan.solid.Remove(k); // the switch decides for everything painted
        if (c is LevelBuilder.PortalBlue or LevelBuilder.PortalOrange) // one of each per level: the one on another floor goes
            foreach (var p in plans.Where(kv => kv.Key != floor).Select(kv => kv.Value))
                foreach (var k in p.cells.Where(kv => kv.Value == c).Select(kv => kv.Key).ToList()) p.cells[k] = '.';
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
                    changed |= before.Count != cells.Count;
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            var n = k + new Vector2Int(dx, dz);
                            if (n == k || !before.Contains(n)) changed |= Column(n);
                        }
                    continue;
                }
                if (c == ' ' && stack && LevelBuilder.EraseTop(cells, angles, under, above, k)) { changed = true; continue; } // only the top module goes
                if (c == ' ')
                {
                    above.Remove(k);
                    changed |= cells.Remove(k);
                    Plan.solid.Remove(k);
                    angles.Remove(k);
                    under.Remove(k);
                    continue;
                }
                changed |= LevelBuilder.SetTile(cells, angles, k, c, angle, under, above, stack) | Column(k);
            }
        if (c == ' ')
            changed |= RemoveObjects(o =>
            {
                var t = TileOf(o.Key);
                return t.x >= min.x && t.x <= max.x && t.y >= min.y && t.y <= max.y;
            });
        return changed;
    }

    /// <summary>What's wrong with the level; goal = the ball to pot (0 = an online piece: no balls needed).</summary>
    public IEnumerable<string> Validate(int goal)
    {
        var all = All.ToList();
        int cues = all.Count(c => c == '0');
        if (goal > 0 && cues != 1) yield return $"Serve esattamente una bianca (ora: {cues}).";
        bool rack = all.Contains('R');
        foreach (var g in all.Where(c => BallChars.IndexOf(c) >= 0).GroupBy(c => c))
            if (g.Count() > 1 || rack) yield return $"Palla {System.Convert.ToInt32(g.Key.ToString(), 16)} presente più di una volta" + (rack ? " (il triangolo le contiene già)" : "");
        if (goal > 0 && !rack && !all.Contains(BallChars[goal - 1])) yield return $"Manca la palla del livello: la {goal} (obiettivo da mandare in buca).";
        if (!all.Contains('O')) yield return "Nessuna buca.";
        if (all.Contains(LevelBuilder.PortalBlue) != all.Contains(LevelBuilder.PortalOrange)) yield return "Portale senza il suo compagno: servono il blu e l'arancione.";
        if (all.Contains('G') && !all.Contains('K')) yield return "Cancello senza pulsante: resta sempre chiuso.";
    }
}
