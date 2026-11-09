using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// MiniPool > Editor Livelli: paint tiles on a grid in the Scene view. The live preview is built by the same
// LevelBuilder the game uses, so rails merge and grow cushions automatically. Saves to Resources/Levels/levelNN.txt.
// Two layers: tiles (1x1 grid) and objects on top of them (balls, rack, pillars) on a 3x finer grid.
// While hovering, the preview already shows the level as it would be with the brush applied there, under a tinted box.
public class LevelEditorWindow : EditorWindow
{
    const string Dir = "Assets/Resources/Levels", PreviewName = "LevelEditorPreview";
    const int RotateStep = 15; // degrees per R press (Shift+R turns back)

    struct Brush
    {
        public string label;
        public char c;
        public Color color;
        public Brush(string label, char c, Color color) { this.label = label; this.c = c; this.color = color; }
    }

    static readonly Brush[] Brushes =
    {
        new("Panno", '.', new Color(0.2f, 0.7f, 0.35f)),
        new("Panno+sponde", LevelBuilder.FeltWithRails, new Color(0.3f, 0.75f, 0.45f)),
        new("Sponda", '#', new Color(0.75f, 0.45f, 0.25f)),
        new("Sponda diag. (R)", LevelBuilder.DiagRail, new Color(0.75f, 0.45f, 0.25f)),
        new("Buca", 'O', new Color(0.25f, 0.25f, 0.25f)),
        new("Bianca", '0', Color.white),
        new("Grigia", '*', new Color(0.55f, 0.55f, 0.57f)), // grey balls: as many as you like (BallArt.Grey)
        new("Triangolo", 'R', new Color(1f, 0.85f, 0.3f)),
        new("Blocco", 'P', new Color(0.6f, 0.55f, 0.7f)),
        new("Spine", 'X', new Color(1f, 0.3f, 0.3f)),
        new("Ventola", 'J', new Color(0.4f, 0.6f, 1f)),
        new("Spinta (R)", '>', new Color(1f, 0.7f, 0.3f)),
        new("Rampa (R)", LevelBuilder.Ramp, new Color(0.3f, 0.75f, 0.45f)),
        new("Portale blu", LevelBuilder.PortalBlue, new Color(0.3f, 0.6f, 1f)),
        new("Portale aranc.", LevelBuilder.PortalOrange, new Color(1f, 0.6f, 0.2f)),
        new("Ghiaccio", 'I', new Color(0.75f, 0.9f, 1f)),
        new("Sabbia", 'M', new Color(0.95f, 0.8f, 0.5f)),
        new("Tombino", 'W', new Color(0.5f, 0.5f, 0.55f)),
        new("Pulsante", 'K', new Color(1f, 0.35f, 0.35f)),
        new("Cancello", LevelBuilder.Gate, new Color(1f, 0.8f, 0.3f)),
        new("Muro a tempo", LevelBuilder.TimedWall, new Color(0.7f, 0.5f, 1f)),
        new("Respingente", 'Q', new Color(1f, 0.45f, 0.7f)),
        new("Nastro (R)", 'N', new Color(0.5f, 0.5f, 0.55f)),
        new("Calamita", 'Z', new Color(1f, 0.3f, 0.3f)),
        new("Piattaforma", LevelBuilder.PlatformTile, new Color(0.4f, 0.8f, 0.65f)),
        new("Gomma", ' ', new Color(1f, 0.4f, 0.4f)),
    };

    static readonly Dictionary<string, Texture> icons = new();

    // 3D thumbnail of a brush: a 3x3 felt patch with the tile in the middle, built by LevelBuilder like the real level
    static Texture Icon(string text)
    {
        if (icons.TryGetValue(text, out var tex) && tex) return tex;
        var pru = new PreviewRenderUtility();
        var lv = LevelBuilder.Build(text, "BrushIcon");
        try
        {
            pru.AddSingleGO(lv.root.gameObject);
            pru.camera.fieldOfView = 30;
            pru.camera.nearClipPlane = 0.1f;
            pru.camera.farClipPlane = 100;
            pru.camera.clearFlags = CameraClearFlags.SolidColor;
            pru.camera.backgroundColor = new Color(0.18f, 0.18f, 0.2f);
            var look = new Vector3(1, 0.1f, 1);
            pru.camera.transform.position = look + new Vector3(0, 3.2f, -3.2f) * (text.Contains('R') ? 1.8f : 1f);
            pru.camera.transform.LookAt(look);
            pru.lights[0].intensity = 1.3f;
            pru.lights[0].transform.rotation = Quaternion.Euler(50, 30, 0);
            pru.ambientColor = new Color(0.35f, 0.35f, 0.35f);
            pru.BeginStaticPreview(new Rect(0, 0, 128, 128));
            pru.Render(true);
            return icons[text] = pru.EndStaticPreview();
        }
        finally
        {
            DestroyImmediate(lv.root.gameObject);
            pru.Cleanup();
        }
    }

    Texture BrushIcon(char c) =>
        c == ' ' ? null : c == LevelBuilder.FeltWithRails ? Icon("###\n#.#\n###")
            : Icon(LevelBuilder.IsOriented(c) ? $"...\n.{c}.\n...\n---\n> 1 1 {boostAngle}" : $"...\n.{c}.\n...");

    const string BallChars = "123456789ABCDEF";

    const string DefaultTable =
        "O########O########O\n#.................#\n#.................#\n#.................#\n#.................#\n" +
        "#.................#\n#.................#\n#.................#\n#.................#\n#.................#\nO########O########O\n---\n0 12 15\n"; // cue ball on tile (4, 5)

    [SerializeField] int level = 1;
    [SerializeField] string draft;          // survives script reloads
    [SerializeField] Vector2Int draftMin;
    [SerializeField] int par;               // fewest shots to win the level (crown); 0 = not set
    [SerializeField] bool dirty, painting = true;
    [SerializeField] char brush = '#';
    [SerializeField] int boostAngle = 90;
    [SerializeField] int floor;             // painted tiles go on this floor (y = floor * FloorStep)

    Dictionary<Vector2Int, char> cells = new();
    Dictionary<Vector2Int, char> objects = new(); // key = world sub cell (LevelBuilder.Sub per tile)
    Dictionary<Vector2Int, int> angles = new();   // oriented tiles: degrees clockwise from the far side
    Dictionary<Vector2Int, int> floors = new();   // raised cells
    GameObject preview;
    Vector2Int? rectStart;
    Vector2 scroll;

    // hover preview
    string hoverKey;
    bool hovering, hoverChanges, hoverErase;
    Vector2Int hoverCell, hoverSub;

    [MenuItem("MiniPool/Editor Livelli")]
    static void Open() => GetWindow<LevelEditorWindow>("Editor Livelli");

    static string PathOf(int i) => $"{Dir}/level{i:00}.txt";

    // multiplayer field pieces (Resources/Pieces/<name>.txt) are edited here too: piece set = editing that piece instead of a level
    const string PieceDir = "Assets/Resources/Pieces";
    const string DefaultPiece = "#####\n#...#\n#...#\n#...#\n#####\n";
    [SerializeField] string piece;
    [SerializeField] string newPieceName = "";
    [SerializeField] bool pieceOverlay; // the piece goes on top of the table (LevelBuilder.OverlayTag), saved as that line
    bool EditingPiece => !string.IsNullOrEmpty(piece);
    string CurrentPath => EditingPiece ? $"{PieceDir}/{piece}.txt" : PathOf(level);

    static string[] PieceNames() => Directory.Exists(PieceDir)
        ? Directory.GetFiles(PieceDir, "*.txt").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToArray()
        : new string[0];

    void LoadPiece(string name)
    {
        if (Moving) CancelClip();
        piece = name;
        var path = CurrentPath;
        if (File.Exists(path)) SetText(File.ReadAllText(path), Vector2Int.zero);
        else SetText(DefaultPiece, Vector2Int.zero);
        pieceOverlay = File.Exists(path) && LevelBuilder.IsOverlay(File.ReadAllText(path));
        draft = GetText(out draftMin);
        dirty = !File.Exists(path);
        Rebuild();
    }

    // second row: pick / create / delete a field piece
    void PieceGUI()
    {
        var names = PieceNames();
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Pezzi online", GUILayout.Width(80));
            int sel = EditingPiece ? System.Array.IndexOf(names, piece) : -1;
            int pick = EditorGUILayout.Popup(sel, names);
            if (pick != sel && pick >= 0 && ConfirmDiscard()) LoadPiece(names[pick]);
            newPieceName = EditorGUILayout.TextField(newPieceName, GUILayout.Width(110));
            bool valid = newPieceName.Trim().Length > 0 && newPieceName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !names.Contains(newPieceName.Trim());
            using (new EditorGUI.DisabledScope(!valid))
                if (GUILayout.Button("Nuovo pezzo") && ConfirmDiscard())
                {
                    LoadPiece(newPieceName.Trim());
                    newPieceName = "";
                }
            using (new EditorGUI.DisabledScope(!EditingPiece))
                if (GUILayout.Button("Elimina pezzo") && EditorUtility.DisplayDialog("Elimina pezzo", $"Eliminare il pezzo {piece}?", "Elimina", "Annulla"))
                {
                    AssetDatabase.DeleteAsset(CurrentPath);
                    dirty = false;
                    Load(level);
                }
        }
        if (EditingPiece)
        {
            bool o = EditorGUILayout.ToggleLeft("Sovrapposizione: si piazza sopra il tavolo (panno o sponde), non attaccato di fianco", pieceOverlay);
            if (o != pieceOverlay) { pieceOverlay = o; dirty = true; }
        }
    }

    static int[] LevelIndices() => Directory.Exists(Dir)
        ? Directory.GetFiles(Dir, "level*.txt").Select(f => int.TryParse(Path.GetFileNameWithoutExtension(f).Substring(5), out int n) ? n : 0)
            .Where(n => n > 0).OrderBy(n => n).ToArray()
        : new int[0];

    void OnEnable()
    {
        // previews leaked by a previous script reload
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            if (go.name == PreviewName && go.transform.parent == null && !EditorUtility.IsPersistent(go)) { Deselect(go); DestroyImmediate(go); }

        if (draft != null) SetText(draft, draftMin);
        else Load(level);
        SceneView.duringSceneGui += OnScene;
        EditorApplication.playModeStateChanged += OnPlayMode;
        Rebuild();
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnScene;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        DestroyPreview();
    }

    void OnPlayMode(PlayModeStateChange s)
    {
        if (s == PlayModeStateChange.ExitingEditMode) DestroyPreview();
        if (s == PlayModeStateChange.EnteredEditMode) Rebuild();
    }

    void SetText(string text, Vector2Int min)
    {
        objects = new Dictionary<Vector2Int, char>();
        angles = new Dictionary<Vector2Int, int>();
        floors = new Dictionary<Vector2Int, int>();
        cells = LevelBuilder.ToCells(text, min, objects, angles, floors);
        par = LevelBuilder.ParOf(text);
    }

    // levels only: par = fewest shots to win (crown), saved as the "tiri N" line
    string GetText(out Vector2Int min)
    {
        var text = LevelBuilder.ToText(cells, out min, objects, angles, floors);
        if (par <= 0 || EditingPiece) return text;
        return text + (text.Split('\n').Contains("---") ? "" : "---\n") + $"{LevelBuilder.ParTag} {par}\n";
    }

    // ---------- Sposta / Copia-Incolla tools ----------
    const char MoveTool = '\u0001', CopyTool = '\u0002'; // brush values that aren't tiles

    // a grabbed piece of level, keyed relative to its anchor tile (objects: relative sub cells)
    class Clip
    {
        public readonly Dictionary<Vector2Int, char> cells = new(), objects = new();
        public readonly Dictionary<Vector2Int, int> angles = new(), floors = new();
        public Vector2Int min, max; // tile footprint, relative
        public bool fine;   // just one object: follows the fine grid
        public bool single; // one tile picked up: it takes the floor of where it's dropped
    }

    Clip clip;                                   // kept across levels and pieces: copy here, paste there
    (Vector2Int min, Vector2Int max)? cut;       // Sposta: source tiles cleared when dropped
    Vector2Int? cutObject;                       // Sposta: source object removed when dropped
    bool Tool => brush is MoveTool or CopyTool;
    bool Moving => cut.HasValue || cutObject.HasValue;

    // what Sposta leaves behind: obstacles become felt, the table itself (felt, rails, pockets) stays
    static bool Obstacle(char c) => c is not ('.' or '#' or 'O' or LevelBuilder.DiagRail);

    static Vector2Int TileOf(Vector2Int sub) => new(Mathf.RoundToInt(sub.x / (float)LevelBuilder.Sub), Mathf.RoundToInt(sub.y / (float)LevelBuilder.Sub));

    void SetBrush(char c)
    {
        if (c != brush && Moving) CancelClip(); // a pick-up never survives a brush change
        brush = c;
    }

    void CancelClip()
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
            }
        foreach (var o in objects)
        {
            var t = TileOf(o.Key);
            if (t.x >= min.x && t.x <= max.x && t.y >= min.y && t.y <= max.y) c.objects[o.Key - center * LevelBuilder.Sub] = o.Value;
        }
        return c;
    }

    // end of a selection drag: Copia copies the area; Sposta picks up the object / obstacle clicked, or the obstacles of the area
    void Select(Vector2Int a, Vector2Int b)
    {
        CancelClip();
        if (brush == CopyTool) { clip = Grab(a, b); return; }
        if (a == b)
        {
            foreach (var o in objects)
                if (Dist(o.Key, hoverSub) < 0.4f)
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

    // drops / pastes the clip at the mouse (Sposta also clears where it came from); used by the hover preview too
    bool Paste()
    {
        if (clip == null || rectStart.HasValue) return false;
        const int S = LevelBuilder.Sub;
        if (cut.HasValue)
        {
            var (cmin, cmax) = cut.Value;
            for (int x = cmin.x; x <= cmax.x; x++)
                for (int z = cmin.y; z <= cmax.y; z++)
                {
                    var k = new Vector2Int(x, z);
                    if (cells.TryGetValue(k, out var t) && Obstacle(t)) { cells[k] = '.'; angles.Remove(k); }
                }
            RemoveObjects(o => { var t = TileOf(o.Key); return t.x >= cmin.x && t.x <= cmax.x && t.y >= cmin.y && t.y <= cmax.y; });
        }
        if (cutObject is Vector2Int co) objects.Remove(co);
        if (clip.fine)
        {
            foreach (var o in clip.objects) PlaceObject(hoverSub + o.Key, o.Value);
            return true;
        }
        var at = hoverCell;
        foreach (var kv in clip.cells)
        {
            var k = at + kv.Key;
            if (kv.Value is LevelBuilder.PortalBlue or LevelBuilder.PortalOrange) // one of each per level: the old one goes
                foreach (var old in cells.Where(c => c.Value == kv.Value && c.Key != k).Select(c => c.Key).ToList()) cells[old] = '.';
            cells[k] = kv.Value;
            if (clip.angles.TryGetValue(kv.Key, out int an)) angles[k] = an; else angles.Remove(k);
            if (clip.single) continue;
            if (clip.floors.TryGetValue(kv.Key, out int f)) floors[k] = f; else floors.Remove(k);
        }
        RemoveObjects(o => clip.cells.ContainsKey(TileOf(o.Key) - at));
        foreach (var o in clip.objects) objects[at * S + o.Key] = o.Value;
        return true;
    }

    void ToolButton(string label, char c)
    {
        if (GUILayout.Toggle(brush == c, label, "Button", GUILayout.Height(24)) && brush != c) SetBrush(c);
    }

    // R turns an oriented brush clockwise, Q (or Shift+R) back (Scene view or this window); PageUp / PageDown change the floor
    bool HandleRotate(Event e)
    {
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && clip != null)
        {
            CancelClip();
            e.Use();
            if (hovering) ShowHover(true);
            Repaint();
            SceneView.RepaintAll();
            return true;
        }
        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.PageUp || e.keyCode == KeyCode.PageDown))
        {
            floor = Mathf.Clamp(floor + (e.keyCode == KeyCode.PageUp ? 1 : -1), 0, MaxFloor);
            e.Use();
            if (hovering) ShowHover(true);
            Repaint();
            SceneView.RepaintAll();
            return true;
        }
        if (e.type != EventType.KeyDown || (e.keyCode != KeyCode.R && e.keyCode != KeyCode.Q) || !LevelBuilder.IsOriented(brush)) return false;
        boostAngle = ((boostAngle + (e.shift || e.keyCode == KeyCode.Q ? -RotateStep : RotateStep)) % 360 + 360) % 360;
        e.Use();
        if (hovering) ShowHover(true);
        Repaint();
        SceneView.RepaintAll();
        return true;
    }

    void OnGUI()
    {
        HandleRotate(Event.current);
        scroll = EditorGUILayout.BeginScrollView(scroll);

        var indices = LevelIndices();
        using (new EditorGUILayout.HorizontalScope())
        {
            int sel = EditingPiece ? -1 : System.Array.IndexOf(indices, level);
            int pick = EditorGUILayout.Popup(sel, indices.Select(i => $"level{i:00}").ToArray());
            if (pick != sel && pick >= 0 && ConfirmDiscard()) Load(indices[pick]);
            if (GUILayout.Button("Nuovo") && ConfirmDiscard())
            {
                piece = null;
                level = indices.Length == 0 ? 1 : indices.Max() + 1;
                SetText(DefaultTable, Vector2Int.zero);
                Changed();
            }
            if (GUILayout.Button(dirty ? "Salva *" : "Salva")) Save();
            using (new EditorGUI.DisabledScope(EditingPiece))
                if (GUILayout.Button("Prova ▶")) Test();
        }
        PieceGUI();
        // copy a level, pick another one, paste: it replaces the level shown (unsaved until Salva)
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Copia livello")) EditorGUIUtility.systemCopyBuffer = GetText(out _);
            if (GUILayout.Button("Incolla livello"))
            {
                var text = EditorGUIUtility.systemCopyBuffer;
                if (string.IsNullOrWhiteSpace(text) || LevelBuilder.ToCells(text, Vector2Int.zero).Count == 0)
                    ShowNotification(new GUIContent("Negli appunti non c'è un livello"));
                else
                {
                    SetText(text, Vector2Int.zero);
                    Changed();
                }
            }
        }
        EditorGUILayout.LabelField($"File: {CurrentPath}", EditorStyles.miniLabel);
        if (!EditingPiece)
        {
            int p = Mathf.Max(0, EditorGUILayout.IntField(new GUIContent("Tiri minimi (corona)", "Con quanti tiri si può finire il livello: chi ci riesce prende la corona. 0 = nessuna corona"), par));
            if (p != par) { par = p; dirty = true; draft = GetText(out draftMin); }
        }
        if (EditingPiece)
            EditorGUILayout.HelpBox("Pezzo online: chiudilo con sponde. Quando si attacca, le sue sponde sovrapposte a quelle del tavolo si aprono dove c'è panno da entrambi i lati. Niente palle. Il centro del pezzo segue il mouse.", MessageType.None);

        bool wasPainting = painting;
        painting = EditorGUILayout.ToggleLeft("Disegna nella Scene view", painting);
        if (wasPainting && !painting) EndHover();
        EditorGUILayout.HelpBox("Click / trascina = disegna   Shift + trascina = rettangolo (tile)\nCtrl + click = cancella   R / Q = ruota (spinta, rampa, portali, nastro, piattaforma)   Alt = naviga la scena\n" +
            "Piano: i tile disegnati vanno sul piano scelto (PagSu / PagGiù); la rampa sale di un piano verso la sua freccia.\n" +
            "Palle e triangolo stanno sopra i tile, su una griglia 3 volte più fine.\nIl riquadro azzurro è l'anteprima: il livello si vede già come sarebbe, ma non è ancora disegnato.", MessageType.None);

        EditorGUILayout.LabelField("Strumenti", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            ToolButton("Sposta", MoveTool);
            ToolButton("Copia / Incolla", CopyTool);
            if (clip != null && GUILayout.Button("Svuota (Esc)", GUILayout.Height(24))) CancelClip();
        }
        if (clip != null)
            EditorGUILayout.LabelField(clip.fine ? "In mano: una palla" : $"{(Moving ? "In mano" : "Appunti")}: {clip.max.x - clip.min.x + 1}×{clip.max.y - clip.min.y + 1} tile", EditorStyles.miniLabel);
        if (brush == MoveTool)
            EditorGUILayout.HelpBox("Sposta: clic su un ostacolo o una palla per prenderlo (trascina = tutti gli ostacoli di un'area), poi clic dove lasciarlo. " +
                "Dove era resta il panno: panno, sponde e buche non si spostano. Esc = annulla.", MessageType.None);
        if (brush == CopyTool)
            EditorGUILayout.HelpBox("Copia: trascina per copiare un'area (tutto: tile, piani, palle), poi clic per incollarla, anche più volte o in un altro livello / pezzo. " +
                "Shift + trascina = nuova copia. Esc = svuota.", MessageType.None);

        EditorGUILayout.LabelField("Tile e oggetti", EditorStyles.boldLabel);
        int perRow = Mathf.Max(1, (int)(position.width - 10) / 84);
        for (int i = 0; i < Brushes.Length; i += perRow)
            using (new EditorGUILayout.HorizontalScope())
                foreach (var b in Brushes.Skip(i).Take(perRow)) BrushButton(b);
        if (LevelBuilder.IsOriented(brush)) EditorGUILayout.LabelField($"Rotazione: {boostAngle}°" +
            (brush is LevelBuilder.Ramp or LevelBuilder.PlatformTile or LevelBuilder.DiagRail ? $" (usa {(Mathf.RoundToInt(boostAngle / 90f) * 90 % 360 + 360) % 360}°: solo multipli di 90)" : ""), EditorStyles.miniLabel);
        floor = EditorGUILayout.IntSlider("Piano (PagSu / PagGiù)", floor, 0, MaxFloor);

        EditorGUILayout.LabelField("Palle singole", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
            for (int i = 0; i < BallChars.Length; i++)
            {
                int n = i + 1;
                GUI.backgroundColor = BallArt.Colors[n <= 8 ? n : n - 8];
                if (GUILayout.Toggle(brush == BallChars[i], n.ToString(), "Button", GUILayout.Width(26))) SetBrush(BallChars[i]);
            }
        GUI.backgroundColor = Color.white;

        foreach (var warning in Validate()) EditorGUILayout.HelpBox(warning, MessageType.Warning);
        EditorGUILayout.EndScrollView();
    }

    void BrushButton(Brush b)
    {
        GUI.backgroundColor = b.color;
        var content = new GUIContent(b.label, BrushIcon(b.c));
        var style = new GUIStyle("Button") { imagePosition = ImagePosition.ImageAbove, fixedHeight = 64, fixedWidth = 80, fontSize = 10 };
        if (GUILayout.Toggle(brush == b.c, content, style)) SetBrush(b.c);
        GUI.backgroundColor = Color.white;
    }

    IEnumerable<string> Validate()
    {
        var all = cells.Values.Concat(objects.Values).ToList();
        int cues = all.Count(c => c == '0');
        if (cues != 1) yield return $"Serve esattamente una bianca (ora: {cues}).";
        bool rack = all.Contains('R');
        foreach (var g in all.Where(c => BallChars.IndexOf(c) >= 0).GroupBy(c => c))
            if (g.Count() > 1 || rack) yield return $"Palla {System.Convert.ToInt32(g.Key.ToString(), 16)} presente più di una volta" + (rack ? " (il triangolo le contiene già)" : "");
        int goal = Worlds.BallOf(level);
        if (!EditingPiece && !rack && !all.Contains(BallChars[goal - 1])) yield return $"Manca la palla del livello: la {goal} (obiettivo da mandare in buca).";
        if (!cells.Values.Contains('O')) yield return "Nessuna buca.";
        if (cells.Values.Contains(LevelBuilder.PortalBlue) != cells.Values.Contains(LevelBuilder.PortalOrange)) yield return "Portale senza il suo compagno: servono il blu e l'arancione.";
        if (cells.Values.Contains('G') && !cells.Values.Contains('K')) yield return "Cancello senza pulsante: resta sempre chiuso.";
    }

    void OnScene(SceneView sv)
    {
        if (!painting || EditorApplication.isPlayingOrWillChangePlaymode) return;
        sv.wantsMouseEnterLeaveWindow = true; // so leaving the Scene view drops the hover preview
        var e = Event.current;
        if (e.type == EventType.MouseLeaveWindow) { EndHover(); return; }
        if (e.alt || HandleRotate(e)) return;

        int id = GUIUtility.GetControlID(FocusType.Passive);
        if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id); // clicks paint instead of selecting

        var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (!new Plane(Vector3.up, Vector3.up * FloorY).Raycast(ray, out float d)) return;
        var hit = ray.GetPoint(d);
        const int S = LevelBuilder.Sub;
        hoverCell = new Vector2Int(Mathf.RoundToInt(hit.x), Mathf.RoundToInt(hit.z));
        hoverSub = new Vector2Int(Mathf.RoundToInt(hit.x * S), Mathf.RoundToInt(hit.z * S));
        hoverErase = !Tool && (e.control || brush == ' ');
        bool small = LevelBuilder.IsObject(brush);

        if (e.type == EventType.Repaint) DrawHover(small);

        if (Tool && e.button == 0 && e.type is EventType.MouseDown or EventType.MouseDrag or EventType.MouseUp)
        {
            // no clip yet (or Shift): drag a selection; with a clip: click drops / pastes it
            if (e.type == EventType.MouseDown)
            {
                GUIUtility.hotControl = id;
                if (clip == null || e.shift) rectStart = hoverCell;
                else if (Paste())
                {
                    if (brush == MoveTool) CancelClip();
                    Changed();
                }
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == id)
            {
                if (rectStart.HasValue) Select(rectStart.Value, hoverCell);
                rectStart = null;
                GUIUtility.hotControl = 0;
                Repaint();
            }
            e.Use();
            ShowHover(true);
            sv.Repaint();
        }
        else if (e.button == 0 && e.type == EventType.MouseDown)
        {
            GUIUtility.hotControl = id;
            if (e.shift && !small) rectStart = hoverCell;
            else if (Apply(small)) Changed();
            e.Use();
        }
        else if (e.button == 0 && e.type == EventType.MouseDrag && GUIUtility.hotControl == id)
        {
            if (!rectStart.HasValue && Apply(small)) Changed();
            e.Use();
        }
        else if (e.button == 0 && e.type == EventType.MouseUp && GUIUtility.hotControl == id)
        {
            bool changed = rectStart.HasValue && Apply(small);
            rectStart = null;
            GUIUtility.hotControl = 0;
            if (changed) Changed();
            e.Use();
        }
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag || e.type == EventType.MouseUp || e.type == EventType.MouseDown)
        {
            ShowHover(false);
            sv.Repaint();
        }
    }

    // the brush applied at the hovered cell (or the Shift rectangle); returns whether anything changed
    bool Apply(bool small)
    {
        if (Tool) return Paste();
        char c = hoverErase ? ' ' : brush;
        if (!small) return Paint(rectStart ?? hoverCell, hoverCell, c);
        if (hoverErase) return RemoveObjects(o => Dist(o.Key, hoverSub) < 0.4f);
        return PlaceObject(hoverSub, c);
    }

    // rebuilds the preview as if the brush were applied where the mouse is, then puts the real level data back
    void ShowHover(bool force)
    {
        bool small = LevelBuilder.IsObject(brush);
        var key = $"{(small || clip is { fine: true } ? hoverSub : hoverCell)} {rectStart} {brush} {boostAngle} {floor} {hoverErase} {cells.Count} {objects.Count} {dirty} {clip?.GetHashCode()}";
        hovering = true;
        if (!force && key == hoverKey) return;
        hoverKey = key;
        var c0 = new Dictionary<Vector2Int, char>(cells);
        var o0 = new Dictionary<Vector2Int, char>(objects);
        var a0 = new Dictionary<Vector2Int, int>(angles);
        var f0 = new Dictionary<Vector2Int, int>(floors);
        bool wasGhost = hoverChanges;
        hoverChanges = Apply(small);
        var lv = hoverChanges ? Rebuild() : null;
        // a ball exists once, so the brush takes it off its old spot: keep it visible there until the click
        if (lv != null && small)
            foreach (var o in o0.Where(o => o.Value == brush && !objects.ContainsKey(o.Key)))
            {
                int n = ("0" + BallChars).IndexOf(o.Value);
                if (n < 0) continue; // the rack is not a ball
                var q = new Vector3(o.Key.x, 0, o.Key.y) / LevelBuilder.Sub;
                q.y = (lv.GroundAt(q - lv.root.position) ?? 0) + Ball.Radius;
                var old = LevelBuilder.SpawnBall(n, q, null).gameObject;
                old.transform.SetParent(lv.root, true);
                foreach (var t in old.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            }
        cells = c0;
        objects = o0;
        angles = a0;
        floors = f0;
        if (!hoverChanges && wasGhost) Rebuild(); // back to the real level
    }

    void EndHover()
    {
        if (!hovering) return;
        hovering = hoverChanges = false;
        hoverKey = null;
        Rebuild();
        SceneView.RepaintAll();
    }

    // tinted box over the area the brush would change: the "not placed yet" filter
    void DrawHover(bool small)
    {
        var color = hoverErase ? new Color(1f, 0.25f, 0.25f) : Tool && (clip == null || rectStart.HasValue) ? new Color(1f, 0.85f, 0.3f) : new Color(0.3f, 0.85f, 1f);
        bool dropping = Tool && clip != null && !rectStart.HasValue; // box over the clip's footprint at the mouse
        if (dropping) small = clip.fine;
        float scale = small ? 1f / LevelBuilder.Sub : 1f;
        var a = small ? hoverSub : dropping ? hoverCell + clip.min : rectStart ?? hoverCell;
        var b = small ? hoverSub : dropping ? hoverCell + clip.max : hoverCell;
        var min = (Vector2)Vector2Int.Min(a, b) * scale - Vector2.one * scale / 2;
        var max = (Vector2)Vector2Int.Max(a, b) * scale + Vector2.one * scale / 2;
        float h = small ? 0.6f : 0.5f;
        Vector3 P(float x, float y, float z) => new(x, y + FloorY, z);
        var fill = color * new Color(1, 1, 1, hoverChanges ? 0.18f : 0.08f);
        void Face(params Vector3[] q) => Handles.DrawSolidRectangleWithOutline(q, fill, color);
        Face(P(min.x, 0.02f, min.y), P(min.x, 0.02f, max.y), P(max.x, 0.02f, max.y), P(max.x, 0.02f, min.y));
        Face(P(min.x, h, min.y), P(min.x, h, max.y), P(max.x, h, max.y), P(max.x, h, min.y));
        Face(P(min.x, 0, min.y), P(min.x, h, min.y), P(max.x, h, min.y), P(max.x, 0, min.y));
        Face(P(min.x, 0, max.y), P(min.x, h, max.y), P(max.x, h, max.y), P(max.x, 0, max.y));
        Face(P(min.x, 0, min.y), P(min.x, h, min.y), P(min.x, h, max.y), P(min.x, 0, max.y));
        Face(P(max.x, 0, min.y), P(max.x, h, min.y), P(max.x, h, max.y), P(max.x, 0, max.y));
        if (!small && LevelBuilder.IsOriented(brush) && !hoverErase) // arrow of the current rotation
        {
            var center = new Vector3((min.x + max.x) / 2, h + FloorY, (min.y + max.y) / 2);
            Handles.color = color;
            Handles.ArrowHandleCap(0, center, Quaternion.LookRotation(LevelBuilder.AngleDir(boostAngle)), 0.6f, EventType.Repaint);
        }
    }

    static float Dist(Vector2Int a, Vector2Int b) => ((Vector2)(a - b)).magnitude / LevelBuilder.Sub;

    bool PlaceObject(Vector2Int sub, char c)
    {
        if (objects.TryGetValue(sub, out var old) && old == c) return false;
        // overlapping objects make way; balls and the rack exist once, so placing one moves it
        RemoveObjects(o => Dist(o.Key, sub) < 2 * Ball.Radius - 1e-3f || (o.Value == c && c != '*'));
        objects[sub] = c;
        return true;
    }

    bool RemoveObjects(System.Func<KeyValuePair<Vector2Int, char>, bool> match)
    {
        var gone = objects.Where(match).Select(o => o.Key).ToList();
        foreach (var k in gone) objects.Remove(k);
        return gone.Count > 0;
    }

    // tiles only; objects stay on top of whatever tile is painted under them, erasing a tile removes them too
    bool Paint(Vector2Int a, Vector2Int b, char c)
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
                    changed |= SetFloor(k);
                    foreach (var n in cells.Keys.Where(n => !before.Contains(n)).ToList()) SetFloor(n); // new rails: same floor
                    continue;
                }
                if (c == ' ')
                {
                    changed |= cells.Remove(k);
                    angles.Remove(k);
                    floors.Remove(k);
                    continue;
                }
                changed |= LevelBuilder.SetTile(cells, angles, k, c, boostAngle);
                changed |= SetFloor(k);
            }
        if (c == ' ')
        {
            const float S = LevelBuilder.Sub;
            changed |= RemoveObjects(o =>
            {
                var t = new Vector2Int(Mathf.RoundToInt(o.Key.x / S), Mathf.RoundToInt(o.Key.y / S));
                return t.x >= min.x && t.x <= max.x && t.y >= min.y && t.y <= max.y;
            });
        }
        return changed;
    }

    const int MaxFloor = 6;
    float FloorY => floor * LevelBuilder.FloorStep;

    bool SetFloor(Vector2Int k)
    {
        if (floors.TryGetValue(k, out int f) ? f == floor : floor == 0) return false;
        if (floor == 0) floors.Remove(k); else floors[k] = floor;
        return true;
    }

    void Changed()
    {
        dirty = true;
        draft = GetText(out draftMin);
        hoverKey = null;
        hoverChanges = false;
        Rebuild();
        Repaint();
    }

    LevelBuilder.Level Rebuild()
    {
        DestroyPreview();
        if (EditorApplication.isPlayingOrWillChangePlaymode || cells.Count == 0) return null;
        LevelBuilder.SetTheme(Worlds.ThemeOf(Worlds.WorldOf(level)));
        var lv = LevelBuilder.Build(GetText(out var min), PreviewName);
        lv.root.position = new Vector3(min.x, 0, min.y);
        foreach (var t in lv.root.GetComponentsInChildren<Transform>(true))
            t.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
        preview = lv.root.gameObject;
        return lv;
    }

    void DestroyPreview()
    {
        if (!preview) return;
        Deselect(preview);
        DestroyImmediate(preview);
    }

    // a selected preview piece destroyed under the Inspector throws SerializedObjectNotCreatableException
    static void Deselect(GameObject root)
    {
        if (Selection.transforms.Any(t => t && t.IsChildOf(root.transform))) Selection.activeObject = null;
    }

    void Load(int i)
    {
        if (Moving) CancelClip(); // a pick-up belongs to the level it came from; a copy travels
        piece = null;
        level = i;
        var path = PathOf(i);
        if (File.Exists(path)) SetText(File.ReadAllText(path), Vector2Int.zero);
        else { cells = new(); objects = new(); angles = new(); floors = new(); par = 0; }
        draft = GetText(out draftMin);
        dirty = false;
        Rebuild();
    }

    void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CurrentPath));
        var text = GetText(out _);
        if (EditingPiece && pieceOverlay) text += (text.Split('\n').Contains("---") ? "" : "---\n") + LevelBuilder.OverlayTag + "\n";
        File.WriteAllText(CurrentPath, text);
        AssetDatabase.ImportAsset(CurrentPath);
        dirty = false;
    }

    bool ConfirmDiscard() => !dirty || EditorUtility.DisplayDialog("Modifiche non salvate", $"Scartare le modifiche a {Path.GetFileNameWithoutExtension(CurrentPath)}?", "Scarta", "Annulla");

    void Test()
    {
        Save();
        SessionState.SetInt(GameManager.TestLevelKey, level);
        EditorApplication.EnterPlaymode();
    }
}
