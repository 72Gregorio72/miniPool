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
    public Func<string, int, int, LevelBuilder.Level> onTable; // (level text, world, table theme) -> the menu table (GameManager.MenuTable)
    public Action onMenu, onHost, onHostPool, onHostNine; // onHostPool / onHostNine: online 8-ball / 9-ball (2 players, turns)
    public Action onCreate; // "Crea livelli": the user's levels (LevelEditor)
    public Action<string> onJoin; // join code
    public List<int> levels = new();

    static readonly Color White = Color.white, Ink = new(0.12f, 0.13f, 0.16f), Grey = new(0.5f, 0.53f, 0.58f),
        Soft = new(0.93f, 0.94f, 0.95f), Cream = new(0.96f, 0.92f, 0.84f), Glass = new(1, 1, 1, 0.14f),
        CrownGold = new(1f, 0.78f, 0.15f); // crown outline around a level's ball
    Font TitleFont => view && view.skinName ? view.skinName.font : null; // the menu's slab font (Alfa Slab One)
    const float PlaneDistance = 0.3f, ModelDistance = 0.2f; // canvas and 3D icons, in front of the camera

    enum Page { None, Home, Intro, Worlds, Levels, Skins, Cues, Pause, Settings, MyLevels, Editor }
    Page screen;
    public bool OnSettings => screen == Page.Settings;
    public int escFrame; // frame in which this UI used Esc, so the game doesn't use it too

    RectTransform root, pop, toasts, hud; // toasts and hud (onboarding hint) survive Hide
    float popT, hintT;
    RectTransform hint;
    string hintText;
    /// <summary>The player closed the hint with its X: no hints until GameManager.Load resets it.</summary>
    public bool hintClosed;
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
    int tableWorld, tableTheme;
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
    readonly Dictionary<int, Transform> pins = new(); // world map: open world -> its ball on the map
    int worldSel;
    float introT; // "Gioca": seconds since the break shot
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
        mapCard = null;
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
        pins.Clear();
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
    GameObject cueShotStick; // the power bar's copy of the cue
    int cueShotSkin = -1;
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
        if (cueShotStick && cueShotSkin != BallArt.SelectedCue) BallArt.PaintCue(cueShotStick, cueShotSkin = BallArt.SelectedCue);
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
    // (copied once, repainted by SetCue when the cue skin changes)
    void BuildCueShot(float aspect)
    {
        var src = FindAnyObjectByType<CueController>(FindObjectsInactive.Include);
        if (!src || !src.Stick) return;
        var far = new Vector3(0, -2000, 0);
        var stick = cueShotStick = Instantiate(src.Stick.gameObject, transform);
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
        if (hintClosed) text = null;
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
        // X in the top right corner: hides the hints until the next level is loaded (GameManager.Load)
        Btn(hint, "X", Ink, White, new Vector2(w0 / 2 - 6, top - 6), new Vector2(48, 48), () => { hintClosed = true; SetHint(null); }, circle);
    }

    // ---------- home ----------

    /// <summary>Builds the menu table if it isn't up: world -1 = the home (rack + cue ball), otherwise that world's empty table.
    /// theme >= 0: the table gets that theme's colors (Worlds.Themes), the room stays the world's.</summary>
    public void ShowTable(int world, int theme = -1)
    {
        if (theme < 0) theme = Mathf.Max(0, world) % Worlds.Themes.Length;
        if (table != null && table.root && tableWorld == world && tableTheme == theme) return;
        var text = Resources.Load<TextAsset>("PoolTable")?.text ?? "";
        tableWorld = world;
        tableTheme = theme;
        table = onTable?.Invoke(world < 0 ? text : text.Split("---")[0], Mathf.Max(0, world), theme);
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
        if (view.tableSkinButton && view.skinBackButton)
        {
            // "Stecche": a copy of the table button one step above it (made before any listener is added, so it has none)
            var cues = Instantiate(view.tableSkinButton, view.tableSkinButton.transform.parent);
            RectTransform a = (RectTransform)view.tableSkinButton.transform, b = (RectTransform)view.skinBackButton.transform;
            ((RectTransform)cues.transform).anchoredPosition = a.anchoredPosition * 2 - b.anchoredPosition;
            cues.GetComponentInChildren<Text>().text = "Stecche";
            cues.onClick.AddListener(() => { Sfx.Play(Sfx.Kind.Click, 1); ShowCues(); });
        }
        if (view.skins) foreach (var t in view.skins.GetComponentsInChildren<Text>(true)) Outlined(t);
        // the table under the skins is the online table skin itself: a click switches it to the next one
        if (view.tableSkinButton) view.tableSkinButton.onClick.AddListener(() =>
        {
            Sfx.Play(Sfx.Kind.Click, 1);
            GameManager.TableSkin = (GameManager.TableSkin + 1) % Worlds.Themes.Length;
            ShowSkin(skinShown);
        });
        if (view.skinBackButton) view.skinBackButton.onClick.AddListener(() => { Sfx.Play(Sfx.Kind.Click, 1); ShowHome(); });
        homeActions = new Action[] { PlayIntro,() => ShowMultiplayer(null), () => onCreate?.Invoke(), ShowSkins, () => ShowSettings(ShowHome), Quit };
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
    /// Cue page: in front of the room's cue rack, turning a little with the mouse; to and from it the camera glides.
    public void PlaceMenuCamera(Camera c)
    {
        Vector3 pos;
        Quaternion rot;
        float fov;
        if (screen == Page.Intro && view && view.view)
        {
            // from the shot on: ease-in-back = a small pull back, then the dive toward the felt; ShowWorlds then eases out over the map
            float u = Mathf.Clamp01(introT / IntroSwap), s = (IntroBack + 1) * u * u * u - IntroBack * u * u;
            var from = view.view.transform;
            c.transform.SetPositionAndRotation(from.position + from.forward * IntroDive * s, from.rotation);
            c.fieldOfView = view.view.fieldOfView;
            return;
        }
        if (screen == Page.Cues && rack)
        {
            fov = RackFov;
            float d = (LevelBuilder.RackCueLength / 2 + 1.5f) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            var m = Mouse.current != null ? Mouse.current.position.ReadValue() / new Vector2(Screen.width, Screen.height) - Vector2.one * 0.5f : Vector2.zero;
            pos = rack.position + rack.forward * d;
            rot = Quaternion.LookRotation(-rack.forward) * Quaternion.Euler(-m.y * 8, m.x * 14, 0);
        }
        else
        {
            var from = !view ? null : screen == Page.Skins && view.skinView ? view.skinView : view.view;
            if (!from) return;
            pos = from.transform.position;
            rot = from.transform.rotation;
            fov = from.fieldOfView;
        }
        float k = glideUntil > Time.unscaledTime || screen == Page.Cues ? 1 - Mathf.Exp((screen == Page.Worlds ? -3 : -6) * Time.unscaledDeltaTime) : 1;
        c.transform.SetPositionAndRotation(Vector3.Lerp(c.transform.position, pos, k), Quaternion.Slerp(c.transform.rotation, rot, k));
        c.fieldOfView = Mathf.Lerp(c.fieldOfView, fov, k);
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

    // intro: seconds to the map, how far the camera dives toward the felt, how much it pulls back first (ease-in-back), cue speed
    const float IntroSwap = 1.1f, IntroDive = 7, IntroBack = 2.5f, IntroSpeed = 38;

    // "Gioca": the home's cue ball breaks the rack for real (PoolPhysics), the camera flies up, then down onto the world map
    void PlayIntro()
    {
        var cue = table?.balls.FirstOrDefault(b => b && b.number == 0);
        var apex = table?.balls.Where(b => b && b.number != 0).OrderBy(b => b.transform.position.x).FirstOrDefault(); // the rack grows toward +x
        if (!cue || !apex || !view || levels.Count == 0) { ShowWorlds(); return; }
        screen = Page.Intro;
        marker = null;
        view.home.gameObject.SetActive(false);
        introT = 0;
        var dir = apex.transform.position - cue.transform.position;
        dir.y = 0;
        PoolPhysics.Shoot(cue, dir.normalized, IntroSpeed, 0, 0.2f);
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
        var t = Worlds.ThemeOf(0);
        var dimmer = Box(root, new Color(0, 0, 0, 0.45f), Vector2.zero, Vector2.zero, null);
        var felt = TablePanel(dimmer, Vector2.zero, new Vector2(640, 720), t);
        pop = (RectTransform)felt.parent;
        popT = 0;
        Plaque(pop, "PAUSA", new Vector2(0, 360), t);
        // each item is a ball: the 8 takes you out of the level
        var items = new List<(string, int, Action)> { ("Riprendi", 1, resume) };
        if (restart != null) items.Add(("Ricomincia", 2, restart)); // online: no restart
        items.Add(("Impostazioni", 3, () => ShowSettings(() => ShowPause(resume, restart, levelsMenu, menuLabel))));
        items.Add((menuLabel, 8, levelsMenu));
        for (int i = 0; i < items.Count; i++)
            BallBtn(felt, items[i].Item1, items[i].Item2, new Vector2(0, 150 - i * 105), new Vector2(420, 84), items[i].Item3);
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
        var t = Worlds.ThemeOf(0);
        var dimmer = Box(root, new Color(0, 0, 0, 0.55f), Vector2.zero, Vector2.zero, null);
        var c = TablePanel(dimmer, Vector2.zero, new Vector2(1240, 820), t);
        Plaque(c.parent, "IMPOSTAZIONI", new Vector2(0, 410), t);
        static float Step(float v, float d, float lo, float hi) => Mathf.Clamp(Mathf.Round((v + d) * 100) / 100, lo, hi);
        Row(c, 240, "Volume", $"{Mathf.RoundToInt(GameManager.Volume * 100)}%", d => GameManager.Volume = Step(GameManager.Volume, d * 0.1f, 0, 1), GameManager.Volume);
        Row(c, 140, "Musica", $"{Mathf.RoundToInt(Sfx.Music * 100)}%", d => Sfx.Music = Step(Sfx.Music, d * 0.1f, 0, 1), Sfx.Music);
        Row(c, 40, "Sensibilità camera", $"×{GameManager.Sensitivity:0.00}", d => GameManager.Sensitivity = Step(GameManager.Sensitivity, d * 0.25f, 0.25f, 3));
        Row(c, -60, "Schermo intero", fullscreen ? "Sì" : "No", _ =>
        {
            fullscreen = !fullscreen;
            Screen.fullScreen = fullscreen;
        });
        var res = Resolutions();
        if (res.Count > 0)
            Row(c, -160, "Risoluzione", $"{res[resIndex].x} × {res[resIndex].y}", d =>
            {
                resIndex = (resIndex + d + res.Count) % res.Count;
                Screen.SetResolution(res[resIndex].x, res[resIndex].y, fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            });
        BallBtn(c, "Indietro", 0, new Vector2(0, -270), new Vector2(300, 76), CloseSettings);
    }

    void CloseSettings()
    {
        PlayerPrefs.Save();
        settingsBack();
    }

    // name on the left, value between two cue-ball arrows (change gets -1 / +1), then the page is rebuilt with the new value;
    // meter >= 0: the value is a row of ten balls, lit up to meter (0..1)
    void Row(Transform parent, float y, string name, string value, Action<int> change, float meter = -1)
    {
        Label(parent, name, 38, Cream, new Vector2(-500, y), new Vector2(460, 60), TextAnchor.MiddleLeft, true, true, TitleFont);
        CueArrow(parent, "‹", new Vector2(40, y), () => { change(-1); BuildSettings(); });
        if (meter < 0) Label(parent, value, 38, Cream, new Vector2(270, y), new Vector2(340, 60), bold: true, shadow: true);
        else
            for (int i = 0, lit = Mathf.RoundToInt(meter * 10); i < 10; i++)
            {
                var at = new Vector2(270 + (i - 4.5f) * 34, y);
                Box(parent, new Color(0, 0, 0, 0.3f), at + new Vector2(2, -3), Vector2.one * 30, circle); // shadow / empty slot on the felt
                if (i >= lit) continue;
                var ball = Box(parent, BallArt.ColorOf(i + 1), at, Vector2.one * 30, circle);
                Box(ball, White, Vector2.zero, Vector2.one * 13, circle);
            }
        CueArrow(parent, "›", new Vector2(500, y), () => { change(1); BuildSettings(); });
    }

    void CueArrow(Transform parent, string s, Vector2 pos, Action click)
    {
        var b = Btn(parent, s, Cream, Ink, pos, new Vector2(64, 64), click, circle);
        b.GetComponentInChildren<Text>().fontSize = 46;
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

    const float MapBallScale = 1f, MapCardScale = 0.8f;
    static readonly Vector3 MapCardSpot = new(2.7f, 0, 2.9f); // the card's center on the table: the empty South Pacific
    RectTransform mapCard;                                     // holder of the world card, kept on MapCardSpot (LateUpdate)

    // the world map on the menu table: continents printed on the felt in a lighter tone with a faint grid of meridians and
    // parallels (WorldMap), one ball per tournament (Worlds.Theme) on its city; locked or future ones dark. The chosen one
    // grows and the prefab's world card shows it in the Pacific; a click on a ball (or the card) opens the world
    void ShowWorlds()
    {
        if (levels.Count == 0 || !view || !view.worlds || !view.worldCard) { ShowLevels(previewed); return; }
        Hide();
        screen = Page.Worlds;
        ShowTable(0);
        if (table == null) return;
        view.worlds.gameObject.SetActive(true);
        view.worldCard.gameObject.SetActive(false);
        foreach (var n in new[] { "Title", "Caption" }) // the card page's title: on the map it would cover the north
            if (view.worlds.Find(n) is { } title) title.gameObject.SetActive(false);
        var felt = Worlds.ThemeOf(0).felt;
        spawned.Add(WorldMap.Build(table.root, Color.Lerp(felt, Cream, 0.3f)));
        if (!lineMat) lineMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        lineMat.color = Color.Lerp(felt, Cream, 0.15f);
        for (int lat = -30; lat <= 60; lat += 30)
            Segment(WorldMap.Project(new Vector2(-172, lat)), WorldMap.Project(new Vector2(182, lat)), lineMat, 0.002f, 0.025f);
        for (int lon = -150; lon <= 180; lon += 30)
            Segment(WorldMap.Project(new Vector2(lon, -45)), WorldMap.Project(new Vector2(lon, 80)), lineMat, 0.002f, 0.025f);
        int last = Worlds.WorldOf(levels.Max());
        for (int w = 0; w < Worlds.Themes.Length; w++)
        {
            var pos = WorldMap.Project(Worlds.Themes[w].city) + Vector3.up * Ball.Radius * MapBallScale;
            var ball = LevelBuilder.SpawnBall(w + 1, pos, table.root).transform;
            spawned.Add(ball.gameObject);
            ball.localScale = Vector3.one * MapBallScale;
            ball.rotation = Facing(ball, Vector3.up, Vector3.forward);
            if (w > last || !WorldOpen(w))
            {
                foreach (var r in ball.GetComponentsInChildren<Renderer>()) r.sharedMaterial = Dim(w + 1);
                continue;
            }
            pins[w] = ball;
            if (GameManager.WorldDone(w, true)) CrownHull(ball);
        }
        int played = Worlds.WorldOf(previewed > 0 ? previewed : levels[0]);
        worldSel = -1;
        if (pins.Count > 0) SelectWorld(pins.ContainsKey(played) ? played : pins.Keys.Max());
    }

    // the prefab's world card (a fresh copy) for the chosen tournament: name, country, progress, its first level turning
    void SelectWorld(int w)
    {
        worldSel = w;
        if (mapCard) { spawned.Remove(mapCard.gameObject); Destroy(mapCard.gameObject); }
        ClearPreviews();
        var theme = Worlds.ThemeOf(w);
        var mine = levels.Where(l => Worlds.WorldOf(l) == w).ToList();
        mapCard = Rect(view.worlds, Vector2.zero, Vector2.one);
        mapCard.localScale = Vector3.one * MapCardScale;
        spawned.Add(mapCard.gameObject);
        var c = Instantiate(view.worldCard, mapCard);
        c.gameObject.SetActive(true);
        var rt = (RectTransform)c.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        if (c.frame) c.frame.color = theme.wood;
        if (c.felt) c.felt.color = theme.felt;
        if (c.number) c.number.text = (w + 1).ToString();
        if (c.title) c.title.text = theme.tournament.ToUpperInvariant();
        int done = mine.Count(GameManager.Completed), crowns = mine.Count(GameManager.Crown);
        if (c.progress) c.progress.text = $"{theme.country.ToUpperInvariant()}   ·   {done} / {mine.Count}" + (crowns > 0 ? $"   ·   {crowns} {(crowns == 1 ? "corona" : "corone")}" : "");
        if (c.highlight) c.highlight.enabled = true;
        if (c.preview && mine.Count > 0)
        {
            LevelBuilder.SetTheme(theme); // the preview in its own world's colors (materials are cached by color)
            c.preview.texture = PiecePreview(Resources.Load<TextAsset>($"Levels/level{mine[0]:00}").text, w, theme.sky);
            LevelBuilder.SetTheme(Worlds.ThemeOf(0)); // back to the table's
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
        if (c.button) c.button.onClick.AddListener(() => { Sfx.Play(Sfx.Kind.Click, 1); OpenWorld(w); });
        pop = rt;
        popT = 0;
        popBall = null;
        LateUpdate();
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
    void Segment(Vector3 a, Vector3 b, Material mat, float y = 0.004f, float width = -1)
    {
        a.y = b.y = y;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        spawned.Add(go);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(table.root, false);
        go.transform.SetPositionAndRotation((a + b) / 2, Quaternion.LookRotation(b - a));
        go.transform.localScale = new Vector3(width > 0 ? width : view.lineWidth, 0.008f, (b - a).magnitude);
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
    // the table is the online table skin (GameManager.TableSkin), its button switches it in place; the room stays world 1's
    void ShowSkin(int s)
    {
        Hide();
        screen = Page.Skins;
        ShowTable(0, GameManager.TableSkin);
        if (!view || !view.skins || table == null) return;
        view.skins.gameObject.SetActive(true);
        if (view.tableSkinButton && view.tableSkinButton.GetComponentInChildren<Text>() is { } label)
            label.text = "Tavolo: " + Worlds.Themes[GameManager.TableSkin].name;
        int n = BallArt.Skins.Length;
        for (int i = 0; i < n; i++)
        {
            var ball = LevelBuilder.SpawnBall(0, view.SkinSpot(i, n, table.bounds.center), table.root).transform;
            spawned.Add(ball.gameObject);
            ball.localScale = Vector3.one * view.skinBallScale;
            var mat = BallArt.Unlocked(i) ? BallArt.CueMaterial(i) : Dim(-1 - i, BallArt.CueMaterial(i), 0.08f);
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

    // ---------- cue skins ----------

    const float RackFov = 60;
    Transform rack;                              // the room's "CueRack" (LevelBuilder.Room): child k = cue skin k
    int cueShown;
    Text cueName, cueState;
    float glideUntil;                            // the menu camera glides (not snaps) until then: to and from the rack

    // the room is the menu: the camera turns to the rack on the wall, the chosen cue slides out, a click uses an open one
    void ShowCues()
    {
        Hide();
        screen = Page.Cues;
        glideUntil = Time.unscaledTime + 1.5f;
        rack = table?.root ? table.root.Find("CueRack") : null;
        var full = Rect(root, Vector2.zero, Vector2.zero);
        var f = view && view.skinName ? view.skinName.font : null;
        Label(Corner(full, new Vector2(0, 1)), "STECCHE", 90, White, new Vector2(90, -110), new Vector2(1000, 110), TextAnchor.MiddleLeft, true, true, f);
        var side = Corner(full, new Vector2(0.5f, 0.5f)); // right of the rack, like the skin page
        cueName = Label(side, "", 64, White, new Vector2(330, 60), new Vector2(900, 80), TextAnchor.MiddleLeft, true, true, f);
        cueState = Label(side, "", 32, new Color(1f, 0.85f, 0.4f), new Vector2(330, 0), new Vector2(900, 50), TextAnchor.MiddleLeft, true, true,
            view && view.skinState ? view.skinState.font : null);
        Outlined(Btn(side, "Indietro", Glass, White, new Vector2(460, -110), new Vector2(260, 70), LeaveCues, round).GetComponentInChildren<Text>());
        CueInfo(BallArt.SelectedCue);
    }

    void CueInfo(int s)
    {
        cueShown = s;
        cueName.text = BallArt.Cues[s].name.ToUpperInvariant();
        cueState.text = BallArt.SelectedCue == s ? "IN USO" : BallArt.CueUnlocked(s) ? "CLIC PER USARLA" : "BLOCCATA: " + BallArt.CueRequirement(s);
    }

    void LeaveCues()
    {
        glideUntil = Time.unscaledTime + 1.5f;
        ShowSkins();
    }

    // the rack cue under the mouse: its column on screen, one rack slot wide
    int HoveredCue()
    {
        var mouse = Mouse.current;
        if (!rack || mouse == null || EventSystem.current && EventSystem.current.IsPointerOverGameObject()) return -1;
        Vector2 m = mouse.position.ReadValue();
        for (int i = 0; i < rack.childCount; i++)
        {
            var p = rack.GetChild(i).position;
            Vector2 c = cam.WorldToScreenPoint(p), top = cam.WorldToScreenPoint(p + Vector3.up * LevelBuilder.RackCueLength / 2),
                bottom = cam.WorldToScreenPoint(p - Vector3.up * LevelBuilder.RackCueLength / 2);
            float half = Mathf.Abs(((Vector2)cam.WorldToScreenPoint(p + rack.right * LevelBuilder.RackSpacing / 2) - c).x);
            if (m.y > bottom.y && m.y < top.y && Mathf.Abs(m.x - c.x) < half) return i;
        }
        return -1;
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
        Bar(center, new Vector2(0, 170), new Vector2(420, 92), 30,
            ("Crea minigolf", Worlds.ThemeOf(0).accent, Ink, () => onHost?.Invoke()),
            ("Crea biliardo (2 giocatori)", Worlds.ThemeOf(0).accent, Ink, () => onHostPool?.Invoke()),
            ("Crea palla 9 (2 giocatori)", Worlds.ThemeOf(0).accent, Ink, () => onHostNine?.Invoke()));
        Label(center, "oppure entra con un codice", 28, new Color(1, 1, 1, 0.7f), new Vector2(0, 75), new Vector2(800, 40), shadow: true);
        var code = Input(center, "CODICE", new Vector2(-70, -10), new Vector2(420, 92));
        Btn(center, "Entra", Glass, White, new Vector2(220, -10), new Vector2(120, 92), () => onJoin?.Invoke(code.text), round);
        code.onSubmit.AddListener(s => onJoin?.Invoke(s));
        if (status != null) Label(center, status, 30, White, new Vector2(0, -120), new Vector2(1200, 44), bold: true, shadow: true);
        Btn(center, "Indietro", Glass, White, new Vector2(0, -330), new Vector2(260, 80), ShowHome, round);
    }

    // start = null: the host can't start yet (8-ball needs exactly 2 players). option = the aim lines setting: the host clicks it to change it
    public void ShowLobby(string code, List<(int skin, Color color, string name)> players, bool host, Action start, Action leave, string mode,
        string option, Action changeOption)
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

        if (host) Btn(center, option, Glass, White, new Vector2(0, -170), new Vector2(560, 64), changeOption, round);
        else Label(center, option, 28, White, new Vector2(0, -170), new Vector2(800, 44), bold: true, shadow: true);
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
        const float w = 900, h = 640;
        var card = TablePanel(dimmer, new Vector2(0, -20), new Vector2(w, h), theme);
        pop = (RectTransform)card.parent;
        popT = 0;

        // the level's ball rests on the top rail
        var ball = Rect(pop, new Vector2(0, h / 2), Vector2.one * 150);
        AddModel(ball, BallArt.Material(Worlds.BallOf(level))).spin = true;
        if (Rules.Crown(shots, par)) CrownOutline(ball);
        Plaque(card, "IN BUCA!", new Vector2(0, 170), theme, 52);
        Label(card, $"{theme.tournament} · {theme.country} · Livello {Worlds.BallOf(level)}", 26, Cream, new Vector2(0, 100), new Vector2(w, 40), shadow: true);

        var stats = new[]
        {
            (par > 0 ? $"Tiri (corona: {par})" : "Tiri", shots.ToString()), ("Tentativi", attempts.ToString()),
            ("Tempo", $"{(int)seconds / 60}:{(int)seconds % 60:00}"),
        };
        for (int i = 0; i < stats.Length; i++)
        {
            var x = (i - (stats.Length - 1) / 2f) * 240f;
            Box(card, theme.cushion, new Vector2(x, -10), new Vector2(220, 140));
            Label(card, stats[i].Item2, 60, Cream, new Vector2(x, 12), new Vector2(220, 80), bold: true, shadow: true, f: TitleFont);
            Label(card, stats[i].Item1, 24, new Color(Cream.r, Cream.g, Cream.b, 0.8f), new Vector2(x, -50), new Vector2(220, 36));
        }

        if (!string.IsNullOrEmpty(note)) Label(card, note, 26, theme.accent, new Vector2(0, -122), new Vector2(w, 60), bold: true, shadow: true);

        // ball buttons: the 8 leaves, the cue ball plays again, the next level's ball goes on
        var buttons = new List<(string, int, Action)> { ("Menu", 8, () => onMenu?.Invoke()), ("Rigioca", 0, () => onPlay?.Invoke(level)) };
        if (next > 0) buttons.Add((Worlds.WorldOf(next) != Worlds.WorldOf(level) ? "Torneo dopo" : "Prossimo", Worlds.BallOf(next), () => onPlay?.Invoke(next)));
        for (int i = 0; i < buttons.Count; i++)
            BallBtn(card, buttons[i].Item1, buttons[i].Item2, new Vector2((i - (buttons.Count - 1) / 2f) * 250, -200), new Vector2(230, 76), buttons[i].Item3);
    }

    // a small pool table as a panel: wood rails with diamonds, cushions, six pockets; returns the felt (place children from its center)
    RectTransform TablePanel(Transform parent, Vector2 pos, Vector2 size, Worlds.Theme t)
    {
        const float Rail = 46, Cushion = 14, Pocket = 58;
        var frame = Rect(parent, pos, size);
        Box(frame, new Color(0, 0, 0, 0.45f), new Vector2(0, -14), size); // shadow
        Box(frame, t.wood, Vector2.zero, size);
        Box(frame, t.darkWood, Vector2.zero, size - Vector2.one * (Rail * 2 - 10));
        Box(frame, t.cushion, Vector2.zero, size - Vector2.one * Rail * 2, null);
        var felt = Box(frame, t.felt, Vector2.zero, size - Vector2.one * (Rail + Cushion) * 2, null);
        float hx = size.x / 2 - Rail * 0.75f, hy = size.y / 2 - Rail * 0.75f, side = size.y / 2 - Rail * 0.6f;
        foreach (var p in new[] { new Vector2(-hx, hy), new Vector2(hx, hy), new Vector2(-hx, -hy), new Vector2(hx, -hy), new Vector2(0, side), new Vector2(0, -side) })
            Box(frame, new Color(0.04f, 0.04f, 0.05f), p, Vector2.one * Pocket, circle);
        for (int k = -3; k <= 3; k++) // diamonds: three per half of a long rail, three on each short rail
        {
            if (k != 0)
                foreach (int s in new[] { 1, -1 }) Diamond(frame, new Vector2(k * (size.x - 2 * Rail) / 8, s * (size.y / 2 - Rail / 2)));
            if (Mathf.Abs(k) <= 1)
                foreach (int s in new[] { 1, -1 }) Diamond(frame, new Vector2(s * (size.x / 2 - Rail / 2), k * (size.y - 2 * Rail) / 4));
        }
        return felt;
    }

    void Diamond(Transform parent, Vector2 at) => Box(parent, Cream, at, new Vector2(11, 11), null).localRotation = Quaternion.Euler(0, 0, 45);

    // a title on a dark wood plaque with an accent border, sized to its text
    Text Plaque(Transform parent, string s, Vector2 pos, Worlds.Theme t, int size = 46)
    {
        float h = size * 1.6f;
        var l = Label(parent, s, size, Cream, pos, new Vector2(10, h), bold: true, shadow: true, f: TitleFont);
        float w = l.preferredWidth + 80;
        Box(parent, t.accent, pos, new Vector2(w + 8, h + 8)).SetSiblingIndex(l.transform.GetSiblingIndex());
        Box(parent, t.darkWood, pos, new Vector2(w, h)).SetSiblingIndex(l.transform.GetSiblingIndex());
        return l;
    }

    // an ivory button with pool ball n (0 = the equipped cue ball) on its left
    Button BallBtn(Transform parent, string s, int n, Vector2 pos, Vector2 size, Action click)
    {
        var b = Btn(parent, s, Cream, Ink, pos, size, click, round);
        var text = b.GetComponentInChildren<Text>().rectTransform;
        float d = size.y - 18;
        var at = new Vector2(-size.x / 2 + d / 2 + 10, 0);
        Box(b.transform, Ink, at, Vector2.one * (d + 4), circle);
        var ball = Box(b.transform, BallArt.ColorOf(n), at, Vector2.one * d, circle);
        if (n > 0)
        {
            Box(ball, White, Vector2.zero, Vector2.one * d * 0.56f, circle);
            Label(ball, n.ToString(), Mathf.RoundToInt(d * 0.36f), Ink, Vector2.zero, Vector2.one * d, bold: true);
        }
        text.anchoredPosition = new Vector2(d / 2, 0); // the label centered in what's left
        return b;
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
        foreach (var (w, t) in pins) Grow(t, w == worldSel, MapBallScale);
        foreach (var (lv, t) in icons) Grow(t, lv == selected, view.ballScale);
        foreach (var (k, t) in skinBalls) Grow(t, k == skinShown, view.skinBallScale);
        if (rack) // the chosen cue slides out of the rack toward the room, the equipped one a little less, the rest back
            for (int i = 0; i < rack.childCount; i++)
            {
                var c = rack.GetChild(i);
                float to = screen != Page.Cues ? 0 : i == cueShown ? 0.6f : i == BallArt.SelectedCue ? 0.25f : 0;
                c.localPosition = new Vector3(c.localPosition.x, c.localPosition.y, Mathf.Lerp(c.localPosition.z, LevelBuilder.RackOut + to, 1 - Mathf.Exp(-14 * dt)));
            }
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
        if (screen == Page.Intro)
        {
            introT += dt;
            if (table != null) PoolPhysics.Simulate(table, Mathf.Min(dt, 0.05f));
            bool skip = kb != null && (kb.enterKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame);
            if (introT > IntroSwap || skip)
            {
                glideUntil = Time.unscaledTime + 3; // from close to the felt back out over the map
                ShowWorlds();
            }
            return;
        }
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
            int hover = Hovered(pins);
            if (hover >= 0)
            {
                if (hover != worldSel) SelectWorld(hover);
                if (Mouse.current.leftButton.wasPressedThisFrame) { Sfx.Play(Sfx.Kind.Click, 1); OpenWorld(hover); return; }
            }
            var open = pins.Keys.OrderBy(w => w).ToList();
            int k = open.IndexOf(worldSel);
            if (move != 0 && k + move >= 0 && k + move < open.Count) SelectWorld(open[k + move]);
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
        else if (screen == Page.Cues)
        {
            int hover = HoveredCue(), n = BallArt.Cues.Length;
            if (hover >= 0 && hover != cueShown) CueInfo(hover);
            bool click = hover >= 0 && Mouse.current.leftButton.wasPressedThisFrame;
            if (move != 0) CueInfo((cueShown + move + n) % n);
            else if ((enter || click) && BallArt.CueUnlocked(cueShown) && BallArt.SelectedCue != cueShown)
            {
                Sfx.Play(Sfx.Kind.Click, 1);
                BallArt.SelectedCue = cueShown;
                CueInfo(cueShown);
            }
            else if (esc) LeaveCues();
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
        if (mapCard && mapCard.parent is RectTransform panel) // the world card stays on its spot of the table while the camera moves
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(panel, cam.WorldToScreenPoint(MapCardSpot), cam, out var at);
            mapCard.anchoredPosition = at - panel.rect.center;
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

    Material Dim(int key, Material src, float shade = 0.3f)
    {
        if (!dim.TryGetValue(key, out var m) || !m) dim[key] = m = new Material(src) { color = new Color(shade, shade, shade) };
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
        if (shadow) Outlined(t);
        return t;
    }

    // black outline: the text reads on any background (room, felt, wallpaper)
    static void Outlined(Text t)
    {
        if (t.GetComponent<UnityEngine.UI.Outline>()) return;
        var o = t.gameObject.AddComponent<UnityEngine.UI.Outline>();
        o.effectColor = new Color(0, 0, 0, 0.9f);
        o.effectDistance = new Vector2(2.5f, -2.5f);
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
