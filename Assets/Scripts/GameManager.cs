using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

// Turn flow + HUD. Creates itself when Play starts, so no scene setup is needed.
public class GameManager : MonoBehaviour
{
    enum State { Menu, Aiming, Rolling, GameOver, Multiplayer }

    public int levelIndex = 1;

    LevelBuilder.Level level;
    CueController cueCtl;
    GameUI ui;
    MultiplayerGame mp;
    List<int> levels; // indices of the levelNN files in Resources/Levels, sorted
    Ball cue;
    State state;
    string message;
    float shotTime;

    // stats of the current level, across restarts; shots only of the current run (a restart clears them)
    int attempts, shots;
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
        set => PlayerPrefs.SetInt("MiniPool.table", value);
    }

    // hard mode: no cue ball aim line in the levels
    public static bool HardMode
    {
        get => PlayerPrefs.GetInt("MiniPool.hard", 0) == 1;
        set => PlayerPrefs.SetInt("MiniPool.hard", value ? 1 : 0);
    }

    // settings screen: master volume 0..1 and camera mouse sensitivity (multiplier)
    public static float Volume
    {
        get => PlayerPrefs.GetFloat("MiniPool.volume", 1);
        set { PlayerPrefs.SetFloat("MiniPool.volume", value); AudioListener.volume = value; }
    }

    public static float Sensitivity
    {
        get => PlayerPrefs.GetFloat("MiniPool.sensitivity", 1);
        set => PlayerPrefs.SetFloat("MiniPool.sensitivity", value);
    }

    bool paused;

    void Start()
    {
        AudioListener.volume = Volume;
        int test = 0;
#if UNITY_EDITOR
        test = UnityEditor.SessionState.GetInt(TestLevelKey, 0);
        UnityEditor.SessionState.EraseInt(TestLevelKey);
#endif
        cueCtl = gameObject.AddComponent<CueController>();
        cueCtl.onShot = OnShot;
        ui = gameObject.AddComponent<GameUI>();
        ui.onPlay = StartLevel;
        ui.onMenu = ShowMenu;
        ui.onSelect = Preview;
        mp = gameObject.AddComponent<MultiplayerGame>();
        mp.ui = ui;
        mp.game = this;
        ui.onHost = () => mp.Host(Resources.Load<TextAsset>($"Levels/level{levels.FirstOrDefault():00}")?.text);
        ui.onHostPool = () => mp.Host(Resources.Load<TextAsset>("PoolTable")?.text, pool: true);
        ui.onJoin = mp.Join;
        levels = Resources.LoadAll<TextAsset>("Levels")
            .Select(t => t.name.StartsWith("level") && int.TryParse(t.name.Substring(5), out int i) ? i : 0)
            .Where(i => i > 0).OrderBy(i => i).ToList();
        ui.levels = levels;
        if (test > 0) StartLevel(test);
        else { SetState(State.Menu); ui.ShowHome(); }
    }

    void ShowMenu()
    {
        message = null;
        SetState(State.Menu);
        ui.ShowLevels(levelIndex); // selecting a ball calls Preview
    }

    // MultiplayerGame takes the table and the camera for the match
    public void EnterMultiplayer()
    {
        if (level != null) Destroy(level.root.gameObject);
        level = null;
        message = null;
        SetState(State.Multiplayer);
    }

    public void ExitMultiplayer(string error = null)
    {
        SetState(State.Menu);
        ui.ForgetPreview(); // the preview table was replaced by the match
        if (error != null) { ui.ShowHome(); ui.ShowMultiplayer(error); }
        else ui.ShowHome();
    }

    // menu: the selected level is built for show (no simulation) and the camera circles it
    void Preview(int index)
    {
        var text = Resources.Load<TextAsset>($"Levels/level{index:00}");
        if (!text) return;
        if (level != null) Destroy(level.root.gameObject);
        ApplyTheme(index);
        level = LevelBuilder.Build(text.text, text.name);
    }

    float previewYaw = 60;

    void PreviewCamera()
    {
        if (level == null) return;
        float dt = Time.deltaTime;
        previewYaw += 5 * dt;
        var b = level.bounds;
        float dist = Mathf.Max(b.size.x, b.size.z) * 1.1f + 6;
        var rot = Quaternion.Euler(35, previewYaw, 0);
        // aim a bit below the center so the table sits above the ball bar
        var pos = b.center + Vector3.down * dist * 0.12f - rot * Vector3.forward * dist;
        var cam = Camera.main.transform;
        float k = 1 - Mathf.Exp(-2.5f * dt);
        cam.SetPositionAndRotation(Vector3.Lerp(cam.position, pos, k), Quaternion.Slerp(cam.rotation, rot, k));
    }

    public static void ApplyTheme(int index)
    {
        var t = Worlds.ThemeOf(Worlds.WorldOf(index));
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
        attempts = 0;
        levelStart = Time.time;
        Load(index);
    }

    void Load(int index)
    {
        var text = Resources.Load<TextAsset>($"Levels/level{index:00}");
        if (!text)
        {
            ShowMenu();
            return;
        }
        if (level != null) Destroy(level.root.gameObject);
        levelIndex = index;
        ApplyTheme(index);
        level = LevelBuilder.Build(text.text, text.name);
        cue = level.balls.FirstOrDefault(b => b.number == 0);
        if (!cue)
        {
            message = $"{text.name}: manca la palla bianca (0)";
            SetState(State.GameOver);
            return;
        }
        cue.onCueHit = b => { if (firstHit < 0) firstHit = b.number; };
        cueCtl.cue = cue;
        cueCtl.level = level;
        cueCtl.guide = !HardMode;
        cueCtl.guideHit = false; // only the cue ball's line
        attempts++;
        shots = 0;
        message = null;
        SetupCamera();
        SetState(State.Aiming);
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
        ui.ShowPause(() => Pause(false), () => { Pause(false); Load(levelIndex); }, () => { Pause(false); ShowMenu(); });
    }

    void OnShot()
    {
        firstHit = -1;
        shots++;
        potted.Clear();
        lost.Clear();
        shotTime = Time.time;
        SetState(State.Rolling);
    }

    void Update()
    {
        ui.SetHint(state == State.Aiming && !paused && !(cueCtl.orbit && cueCtl.orbit.Free) ? Hint() : null);
        if (state == State.Multiplayer) return; // MultiplayerGame drives
        if (state == State.Menu) { PreviewCamera(); return; }
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

        // slow motion while the black rolls into a pocket (single player only: online the clock is the host's)
        var black = level.balls.FirstOrDefault(b => b.number == 8);
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
        var o = Rules.Evaluate(firstHit, potted, lost);
        if (o.restart)
        {
            Load(levelIndex);
            message = o.message + "\nSi ricomincia il livello";
            return;
        }
        message = null;

        // no restart: object balls that left the table (potted or lost) are simply gone
        foreach (var b in level.balls.Where(b => !b.gameObject.activeSelf).ToList())
        {
            level.balls.Remove(b);
            Destroy(b.gameObject);
        }

        if (o.win)
        {
            Win();
            return;
        }
        SetState(State.Aiming);
    }

    void Win()
    {
        SetState(State.GameOver);
        bool first = !Completed(levelIndex), hadCrown = Crown(levelIndex);
        int best = Best(levelIndex);
        var locked = Enumerable.Range(0, BallArt.Skins.Length).Where(s => !BallArt.Unlocked(s)).ToList();
        PlayerPrefs.SetInt($"MiniPool.done.{levelIndex}", 1);
        bool record = best == 0 || shots < best;
        if (record) PlayerPrefs.SetInt($"MiniPool.best.{levelIndex}", shots);
        PlayerPrefs.Save();

        var notes = new List<string>();
        if (Crown(levelIndex) && !hadCrown) notes.Add("Corona conquistata!");
        else if (first) notes.Add("Primo completamento!");
        else if (record) notes.Add("Nuovo record!");
        notes.AddRange(locked.Where(BallArt.Unlocked).Select(s => $"Nuova skin sbloccata: {BallArt.Skins[s].name}"));
        ui.ShowResults(levelIndex, NextLevel(), shots, Par(levelIndex), attempts, Time.time - levelStart, string.Join("\n", notes));
    }

    int NextLevel() => levels.FirstOrDefault(i => i > levelIndex);

    // onboarding: only in the levels that introduce a control ([KEY] = key cap, GameUI.SetHint)
    string Hint() => levelIndex switch
    {
        1 => cueCtl.Pulling ? "Rilascia [DESTRO] per tirare" : "Tieni [DESTRO] e tira il mouse indietro per caricare il tiro",
        2 => cueCtl.Pulling ? "Tieni anche [SINISTRO] e muovi il mouse: effetto sulla bianca"
            : "[DESTRO] + [SINISTRO] insieme: il mouse sposta l'effetto sulla bianca",
        3 => "[SINISTRO] + mouse  gira e mira\n[CTRL] + [SINISTRO]  guarda senza muovere la stecca\n[ROTELLA] zoom   [T] dall'alto   [F] volo libero",
        7 => "[W] / [S] alzano e abbassano la stecca: dall'alto la bianca gira",
        _ => null,
    };

    // shortcut: IMGUI HUD for the prototype; replace with a uGUI/UI Toolkit canvas when the art pass starts.
    void OnGUI()
    {
        if (state is State.Menu or State.Multiplayer || paused) return;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 22, normal = { textColor = Color.white } };
        int par = Par(levelIndex); // shortcut: reads the level file every frame, fine while the HUD is IMGUI
        GUI.Label(new Rect(16, 12, 1600, 32), $"Livello {levelIndex}   Tiri {shots}{(par > 0 ? $" (corona: {par})" : "")}   Obiettivo: manda in buca la nera (non perdere la bianca o la nera, colpisci sempre una palla)", style);
        if (!string.IsNullOrEmpty(message)) GUI.Label(new Rect(16, 44, 1200, 120), message, style);
        style.fontSize = 16;
        GUI.Label(new Rect(16, Screen.height - 52, 1400, 48),
            "Sinistro + mouse = gira intorno / mira (Shift = fine, frecce = ritocco, Ctrl = solo camera)   Rotella = zoom   T = vista dall'alto   F = volo libero (WASD, Q/E, Shift)\n" +
            "Destro tenuto + mouse indietro = potenza (+ sinistro = sposta l'effetto), rilascia = tiro, Esc = annulla / pausa   R = restart   M = menu", style);
    }
}
