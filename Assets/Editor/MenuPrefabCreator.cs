using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Creates Resources/Menu/Menu.prefab (MenuView): the home and level select layout to edit by hand in Unity.
// Made once (on load if missing, or from the menu); after that the prefab is the source and is never overwritten
// except by "Rigenera prefab menu".
[InitializeOnLoad]
static class MenuPrefabCreator
{
    const string Folder = "Assets/Resources/Menu", Path = Folder + "/Menu.prefab";
    static readonly Color Cream = new(0.96f, 0.92f, 0.84f), Gold = new(0.96f, 0.78f, 0.32f), Ink = new(0.12f, 0.13f, 0.16f),
        Glass = new(1, 1, 1, 0.14f);
    static Font title, body;
    static Sprite rounded, knob;

    static MenuPrefabCreator()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists(Path)) Create();
            else Upgrade();
        };
    }

    [MenuItem("MiniPool/Rigenera prefab menu")]
    static void Regenerate()
    {
        if (File.Exists(Path) && !EditorUtility.DisplayDialog("Prefab menu", "Ricreare il prefab del menu? Le modifiche fatte a mano vanno perse.", "Rigenera", "Annulla")) return;
        AssetDatabase.DeleteAsset(Path);
        Create();
    }

    const int Version = 4; // 2: popup = only the preview, world select panel; 3: UI glued to the table, fewer texts;
    // 4: world cards with preview / dots / highlight, skin page on the upright table

    static void LoadAssets()
    {
        title = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/AlfaSlabOne-Regular.ttf");
        body = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/BarlowCondensed-SemiBold.ttf");
        rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
    }

    // shortcut: if the prefab is open in Prefab Mode its auto save can write the old copy back; then close it and run this
    [MenuItem("MiniPool/Aggiorna prefab menu")]
    static void UpgradeNow()
    {
        if (UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == Path)
        {
            EditorUtility.DisplayDialog("Prefab menu", "Chiudi il prefab Menu (Prefab Mode) e riprova.", "OK");
            return;
        }
        Upgrade();
    }

    // brings a prefab made by an older version up to date, touching only what changed (the rest of the hand edits stay)
    static void Upgrade()
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
        if (!asset || !asset.TryGetComponent(out MenuView current) || current.layoutVersion >= Version) return;
        var root = PrefabUtility.LoadPrefabContents(Path);
        var v = root.GetComponent<MenuView>();
        if (v)
        {
            LoadAssets();
            if (v.layoutVersion < 2)
            {
                if (v.popupName) Object.DestroyImmediate(v.popupName.gameObject);
                if (v.popupInfo) Object.DestroyImmediate(v.popupInfo.gameObject);
                if (v.popupPreview) FillPopup(v.popupPreview);
                if (!v.worlds && v.canvas) BuildWorlds(v, (RectTransform)v.canvas.transform);
            }
            if (v.layoutVersion < 3) GlueToTable(v);
            if (v.layoutVersion < 4)
            {
                if (v.worldCard) CardDetails(v.worldCard);
                if (!v.skins && v.canvas) BuildSkins(v, (RectTransform)v.canvas.transform);
            }
            v.layoutVersion = Version;
            PrefabUtility.SaveAsPrefabAsset(root, Path);
            Debug.Log($"MiniPool: {Path} aggiornato alla versione {Version}");
        }
        PrefabUtility.UnloadPrefabContents(root);
    }

    // v3: the canvas scales with the screen height like the table does (the camera's FOV is vertical) and every element is
    // anchored to the screen center, so the UI stays on the felt on any screen shape (same look on 16:9); home texts move
    // right, off the rail; the captions around MINIPOOL and the controls lines go
    static void GlueToTable(MenuView v)
    {
        var scaler = v.canvas ? v.canvas.GetComponent<CanvasScaler>() : null;
        if (scaler) scaler.matchWidthOrHeight = 1;
        var size = scaler ? scaler.referenceResolution : new Vector2(1920, 1080);
        foreach (var panel in new[] { v.home, v.levels, v.worlds })
        {
            if (!panel) continue;
            foreach (RectTransform rt in panel)
            {
                if (rt.anchorMin != rt.anchorMax) continue; // stretched: already follows the screen
                rt.anchoredPosition += Vector2.Scale(rt.anchorMin - new Vector2(0.5f, 0.5f), size);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                if (panel == v.home) rt.anchoredPosition += new Vector2(HomeShift, 0);
            }
        }
        foreach (var (panel, name) in new[] { (v.home, "Caption"), (v.home, "Subtitle"), (v.home, "Help"), (v.levels, "Help") })
            if (panel && panel.Find(name) is { } t) Object.DestroyImmediate(t.gameObject);
    }

    const float HomeShift = 110;

    // world card: the felt shows the world's first level turning, the number in a dark badge on its corner,
    // a row of dots (one per level) between name and progress, a gold outline on the chosen card
    static void CardDetails(MenuWorldCard card)
    {
        var c = (RectTransform)card.transform;
        if (card.felt && !card.preview)
        {
            var pr = new GameObject("Preview", typeof(RectTransform), typeof(RawImage)).GetComponent<RectTransform>();
            pr.SetParent(card.felt.transform, false);
            pr.SetAsFirstSibling();
            pr.anchorMin = Vector2.zero;
            pr.anchorMax = Vector2.one;
            pr.offsetMin = new Vector2(8, 8);
            pr.offsetMax = new Vector2(-8, -8);
            card.preview = pr.GetComponent<RawImage>();
            card.preview.raycastTarget = false;
        }
        if (card.number && card.felt)
        {
            var badge = Img(card.felt.transform, "Badge", Ink, knob, Vector2.zero, new Vector2(68, 68), new Vector2(0, 1));
            badge.pivot = new Vector2(0.5f, 0.5f);
            badge.anchoredPosition = new Vector2(30, -30);
            var n = card.number.rectTransform;
            n.SetParent(badge, false);
            n.anchorMin = Vector2.zero;
            n.anchorMax = Vector2.one;
            n.pivot = new Vector2(0.5f, 0.5f);
            n.offsetMin = n.offsetMax = Vector2.zero;
            card.number.fontSize = 36;
        }
        if (card.title) card.title.rectTransform.anchoredPosition = new Vector2(0, 112);
        if (card.progress) card.progress.rectTransform.anchoredPosition = new Vector2(0, 26);
        if (!card.dot)
        {
            var row = Rect(c, "Dots", new Vector2(0, 66), new Vector2(330, 16), new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f));
            var g = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = 6;
            g.childAlignment = TextAnchor.MiddleCenter;
            g.childControlWidth = g.childControlHeight = g.childForceExpandWidth = g.childForceExpandHeight = false;
            card.dot = Img(row, "Dot", Cream, knob, Vector2.zero, new Vector2(14, 14), new Vector2(0.5f, 0.5f)).GetComponent<Image>();
        }
        if (!card.highlight && card.frame)
        {
            var o = card.frame.gameObject.AddComponent<UnityEngine.UI.Outline>();
            o.effectColor = Gold;
            o.effectDistance = new Vector2(7, 7);
            o.enabled = false;
            card.highlight = o;
        }
    }

    // skin page: its own camera with the table upright (long side up the screen), title on the left, name / state / buttons on the right
    static void BuildSkins(MenuView v, RectTransform canvas)
    {
        v.skinView = new GameObject("SkinCamera").AddComponent<Camera>();
        v.skinView.transform.SetParent(v.transform);
        v.skinView.fieldOfView = 60;
        v.skinView.nearClipPlane = 0.05f;
        v.skinView.depth = 10;
        v.skinView.clearFlags = CameraClearFlags.SolidColor;
        v.skinView.backgroundColor = Worlds.ThemeOf(0).sky;
        v.skinView.enabled = false; // MenuView turns it on while the Skins panel is being edited
        float h = 9.5f * 1.03f / Mathf.Tan(30 * Mathf.Deg2Rad); // 19 tiles from top to bottom of the screen
        v.skinView.transform.SetPositionAndRotation(new Vector3(9, h, 5), Quaternion.Euler(90, 90, 0));

        var center = new Vector2(0.5f, 0.5f);
        v.skins = Panel(canvas, "Skins");
        Txt(v.skins, "Title", "SKIN", title, 130, Cream, new Vector2(-880, 380), new Vector2(700, 160), TextAnchor.MiddleLeft, center);
        v.skinName = Txt(v.skins, "Name", "CLASSICA", title, 64, Cream, new Vector2(380, 80), new Vector2(560, 80), TextAnchor.MiddleLeft, center);
        v.skinState = Txt(v.skins, "State", "IN USO", body, 30, Gold, new Vector2(384, 20), new Vector2(560, 40), TextAnchor.MiddleLeft, center);
        v.tableSkinButton = Btn(v.skins, "Skin tavolo", Glass, rounded, new Vector2(380, -250), new Vector2(300, 70), 30, Color.white, TextAnchor.MiddleCenter, center);
        v.skinBackButton = Btn(v.skins, "Indietro", Glass, rounded, new Vector2(380, -340), new Vector2(300, 70), 30, Color.white, TextAnchor.MiddleCenter, center);
        v.skins.gameObject.SetActive(false);
    }

    // the level preview fills the card (10 units of border), at full brightness
    static void FillPopup(RawImage img)
    {
        img.color = Color.white;
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(10, 10);
        rt.offsetMax = new Vector2(-10, -10);
    }

    // world select: title, then one card per world (the template, cloned at runtime) in a centered row
    static void BuildWorlds(MenuView v, RectTransform canvas)
    {
        const float x = 212;
        v.worlds = Panel(canvas, "Worlds");
        v.worlds.SetSiblingIndex(v.levels ? v.levels.GetSiblingIndex() : 0);
        Txt(v.worlds, "Caption", "GIOCA", body, 22, Gold, new Vector2(x, -172), new Vector2(800, 30));
        Txt(v.worlds, "Title", "SCEGLI IL MONDO", title, 96, Cream, new Vector2(x - 4, -255), new Vector2(1600, 120));
        var row = Rect(v.worlds, "Cards", new Vector2(0, -90), new Vector2(1600, 460), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        var g = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        g.spacing = 48;
        g.childAlignment = TextAnchor.MiddleCenter;
        g.childControlWidth = g.childControlHeight = g.childForceExpandWidth = g.childForceExpandHeight = false;

        var c = Img(row, "Card", new Color(0.36f, 0.22f, 0.13f), rounded, Vector2.zero, new Vector2(360, 440), new Vector2(0.5f, 0.5f));
        c.pivot = new Vector2(0.5f, 0.5f);
        var card = c.gameObject.AddComponent<MenuWorldCard>();
        card.frame = c.GetComponent<Image>();
        card.frame.raycastTarget = true;
        card.button = c.gameObject.AddComponent<Button>();
        var felt = Img(c, "Felt", new Color(0.18f, 0.5f, 0.35f), rounded, Vector2.zero, Vector2.zero, Vector2.zero);
        felt.anchorMin = Vector2.zero;
        felt.anchorMax = Vector2.one;
        felt.offsetMin = new Vector2(20, 150);
        felt.offsetMax = new Vector2(-20, -20);
        card.felt = felt.GetComponent<Image>();
        card.number = Txt(felt, "Number", "1", title, 150, Cream, Vector2.zero, new Vector2(300, 200), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f));
        card.title = Txt(c, "Title", "CLASSICO", title, 40, Cream, new Vector2(0, 100), new Vector2(340, 50), TextAnchor.MiddleCenter, new Vector2(0.5f, 0));
        card.progress = Txt(c, "Progress", "0 / 15", body, 28, Gold, new Vector2(0, 50), new Vector2(340, 36), TextAnchor.MiddleCenter, new Vector2(0.5f, 0));
        v.worldCard = card;
        CardDetails(card);
        v.worlds.gameObject.SetActive(false);
    }

    [MenuItem("MiniPool/Crea prefab menu")]
    static void Create()
    {
        if (File.Exists(Path)) { Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(Path); return; }
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        LoadAssets();

        var root = new GameObject("Menu");
        var v = root.AddComponent<MenuView>();

        // camera fitted to the regulation table (PoolTable.txt, 19 x 11 tiles centered on (9, 5)) on a 16:9 screen, from straight above
        v.view = new GameObject("MenuCamera").AddComponent<Camera>();
        v.view.transform.SetParent(root.transform);
        v.view.fieldOfView = 60;
        v.view.nearClipPlane = 0.05f;
        v.view.depth = 10; // over the scene's main camera in the Game view while editing
        v.view.clearFlags = CameraClearFlags.SolidColor;
        v.view.backgroundColor = Worlds.ThemeOf(0).sky;
        float h = 5.5f * 1.03f / Mathf.Tan(30 * Mathf.Deg2Rad);
        v.view.transform.SetPositionAndRotation(new Vector3(9, h, 5), Quaternion.Euler(90, 0, 0));

        // level balls in an S: 1-5 left to right, 6-10 back, 11-15 left to right
        var slots = new GameObject("LevelSlots").transform;
        slots.SetParent(root.transform);
        for (int b = 1; b <= 15; b++)
        {
            int row = (b - 1) / 5, col = (b - 1) % 5;
            if (row % 2 == 1) col = 4 - col;
            var s = new GameObject(b.ToString()).transform;
            s.SetParent(slots);
            s.position = new Vector3(9 + (col - 2) * 2.8f, 0, 5 + (1 - row) * 2 - 0.6f);
            v.levelSlots[b - 1] = s;
        }

        var cgo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        cgo.transform.SetParent(root.transform);
        v.canvas = cgo.GetComponent<Canvas>();
        v.canvas.renderMode = RenderMode.ScreenSpaceCamera;
        v.canvas.worldCamera = v.view;
        v.canvas.planeDistance = 1;
        var scaler = cgo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var canvas = (RectTransform)cgo.transform;

        // ---- home: positions from the top left corner, like the reference picture ----
        const float x = 212;
        v.home = Panel(canvas, "Home");
        Txt(v.home, "Caption", "BILIARDO  ·  MINIGOLF", body, 22, Gold, new Vector2(x, -172), new Vector2(800, 30));
        Txt(v.home, "Title", "MINIPOOL", title, 150, Cream, new Vector2(x - 6, -265), new Vector2(1200, 180));
        Txt(v.home, "Subtitle", "Biliardo, minigolf e niente falli.", body, 26, new Color(Cream.r, Cream.g, Cream.b, 0.8f), new Vector2(x, -360), new Vector2(900, 34)).fontStyle = FontStyle.Italic;
        var names = new[] { "GIOCA", "MULTIPLAYER", "CREA LIVELLI", "SKIN", "IMPOSTAZIONI", "ESCI" };
        for (int i = 0; i < names.Length; i++)
        {
            var b = Btn(v.home, names[i], Color.clear, null, new Vector2(x + 40, -472 - i * 68), new Vector2(520, 62), 40, i == 0 ? Gold : Cream, TextAnchor.MiddleLeft);
            b.transition = Selectable.Transition.None;
            var t = (RectTransform)b.GetComponentInChildren<Text>().transform;
            t.offsetMin = new Vector2(38, 0); // text starts a bit right of the hit area: the cue ball sits there
            v.homeItems[i] = b;
        }
        Txt(v.home, "Help", "↑ ↓  SCEGLI   ·   INVIO  CONFERMA   ·   ESC  INDIETRO", body, 20, new Color(Cream.r, Cream.g, Cream.b, 0.75f), new Vector2(x, -925), new Vector2(900, 30));

        // ---- level select ----
        v.levels = Panel(canvas, "Levels");
        v.worldCaption = Txt(v.levels, "Caption", "MONDO 1", body, 22, Gold, new Vector2(x, -172), new Vector2(800, 30));
        v.worldTitle = Txt(v.levels, "Title", Worlds.ThemeOf(0).name.ToUpperInvariant(), title, 110, Cream, new Vector2(x - 4, -250), new Vector2(1200, 130));
        Txt(v.levels, "Help", "PASSA SU UNA PALLA   ·   CLIC GIOCA   ·   ← →  SCEGLI   ·   INVIO  GIOCA   ·   ESC  MENU", body, 20,
            new Color(Cream.r, Cream.g, Cream.b, 0.75f), new Vector2(x, -925), new Vector2(1400, 30));
        v.menuButton = Btn(v.levels, "Menu", Glass, rounded, new Vector2(-430, -200), new Vector2(200, 70), 30, Color.white, TextAnchor.MiddleCenter, new Vector2(1, 1));
        v.hardButton = Btn(v.levels, "Hard mode: no", Glass, rounded, new Vector2(-760, -200), new Vector2(300, 70), 30, Color.white, TextAnchor.MiddleCenter, new Vector2(1, 1));
        v.prevWorld = Btn(v.levels, "‹", Glass, knob, new Vector2(70, -60), new Vector2(80, 80), 56, Color.white, TextAnchor.MiddleCenter, new Vector2(0, 0.5f));
        v.nextWorld = Btn(v.levels, "›", Glass, knob, new Vector2(-150, -60), new Vector2(80, 80), 56, Color.white, TextAnchor.MiddleCenter, new Vector2(1, 0.5f));

        const float W = 460, H = 320;
        v.popup = Img(v.levels, "Popup", Ink, rounded, Vector2.zero, new Vector2(W, H), new Vector2(0.5f, 0.5f));
        v.popup.pivot = new Vector2(0.5f, 0.5f);
        var ptr = Img(v.popup, "Pointer", Ink, null, Vector2.zero, new Vector2(30, 30), new Vector2(0.5f, 0)); // diamond under the card, toward the ball
        ptr.pivot = new Vector2(0.5f, 0.5f);
        ptr.anchoredPosition = Vector2.zero;
        ptr.localRotation = Quaternion.Euler(0, 0, 45);
        var prev = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
        prev.transform.SetParent(v.popup, false);
        var pr = (RectTransform)prev.transform;
        pr.anchoredPosition = new Vector2(0, 40);
        pr.sizeDelta = new Vector2(W - 20, H - 100);
        v.popupPreview = prev.GetComponent<RawImage>();
        v.popupPreview.raycastTarget = false;
        FillPopup(v.popupPreview);
        v.levels.gameObject.SetActive(false); // turn it on to edit it: the table preview puts the balls on the slots

        BuildWorlds(v, canvas);
        GlueToTable(v);
        BuildSkins(v, canvas);
        v.layoutVersion = Version;
        PrefabUtility.SaveAsPrefabAsset(root, Path);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"MiniPool: creato {Path} (trascinalo nella scena per modificarlo vedendo il Game view)");
    }

    static RectTransform Panel(Transform parent, string name)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        return rt;
    }

    // anchor = point of the parent the position counts from (default top left); MiddleLeft text has its pivot on the left edge
    static RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size, Vector2 anchor, Vector2 pivot)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return rt;
    }

    static Text Txt(Transform parent, string name, string s, Font f, int size, Color c, Vector2 pos, Vector2 box,
        TextAnchor align = TextAnchor.MiddleLeft, Vector2? anchor = null)
    {
        var rt = Rect(parent, name, pos, box, anchor ?? new Vector2(0, 1), align == TextAnchor.MiddleLeft ? new Vector2(0, 0.5f) : new Vector2(0.5f, 0.5f));
        var t = rt.gameObject.AddComponent<Text>();
        t.font = f;
        t.text = s;
        t.fontSize = size;
        t.color = c;
        t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static RectTransform Img(Transform parent, string name, Color c, Sprite sprite, Vector2 pos, Vector2 size, Vector2 anchor)
    {
        var rt = Rect(parent, name, pos, size, anchor, new Vector2(0, 0.5f));
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c;
        img.sprite = sprite;
        if (sprite == rounded && sprite) img.type = Image.Type.Sliced;
        img.raycastTarget = false;
        return rt;
    }

    static Button Btn(Transform parent, string label, Color bg, Sprite sprite, Vector2 pos, Vector2 size, int fontSize, Color fg, TextAnchor align, Vector2? anchor = null)
    {
        var rt = Img(parent, label, bg, sprite, pos, size, anchor ?? new Vector2(0, 1));
        rt.GetComponent<Image>().raycastTarget = true;
        var b = rt.gameObject.AddComponent<Button>();
        var t = Txt(rt, "Text", label, body, fontSize, fg, Vector2.zero, Vector2.zero, align, Vector2.zero);
        var tr = (RectTransform)t.transform;
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.pivot = new Vector2(0.5f, 0.5f);
        tr.offsetMin = tr.offsetMax = Vector2.zero;
        return b;
    }
}
