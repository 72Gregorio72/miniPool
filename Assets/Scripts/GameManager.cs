using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

// Turn flow + HUD. Creates itself when Play starts, so no scene setup is needed.
public class GameManager : MonoBehaviour
{
    enum State { Menu, Aiming, Rolling, GameOver, Multiplayer, Editor }

    public int levelIndex = 1;

    LevelBuilder.Level level;
    CueController cueCtl;
    GameUI ui;
    MultiplayerGame mp;
    LevelEditor editor;

    // a user level (LevelEditor) played instead of levelNN: its text, name and where to go when leaving it; its goal is always the 8
    string customText, customName;
    System.Action customBack;
    int Goal => customText != null ? 8 : Worlds.BallOf(levelIndex);
    List<int> levels; // indices of the levelNN files in Resources/Levels, sorted
    Ball cue;
    State state;
    float shotTime;

    // shots, attempts and fouls of the current level (Rules.Run: a restart clears shots and gives the fouls back)
    readonly Rules.Run run = new();
    int par; // the level's "tiri" line, read once at load (HUD)
    readonly Dictionary<int, Vector3> startSpot = new(); // cue and level ball where the level starts them (back there after a forgiven foul)
    float levelStart;

    // per-shot record
    int firstHit;
    readonly List<int> potted = new();
    readonly List<int> lost = new(); // left the table without a pocket (fell off, spikes)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!FindAnyObjectByType<GameManager>()) new GameObject("Game").AddComponent<GameManager>();
    }

    public const string TestLevelKey = "MiniPool.TestLevel"; // set by the level editor's "Prova" button

    // level beaten at least once
    public static bool Completed(int index) => PlayerPrefs.GetInt($"MiniPool.done.{index}", 0) == 1;

    // fewest shots of a winning run (0 = never won); par = the level's "tiri" line (LevelBuilder.ParOf)
    public static int Best(int index) => PlayerPrefs.GetInt($"MiniPool.best.{index}", 0);
    public static int Par(int index) => Resources.Load<TextAsset>($"Levels/level{index:00}") is { } t ? LevelBuilder.ParOf(t.text) : 0;
    public static bool Crown(int index) => Rules.Crown(Best(index), Par(index));

    // every level of the world beaten (with the crown if `crowns`): unlocks skins (BallArt.Skin.world)
    public static bool WorldDone(int world, bool crowns) =>
        Enumerable.Range(1, Worlds.LevelsPerWorld).Select(b => Worlds.Level(world, b)).All(lv => crowns ? Crown(lv) : Completed(lv));

    // online table colors: a world theme (Worlds.Themes); the host's choice is used
    public static int TableSkin
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt("MiniPool.table", 0), 0, Worlds.Themes.Length - 1);
        set { PlayerPrefs.SetInt("MiniPool.table", value); PlayerPrefs.Save(); } // saved at once: a crash must not lose a setting
    }

    // hard mode: no cue ball aim line in the levels
    public static bool HardMode
    {
        get => PlayerPrefs.GetInt("MiniPool.hard", 0) == 1;
        set { PlayerPrefs.SetInt("MiniPool.hard", value ? 1 : 0); PlayerPrefs.Save(); }
    }

    // settings screen: master volume 0..1 and camera mouse sensitivity (multiplier)
    public static float Volume
    {
        get => PlayerPrefs.GetFloat("MiniPool.volume", 1);
        set { PlayerPrefs.SetFloat("MiniPool.volume", value); PlayerPrefs.Save(); AudioListener.volume = value; }
    }

    public static float Sensitivity
    {
        get => PlayerPrefs.GetFloat("MiniPool.sensitivity", 1);
        set { PlayerPrefs.SetFloat("MiniPool.sensitivity", value); PlayerPrefs.Save(); }
    }

    bool paused;

    void Start()
    {
        AudioListener.volume = Volume;
        gameFov = Camera.main.fieldOfView;
        int test = 0;
#if UNITY_EDITOR
        test = UnityEditor.SessionState.GetInt(TestLevelKey, 0);
        UnityEditor.SessionState.EraseInt(TestLevelKey);
#endif
        cueCtl = gameObject.AddComponent<CueController>();
        cueCtl.onShot = OnShot;
        ui = gameObject.AddComponent<GameUI>();
        cueCtl.ui = ui;
        ui.onPlay = StartLevel;
        ui.onMenu = ShowMenu;
        ui.onTable = MenuTable;
        mp = gameObject.AddComponent<MultiplayerGame>();
        mp.ui = ui;
        mp.game = this;
        ui.onHost = () => mp.Host(LevelBuilder.GoalToEight(Resources.Load<TextAsset>($"Levels/level{levels.FirstOrDefault():00}")?.text, Worlds.BallOf(levels.FirstOrDefault())));
        ui.onHostPool = () => mp.Host(Resources.Load<TextAsset>("PoolTable")?.text, pool: true);
        ui.onJoin = mp.Join;
        editor = gameObject.AddComponent<LevelEditor>();
        editor.game = this;
        editor.ui = ui;
        ui.onCreate = editor.ShowList;
        levels = Resources.LoadAll<TextAsset>("Levels")
            .Select(t => t.name.StartsWith("level") && int.TryParse(t.name.Substring(5), out int i) ? i : 0)
            .Where(i => i > 0).OrderBy(i => i).ToList();
        ui.levels = levels;
        if (test > 0) StartLevel(test);
        else { SetState(State.Menu); ui.ShowHome(); }
    }

    void ShowMenu()
    {
        if (customText != null) { ExitCustom(); return; }
        SetState(State.Menu);
        ui.ShowLevels(levelIndex);
    }

    // MultiplayerGame takes the table and the camera for the match
    public void EnterMultiplayer()
    {
        if (level != null) Destroy(level.root.gameObject);
        level = null;
        SetState(State.Multiplayer);
    }

    public void ExitMultiplayer(string error = null)
    {
        SetState(State.Menu);
        if (error != null) { ui.ShowHome(); ui.ShowMultiplayer(error); }
        else ui.ShowHome();
    }

    /// <summary>Menu background: a table built for show (no simulation), seen from the menu camera.</summary>
    public LevelBuilder.Level MenuTable(string text, int world)
    {
        if (level != null) Destroy(level.root.gameObject);
        ApplyWorld(world);
        level = LevelBuilder.Build(text, "MenuTable");
        LevelBuilder.Room(level); // the world's room around the table (home: world 1's)
        MenuCamera();
        return level;
    }

    // every frame in the menu: the pose of the menu prefab's camera (MenuView), edited by hand
    void MenuCamera() => ui.PlaceMenuCamera(Camera.main);

    float gameFov = 60; // the scene camera's, used everywhere but the menu

    public static void ApplyTheme(int index) => ApplyWorld(Worlds.WorldOf(index));

    public static void ApplyWorld(int world)
    {
        var t = Worlds.ThemeOf(world);
        LevelBuilder.SetTheme(t);
        var cam = Camera.main;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = t.sky;
        RenderSettings.fog = true; // the room floor fades into the sky color
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = t.sky;
        RenderSettings.fogStartDistance = 35;
        RenderSettings.fogEndDistance = 90;
    }

    // fresh start of a level (from the menu or "next"); Load alone is a restart that counts as a new attempt
    void StartLevel(int index)
    {
        run.attempts = 0;
        levelStart = Time.time;
        Load(index);
    }

    void Load(int index)
    {
        var asset = customText != null ? null : Resources.Load<TextAsset>($"Levels/level{index:00}");
        string text = customText ?? (asset ? asset.text : null), name = customName ?? (asset ? asset.name : null);
        if (text == null)
        {
            ShowMenu();
            return;
        }
        if (level != null) Destroy(level.root.gameObject);
        levelIndex = index;
        if (customText != null) ApplyWorld(Mathf.Max(0, LevelBuilder.ThemeLine(text)));
        else ApplyTheme(index);
        level = LevelBuilder.Build(text, name);
        LevelBuilder.Room(level);
        cue = level.balls.FirstOrDefault(b => b.number == 0);
        if (!cue)
        {
            ui.Toast($"{name}: manca la palla bianca (0)");
            SetState(State.GameOver);
            return;
        }
        cue.onCueHit = b => { if (firstHit < 0) firstHit = b.number; };
        cueCtl.cue = cue;
        cueCtl.level = level;
        cueCtl.guide = !HardMode;
        cueCtl.guideHit = false; // only the cue ball's line
        run.Begin(LevelBuilder.FoulsOf(text));
        par = LevelBuilder.ParOf(text);
        startSpot.Clear();
        foreach (var b in level.balls)
            if (b.number == 0 || b.number == Goal) startSpot[b.number] = b.transform.position;
        SetupCamera();
        SetState(State.Aiming);
    }

    /// <summary>Plays a user level (goal: the 8); leaving it (menu, M, pause > Esci) destroys it and calls back.</summary>
    public void PlayCustom(string text, string name, System.Action back)
    {
        customText = text;
        customName = name;
        customBack = back;
        StartLevel(0);
    }

    void ExitCustom()
    {
        var back = customBack;
        customText = customName = null;
        customBack = null;
        if (level != null) Destroy(level.root.gameObject);
        level = null;
        SetState(State.Menu);
        back?.Invoke();
    }

    /// <summary>The level editor takes the camera and the screen: no table, no game input.</summary>
    public void EnterEditor()
    {
        if (level != null) Destroy(level.root.gameObject);
        level = null;
        SetState(State.Editor);
    }

    /// <summary>Back to the menu with the home table behind it.</summary>
    public void BackToMenu()
    {
        SetState(State.Menu);
        ui.ShowTable(-1);
    }

    void SetupCamera()
    {
        var cam = Camera.main;
        cam.nearClipPlane = 0.05f;
        if (!cam.TryGetComponent(out OrbitCamera orbit)) orbit = cam.gameObject.AddComponent<OrbitCamera>();
        orbit.table = level.bounds;
        orbit.follow = cue.transform.position;
        orbit.Snap();
        orbit.yaw = 90; // looking down the table toward the rack (+x)
        cueCtl.orbit = orbit;
    }

    void SetState(State s)
    {
        state = s;
        if (s != State.Menu) Camera.main.fieldOfView = gameFov; // the menu camera may use another one
        if (!paused) Time.timeScale = 1; // slow motion ends with the shot
        cueCtl.enabled = s == State.Aiming;
        // the camera grabs the mouse (cursor lock), so it is off while a screen with buttons is up
        bool playing = s == State.Aiming || s == State.Rolling;
        if (cueCtl.orbit) cueCtl.orbit.enabled = playing;
        if (!playing) Cursor.lockState = CursorLockMode.None;
        if (playing) ui.Hide();
    }

    // time stops (physics, moving parts); the cue and the camera ignore the mouse while the menu is up
    void Pause(bool on)
    {
        paused = on;
        Time.timeScale = on ? 0 : 1;
        if (!on) { SetState(state); return; }
        cueCtl.enabled = false;
        if (cueCtl.orbit) cueCtl.orbit.enabled = false;
        Cursor.lockState = CursorLockMode.None;
        ui.ShowPause(() => Pause(false), () => { Pause(false); Load(levelIndex); }, () => { Pause(false); ShowMenu(); }, customText != null ? "Esci" : "Livelli");
    }

    void OnShot()
    {
        firstHit = -1;
        run.Shot();
        potted.Clear();
        lost.Clear();
        shotTime = Time.time;
        SetState(State.Rolling);
    }

    void Update()
    {
        ui.SetHint(state == State.Aiming && !paused && !(cueCtl.orbit && cueCtl.orbit.Free) ? Hint() : null);
        if (state is State.Multiplayer or State.Editor) return; // MultiplayerGame / LevelEditor drive
        if (state == State.Menu) { MenuCamera(); return; }
        var kb = Keyboard.current;
        // Esc with the right button held cancels the shot (CueController); otherwise it opens/closes the pause menu
        bool esc = kb != null && kb.escapeKey.wasPressedThisFrame && ui.escFrame != Time.frameCount;
        if (paused)
        {
            if (esc && !ui.OnSettings) Pause(false);
            return;
        }
        if (esc && state is State.Aiming or State.Rolling && !(Mouse.current != null && Mouse.current.rightButton.isPressed))
        {
            Pause(true);
            return;
        }
        if (kb != null && kb.rKey.wasPressedThisFrame) { if (state == State.GameOver) StartLevel(levelIndex); else Load(levelIndex); }
        if (kb != null && kb.mKey.wasPressedThisFrame) { ShowMenu(); return; }
        int next = NextLevel();
        if (kb != null && kb.nKey.wasPressedThisFrame && state == State.GameOver && next > 0) StartLevel(next);

        if (level == null) return;
        PoolPhysics.Simulate(level, Mathf.Min(Time.deltaTime, 0.05f));
        // camera follows the cue ball (OrbitCamera smooths it); not below the felt, so a pocketed cue doesn't drag it down
        if (cue && cue.gameObject.activeSelf && cueCtl.orbit)
        {
            var p = cue.transform.position;
            cueCtl.orbit.follow = new Vector3(p.x, Mathf.Max(p.y, Ball.Radius), p.z);
        }
        if (state != State.Rolling) return;

        // slow motion while the level's ball rolls into a pocket (single player only: online the clock is the host's)
        var black = level.balls.FirstOrDefault(b => b.number == Goal);
        float slow = black && Juice.Approach(level, black) > 0 ? Juice.SlowScale : 1;
        Time.timeScale = Mathf.MoveTowards(Time.timeScale, slow, Time.unscaledDeltaTime * 4);

        foreach (var b in level.balls)
        {
            if (!b.gameObject.activeSelf || b.transform.position.y > -0.6f) continue;
            if (b.pocket != null) potted.Add(b.number);
            else lost.Add(b.number);
            b.gameObject.SetActive(false);
        }

        if (Time.time - shotTime < 0.2f || level.balls.Any(b => b.gameObject.activeSelf && b.IsMoving)) return;
        EndShot();
    }

    void EndShot()
    {
        var end = run.EndShot(firstHit, potted, lost, Goal, out _);
        if (end == Rules.Run.End.Restart)
        {
            Sfx.Play(Sfx.Kind.Fail, 1);
            Load(levelIndex);
            return;
        }
        if (end == Rules.Run.End.Forgiven) // the run goes on from here; without the cue or the level ball it couldn't
        {
            Sfx.Play(Sfx.Kind.Fail, 1);
            foreach (var b in level.balls)
                if (!b.gameObject.activeSelf && startSpot.TryGetValue(b.number, out var spot))
                {
                    // shortcut: no check for a ball sitting on the spot (the physics pushes them apart); find a free spot if it shows
                    b.Stop();
                    b.transform.position = spot;
                    b.gameObject.SetActive(true);
                }
        }

        // no restart: object balls that left the table (potted or lost) are simply gone
        foreach (var b in level.balls.Where(b => !b.gameObject.activeSelf).ToList())
        {
            level.balls.Remove(b);
            Destroy(b.gameObject);
        }

        if (end == Rules.Run.End.Win)
        {
            Win();
            return;
        }
        SetState(State.Aiming);
    }

    void Win()
    {
        SetState(State.GameOver);
        int shots = run.shots, attempts = run.attempts;
        if (customText != null) // user level: no records, no unlocks
        {
            Sfx.Play(Rules.Crown(shots, par) ? Sfx.Kind.Crown : Sfx.Kind.Win, 1);
            ui.ShowCustomResults(customName, shots, par, attempts, Time.time - levelStart, () => StartLevel(0), ShowMenu);
            return;
        }
        bool first = !Completed(levelIndex), hadCrown = Crown(levelIndex);
        int best = Best(levelIndex);
        var locked = Enumerable.Range(0, BallArt.Skins.Length).Where(s => !BallArt.Unlocked(s)).ToList();
        PlayerPrefs.SetInt($"MiniPool.done.{levelIndex}", 1);
        bool record = best == 0 || shots < best;
        if (record) PlayerPrefs.SetInt($"MiniPool.best.{levelIndex}", shots);
        PlayerPrefs.Save();

        bool crown = Crown(levelIndex) && !hadCrown;
        Sfx.Play(crown ? Sfx.Kind.Crown : Sfx.Kind.Win, 1);
        var notes = new List<string>();
        if (crown) notes.Add("Corona conquistata!");
        else if (first) notes.Add("Primo completamento!");
        else if (record) notes.Add("Nuovo record!");
        notes.AddRange(locked.Where(BallArt.Unlocked).Select(s => $"Nuova skin sbloccata: {BallArt.Skins[s].name}"));
        ui.ShowResults(levelIndex, NextLevel(), shots, par, attempts, Time.time - levelStart, string.Join("\n", notes));
    }

    int NextLevel() => customText != null ? 0 : levels.FirstOrDefault(i => i > levelIndex);

    // onboarding: only in the levels that introduce a control ([KEY] = key cap, GameUI.SetHint)
    string Hint() => customText != null ? null : levelIndex switch
    {
        1 => cueCtl.Pulling ? "Rilascia [DESTRO] per tirare" : "Tieni [DESTRO] e tira il mouse indietro per caricare il tiro",
        2 => cueCtl.Pulling ? "Tieni anche [SINISTRO] e muovi il mouse: effetto sulla bianca"
            : "[DESTRO] + [SINISTRO] insieme: il mouse sposta l'effetto sulla bianca",
        3 => "[SINISTRO] + mouse  gira e mira\n[CTRL] + [SINISTRO]  guarda senza muovere la stecca\n[ROTELLA] zoom   [T] dall'alto   [F] volo libero",
        7 => "[W] / [S] alzano e abbassano la stecca: dall'alto la bianca gira",
        _ => null,
    };
}
