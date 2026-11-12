using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The menu's hand-edited layout (Resources/Menu/Menu.prefab, made by MenuPrefabCreator; an instance in the open scene wins):
/// the canvas with the home and level select panels, the camera pose over the table and where the level balls sit.
/// GameUI only fills texts, wires the buttons and moves things. In edit mode it builds the table (and the level balls when
/// the Levels panel is active, empty with Worlds) as an unsaved preview, so the Game view shows the menu as it will look.
/// The table is always built at the world origin: keep this object's root at (0, 0, 0).
/// </summary>
[ExecuteAlways]
public class MenuView : MonoBehaviour
{
    [Header("Camera e tavolo")]
    [Tooltip("Il menu si vede da questa camera (posizione, rotazione, FOV). Al Play la camera principale ne copia la posa e questa si spegne.")]
    public Camera view;
    [Tooltip("Dove stanno le palle dei livelli 1..15, in ordine (si spostano nella Scene view)")]
    public Transform[] levelSlots = new Transform[15];
    public float ballScale = 1.4f;
    [Tooltip("Ingrandimento della palla sotto il mouse")] public float hoverScale = 1.25f;
    [Tooltip("Linea sul panno che unisce le palle in ordine")] public bool joinLine = true;
    public float lineWidth = 0.07f;
    [Tooltip("Colore della linea: 0 = colore del panno, 1 = lineColor")] [Range(0, 1)] public float lineMix = 0.35f;
    public Color lineColor = new(0.96f, 0.92f, 0.84f);

    [Header("UI")]
    public Canvas canvas;
    public RectTransform home, levels;
    [Tooltip("Gioca, Multiplayer, Crea livelli, Skin, Impostazioni, Esci: in quest'ordine. Il testo è il primo Text figlio")]
    public Button[] homeItems = new Button[6];
    public Color itemColor = new(0.96f, 0.92f, 0.84f), itemSelected = new(0.96f, 0.78f, 0.32f);
    [Tooltip("Distanza (unità del canvas) della bianca dal bordo sinistro del testo scelto")] public float markerGap = 42;
    public Text worldCaption, worldTitle;
    [Tooltip("{0} = numero del mondo")] public string captionFormat = "MONDO {0}";
    public Button menuButton, hardButton, prevWorld, nextWorld;
    [Tooltip("Finestra sopra la palla sotto il mouse; la posizione la decide il gioco")] public RectTransform popup;
    public RawImage popupPreview;
    public Text popupName, popupInfo;
    [Tooltip("{0} = numero del livello nel mondo")] public string nameFormat = "LIVELLO {0}";
    [Tooltip("Spazio tra la palla e la finestra (unità del canvas). Se sopra non c'è posto la finestra va sotto la palla")] public float popupGap = 36;
    [Tooltip("Pannello della scelta del mondo (prima dei livelli)")] public RectTransform worlds;
    [Tooltip("Carta modello: si copia una volta per mondo nello stesso genitore (es. con un HorizontalLayoutGroup)")] public MenuWorldCard worldCard;
    [Tooltip("Ingrandimento della carta scelta")] public float cardHoverScale = 1.06f;

    [Header("Skin della bianca")]
    [Tooltip("Camera della pagina skin (il tavolo in verticale)")] public Camera skinView;
    public RectTransform skins;
    public Text skinName, skinState;
    public Button tableSkinButton, skinBackButton;
    [Tooltip("Palle per riga (attraverso il tavolo); le righe scendono lungo il tavolo, l'ultima è centrata")] public int skinColumns = 3;
    [Tooltip("Distanza tra le palle: x attraverso il tavolo, y lungo il tavolo (unità del mondo)")] public Vector2 skinSpacing = new(2.4f, 2.6f);
    public float skinBallScale = 2f;

    [HideInInspector] public int layoutVersion; // MenuPrefabCreator.Upgrade

    /// <summary>Where skin `i` of `count` sits on the table centered on `center`: rows along the table (+x is up on the skin camera), last row centered.</summary>
    public Vector3 SkinSpot(int i, int count, Vector3 center)
    {
        int cols = Mathf.Max(1, skinColumns), rows = (count + cols - 1) / cols, row = i / cols, col = i % cols;
        int inRow = Mathf.Min(cols, count - row * cols);
        return new Vector3(center.x + ((rows - 1) / 2f - row) * skinSpacing.y, Ball.Radius * skinBallScale, center.z - (col - (inRow - 1) / 2f) * skinSpacing.x);
    }

#if UNITY_EDITOR
    Transform preview;
    int previewState; // 1 = Levels panel on (balls on the slots), 2 = Worlds panel on: empty table

    int PanelState() => (levels && levels.gameObject.activeInHierarchy ? 1 : 0) | (worlds && worlds.gameObject.activeInHierarchy ? 2 : 0)
        | (skins && skins.gameObject.activeInHierarchy ? 4 : 0);
    readonly List<Transform> previewBalls = new();

    void OnEnable()
    {
        if (!Application.isPlaying) UnityEditor.EditorApplication.delayCall += Rebuild;
    }

    void OnValidate() // a changed parameter (spacing, scale...) shows right away
    {
        if (!Application.isPlaying) UnityEditor.EditorApplication.delayCall += Rebuild;
    }

    void OnDisable() => Clear();

    void Clear()
    {
        if (preview) DestroyImmediate(preview.gameObject);
        preview = null;
        previewBalls.Clear();
    }

    void Rebuild()
    {
        if (!this || Application.isPlaying || !isActiveAndEnabled) return;
        Clear();
        var text = Resources.Load<TextAsset>("PoolTable")?.text;
        if (text == null) return;
        previewState = PanelState();
        LevelBuilder.SetTheme(Worlds.ThemeOf(0));
        var lv = LevelBuilder.Build(previewState != 0 ? text.Split("---")[0] : text, "MenuPreview");
        preview = lv.root;
        LevelBuilder.Room(lv);
        // the lamps hide themselves only in Play (LampHider): here they'd hang between the menu camera and the table
        if (preview.Find("Lamps") is { } lamps) foreach (var r in lamps.GetComponentsInChildren<Renderer>()) r.enabled = false;
        if ((previewState & 1) != 0)
            for (int i = 0; i < levelSlots.Length; i++)
                if (levelSlots[i]) previewBalls.Add(LevelBuilder.SpawnBall(i + 1, levelSlots[i].position, preview).transform);
        if ((previewState & 4) != 0)
            for (int i = 0; i < BallArt.Skins.Length; i++)
            {
                var b = LevelBuilder.SpawnBall(0, SkinSpot(i, BallArt.Skins.Length, lv.bounds.center), preview);
                foreach (var r in b.GetComponentsInChildren<Renderer>()) r.sharedMaterial = BallArt.CueMaterial(i);
                b.transform.localScale = Vector3.one * skinBallScale;
            }
        // the skin page has its own camera: the Game view and the canvas follow the panel being edited
        bool skinPage = (previewState & 4) != 0 && skinView;
        if (skinView) skinView.enabled = skinPage;
        if (view) view.enabled = !skinPage;
        if (canvas) canvas.worldCamera = skinPage ? skinView : view;
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(preview.gameObject, gameObject.scene); // prefab mode too
        foreach (var t in preview.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
        Update();
    }

    // edit mode: follows the slots while they are dragged, rebuilds when a panel is turned on / off
    void Update()
    {
        if (Application.isPlaying) return;
        if (previewState != PanelState()) { Rebuild(); return; }
        for (int i = 0; i < previewBalls.Count && i < levelSlots.Length; i++)
            if (previewBalls[i] && levelSlots[i])
            {
                previewBalls[i].localScale = Vector3.one * ballScale;
                previewBalls[i].position = new Vector3(levelSlots[i].position.x, Ball.Radius * ballScale, levelSlots[i].position.z);
            }
    }
#endif
}
