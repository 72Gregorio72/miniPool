using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// In-game level editor ("Crea livelli", Geometry Dash style): Costruisci / Modifica / Elimina tabs, undo / redo, playtest,
// the user's levels saved in persistentDataPath/Livelli as level text (same format as Resources/Levels) and exported
// anywhere on the PC (FileDialog). The editing itself is LevelDraft, the same as the Unity editor window; the preview
// is the real level built by LevelBuilder, shown as if the brush were already applied under the mouse.
// User levels: the goal is always the 8 (GameManager.PlayCustom).
public class LevelEditor : MonoBehaviour
{
    public GameManager game;
    public GameUI ui;

    public static string Dir => Path.Combine(Application.persistentDataPath, "Livelli");
    static string PathOf(string name) => Path.Combine(Dir, name + ".txt");

    /// <summary>The user's levels, most recently saved first.</summary>
    public static List<string> Names() => Directory.Exists(Dir)
        ? Directory.GetFiles(Dir, "*.txt").OrderByDescending(File.GetLastWriteTimeUtc).Select(Path.GetFileNameWithoutExtension).ToList()
        : new List<string>();

    const string NewLevel = LevelDraft.DefaultTable + "8 36 15\n"; // the default table plus the goal ball
    public const int Goal = 8;

    public readonly LevelDraft d = new();
    public string levelName;  // saved file name, null = never saved
    public string editName;   // name shown / typed in the settings (the file is renamed on save)
    public bool dirty, modal; // modal: a card (settings, exit) is up, the table ignores the mouse

    public enum Tab { Build, Edit, Delete }
    public Tab tab;
    public int category;
    public char brush = '.';
    public int angle = 90, floor;
    public int tilt; // track piece: 1 = up, -1 = down (W / S), 0 = flat along its angle

    // what gets painted: the track brush tilted up / down is a vertical piece
    char PaintBrush => brush == LevelBuilder.Track && tilt != 0 ? (tilt > 0 ? LevelBuilder.TrackUp : LevelBuilder.TrackDown) : brush;
    public bool stack;
    public bool column; // "Colonna piena": what gets painted is full down to what's under it (LevelDraft.column)
    char buildBrush = '.'; // the Build tab's brush, back when you return to it

    readonly List<(string text, Vector2Int min)> undo = new(), redo = new();
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    const int MaxUndo = 200;

    LevelBuilder.Level preview;
    GameObject grid, box, arrow;
    Material boxPaint, boxErase, boxSelect;

    // mouse on the table
    Vector2Int cell, sub;
    Vector2Int? rectStart;
    bool hit, erase, stroke, strokeSaved, shiftPick; // shiftPick: Shift + click on an object picks it (if the mouse doesn't drag)
    Ray mouseRay;
    (Vector2Int, Vector2Int) lastPaint;
    string hoverKey, pendingKey; // pendingKey: the hover not yet shown, waiting HoverDelay since it last changed
    float pendingAt;
    const float HoverDelay = 0.05f;
    bool ghost;  // the preview shows the brush applied under the mouse
    int version; // bumped at every change: the hover preview is rebuilt

    // camera
    Vector3 focus;
    float yaw, pitch = 55, distance = 20;

    public bool IsObjectBrush => LevelBuilder.IsObject(brush);
    public IEnumerable<string> Warnings => d.Validate(Goal);

    void Awake() => enabled = false; // on only while editing

    // ---------- list of the user's levels ----------

    /// <summary>"Crea livelli": the user's levels (new, edit, play, copy, export, delete, paste, import).</summary>
    public void ShowList() => ui.ShowMyLevels(Names(), () => Open(null), Open, PlayFromList, CopyFromList, Duplicate, ExportFile, Delete, PasteFromList, Import, OpenFolder, ui.ShowHome);

    void CopyFromList(string name)
    {
        var text = Read(name);
        if (text == null) return;
        GUIUtility.systemCopyBuffer = text;
        ui.Toast($"\"{name}\" copiato negli appunti");
    }

    void PasteFromList()
    {
        var text = GUIUtility.systemCopyBuffer;
        if (string.IsNullOrWhiteSpace(text) || LevelBuilder.ToCells(text, Vector2Int.zero).Count == 0) { ui.Toast("Negli appunti non c'è un livello"); return; }
        var draft = new LevelDraft();
        draft.SetText(text, Vector2Int.zero);
        draft.SetGoal(Goal);
        var name = Unique("Livello incollato");
        Directory.CreateDirectory(Dir);
        File.WriteAllText(PathOf(name), draft.GetText(out _));
        ui.Toast($"Incollato: {name}");
        ShowList();
    }

    void Duplicate(string name)
    {
        var text = Read(name);
        if (text == null) return;
        try { File.WriteAllText(PathOf(Unique(name)), text); }
        catch (IOException e) { ui.Toast("Non riesco a copiarlo: " + e.Message); }
        ShowList();
    }

    void PlayFromList(string name)
    {
        var text = Read(name);
        if (text == null) return;
        game.PlayCustom(text, name, () => { game.BackToMenu(); ShowList(); });
    }

    static string Read(string name)
    {
        try { return File.ReadAllText(PathOf(name)); }
        catch (IOException) { return null; }
    }

    void Delete(string name)
    {
        try { File.Delete(PathOf(name)); }
        catch (IOException e) { ui.Toast("Non riesco a eliminarlo: " + e.Message); }
        ShowList();
    }

    void ExportFile(string name)
    {
        var text = Read(name);
        if (text != null) Export(name, text);
    }

    void Export(string name, string text)
    {
        try
        {
            var path = FileDialog.Save(name, text);
            if (path != null) ui.Toast("Salvato sul PC: " + path);
        }
        catch (System.Exception e) { ui.Toast("Esportazione non riuscita: " + e.Message); }
    }

    void Import()
    {
        string path;
        try { path = FileDialog.Open(); }
        catch (System.Exception e) { ui.Toast("Importazione non riuscita: " + e.Message); return; }
        if (path == null)
        {
#if !(UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN)
            ui.Toast("Copia il file .txt nella cartella dei livelli");
            OpenFolder();
#endif
            return;
        }
        string text;
        try { text = File.ReadAllText(path); }
        catch (IOException e) { ui.Toast("Non riesco a leggerlo: " + e.Message); return; }
        if (LevelBuilder.ToCells(text, Vector2Int.zero).Count == 0) { ui.Toast("Il file non è un livello di MiniPool"); return; }
        var name = Unique(Clean(Path.GetFileNameWithoutExtension(path)));
        Directory.CreateDirectory(Dir);
        File.WriteAllText(PathOf(name), text);
        ui.Toast($"Importato: {name}");
        ShowList();
    }

    static void OpenFolder()
    {
        Directory.CreateDirectory(Dir);
        Application.OpenURL("file:///" + Dir.Replace('\\', '/'));
    }

    // a level name that is a valid file name (the name field already only takes these)
    static string Clean(string s)
    {
        s = new string(s.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').Take(24).ToArray()).Trim();
        return s.Length > 0 ? s : "Livello";
    }

    static string Unique(string name)
    {
        if (!File.Exists(PathOf(name))) return name;
        for (int i = 2; ; i++)
            if (!File.Exists(PathOf($"{name} {i}"))) return $"{name} {i}";
    }

    // ---------- open / close ----------

    /// <summary>Edits a saved level, or a new one (name null).</summary>
    public void Open(string name)
    {
        var text = name == null ? NewLevel : Read(name);
        if (text == null) { ShowList(); return; }
        levelName = name;
        editName = name ?? Unique("Livello 1");
        dirty = name == null;
        undo.Clear();
        redo.Clear();
        d.CancelClip();
        d.SetText(text, Vector2Int.zero);
        tab = Tab.Build;
        brush = buildBrush;
        if (!icons.Values.Any(t => t)) MakeIcons(); // once (the textures die when Play ends without a domain reload)
        game.EnterEditor();
        Begin();
        Frame();
    }

    void Begin()
    {
        enabled = true;
        modal = false;
        rectStart = null;
        stroke = false;
        Visuals();
        Rebuild();
        ShowHud();
    }

    void End()
    {
        enabled = false;
        if (preview != null) Destroy(preview.root.gameObject);
        preview = null;
        foreach (var g in new[] { grid, box, arrow }) if (g) Destroy(g);
    }

    /// <summary>Leaves the editor (asks first when there are unsaved changes).</summary>
    public void Exit()
    {
        if (!dirty) { Close(); return; }
        modal = true;
        ui.ShowEditorExit(() => { if (Save()) Close(); }, Close, ShowHud);
    }

    void Close()
    {
        End();
        game.BackToMenu();
        ShowList();
    }

    /// <summary>Playtest: the level as it is now (saved or not), back here when leaving it.</summary>
    public void Playtest()
    {
        if (!d.All.Contains('0')) { ui.Toast("Metti la bianca prima di provare il livello"); return; }
        End();
        game.PlayCustom(d.GetText(out _), editName, () => { game.EnterEditor(); Begin(); });
    }

    // ---------- commands (GameUI.ShowEditor buttons and keys) ----------

    public void ShowHud()
    {
        modal = false;
        ui.ShowEditor(this);
    }

    public void SelectTab(Tab t)
    {
        tab = t;
        SetBrush(t == Tab.Build ? buildBrush : t == Tab.Edit ? (IsToolBrush ? brush : LevelDraft.MoveTool) : ' ');
    }

    bool IsToolBrush => LevelDraft.IsTool(brush);

    public void SelectCategory(int c)
    {
        category = c;
        ShowHud();
    }

    public void SetBrush(char c)
    {
        if (c != brush && d.Moving) d.CancelClip(); // a pick-up never survives a brush change
        brush = c;
        if (!LevelDraft.IsTool(c) && c != ' ') buildBrush = c;
        ShowHud();
    }

    /// <summary>Turns the brush; with a piece in hand (Sposta / Copia), the piece turns instead, by 90°.</summary>
    public void Rotate(int degrees)
    {
        if (TurningClip) d.RotateClip(degrees / 90);
        else { angle = ((angle + degrees) % 360 + 360) % 360; tilt = 0; }
        ShowHud();
    }

    public bool TurningClip => IsToolBrush && d.clip != null;

    /// <summary>Pipetta (P): the brush and angle of what's under the mouse on the current floor (a ball, the top of a pile, the tile).</summary>
    void Pick()
    {
        if (!hit) return;
        int a = angle;
        char c = d.objects.FirstOrDefault(o => LevelDraft.Dist(o.Key, sub) < 0.4f).Value;
        if (c == default)
        {
            if (!d.cells.TryGetValue(cell, out c)) return;
            if (d.above.TryGetValue(cell, out var pile) && pile.Count > 0) (c, a) = pile[^1];
            else a = d.angles.GetValueOrDefault(cell, angle);
        }
        if (LevelBuilder.IsOriented(c)) angle = a;
        tilt = c == LevelBuilder.TrackUp ? 1 : c == LevelBuilder.TrackDown ? -1 : 0;
        if (tilt != 0) c = LevelBuilder.Track;
        tab = Tab.Build;
        category = LevelDraft.Brushes.Where(b => b.c == c).Select(b => b.category).DefaultIfEmpty(1).First(); // the numbered balls aren't in Brushes
        SetBrush(c);
    }

    /// <summary>Shift + click: the topmost thing under the mouse on any floor; if it's an object (ball or module), goes to its floor and picks it (pipetta).</summary>
    bool PickAnyFloor()
    {
        foreach (var (f, plan) in d.plans.Reverse())
        {
            if (!new Plane(Vector3.up, Vector3.up * f * LevelBuilder.FloorStep).Raycast(mouseRay, out float dist)) continue;
            var p = mouseRay.GetPoint(dist);
            var c = new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.z));
            var s = new Vector2Int(Mathf.RoundToInt(p.x * LevelBuilder.Sub), Mathf.RoundToInt(p.z * LevelBuilder.Sub));
            bool ball = plan.objects.Keys.Any(o => LevelDraft.Dist(o, s) < 0.4f);
            bool tile = plan.cells.TryGetValue(c, out var ch);
            if (!ball && !tile) continue; // nothing on this floor: look below
            if (!ball && ch is '.' or '#' or 'O') return false; // felt / rail / pocket on top: not an object
            floor = d.floor = f;
            cell = c;
            sub = s;
            Pick();
            return true;
        }
        return false;
    }

    public void ChangeFloor(int delta)
    {
        floor = Mathf.Clamp(floor + delta, 0, LevelDraft.MaxFloor);
        ShowHud();
    }

    public void ToggleColumn()
    {
        column = !column;
        ShowHud();
    }

    public void ToggleStack()
    {
        stack = !stack;
        ShowHud();
    }

    public void CancelClip()
    {
        d.CancelClip();
        ShowHud();
    }

    /// <summary>Empties the level (undoable).</summary>
    public void Clear()
    {
        Record();
        var (par, fouls, theme) = (d.par, d.fouls, d.theme); // the settings stay
        d.SetText("", Vector2Int.zero);
        (d.par, d.fouls, d.theme) = (par, fouls, theme);
        Changed();
        ui.Toast("Livello svuotato (Ctrl+Z per annullare)");
    }

    public void SetPar(int v) { Record(); d.par = Mathf.Max(0, v); Changed(); }
    public void SetFouls(int v) { Record(); d.fouls = Mathf.Max(0, v); Changed(); }
    public void SetTheme(int v) { Record(); d.theme = (v % Worlds.Themes.Length + Worlds.Themes.Length) % Worlds.Themes.Length; Changed(); }

    public void Rename(string name)
    {
        name = Clean(name);
        if (name == editName) return;
        editName = name;
        dirty = true;
    }

    public void Settings()
    {
        modal = true;
        ui.ShowEditorSettings(this);
    }

    public void CopyLevel()
    {
        GUIUtility.systemCopyBuffer = d.GetText(out _);
        ui.Toast("Livello copiato negli appunti");
    }

    public void PasteLevel()
    {
        var text = GUIUtility.systemCopyBuffer;
        if (string.IsNullOrWhiteSpace(text) || LevelBuilder.ToCells(text, Vector2Int.zero).Count == 0) { ui.Toast("Negli appunti non c'è un livello"); return; }
        Record();
        d.SetText(text, Vector2Int.zero);
        d.SetGoal(Goal);
        Changed();
        Frame();
        ui.Toast("Livello incollato dagli appunti");
    }

    public void ExportCurrent() => Export(editName, d.GetText(out _));

    /// <summary>Writes the level to its file (renamed if the name changed); false = not saved.</summary>
    public bool Save()
    {
        var name = Clean(editName);
        bool renamed = levelName != null && levelName != name;
        if (!string.Equals(name, levelName, System.StringComparison.OrdinalIgnoreCase) && File.Exists(PathOf(name)))
        {
            ui.Toast($"Esiste già un livello che si chiama \"{name}\"");
            return false;
        }
        try
        {
            Directory.CreateDirectory(Dir);
            if (renamed && File.Exists(PathOf(levelName))) File.Move(PathOf(levelName), PathOf(name)); // a move keeps a case-only rename too
            File.WriteAllText(PathOf(name), d.GetText(out _));
        }
        catch (IOException e)
        {
            ui.Toast("Salvataggio non riuscito: " + e.Message);
            return false;
        }
        levelName = editName = name;
        dirty = false;
        ui.Toast("Livello salvato");
        if (enabled && !modal) ShowHud();
        return true;
    }

    // ---------- undo ----------

    (string, Vector2Int) Snapshot() => (d.GetText(out var min), min);

    void Record() => Push(Snapshot());

    void Push((string, Vector2Int) s)
    {
        undo.Add(s);
        if (undo.Count > MaxUndo) undo.RemoveAt(0);
        redo.Clear();
    }

    public void Undo() => Step(undo, redo);
    public void Redo() => Step(redo, undo);

    void Step(List<(string text, Vector2Int min)> from, List<(string text, Vector2Int min)> to)
    {
        if (from.Count == 0) return;
        to.Add(Snapshot());
        var (text, min) = from[^1];
        from.RemoveAt(from.Count - 1);
        d.SetText(text, min);
        Changed();
    }

    void Changed()
    {
        dirty = true;
        version++;
        ghost = false;
        hoverKey = null;
        Rebuild();
        if (!stroke && !modal) ShowHud(); // while painting the HUD waits for the end of the stroke
    }

    // ---------- per frame ----------

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null) return;
        if (modal)
        {
            if (kb.escapeKey.wasPressedThisFrame) ShowHud();
            HideHover();
            return;
        }
        Keys(kb);
        if (!enabled) return; // a key left the editor (playtest, exit)
        bool overUi = EventSystem.current && EventSystem.current.IsPointerOverGameObject();
        CameraInput(kb, mouse, overUi);
        var ray = mouseRay = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
        float dist = 0;
        hit = !overUi && new Plane(Vector3.up, Vector3.up * FloorY).Raycast(ray, out dist);
        if (hit)
        {
            var p = ray.GetPoint(dist);
            cell = new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.z));
            sub = new Vector2Int(Mathf.RoundToInt(p.x * LevelBuilder.Sub), Mathf.RoundToInt(p.z * LevelBuilder.Sub));
        }
        bool ctrl = kb.ctrlKey.isPressed;
        erase = !IsToolBrush && (ctrl || brush == ' ');
        MouseInput(mouse, kb.shiftKey.isPressed);
        ShowHover();
        Grid();
    }

    void Keys(Keyboard kb)
    {
        bool ctrl = kb.ctrlKey.isPressed, shift = kb.shiftKey.isPressed;
        if (ctrl && kb.zKey.wasPressedThisFrame) { if (shift) Redo(); else Undo(); }
        if (ctrl && kb.yKey.wasPressedThisFrame) Redo();
        if (ctrl && kb.sKey.wasPressedThisFrame) Save();
        if (kb.digit1Key.wasPressedThisFrame) SelectTab(Tab.Build);
        if (kb.digit2Key.wasPressedThisFrame) SelectTab(Tab.Edit);
        if (kb.digit3Key.wasPressedThisFrame) SelectTab(Tab.Delete);
        if ((LevelBuilder.IsOriented(brush) || TurningClip) && !ctrl)
        {
            int step = shift && !TurningClip ? 45 : 90; // a clip only turns by 90
            if (kb.rKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame) Rotate(step);
            if (kb.qKey.wasPressedThisFrame) Rotate(-step);
        }
        if (brush == LevelBuilder.Track && !ctrl && (kb.wKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame))
        {
            tilt = Mathf.Clamp(tilt + (kb.wKey.wasPressedThisFrame ? 1 : -1), -1, 1);
            ShowHud();
        }
        if (kb.pKey.wasPressedThisFrame) Pick();
        if (kb.pageUpKey.wasPressedThisFrame) ChangeFloor(1);
        if (kb.pageDownKey.wasPressedThisFrame) ChangeFloor(-1);
        if (kb.tKey.wasPressedThisFrame) pitch = pitch > 85 ? 55 : 89.9f;
        if (kb.homeKey.wasPressedThisFrame) Frame();
        if (kb.escapeKey.wasPressedThisFrame)
        {
            if (rectStart.HasValue) { rectStart = null; stroke = false; }
            else if (d.clip != null) CancelClip();
            else Exit();
        }
    }

    void CameraInput(Keyboard kb, Mouse mouse, bool overUi)
    {
        var delta = mouse.delta.ReadValue();
        float k = distance * 0.0016f * GameManager.Sensitivity;
        var flatRot = Quaternion.Euler(0, yaw, 0);
        if (mouse.rightButton.isPressed) focus -= flatRot * new Vector3(delta.x, 0, delta.y) * k; // drag the table
        if (mouse.middleButton.isPressed || (kb.altKey.isPressed && mouse.leftButton.isPressed))
        {
            yaw += delta.x * 0.25f * GameManager.Sensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * 0.25f * GameManager.Sensitivity, 15, 89.9f);
        }
        if (!overUi)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0) distance = Mathf.Clamp(distance * Mathf.Pow(0.9f, Mathf.Sign(scroll)), 3, 80);
        }
        if (!kb.ctrlKey.isPressed)
        {
            var move = new Vector3((kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1 : 0) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1 : 0), 0,
                (kb.wKey.isPressed && brush != LevelBuilder.Track || kb.upArrowKey.isPressed ? 1 : 0) - (kb.sKey.isPressed && brush != LevelBuilder.Track || kb.downArrowKey.isPressed ? 1 : 0)); // W / S tilt a track piece
            focus += flatRot * move * (distance * 0.8f * Time.unscaledDeltaTime);
        }
        var rot = Quaternion.Euler(pitch, yaw, 0);
        Camera.main.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
    }

    // the whole level in view, from the near side
    void Frame()
    {
        yaw = 0;
        pitch = 55;
        if (d.Empty) { focus = Vector3.zero; distance = 20; return; }
        var all = d.AllCells.ToList();
        float x0 = all.Min(k => k.x), x1 = all.Max(k => k.x), z0 = all.Min(k => k.y), z1 = all.Max(k => k.y);
        focus = new Vector3((x0 + x1) / 2, 0, (z0 + z1) / 2 - 1.5f); // a bit toward the camera: the panel covers the bottom
        distance = Mathf.Clamp(Mathf.Max(x1 - x0, (z1 - z0) * 1.6f) * 0.9f + 6, 6, 80);
    }

    void MouseInput(Mouse mouse, bool shift)
    {
        var left = mouse.leftButton;
        if (Keyboard.current.altKey.isPressed) return; // Alt + left = orbit
        if (left.wasPressedThisFrame && hit)
        {
            shiftPick = shift;
            if (IsToolBrush)
            {
                // no clip yet (or Shift): drag a selection; with a clip: click drops / pastes it
                if (d.clip == null || shift) rectStart = cell;
                else
                {
                    var before = Snapshot();
                    if (d.Paste(cell, sub))
                    {
                        Push(before);
                        if (brush == LevelDraft.MoveTool) d.CancelClip();
                        Changed();
                    }
                }
            }
            else
            {
                stroke = true;
                strokeSaved = false;
                lastPaint = default;
                if (shift) rectStart = cell; // a ball brush has no rectangle: the click paints on release (or picks)
                else Paint();
            }
        }
        else if (left.isPressed && stroke && !rectStart.HasValue && hit) Paint();
        if (left.wasReleasedThisFrame)
        {
            if (shiftPick && rectStart == cell && PickAnyFloor()) { rectStart = null; stroke = false; shiftPick = false; ShowHud(); return; }
            shiftPick = false;
            if (IsToolBrush && rectStart.HasValue)
            {
                d.Select(brush, rectStart.Value, cell, sub);
                rectStart = null;
                ShowHud();
            }
            else if (stroke)
            {
                if (IsObjectBrush) rectStart = null;
                if (hit) Paint(); // the Shift rectangle (a ball brush: one ball)
                rectStart = null;
                stroke = false;
                ShowHud();
            }
        }
    }

    void Paint()
    {
        if (!rectStart.HasValue && lastPaint == (cell, sub)) return;
        lastPaint = (cell, sub);
        var before = strokeSaved ? default : Snapshot();
        d.column = column;
        if (!d.Apply(PaintBrush, cell, sub, rectStart, angle, floor, stack, erase)) return;
        if (!strokeSaved) { Push(before); strokeSaved = true; }
        Changed();
    }

    // ---------- preview ----------

    float FloorY => floor * LevelBuilder.FloorStep;

    void Rebuild()
    {
        if (preview != null) Destroy(preview.root.gameObject);
        preview = null;
        GameManager.ApplyWorld(Mathf.Max(0, d.theme));
        if (d.Empty) return;
        var lv = LevelBuilder.Build(d.GetText(out var min), "EditorLevel");
        lv.root.position = new Vector3(min.x, 0, min.y);
        preview = lv;
    }

    // rebuilds the preview as if the brush were applied where the mouse is, then puts the real level back
    void ShowHover()
    {
        bool small = IsObjectBrush || d.clip is { fine: true };
        var key = hit ? $"{(small ? sub : cell)} {rectStart} {PaintBrush} {angle} {floor} {stack} {column} {erase} {version} {d.clip?.GetHashCode()}" : null;
        Box();
        if (key == hoverKey) return;
        // shortcut: the preview rebuilds the whole level, so it waits for the mouse to settle instead of chasing every cell; rebuild only the touched chunk if big levels still stutter
        if (key != pendingKey) { pendingKey = key; pendingAt = Time.unscaledTime; }
        if (key != null && Time.unscaledTime - pendingAt < HoverDelay) return;
        hoverKey = key;
        if (!hit)
        {
            if (ghost) { ghost = false; Rebuild(); }
            return;
        }
        var (real, min) = Snapshot();
        d.column = column;
        bool applied = d.Apply(PaintBrush, cell, sub, rectStart, angle, floor, stack, erase);
        if (applied) Rebuild();
        d.SetText(real, min); // the clip and the pick-up stay
        if (!applied && ghost) Rebuild();
        ghost = applied;
    }

    void HideHover()
    {
        if (box) box.SetActive(false);
        if (arrow) arrow.SetActive(false);
        if (ghost) { ghost = false; hoverKey = null; Rebuild(); }
    }

    void Visuals()
    {
        boxPaint ??= BallArt.Ghost(LevelBuilder.Mat(new Color(0.3f, 0.85f, 1f)));
        boxErase ??= BallArt.Ghost(LevelBuilder.Mat(new Color(1f, 0.25f, 0.25f)));
        boxSelect ??= BallArt.Ghost(LevelBuilder.Mat(new Color(1f, 0.85f, 0.3f)));
        box = Plain(LevelBuilder.Visual("EditorBox", PrimitiveType.Cube, Vector3.zero, Vector3.one, Quaternion.identity, Color.white, transform), boxPaint);
        arrow = Plain(LevelBuilder.Visual("EditorArrow", PrimitiveType.Cube, Vector3.zero, Vector3.one, Quaternion.identity, Color.white, transform), boxPaint);
        grid = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(grid.GetComponent<Collider>()); // no PhysX in the levels
        grid.name = "EditorGrid";
        grid.transform.SetParent(transform, false);
        grid.transform.rotation = Quaternion.Euler(90, 0, 0);
        grid.transform.localScale = Vector3.one * GridSize;
        Plain(grid, GridMaterial());
    }

    static GameObject Plain(GameObject g, Material m)
    {
        foreach (var r in g.GetComponentsInChildren<Renderer>())
        {
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        return g;
    }

    const int GridSize = 120;
    static Material gridMat;

    // see-through lines on the tile borders, tiled once per tile
    static Material GridMaterial()
    {
        if (gridMat) return gridMat;
        const int N = 32;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
                px[y * N + x] = x == 0 || y == 0 || x == N - 1 || y == N - 1 ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
        tex.SetPixels32(px);
        tex.Apply();
        gridMat = new Material(BallArt.Ghost(LevelBuilder.Mat(Color.white))) { mainTexture = tex, mainTextureScale = Vector2.one * GridSize };
        gridMat.color = new Color(1, 1, 1, 0.35f);
        return gridMat;
    }

    // the grid on the current floor, around the camera's focus, lines on the tile borders
    void Grid()
    {
        if (!grid) return;
        grid.transform.position = new Vector3(Mathf.Round(focus.x) + 0.5f, FloorY + 0.012f, Mathf.Round(focus.z) + 0.5f);
    }

    // tinted box over the area the brush would change (red = erase, yellow = selecting), arrow = rotation
    void Box()
    {
        if (!box) return;
        box.SetActive(hit);
        arrow.SetActive(false);
        if (!hit) return;
        var clip = d.clip;
        bool selecting = IsToolBrush && (clip == null || rectStart.HasValue);
        bool dropping = IsToolBrush && clip != null && !rectStart.HasValue;
        bool small = dropping ? clip.fine : IsObjectBrush;
        float scale = small ? 1f / LevelBuilder.Sub : 1f;
        var a = small ? sub : dropping ? cell + clip.min : rectStart ?? cell;
        var b = small ? sub : dropping ? cell + clip.max : cell;
        var min = (Vector2)Vector2Int.Min(a, b) * scale - Vector2.one * scale / 2;
        var max = (Vector2)Vector2Int.Max(a, b) * scale + Vector2.one * scale / 2;
        float h = small ? 0.6f : 0.5f;
        var mat = erase ? boxErase : selecting ? boxSelect : boxPaint;
        box.transform.position = new Vector3((min.x + max.x) / 2, FloorY + h / 2, (min.y + max.y) / 2);
        box.transform.localScale = new Vector3(max.x - min.x, h, max.y - min.y);
        Plain(box, mat);
        if (small || erase || IsToolBrush || !LevelBuilder.IsOriented(brush)) return;
        var dir = LevelBuilder.AngleDir(SnappedAngle);
        arrow.SetActive(true);
        arrow.transform.SetPositionAndRotation(box.transform.position + Vector3.up * (h / 2 + 0.05f) + dir * 0.25f, Quaternion.LookRotation(dir));
        arrow.transform.localScale = new Vector3(0.1f, 0.05f, 0.6f);
        Plain(arrow, boxSelect);
    }

    /// <summary>The angle the brush really uses: ramps, platforms, diagonal rails, fans, tracks and saws only turn by 90°.</summary>
    public int SnappedAngle => brush is LevelBuilder.Ramp or LevelBuilder.PlatformTile or LevelBuilder.DiagRail or LevelBuilder.WindFan or LevelBuilder.Track or LevelBuilder.Saw
        ? (Mathf.RoundToInt(angle / 90f) * 90 % 360 + 360) % 360 : angle;

    // ---------- brush icons ----------

    public static readonly Dictionary<char, Texture> icons = new(); // by brush char; EditorIconTests saves them as evidence

    /// <summary>3D thumbnail of a brush (null until MakeIcons has rendered it).</summary>
    public static Texture Icon(char c) => icons.TryGetValue(c, out var t) && t ? t : null;

    /// <summary>Renders every brush (a 3x3 felt patch with the tile in the middle, built far away) into its icon texture, now.</summary>
    public static void MakeIcons()
    {
        var cam = new GameObject("BrushIconCam").AddComponent<Camera>();
        cam.fieldOfView = 30;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 20;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.18f, 0.19f, 0.23f);
#if UNITY_EDITOR
        bool async = UnityEditor.ShaderUtil.allowAsyncCompilation;
        UnityEditor.ShaderUtil.allowAsyncCompilation = false; // or a shader still compiling renders as a placeholder
#endif
        var at = new Vector3(-3000, 0, -3000); // far from the table
        foreach (char c in LevelDraft.Brushes.Select(b => b.c).Concat(LevelDraft.BallChars).Where(c => c != ' ').Distinct())
        {
            var text = c == LevelBuilder.FeltWithRails ? "###\n#.#\n###" : LevelBuilder.IsOriented(c) ? $"...\n.{c}.\n...\n---\n> 1 1 90" : $"...\n.{c}.\n...";
            var lv = LevelBuilder.Build(text, "BrushIcon");
            lv.root.position = at;
            var look = at + new Vector3(1, 0.1f, 1);
            cam.transform.position = look + new Vector3(0, 3.2f, -3.2f) * (c == 'R' ? 1.8f : 1f);
            cam.transform.LookAt(look);
            cam.targetTexture = new RenderTexture(160, 160, 24) { antiAliasing = 2 };
            cam.Render();
            icons[c] = cam.targetTexture;
            Object.DestroyImmediate(lv.root.gameObject);
        }
#if UNITY_EDITOR
        UnityEditor.ShaderUtil.allowAsyncCompilation = async;
#endif
        cam.targetTexture = null; // destroyed with its target still attached, the last render came out black
        Object.DestroyImmediate(cam.gameObject);
    }
}
