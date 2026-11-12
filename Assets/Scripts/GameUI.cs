using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.UI;

// Home and level select drawn on a real table seen from above (GameManager.MenuTable): the home has the rack and the cue ball
// beside the chosen item, the level select one real ball per level in an S (5 per row) with a live preview over the hovered one;
// cue-ball skins and the results card. uGUI built in code: no prefabs, no scene setup.
// The canvas sits in front of the camera (Screen Space - Camera) so the 3D balls, placed between the two, draw over it.
public partial class GameUI : MonoBehaviour
{
    public Action<int> onPlay; // level index
    public Func<string, int, LevelBuilder.Level> onTable; // (level text, world) -> the menu table (GameManager.MenuTable)
    public Action onMenu, onHost, onHostPool; // onHostPool: online 8-ball (2 players, turns)
    public Action onCreate; // "Crea livelli": the user's levels (LevelEditor)
    public Action<string> onJoin; // join code
    public List<int> levels = new();

    static readonly Color White = Color.white, Ink = new(0.12f, 0.13f, 0.16f), Grey = new(0.5f, 0.53f, 0.58f),
        Soft = new(0.93f, 0.94f, 0.95f), Glass = new(1, 1, 1, 0.14f), RecordColor = new(0.9f, 0.55f, 0.05f),
        CrownGold = new(1f, 0.78f, 0.15f); // crown outline around a level's ball
    const float PlaneDistance = 0.3f, ModelDistance = 0.2f; // canvas and 3D icons, in front of the camera

    enum Page { None, Home, Worlds, Levels, Skins, TableSkins, Pause, Settings, MyLevels, Editor }
    Page screen;
    public bool OnSettings => screen == Page.Settings;
    public int escFrame; // frame in which this UI used Esc, so the game doesn't use it too

    RectTransform root, pop, toasts, hud; // toasts and hud (onboarding hint) survive Hide
    float popT, hintT;
    RectTransform hint;
    string hintText;
    Font font;
    Sprite round, circle;
    Camera cam;

    // 3D balls glued to UI slots
    class Model
    {
        public RectTransform slot;
        public Transform t;
        public Quaternion face;
        public bool spin;
    }
    readonly List<Model> models = new();
    readonly Dictionary<int, Material> dim = new();

    // menu table: world shown (-1 = home), the home's cue ball and items, the level balls
    LevelBuilder.Level table;
    int tableWorld;
    MenuView view; // hand-edited layout of home and level select (Resources/Menu/Menu.prefab, or the instance in the scene)
    Action[] homeActions;
    Transform marker;
    bool markerSnap;
    int homeSel;

    // level select
    int selected, previewed;
    readonly Dictionary<int, Transform> icons = new(); // existing levels' balls on the table
    Transform popBall;
    readonly List<GameObject> spawned = new(); // level balls, line pieces, world cards: gone with Hide
    readonly List<(int world, MenuWorldCard card)> worldCards = new(); // open worlds only
    int worldSel;
    readonly Dictionary<int, Transform> skinBalls = new(); // skin index -> its ball on the vertical table
    static Material lineMat;

    // skins
    int skinShown;

    void Awake()
    {
        // an OS font where one of these exists (Windows / Mac); elsewhere Unity's bundled Liberation Sans, so the text never vanishes
        var names = new[] { "Segoe UI", "Helvetica Neue", "Arial" };
        var installed = Font.GetOSInstalledFontNames();
        font = names.Any(installed.Contains) ? Font.CreateDynamicFontFromOSFont(names, 32) : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        round = RoundedSprite(64, 28);
        circle = RoundedSprite(128, 64);
        cam = Camera.main;
        cam.nearClipPlane = 0.05f;
        var go = new GameObject("UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = PlaneDistance;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        root = (RectTransform)go.transform;
        toasts = Rect(root, Vector2.zero, Vector2.zero);
        hud = Rect(root, Vector2.zero, Vector2.zero);
        if (!FindAnyObjectByType<EventSystem>())
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)).transform.SetParent(transform);
        view = FindAnyObjectByType<MenuView>(FindObjectsInactive.Include);
        if (!view && Resources.Load<MenuView>("Menu/Menu") is { } prefab) view = Instantiate(prefab);
        if (view) WireMenu();
        else Debug.LogError("MiniPool: manca Resources/Menu/Menu.prefab (MiniPool > Crea prefab menu)");
    }

    public void Hide()
    {
        screen = Page.None;
        icons.Clear();
        foreach (var m in models) if (m.t) Destroy(m.t.gameObject);
        models.Clear();
        pop = null;
        popBall = null;
        marker = null;
        if (view)
        {
            view.home.gameObject.SetActive(false);
            view.levels.gameObject.SetActive(false);
            if (view.worlds) view.worlds.gameObject.SetActive(false);
        }
        skinBalls.Clear();
        if (view && view.skins) view.skins.gameObject.SetActive(false);
        foreach (var o in spawned) if (o) Destroy(o);
        spawned.Clear();
        worldCards.Clear();
        ClearPreviews();
        foreach (Transform c in root) if (c != toasts && c != hud) Destroy(c.gameObject);
    }

    // ---------- single-player HUD (CueController) ----------

    RectTransform cuePanel, cueBallSlot, cueDot, cueStick;
    Canvas overlay; // in front of the 3D ball: the tip spot
    Camera cueCam;
    RenderTexture cueTex;
    Model cueModel;
    int cueSkin = -1;
    static Sprite powerSprite;

    // bottom right, no text: the equipped cue ball (3D) with the tip spot (tip in ball radii, max 0.5) and the power bar beside it,
    // green at the top to red at the bottom, covered by a cue that slides down to show it as the power grows
    public void SetCue(bool on, Vector2 tip, float power, float elevation)
    {
        const float Size = 130, BarW = 30, BarH = 150;
        if (!cuePanel)
        {
            cuePanel = Corner(hud, new Vector2(1, 0));
            cueBallSlot = Rect(cuePanel, new Vector2(-110, 120), new Vector2(Size, Size));
            // the dot on a canvas drawn over everything, at the same layout units as the HUD
            var go = new GameObject("CueOverlay", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform);
            overlay = go.GetComponent<Canvas>();
            overlay.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var ball = Rect(Corner((RectTransform)go.transform, new Vector2(1, 0)), new Vector2(-110, 120), new Vector2(Size, Size));
            cueDot = Box(ball, Color.black, Vector2.zero, new Vector2(24, 24), circle); // black outline
            Box(cueDot, White, Vector2.zero, new Vector2(16, 16), circle);
            var bar = Box(cuePanel, Color.black, new Vector2(-260, 120), new Vector2(BarW + 6, BarH + 6), null); // rectangular black outline
            var inside = Box(bar, White, Vector2.zero, new Vector2(BarW, BarH), PowerSprite());
            inside.gameObject.AddComponent<RectMask2D>();
            // the equipped cue (a copy of CueController's), filmed by its own camera far from the table, shown as long as the bar
            cueStick = Rect(inside, Vector2.zero, new Vector2(BarW, BarH));
            var img = cueStick.gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            img.texture = cueTex = new RenderTexture(64, Mathf.RoundToInt(64 * BarH / BarW), 16) { antiAliasing = 4 };
            BuildCueShot(BarW / BarH);
        }
        if (cueModel == null || !cueModel.t || cueSkin != BallArt.SelectedSkin) // Hide drops the menu models, this one too
        {
            if (cueModel != null && cueModel.t) { models.Remove(cueModel); Destroy(cueModel.t.gameObject); }
            cueSkin = BallArt.SelectedSkin;
            cueModel = AddModel(cueBallSlot, BallArt.CueMaterial(cueSkin));
        }
        if (cuePanel.gameObject.activeSelf != on) cuePanel.gameObject.SetActive(on);
        if (overlay.gameObject.activeSelf != on) overlay.gameObject.SetActive(on);
        if (cueCam) cueCam.enabled = on;
        if (cueModel.t.gameObject.activeSelf != on) cueModel.t.gameObject.SetActive(on);
        if (!on) return;
        cueDot.anchoredPosition = tip * Size;
        cueStick.anchoredPosition = new Vector2(0, -BarH * Mathf.Clamp01(power));
    }

    // the power bar's cue: a copy of the equipped one, upright, made as thick as the bar (aspect = width / height), and an
    // orthographic camera far below the room that films it into cueTex (transparent background)
    // shortcut: copied once; when cue skins exist, copy it again when the skin changes
    void BuildCueShot(float aspect)
    {
        var src = FindAnyObjectByType<CueController>(FindObjectsInactive.Include);
        if (!src || !src.Stick) return;
        var far = new Vector3(0, -2000, 0);
        var stick = Instantiate(src.Stick.gameObject, transform);
        stick.SetActive(true);
        stick.transform.SetPositionAndRotation(far, Quaternion.identity); // upright, tip at the top
        stick.transform.localScale = Vector3.one;
        var b = stick.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
        float len = b.size.y, thick = Mathf.Max(b.size.x, b.size.z);
        stick.transform.localScale = new Vector3(len * aspect / thick, 1, len * aspect / thick);
        foreach (var r in stick.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
        var c = stick.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; }).center;
        cueCam = new GameObject("CueCamera").AddComponent<Camera>();
        cueCam.transform.SetParent(transform);
        cueCam.transform.SetPositionAndRotation(c - Vector3.forward * 5, Quaternion.identity);
        cueCam.orthographic = true;
        cueCam.orthographicSize = len / 2;
        cueCam.nearClipPlane = 0.1f;
        cueCam.farClipPlane = 10;
        cueCam.clearFlags = CameraClearFlags.SolidColor;
        cueCam.backgroundColor = Color.clear;
        cueCam.targetTexture = cueTex;
    }

    // vertical gradient, green at the top to red at the bottom
    static Sprite PowerSprite()
    {
        if (powerSprite) return powerSprite;
        var tex = new Texture2D(1, 64) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < 64; y++)
        {
            float t = y / 63f; // 0 = bottom
            tex.SetPixel(0, y, t < 0.5f ? Color.Lerp(new Color(0.9f, 0.15f, 0.1f), new Color(1f, 0.85f, 0.1f), t * 2) : Color.Lerp(new Color(1f, 0.85f, 0.1f), new Color(0.2f, 0.8f, 0.25f), t * 2 - 1));
        }
        tex.Apply();
        return powerSprite = Sprite.Create(tex, new Rect(0, 0, 1, 64), new Vector2(0.5f, 0.5f));
    }

    // ---------- onboarding hint ----------

    // A chunky slab at the bottom of the screen (thick edge underneath, like the low poly tiles); "[KEY]" becomes a key cap.
    // Lines split by '\n'. null hides it. Rebuilt only when the text changes.
    public void SetHint(string text)
    {
        if (text == hintText) return;
        hintText = text;
        if (hint) Destroy(hint.gameObject);
        if (string.IsNullOrEmpty(text)) return;
        const float LineH = 66, PadX = 40, PadY = 26, Edge = 10;
        hint = Corner(hud, new Vector2(0.5f, 0));
        hintT = 0;
        var theme = Worlds.ThemeOf(0);
        var edge = Box(hint, new Color(0.05f, 0.05f, 0.07f, 0.9f), Vector2.zero, Vector2.one);
        var slab = Box(hint, new Color(Ink.r, Ink.g, Ink.b, 0.94f), Vector2.zero, Vector2.one);
        var stripe = Box(slab, theme.accent, Vector2.zero, Vector2.one);
        var lines = text.Split('\n');
        float maxW = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            var row = Rect(hint, Vector2.zero, Vector2.one);
            float x = 0, y = -i * LineH;
            foreach (var part in System.Text.RegularExpressions.Regex.Split(lines[i], @"(\[[^\]]+\])"))
            {
                if (part.Length == 0) continue;
                if (part[0] == '[')
                {
                    var key = Label(row, part.Substring(1, part.Length - 2), 26, Ink, Vector2.zero, new Vector2(400, 46), bold: true);
                    float w = Mathf.Max(46, key.preferredWidth + 30);
                    var pos = new Vector2(x + w / 2, y);
                    Box(row, Grey, pos + new Vector2(0, -6), new Vector2(w, 46)).SetSiblingIndex(0); // key cap side
                    Box(row, White, pos, new Vector2(w, 46)).SetSiblingIndex(1);
                    ((RectTransform)key.transform).anchoredPosition = pos + new Vector2(0, 1);
                    x += w + 10;
                }
                else
                {
                    var t = Label(row, part, 30, White, new Vector2(x, y), new Vector2(10, LineH), TextAnchor.MiddleLeft, true, true);
                    x += t.preferredWidth + 10;
                }
            }
            maxW = Mathf.Max(maxW, x - 10);
            foreach (RectTransform c in row) c.anchoredPosition -= new Vector2((x - 10) / 2, 0); // center the line
        }
        float h = lines.Length * LineH + PadY * 2, w0 = maxW + PadX * 2, top = 150 + h; // bottom edge above the controls line
        foreach (RectTransform c in hint)
            if (c != edge && c != slab) c.anchoredPosition += new Vector2(0, top - PadY - LineH / 2);
        slab.anchoredPosition = new Vector2(0, top - h / 2);
        slab.sizeDelta = new Vector2(w0, h);
        edge.anchoredPosition = slab.anchoredPosition - new Vector2(0, Edge);
        edge.sizeDelta = slab.sizeDelta;
        stripe.anchoredPosition = new Vector2(0, h / 2 - 5);
        stripe.sizeDelta = new Vector2(w0 - 56, 6);
    }

    // ---------- home ----------

    /// <summary>Builds the menu table if it isn't up: world -1 = the home (rack + cue ball), otherwise that world's empty table.</summary>
    public void ShowTable(int world)
    {
        if (table != null && table.root && tableWorld == world) return;
        var text = Resources.Load<TextAsset>("PoolTable")?.text ?? "";
        tableWorld = world;
        table = onTable?.Invoke(world < 0 ? text : text.Split("---")[0], Mathf.Max(0, world));
    }

    // the prefab's buttons get their actions once; its own camera only shows the layout while editing
    void WireMenu()
    {
        if (view.view) view.view.enabled = false;
        view.canvas.worldCamera = cam;
        view.home.gameObject.SetActive(false);
        view.levels.gameObject.SetActive(false);
        if (view.worlds) view.worlds.gameObject.SetActive(false);
        if (view.skins) view.skins.gameObject.SetActive(false);
        if (view.skinView) view.skinView.enabled = false;
        if (view.tableSkinButton) view.tableSkinButton.onClick.AddListener(() => { Sfx.Play(Sfx.Kind.Click, 1); ShowTableSkin(GameManager.TableSkin); });
        if (view.skinBackButton) view.skinBackButton.onClick.AddListener(() => { Sfx.Play(Sfx.Kind.Click, 1); ShowHome(); });
        homeActions = new Action[] { ShowWorlds, () => ShowMultiplayer(null), () => onCreate?.Invoke(), ShowSkins, () => ShowSettings(ShowHome), Quit };
        for (int i = 0; i < view.homeItems.Length && i < homeActions.Length; i++)
        {
            int k = i;
            if (!view.homeItems[i]) continue;
            view.homeItems[i].onClick.AddListener(() => { Sfx.Play(Sfx.Kind.Click, 1); homeActions[k](); });
            OnEnter(view.homeItems[i], () => HomeSelect(k));
        }
        if (view.menuButton) view.menuButton.onClick.AddListener(() => { Sfx.Play(Sfx.Kind.Click, 1); ShowHome(); });
        if (view.hardButton) view.hardButton.onClick.AddListener(() =>
        {
            Sfx.Play(Sfx.Kind.Click, 1);
            GameManager.HardMode = !GameManager.HardMode;
            HardText();
        });
    }

    void HardText()
    {
        var t = view.hardButton ? view.hardButton.GetComponentInChildren<Text>() : null;
        if (t) t.text = GameManager.HardMode ? "Hard mode: sì" : "Hard mode: no";
    }

    /// <summary>Puts the camera where the menu prefab's camera is (position, rotation, field of view).</summary>
    public void PlaceMenuCamera(Camera c)
    {
        var from = !view ? null : screen == Page.Skins && view.skinView ? view.skinView : view.view;
        if (!from) return;
        c.transform.SetPositionAndRotation(from.transform.position, from.transform.rotation);
        c.fieldOfView = from.fieldOfView;
    }

    public void ShowHome()
    {
        Hide();
        screen = Page.Home;
        if (previewed == 0 && levels.Count > 0) previewed = levels[0];
        ShowTable(-1);
        if (!view) return;
        var cue = table?.balls.FirstOrDefault(b => b && b.number == 0);
        if (cue)
        {
            marker = cue.transform;
            markerSnap = true;
            foreach (var r in cue.GetComponentsInChildren<Renderer>()) r.sharedMaterial = BallArt.CueMaterial(BallArt.SelectedSkin);
        }
        view.home.gameObject.SetActive(true);
        HomeSelect(Mathf.Clamp(homeSel, 0, homeActions.Length - 1));
    }

    Text ItemLabel(int i) => i < view.homeItems.Length && view.homeItems[i] ? view.homeItems[i].GetComponentInChildren<Text>() : null;

    void HomeSelect(int i)
    {
        homeSel = i;
        for (int k = 0; k < view.homeItems.Length; k++)
            if (ItemLabel(k) is { } t) t.color = k == i ? view.itemSelected : view.itemColor;
    }

    static void OnEnter(Component c, Action a)
    {
        var trigger = c.gameObject.AddComponent<EventTrigger>();
        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => a());
        trigger.triggers.Add(enter);
    }

    // ---------- pause / settings ----------

    public void ShowPause(Action resume, Action restart, Action levelsMenu, string menuLabel = "Livelli")
    {
        Hide();
        screen = Page.Pause;
        var dimmer = Box(root, new Color(0, 0, 0, 0.45f), Vector2.zero, Vector2.zero, null);
        const float w = 560, h = 600;
        var card = Box(dimmer, White, Vector2.zero, new Vector2(w, h));
        pop = card;
        popT = 0;
        Label(card, "Pausa", 60, Ink, new Vector2(0, h / 2 - 80), new Vector2(w, 80), bold: true);
        var items = new (string, Color, Action)[]
        {
            ("Riprendi", Worlds.ThemeOf(0).accent, resume),
            ("Ricomincia", Soft, restart),
            ("Impostazioni", Soft, () => ShowSettings(() => ShowPause(resume, restart, levelsMenu, menuLabel))),
            (menuLabel, Soft, levelsMenu),
        };
        for (int i = 0; i < items.Length; i++)
            Btn(card, items[i].Item1, items[i].Item2, Ink, new Vector2(0, 110 - i * 105), new Vector2(400, 84), items[i].Item3, round);
    }

    Action settingsBack;
    int resIndex; // chosen resolution: Screen.width/height only change on the next frame
    bool fullscreen;

    void ShowSettings(Action back)
    {
        settingsBack = back;
        var res = Resolutions();
        resIndex = Mathf.Max(0, res.FindIndex(r => r.x == Screen.width && r.y == Screen.height));
        fullscreen = Screen.fullScreen;
        BuildSettings();
    }

    static List<Vector2Int> Resolutions() =>
        Screen.resolutions.Select(r => new Vector2Int(r.width, r.height)).Distinct().ToList();

    void BuildSettings()
    {
        Hide();
        screen = Page.Settings;
        var full = Rect(root, Vector2.zero, Vector2.zero);
        Box(full, new Color(0, 0, 0, 0.6f), Vector2.zero, Vector2.zero, null);
        var top = Corner(full, new Vector2(0, 1));
        Label(top, "M I N I P O O L", 26, new Color(1, 1, 1, 0.7f), new Vector2(90, -70), new Vector2(600, 40), TextAnchor.MiddleLeft, true, true);
        Label(top, "Impostazioni", 76, White, new Vector2(90, -135), new Vector2(1000, 90), TextAnchor.MiddleLeft, true, true);

        var c = Corner(full, new Vector2(0.5f, 0.5f));
        static float Step(float v, float d, float lo, float hi) => Mathf.Clamp(Mathf.Round((v + d) * 100) / 100, lo, hi);
        Row(c, 220, "Volume", $"{Mathf.RoundToInt(GameManager.Volume * 100)}%", d => GameManager.Volume = Step(GameManager.Volume, d * 0.1f, 0, 1));
        Row(c, 120, "Musica", $"{Mathf.RoundToInt(Sfx.Music * 100)}%", d => Sfx.Music = Step(Sfx.Music, d * 0.1f, 0, 1));
        Row(c, 20, "Sensibilità camera", $"×{GameManager.Sensitivity:0.00}", d => GameManager.Sensitivity = Step(GameManager.Sensitivity, d * 0.25f, 0.25f, 3));
        Row(c, -80, "Schermo intero", fullscreen ? "Sì" : "No", _ =>
        {
            fullscreen = !fullscreen;
            Screen.fullScreen = fullscreen;
        });
        var res = Resolutions();
        if (res.Count > 0)
            Row(c, -180, "Risoluzione", $"{res[resIndex].x} × {res[resIndex].y}", d =>
            {
                resIndex = (resIndex + d + res.Count) % res.Count;
                Screen.SetResolution(res[resIndex].x, res[resIndex].y, fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            });
        Btn(c, "Indietro", Glass, White, new Vector2(0, -360), new Vector2(260, 80), CloseSettings, round);
    }

    void CloseSettings()
    {
        PlayerPrefs.Save();
        settingsBack();
    }

    // name on the left, value between ‹ › (change gets -1 / +1), then the page is rebuilt with the new value
    void Row(Transform parent, float y, string name, string value, Action<int> change)
    {
        Label(parent, name, 38, White, new Vector2(-520, y), new Vector2(460, 60), TextAnchor.MiddleLeft, true, true);
        ArrowButton(parent, "‹", new Vector2(60, y), () => { change(-1); BuildSettings(); });
        Label(parent, value, 38, White, new Vector2(260, y), new Vector2(300, 60), bold: true, shadow: true);
        ArrowButton(parent, "›", new Vector2(460, y), () => { change(1); BuildSettings(); });
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------- level select ----------

    // levels open in order (Rules.Unlocked): locked balls stay dark, locked worlds can't be opened
    bool Open(int lv) => Rules.Unlocked(levels, lv, GameManager.Completed);

    bool WorldOpen(int w) => levels.Any(l => Worlds.WorldOf(l) == w && Open(l));

    // ---------- world select ----------

    // one card per world (the prefab's template cloned next to it), colored with the world's table; then its levels
    void ShowWorlds()
    {
        if (levels.Count == 0 || !view || !view.worlds || !view.worldCard) { ShowLevels(previewed); return; }
        Hide();
        screen = Page.Worlds;
        ShowTable(0);
        view.worlds.gameObject.SetActive(true);
        view.worldCard.gameObject.SetActive(false);
        for (int w = 0; w <= Worlds.WorldOf(levels.Max()); w++)
        {
            int k = w;
            var theme = Worlds.ThemeOf(w);
            var mine = levels.Where(l => Worlds.WorldOf(l) == w).ToList();
            bool open = WorldOpen(w);
            var c = Instantiate(view.worldCard, view.worldCard.transform.parent);
            c.gameObject.SetActive(true);
            spawned.Add(c.gameObject);
            if (c.frame) c.frame.color = theme.wood;
            if (c.felt) c.felt.color = theme.felt;
            if (c.number) c.number.text = (w + 1).ToString();
            if (c.title) c.title.text = theme.name.ToUpperInvariant();
            int done = mine.Count(GameManager.Completed), crowns = mine.Count(GameManager.Crown);
            if (c.progress) c.progress.text = !open ? "BLOCCATO" : $"{done} / {mine.Count}" + (crowns > 0 ? $"   ·   {crowns} {(crowns == 1 ? "corona" : "corone")}" : "");
            if (c.highlight) c.highlight.enabled = false;
            if (c.preview && mine.Count > 0)
            {
                LevelBuilder.SetTheme(theme); // the preview in its own world's colors (materials are cached by color)
                c.preview.texture = PiecePreview(Resources.Load<TextAsset>($"Levels/level{mine[0]:00}").text, w, theme.sky);
            }
            if (c.dot)
            {
                // one dot per level: done, crowned, still to do
                c.dot.gameObject.SetActive(false);
                foreach (int l in mine)
                {
                    var d = Instantiate(c.dot, c.dot.transform.parent);
                    d.gameObject.SetActive(true);
                    d.color = GameManager.Crown(l) ? c.dotCrown : GameManager.Completed(l) ? c.dotDone : c.dotTodo;
                }
            }
            if (!open)
            {
                c.gameObject.AddComponent<CanvasGroup>().alpha = c.lockedAlpha;
                if (c.button) c.button.interactable = false;
                continue;
            }
            worldCards.Add((w, c));
            if (c.button) c.button.onClick.AddListener(() => { Sfx.Play(Sfx.Kind.Click, 1); OpenWorld(k); });
            OnEnter(c, () => worldSel = k);
        }
        LevelBuilder.SetTheme(Worlds.ThemeOf(0)); // back to the table's
        int last = Worlds.WorldOf(previewed > 0 ? previewed : levels[0]);
        worldSel = worldCards.Any(c => c.world == last) ? last : worldCards.Count > 0 ? worldCards[^1].world : 0;
    }

    // the world's levels, starting on the last level played there if it's open, otherwise its last open level
    void OpenWorld(int w)
    {
        int lv = Worlds.WorldOf(previewed) == w && Open(previewed) ? previewed : levels.LastOrDefault(l => Worlds.WorldOf(l) == w && Open(l));
        if (lv > 0) ShowLevels(lv);
    }

    // ---------- level select ----------

    public void ShowLevels(int select)
    {
        if (levels.Count == 0)
        {
            Hide();
            Label(Rect(root, Vector2.zero, Vector2.zero), "Nessun livello in Resources/Levels", 40, White, Vector2.zero, new Vector2(1400, 80));
            return;
        }
        if (!levels.Contains(select) || !Open(select)) select = levels.Last(Open);
        selected = 0;
        BuildWorld(Worlds.WorldOf(select));
        Select(select);
    }

    // the world's table with its balls on the prefab's slots (an S by default), a line joining them in order
    void BuildWorld(int world)
    {
        Hide();
        screen = Page.Levels;
        var theme = Worlds.ThemeOf(world);
        ShowTable(world);
        if (!view) return;
        view.levels.gameObject.SetActive(true);
        if (view.popup) view.popup.gameObject.SetActive(false);
        if (view.worldCaption) view.worldCaption.text = string.Format(view.captionFormat, world + 1);
        if (view.worldTitle) view.worldTitle.text = theme.name.ToUpperInvariant();
        HardText();
        int maxWorld = Worlds.WorldOf(levels.Max());
        void Arrow(Button b, bool on, int w)
        {
            if (!b) return;
            b.gameObject.SetActive(on);
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() => { Sfx.Play(Sfx.Kind.Click, 1); OpenWorld(w); });
        }
        Arrow(view.prevWorld, world > 0, world - 1);
        Arrow(view.nextWorld, world < maxWorld && WorldOpen(world + 1), world + 1);
        if (table == null) return;

        if (!lineMat) lineMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        lineMat.color = Color.Lerp(theme.felt, view.lineColor, view.lineMix);
        Vector3 prev = default;
        for (int b = 1; b <= Worlds.LevelsPerWorld && b <= view.levelSlots.Length; b++)
        {
            if (!view.levelSlots[b - 1]) continue;
            var pos = view.levelSlots[b - 1].position;
            pos.y = Ball.Radius * view.ballScale;
            int lv = Worlds.Level(world, b);
            var ball = LevelBuilder.SpawnBall(b, pos, table.root).transform;
            spawned.Add(ball.gameObject);
            ball.localScale = Vector3.one * view.ballScale;
            ball.rotation = Facing(ball, Vector3.up, Vector3.forward); // number up, readable from the camera
            if (b > 1 && view.joinLine) Segment(prev, pos, lineMat);
            prev = pos;
            if (!levels.Contains(lv) || !Open(lv)) // missing or still locked: dark, not selectable
            {
                foreach (var r in ball.GetComponentsInChildren<Renderer>()) r.sharedMaterial = Dim(b);
                continue;
            }
            icons[lv] = ball;
            if (GameManager.Crown(lv)) CrownHull(ball);
        }
    }

    // flat strip on the felt between two balls
    void Segment(Vector3 a, Vector3 b, Material mat)
    {
        a.y = b.y = 0.004f;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        spawned.Add(go);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(table.root, false);
        go.transform.SetPositionAndRotation((a + b) / 2, Quaternion.LookRotation(b - a));
        go.transform.localScale = new Vector3(view.lineWidth, 0.008f, (b - a).magnitude);
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    void ArrowButton(Transform parent, string s, Vector2 pos, Action click)
    {
        var b = Btn(parent, s, Glass, White, pos, new Vector2(80, 80), click, circle);
        b.GetComponentInChildren<Text>().fontSize = 56;
    }

    // the selected level gets the popup: the prefab's card above its ball with the level turning live, its name and record
    void Select(int lv)
    {
        if (lv == selected || !icons.TryGetValue(lv, out var ball)) return;
        selected = previewed = lv;
        ClearPreviews();
        if (!view.popup) return;
        view.popup.gameObject.SetActive(true);
        if (view.popupPreview) view.popupPreview.texture = PiecePreview(Resources.Load<TextAsset>($"Levels/level{lv:00}").text, 0);
        if (view.popupName) view.popupName.text = string.Format(view.nameFormat, Worlds.BallOf(lv));
        int best = GameManager.Best(lv), par = GameManager.Par(lv);
        var info = best > 0 ? $"Record: {best} {(best == 1 ? "tiro" : "tiri")}" : GameManager.Completed(lv) ? "Completato" : "Da completare";
        if (par > 0) info += GameManager.Crown(lv) ? "  ·  Corona!" : $"  ·  Corona in {par}";
        if (view.popupInfo) view.popupInfo.text = info;
        pop = view.popup;
        popT = 0;
        popBall = ball;
        LateUpdate();
    }

    // ---------- skins ----------

    void ShowSkins() => ShowSkin(BallArt.SelectedSkin);

    // the table turned upright (the prefab's skin camera), one cue ball per skin in rows and columns on it:
    // locked ones dark, the one in use with a gold ring; hover shows the name, a click on an open one uses it
    void ShowSkin(int s)
    {
        Hide();
        screen = Page.Skins;
        ShowTable(0);
        if (!view || !view.skins || table == null) return;
        view.skins.gameObject.SetActive(true);
        int n = BallArt.Skins.Length;
        for (int i = 0; i < n; i++)
        {
            var ball = LevelBuilder.SpawnBall(0, view.SkinSpot(i, n, table.bounds.center), table.root).transform;
            spawned.Add(ball.gameObject);
            ball.localScale = Vector3.one * view.skinBallScale;
            var mat = BallArt.Unlocked(i) ? BallArt.CueMaterial(i) : Dim(-1 - i, BallArt.CueMaterial(i));
            foreach (var r in ball.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
            if (i == BallArt.SelectedSkin) CrownHull(ball);
            skinBalls[i] = ball;
        }
        SkinInfo(s);
    }

    void SkinInfo(int s)
    {
        skinShown = s;
        if (view.skinName) view.skinName.text = BallArt.Skins[s].name.ToUpperInvariant();
        if (view.skinState)
            view.skinState.text = BallArt.SelectedSkin == s ? "IN USO" : BallArt.Unlocked(s) ? "CLIC PER USARLA" : "BLOCCATA: " + BallArt.Requirement(s);
    }

    // online table colors: one world theme per skin, drawn as a flat table (wood frame, cushions, felt, diamonds)
    void ShowTableSkin(int s)
    {
        Hide();
        screen = Page.TableSkins;
        skinShown = s;
        var themes = Worlds.Themes;
        var t = themes[s];
        var full = Rect(root, Vector2.zero, Vector2.zero);
        Box(full, new Color(0, 0, 0, 0.35f), Vector2.zero, Vector2.zero, null);

        var top = Corner(full, new Vector2(0, 1));
        Label(top, "M I N I P O O L", 26, new Color(1, 1, 1, 0.7f), new Vector2(90, -70), new Vector2(600, 40), TextAnchor.MiddleLeft, true, true);
        Label(top, "Skin del tavolo (online)", 76, White, new Vector2(90, -135), new Vector2(1200, 90), TextAnchor.MiddleLeft, true, true);
        Btn(Corner(full, new Vector2(1, 1)), "Skin bianca", Glass, White, new Vector2(-200, -80), new Vector2(280, 70), ShowSkins, round);

        var center = Corner(full, new Vector2(0.5f, 0.5f));
        var table = Box(center, t.wood, new Vector2(0, 60), new Vector2(640, 360), null);
        Box(table, t.cushion, Vector2.zero, new Vector2(560, 280), null);
        Box(table, t.felt, Vector2.zero, new Vector2(536, 256), null);
        for (int i = 1; i < 8; i++)
            foreach (var y in new[] { -160f, 160f })
                if (i != 4) Box(table, new Color(0.95f, 0.92f, 0.82f), new Vector2(-280 + i * 70, y), Vector2.one * 10, circle);
        ArrowButton(center, "‹", new Vector2(-420, 60), () => ShowTableSkin((s + themes.Length - 1) % themes.Length));
        ArrowButton(center, "›", new Vector2(420, 60), () => ShowTableSkin((s + 1) % themes.Length));
        Label(center, t.name, 56, White, new Vector2(0, -200), new Vector2(800, 70), bold: true, shadow: true);
        for (int i = 0; i < themes.Length; i++)
            Box(center, i == s ? White : new Color(1, 1, 1, 0.3f), new Vector2((i - (themes.Length - 1) / 2f) * 30, -255), Vector2.one * 14, circle);

        bool inUse = GameManager.TableSkin == s;
        Btn(center, "Indietro", Glass, White, new Vector2(-150, -360), new Vector2(260, 80), ShowHome, round);
        Btn(center, inUse ? "In uso" : "Usa questa", inUse ? Soft : Worlds.ThemeOf(0).accent, Ink, new Vector2(150, -360), new Vector2(260, 80),
            () => { GameManager.TableSkin = s; ShowTableSkin(s); }, round);
    }

    // ---------- multiplayer ----------

    // create or join; status = progress or error line
    public void ShowMultiplayer(string status)
    {
        Hide();
        var full = Rect(root, Vector2.zero, Vector2.zero);
        Box(full, new Color(0, 0, 0, 0.35f), Vector2.zero, Vector2.zero, null);
        var top = Corner(full, new Vector2(0, 1));
        Label(top, "M I N I P O O L", 26, new Color(1, 1, 1, 0.7f), new Vector2(90, -70), new Vector2(600, 40), TextAnchor.MiddleLeft, true, true);
        Label(top, "Multiplayer", 76, White, new Vector2(90, -135), new Vector2(1000, 90), TextAnchor.MiddleLeft, true, true);

        var center = Corner(full, new Vector2(0.5f, 0.5f));
        Bar(center, new Vector2(0, 170), new Vector2(540, 92), 40,
            ("Crea minigolf", Worlds.ThemeOf(0).accent, Ink, () => onHost?.Invoke()),
            ("Crea biliardo (2 giocatori)", Worlds.ThemeOf(0).accent, Ink, () => onHostPool?.Invoke()));
        Label(center, "oppure entra con un codice", 28, new Color(1, 1, 1, 0.7f), new Vector2(0, 75), new Vector2(800, 40), shadow: true);
        var code = Input(center, "CODICE", new Vector2(-70, -10), new Vector2(420, 92));
        Btn(center, "Entra", Glass, White, new Vector2(220, -10), new Vector2(120, 92), () => onJoin?.Invoke(code.text), round);
        code.onSubmit.AddListener(s => onJoin?.Invoke(s));
        if (status != null) Label(center, status, 30, White, new Vector2(0, -120), new Vector2(1200, 44), bold: true, shadow: true);
        Btn(center, "Indietro", Glass, White, new Vector2(0, -330), new Vector2(260, 80), ShowHome, round);
    }

    // start = null: the host can't start yet (8-ball needs exactly 2 players)
    public void ShowLobby(string code, List<(int skin, Color color, string name)> players, bool host, Action start, Action leave, string mode)
    {
        Hide();
        var full = Rect(root, Vector2.zero, Vector2.zero);
        Box(full, new Color(0, 0, 0, 0.35f), Vector2.zero, Vector2.zero, null);
        var top = Corner(full, new Vector2(0, 1));
        Label(top, "M I N I P O O L", 26, new Color(1, 1, 1, 0.7f), new Vector2(90, -70), new Vector2(600, 40), TextAnchor.MiddleLeft, true, true);
        Label(top, "Partita · " + mode, 76, White, new Vector2(90, -135), new Vector2(1400, 90), TextAnchor.MiddleLeft, true, true);

        var center = Corner(full, new Vector2(0.5f, 0.5f));
        Label(center, "Codice partita", 28, new Color(1, 1, 1, 0.7f), new Vector2(0, 300), new Vector2(800, 40), shadow: true);
        var codeBox = Box(center, White, new Vector2(0, 220), new Vector2(520, 110));
        Label(codeBox, code, 72, Ink, Vector2.zero, new Vector2(520, 110), bold: true);
        Btn(center, "Copia", Glass, White, new Vector2(345, 220), new Vector2(140, 110), () => { GUIUtility.systemCopyBuffer = code; Toast("Codice copiato"); }, round);
        Label(center, host ? "Condividilo con gli amici: entrano da Multiplayer > Entra" : "In attesa che l'host inizi la partita…", 26,
            new Color(1, 1, 1, 0.7f), new Vector2(0, 140), new Vector2(1200, 40), shadow: true);

        // one card per player: their cue ball (with skin), name, color strip
        const float step = 200;
        for (int i = 0; i < players.Count; i++)
        {
            var (skin, color, name) = players[i];
            var card = Box(center, new Color(0, 0, 0, 0.35f), new Vector2((i - (players.Count - 1) / 2f) * step, -20), new Vector2(180, 210));
            Box(card, color, new Vector2(0, -95), new Vector2(180, 20));
            AddModel(Rect(card, new Vector2(0, 25), Vector2.one * 110), BallArt.CueMaterial(skin)).spin = true;
            Label(card, name, 22, White, new Vector2(0, -55), new Vector2(180, 30), bold: true);
        }

        if (host && start != null) Btn(center, "Inizia", Worlds.ThemeOf(0).accent, Ink, new Vector2(150, -260), new Vector2(260, 84), start, round);
        Btn(center, "Esci", Glass, White, host ? new Vector2(-150, -260) : new Vector2(0, -260), new Vector2(260, 84), leave, round);
    }

    // in-match scoreboard, top right
    public void ShowMatchHud(string title, List<(Color color, string name, int score, bool finished)> players, string corner, string help)
    {
        Hide();
        var anchor = Corner(Rect(root, Vector2.zero, Vector2.zero), new Vector2(1, 1));
        float h = 90 + players.Count * 46;
        var panel = Box(anchor,new Color(0, 0, 0, 0.45f), new Vector2(-230, -30 - h / 2), new Vector2(400, h));
        Label(panel, title, 30, White, new Vector2(-170, h / 2 - 34), new Vector2(340, 40), TextAnchor.MiddleLeft, true);
        Label(panel, corner, 22, new Color(1, 1, 1, 0.6f), new Vector2(150, h / 2 - 34), new Vector2(120, 40), TextAnchor.MiddleRight);
        for (int i = 0; i < players.Count; i++)
        {
            var (color, name, score, finished) = players[i];
            float y = h / 2 - 84 - i * 46;
            Box(panel, color, new Vector2(-170, y), new Vector2(18, 18), circle);
            Label(panel, name + (finished ? "  ✓" : ""), 24, finished ? new Color(1, 1, 1, 0.6f) : White, new Vector2(-150, y), new Vector2(260, 36), TextAnchor.MiddleLeft);
            Label(panel, score.ToString(), 26, White, new Vector2(110, y), new Vector2(120, 36), TextAnchor.MiddleRight, true);
        }
        var bottom = Corner(Rect(root, Vector2.zero, Vector2.zero), new Vector2(0.5f, 0));
        Label(bottom, help, 22,
            new Color(1, 1, 1, 0.7f), new Vector2(0, 30), new Vector2(1600, 36), shadow: true);
    }

    // between rounds: one card per field piece with a live 3D preview (the piece built far away, a camera circling it into a texture),
    // scattered over the screen in a random order (same `seed` = same layout, so it doesn't reshuffle when the screen is rebuilt)
    public void ShowPiecePicker(List<(string name, string text)> pieces, int seed, Action<int> pick)
    {
        Hide();
        var full = Rect(root, Vector2.zero, Vector2.zero);
        Box(full, new Color(0, 0, 0, 0.55f), Vector2.zero, Vector2.zero, null);
        var center = Corner(full, new Vector2(0.5f, 0.5f));
        Label(center, "Scegli un pezzo di campo da attaccare al tavolo", 44, White, new Vector2(0, 470), new Vector2(1600, 60), bold: true, shadow: true);

        int n = pieces.Count, cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(n * 1.8f))), rows = (n + cols - 1) / cols;
        float cellW = 1760f / cols, cellH = 820f / Mathf.Max(rows, 1);
        var card = new Vector2(Mathf.Min(320, cellW - 30), Mathf.Min(260, cellH - 30));
        var rng = new System.Random(seed);
        var slots = Enumerable.Range(0, cols * rows).OrderBy(_ => rng.Next()).ToList();
        for (int i = 0; i < n; i++)
        {
            int k = i, slot = slots[i];
            var jitter = new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f);
            var pos = new Vector2((slot % cols - (cols - 1) / 2f) * cellW + jitter.x * (cellW - card.x),
                                  -30 - (slot / cols - (rows - 1) / 2f) * cellH + jitter.y * (cellH - card.y));
            var b = Btn(center, "", White, Ink, pos, card, () => pick(k), round);
            b.transform.localRotation = Quaternion.Euler(0, 0, ((float)rng.NextDouble() - 0.5f) * 10);
            var img = Rect(b.transform, new Vector2(0, 18), card - new Vector2(20, 56)).gameObject.AddComponent<RawImage>();
            img.texture = PiecePreview(pieces[i].text, i);
            img.raycastTarget = false;
            Label(b.transform, pieces[i].name, 26, Ink, new Vector2(0, -card.y / 2 + 22), new Vector2(card.x, 36), bold: true);
        }
    }

    void ClearPreviews()
    {
        foreach (var o in previewStuff) if (o) Destroy(o);
        previewStuff.Clear();
        previewCams.Clear();
    }

    // piece previews: built 300 units apart far from the table; cameras circle them in Update, everything goes away in Hide
    readonly List<(Camera cam, Bounds b)> previewCams = new();
    readonly List<UnityEngine.Object> previewStuff = new();

    Texture PiecePreview(string text, int i, Color? background = null)
    {
        var lv = LevelBuilder.Build(text, "PiecePreview");
        var offset = new Vector3(2000 + i * 300, 0, 2000);
        lv.root.position = offset;
        var b = lv.bounds;
        b.center += offset;
        var rt = new RenderTexture(512, 400, 24) { antiAliasing = 2 };
        var c = new GameObject("PiecePreviewCam").AddComponent<Camera>();
        c.targetTexture = rt;
        c.fieldOfView = 35;
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = background ?? new Color(0.12f, 0.13f, 0.16f);
        c.farClipPlane = 200;
        previewCams.Add((c, b));
        previewStuff.Add(lv.root.gameObject);
        previewStuff.Add(c.gameObject);
        previewStuff.Add(rt);
        return rt;
    }

    // while placing a piece / the pocket: what to do (top left), an optional "Salta" button, the controls at the bottom
    public void ShowBuildHud(string line, string help, Action done)
    {
        Hide();
        var full = Rect(root, Vector2.zero, Vector2.zero);
        Label(Corner(full, new Vector2(0, 1)), line, 36, White, new Vector2(60, -60), new Vector2(1200, 50), TextAnchor.MiddleLeft, true, true);
        if (done != null) Btn(Corner(full, new Vector2(1, 1)), "Salta", Glass, White, new Vector2(-160, -70), new Vector2(220, 80), done, round);
        Label(Corner(full, new Vector2(0.5f, 0)), help, 22, new Color(1, 1, 1, 0.7f), new Vector2(0, 30), new Vector2(1600, 36), shadow: true);
    }

    // short message in the top center, fades by itself
    public void Toast(string text)
    {
        toasts.SetAsLastSibling();
        var t = Label(Corner(toasts, new Vector2(0.5f, 1)), text, 34, White, new Vector2(0, -90 - toasts.childCount * 50), new Vector2(1400, 50), bold: true, shadow: true);
        Destroy(t.transform.parent.gameObject, 3);
    }

    public void ShowStandings(string title, string subtitle, List<(Color color, string name, int roundPoints, int score, float time)> rows, Action menu)
    {
        Hide();
        var dimmer = Box(root, new Color(0, 0, 0, 0.45f), Vector2.zero, Vector2.zero, null);
        float w = 820, h = 260 + rows.Count * 64 + (menu != null ? 110 : 0);
        var card = Box(dimmer, White, Vector2.zero, new Vector2(w, h));
        pop = card;
        popT = 0;
        Label(card, title, 52, Ink, new Vector2(0, h / 2 - 70), new Vector2(w, 70), bold: true);
        Label(card, subtitle, 26, Grey, new Vector2(0, h / 2 - 120), new Vector2(w, 40));
        for (int i = 0; i < rows.Count; i++)
        {
            var (color, name, roundPoints, score, time) = rows[i];
            float y = h / 2 - 190 - i * 64;
            Box(card, Soft, new Vector2(0, y), new Vector2(w - 80, 54));
            Box(card, color, new Vector2(-w / 2 + 70, y), new Vector2(20, 20), circle);
            Label(card, $"{i + 1}. {name}", 28, Ink, new Vector2(-w / 2 + 95, y), new Vector2(380, 54), TextAnchor.MiddleLeft, true);
            Label(card, float.IsNaN(time) ? "" : roundPoints > 0 ? $"+{roundPoints}  ·  " + (time < 0 ? "ultimo" : $"{(int)time / 60}:{(int)time % 60:00}") : "non finito", 24, Grey, new Vector2(110, y), new Vector2(220, 54));
            Label(card, score.ToString(), 32, Ink, new Vector2(w / 2 - 100, y), new Vector2(100, 54), TextAnchor.MiddleRight, true);
        }
        if (menu != null) Btn(card, "Menu", Worlds.ThemeOf(0).accent, Ink, new Vector2(0, -h / 2 + 70), new Vector2(260, 80), menu, round);
    }

    // name = true: a level name (letters, digits, spaces, - and _, up to 24); otherwise a relay code
    InputField Input(Transform parent, string placeholder, Vector2 pos, Vector2 size, bool name = false)
    {
        if (name)
        {
            var f0 = Input(parent, placeholder, pos, size);
            f0.characterLimit = 24;
            f0.textComponent.fontSize = 34;
            f0.onValidateInput = (_, _, c) => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '\0';
            return f0;
        }
        var rt = Box(parent, White, pos, size);
        rt.GetComponent<Image>().raycastTarget = true;
        var ph = Label(rt, placeholder, 34, new Color(0, 0, 0, 0.3f), Vector2.zero, size, bold: true);
        var text = Label(rt, "", 44, Ink, Vector2.zero, size, bold: true);
        text.supportRichText = false;
        var f = rt.gameObject.AddComponent<InputField>();
        f.textComponent = text;
        f.placeholder = ph;
        f.characterLimit = 10;
        f.onValidateInput = (_, _, c) => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '\0'; // relay codes: letters and digits
        return f;
    }

    // ---------- results ----------

    // note: lines like "Corona conquistata!" / unlocked skins, empty for none
    public void ShowResults(int level, int next, int shots, int par, int attempts, float seconds, string note)
    {
        Hide();
        var theme = Worlds.ThemeOf(Worlds.WorldOf(level));
        var dimmer = Box(root, new Color(0, 0, 0, 0.45f), Vector2.zero, Vector2.zero, null);
        const float w = 760, h = 560;
        var card = Box(dimmer, White, new Vector2(0, -20), new Vector2(w, h));
        pop = card;
        popT = 0;

        var ball = Rect(card, new Vector2(0, h / 2), Vector2.one * 150);
        AddModel(ball, BallArt.Material(Worlds.BallOf(level))).spin = true;
        if (Rules.Crown(shots, par)) CrownOutline(ball);
        Label(card, "Livello completato!", 52, Ink, new Vector2(0, h / 2 - 125), new Vector2(w, 70), bold: true);
        Label(card, $"Mondo {Worlds.WorldOf(level) + 1} · {theme.name} · Livello {Worlds.BallOf(level)}", 26, Grey, new Vector2(0, h / 2 - 175), new Vector2(w, 40));

        var stats = new[]
        {
            (par > 0 ? $"Tiri (corona: {par})" : "Tiri", shots.ToString()), ("Tentativi", attempts.ToString()),
            ("Tempo", $"{(int)seconds / 60}:{(int)seconds % 60:00}"),
        };
        for (int i = 0; i < stats.Length; i++)
        {
            var x = (i - (stats.Length - 1) / 2f) * 220f;
            Box(card, Soft, new Vector2(x, -10), new Vector2(200, 140));
            Label(card, stats[i].Item2, 60, Ink, new Vector2(x, 10), new Vector2(200, 80), bold: true);
            Label(card, stats[i].Item1, 24, Grey, new Vector2(x, -50), new Vector2(200, 36));
        }

        if (!string.IsNullOrEmpty(note)) Label(card, note, 24, RecordColor, new Vector2(0, -128), new Vector2(w, 84), bold: true);

        var buttons = new List<(string, Color, Color, Action)>
        {
            ("Menu", Soft, Ink, () => onMenu?.Invoke()),
            ("Rigioca", Soft, Ink, () => onPlay?.Invoke(level)),
        };
        if (next > 0) buttons.Add((Worlds.WorldOf(next) != Worlds.WorldOf(level) ? "Mondo successivo" : "Prossimo", theme.accent, Ink, () => onPlay?.Invoke(next)));
        Bar(card, new Vector2(0, -h / 2 + 70), new Vector2(next > 0 ? 210 : 260, 76), 20, buttons.ToArray());
    }

    // a row of equal buttons centered on pos, laid out by uGUI (HorizontalLayoutGroup): one more button needs no new coordinates
    Button[] Bar(Transform parent, Vector2 pos, Vector2 size, float gap, params (string label, Color bg, Color fg, Action click)[] items)
    {
        var row = Rect(parent, pos, new Vector2(items.Length * (size.x + gap) - gap, size.y));
        var g = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        g.spacing = gap;
        g.childAlignment = TextAnchor.MiddleCenter;
        g.childControlWidth = g.childControlHeight = g.childForceExpandWidth = g.childForceExpandHeight = false; // Btn sets the size
        var buttons = new Button[items.Length];
        for (int i = 0; i < items.Length; i++)
            buttons[i] = Btn(row, items[i].label, items[i].bg, items[i].fg, Vector2.zero, size, items[i].click, round);
        return buttons;
    }

    // ---------- per frame ----------

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < previewCams.Count; i++)
        {
            var (c, b) = previewCams[i];
            var rot = Quaternion.Euler(50, Time.unscaledTime * 25 + i * 47, 0);
            c.transform.SetPositionAndRotation(b.center - rot * Vector3.forward * (Mathf.Max(b.size.x, b.size.z) * 1.6f + 3), rot);
        }
        foreach (var (w, c) in worldCards)
        {
            float s = Mathf.Lerp(c.transform.localScale.x, w == worldSel ? view.cardHoverScale : 1f, 1 - Mathf.Exp(-14 * dt));
            c.transform.localScale = Vector3.one * s;
            if (c.highlight && c.highlight.enabled != (w == worldSel)) c.highlight.enabled = w == worldSel;
        }
        foreach (var (lv, t) in icons) Grow(t, lv == selected, view.ballScale);
        foreach (var (k, t) in skinBalls) Grow(t, k == skinShown, view.skinBallScale);
        if (pop)
        {
            popT = Mathf.Min(1, popT + dt * 3.5f);
            float t = popT - 1, back = 1 + 2.7f * t * t * t + 1.7f * t * t; // ease-out-back
            pop.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1, back);
        }
        if (hint)
        {
            hintT = Mathf.Min(1, hintT + dt * 3f);
            float t = hintT - 1, back = 1 + 2.7f * t * t * t + 1.7f * t * t;
            hint.localScale = Vector3.one * Mathf.LerpUnclamped(0.6f, 1, back); // pops up from the bottom edge
        }

        var kb = Keyboard.current;
        if (kb == null) return;
        int move = (kb.rightArrowKey.wasPressedThisFrame ? 1 : 0) - (kb.leftArrowKey.wasPressedThisFrame ? 1 : 0);
        bool enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame, esc = kb.escapeKey.wasPressedThisFrame;
        if (screen == Page.Home)
        {
            int dy = (kb.downArrowKey.wasPressedThisFrame ? 1 : 0) - (kb.upArrowKey.wasPressedThisFrame ? 1 : 0);
            if (dy != 0) HomeSelect((homeSel + dy + homeActions.Length) % homeActions.Length);
            else if (enter) { Sfx.Play(Sfx.Kind.Click, 1); homeActions[homeSel](); }
        }
        else if (screen == Page.Levels)
        {
            int hover = Hovered(icons);
            if (hover > 0)
            {
                Select(hover);
                if (Mouse.current.leftButton.wasPressedThisFrame) { Sfx.Play(Sfx.Kind.Click, 1); onPlay?.Invoke(hover); return; }
            }
            int i = levels.IndexOf(selected);
            if (move != 0 && i + move >= 0 && i + move < levels.Count && Open(levels[i + move]))
            {
                int lv = levels[i + move];
                if (Worlds.WorldOf(lv) != Worlds.WorldOf(selected)) { BuildWorld(Worlds.WorldOf(lv)); selected = 0; }
                Select(lv);
            }
            if (enter) onPlay?.Invoke(selected);
            else if (esc) ShowWorlds();
        }
        else if (screen == Page.Worlds)
        {
            int k = worldCards.FindIndex(c => c.world == worldSel);
            if (move != 0 && k + move >= 0 && k + move < worldCards.Count) worldSel = worldCards[k + move].world;
            if (enter && k >= 0) OpenWorld(worldSel);
            else if (esc) ShowHome();
        }
        else if (screen == Page.Skins)
        {
            int hover = Hovered(skinBalls), n = BallArt.Skins.Length;
            if (hover >= 0 && hover != skinShown) SkinInfo(hover);
            bool click = hover >= 0 && Mouse.current.leftButton.wasPressedThisFrame;
            if (move != 0) SkinInfo((skinShown + move + n) % n);
            else if ((enter || click) && BallArt.Unlocked(skinShown) && BallArt.SelectedSkin != skinShown)
            {
                Sfx.Play(Sfx.Kind.Click, 1);
                BallArt.SelectedSkin = skinShown;
                ShowSkin(skinShown); // the gold ring moves
            }
            else if (esc) ShowHome();
        }
        else if (screen == Page.TableSkins)
        {
            int n = Worlds.Themes.Length;
            if (move != 0) ShowTableSkin((skinShown + move + n) % n);
            else if (enter) { GameManager.TableSkin = skinShown; ShowTableSkin(skinShown); }
            else if (esc) ShowHome();
        }
        else if (screen == Page.MyLevels && esc) myLevelsBack?.Invoke();
        else if (screen == Page.Settings && esc)
        {
            escFrame = Time.frameCount;
            CloseSettings();
        }
    }

    // the chosen ball eases to hoverScale × its size, the others back; it stays on the felt
    void Grow(Transform t, bool chosen, float size)
    {
        if (!t) return;
        float s = Mathf.Lerp(t.localScale.x, (chosen ? view.hoverScale : 1f) * size, 1 - Mathf.Exp(-14 * Time.unscaledDeltaTime));
        t.localScale = Vector3.one * s;
        t.position = new Vector3(t.position.x, Ball.Radius * s, t.position.z);
    }

    // the key of the ball under the mouse (screen distance to its center, a bit more than its radius), -1 for none
    int Hovered(Dictionary<int, Transform> balls)
    {
        var mouse = Mouse.current;
        if (mouse == null || EventSystem.current && EventSystem.current.IsPointerOverGameObject()) return -1;
        Vector2 m = mouse.position.ReadValue();
        foreach (var (key, t) in balls)
        {
            if (!t) continue;
            Vector2 c = cam.WorldToScreenPoint(t.position), edge = cam.WorldToScreenPoint(t.position + cam.transform.up * Ball.Radius * t.localScale.x);
            if ((m - c).magnitude < (edge - c).magnitude * 1.3f) return key;
        }
        return -1;
    }

    // after the camera moved: keep each 3D ball on its UI slot, at the slot's size, number toward the camera
    void LateUpdate()
    {
        float k = ModelDistance / PlaneDistance, s = root.lossyScale.x;
        foreach (var m in models)
        {
            if (!m.slot) continue;
            var p = root.InverseTransformPoint(m.slot.position); // canvas units, origin at the screen center
            m.t.localPosition = new Vector3(p.x * s, p.y * s, PlaneDistance) * k;
            m.t.localScale = Vector3.one * (m.slot.rect.height * m.slot.lossyScale.y * k);
            m.t.localRotation = (m.spin ? Quaternion.AngleAxis(Time.unscaledTime * 70, Vector3.up) : Quaternion.identity) * m.face;
        }
        if (pop && popBall && pop.parent is RectTransform layer)
        {
            // card above the ball, or under it when it doesn't fit above; kept inside the screen (card anchored and pivoted on its center)
            var r = Vector3.forward * Ball.Radius * popBall.localScale.x;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, cam.WorldToScreenPoint(popBall.position + r), cam, out var top);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, cam.WorldToScreenPoint(popBall.position - r), cam, out var bottom);
            var half = pop.sizeDelta / 2;
            var lim = layer.rect.size / 2 - half - Vector2.one * 20;
            bool above = top.y + half.y + view.popupGap <= lim.y;
            float y = above ? top.y + half.y + view.popupGap : bottom.y - half.y - view.popupGap;
            pop.anchoredPosition = new Vector2(Mathf.Clamp(top.x, -lim.x, lim.x), Mathf.Clamp(y, -lim.y, lim.y));
            if (pop.Find("Pointer") is RectTransform pointer) // toward the ball: bottom edge when above, top edge when under
            {
                pointer.anchorMin = pointer.anchorMax = new Vector2(0.5f, above ? 0 : 1);
                pointer.anchoredPosition = Vector2.zero;
            }
        }
        if (marker && view && ItemLabel(homeSel) is { } item)
        {
            // the home's cue ball rolls on the felt to the left of the chosen item's text
            var label = item.rectTransform;
            var ray = cam.ScreenPointToRay(RectTransformUtility.WorldToScreenPoint(cam, label.TransformPoint(new Vector3(label.rect.xMin - view.markerGap, label.rect.center.y, 0))));
            if (new Plane(Vector3.up, new Vector3(0, Ball.Radius, 0)).Raycast(ray, out float d))
            {
                var target = ray.GetPoint(d);
                var next = markerSnap ? target : Vector3.Lerp(marker.position, target, 1 - Mathf.Exp(-16 * Time.unscaledDeltaTime));
                var move = next - marker.position;
                if (move.sqrMagnitude > 1e-8f) marker.rotation = Quaternion.AngleAxis(move.magnitude / Ball.Radius * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, move)) * marker.rotation;
                marker.position = next;
                markerSnap = false;
            }
        }
    }

    // ---------- building blocks ----------

    static Material outlineMat;

    static Material CrownMat()
    {
        if (!outlineMat)
        {
            outlineMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = CrownGold };
            outlineMat.SetFloat("_Cull", (float)CullMode.Front);
        }
        return outlineMat;
    }

    // crown on a ball on the table: the same inverted hull, as a child of the ball
    static void CrownHull(Transform ball)
    {
        var go = LevelBuilder.Visual("Crown", PrimitiveType.Sphere, Vector3.zero, Vector3.one * Ball.Radius * 2 * 1.12f, Quaternion.identity, Color.white, ball, "Ball");
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            r.sharedMaterial = CrownMat();
            r.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    // crown: flat gold outline around a ball's slot (inverted hull: a bigger copy showing only its back faces, unlit, no glow)
    void CrownOutline(RectTransform ballSlot)
    {
        AddModel(Rect(ballSlot, Vector2.zero, Vector2.one * ballSlot.rect.height * 1.12f), CrownMat()); // child: grows with the ball
    }

    Model AddModel(RectTransform slot, Material mat)
    {
        var go = LevelBuilder.Visual("UIBall", PrimitiveType.Sphere, Vector3.zero, Vector3.one, Quaternion.identity, Color.white, null, "Ball");
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        var m = new Model { slot = slot, t = go.transform, face = Facing(go.transform, Vector3.back, Vector3.up) };
        m.t.SetParent(cam.transform, false);
        models.Add(m);
        LateUpdate();
        return m;
    }

    // rotation that turns the ball's first number disc (texture u = 0.25, v = 0.5) toward `toward`, its top toward `upDir`
    static Quaternion Facing(Transform holder, Vector3 toward, Vector3 upDir)
    {
        var mf = holder.GetComponentInChildren<MeshFilter>();
        var mesh = mf ? mf.sharedMesh : null;
        // shortcut: needs a readable Ball mesh (BallModelImport sets it); otherwise the number may face away
        if (!mesh || !mesh.isReadable) return Quaternion.identity;
        var uv = mesh.uv;
        var v = mesh.vertices;
        Vector3 At(Vector2 target)
        {
            int best = 0;
            for (int i = 1; i < uv.Length; i++)
                if ((uv[i] - target).sqrMagnitude < (uv[best] - target).sqrMagnitude) best = i;
            return holder.InverseTransformPoint(mf.transform.TransformPoint(v[best])) - holder.InverseTransformPoint(mf.transform.TransformPoint(mesh.bounds.center));
        }
        var n = At(new Vector2(0.25f, 0.5f));
        var up = At(new Vector2(0.25f, 0.62f)) - n;
        return Quaternion.LookRotation(toward, upDir) * Quaternion.Inverse(Quaternion.LookRotation(n, up));
    }

    // darkened ball for levels that don't exist yet
    Material Dim(int n) => Dim(n, BallArt.Material(n));

    Material Dim(int key, Material src)
    {
        if (!dim.TryGetValue(key, out var m) || !m) dim[key] = m = new Material(src) { color = new Color(0.3f, 0.3f, 0.3f) };
        return m;
    }

    // size zero = stretch over the whole parent
    RectTransform Rect(Transform parent, Vector2 pos, Vector2 size)
    {
        var rt = new GameObject("ui", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        if (size == Vector2.zero) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero; }
        else { rt.anchoredPosition = pos; rt.sizeDelta = size; }
        return rt;
    }

    // zero-size anchor point at a screen corner/edge (0..1), children are placed relative to it
    RectTransform Corner(Transform parent, Vector2 anchor)
    {
        var rt = Rect(parent, Vector2.zero, Vector2.one);
        rt.anchorMin = rt.anchorMax = anchor;
        return rt;
    }

    RectTransform Box(Transform parent, Color c, Vector2 pos, Vector2 size, Sprite sprite)
    {
        var rt = Rect(parent, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        if (sprite) { img.sprite = sprite; img.type = sprite == circle ? Image.Type.Simple : Image.Type.Sliced; }
        return rt;
    }

    RectTransform Box(Transform parent, Color c, Vector2 pos, Vector2 size) => Box(parent, c, pos, size, round);

    Text Label(Transform parent, string s, int size, Color c, Vector2 pos, Vector2 box, TextAnchor align = TextAnchor.MiddleCenter, bool bold = false, bool shadow = false, Font f = null)
    {
        var rt = Rect(parent, pos, box);
        if (align == TextAnchor.MiddleLeft) rt.pivot = new Vector2(0, 0.5f); // pos = left edge
        rt.anchoredPosition = pos;
        var t = rt.gameObject.AddComponent<Text>();
        t.font = f ? f : font;
        t.text = s;
        t.fontSize = size;
        t.color = c;
        t.alignment = align;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        if (shadow) t.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.5f);
        return t;
    }

    Button Btn(Transform parent, string s, Color bg, Color fg, Vector2 pos, Vector2 size, Action click, Sprite sprite)
    {
        var rt = Box(parent, bg, pos, size, sprite);
        var img = rt.GetComponent<Image>();
        img.raycastTarget = true;
        var b = rt.gameObject.AddComponent<Button>();
        var colors = b.colors;
        colors.normalColor = colors.selectedColor = new Color(0.92f, 0.92f, 0.92f); // hover brightens to the full color
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f);
        colors.fadeDuration = 0.08f;
        b.colors = colors;
        b.onClick.AddListener(() => { Sfx.Play(Sfx.Kind.Click, 1); click(); });
        Label(rt, s, 30, fg, Vector2.zero, size, bold: true);
        return b;
    }

    static Sprite RoundedSprite(int size, int r)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = Mathf.Max(r - (x + 0.5f), x + 0.5f - (size - r), 0);
            float dy = Mathf.Max(r - (y + 0.5f), y + 0.5f - (size - r), 0);
            float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
            px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
    }
}
