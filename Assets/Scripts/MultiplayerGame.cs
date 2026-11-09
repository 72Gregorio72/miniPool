using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Online match on one shared table: each player has their own cue and their own 8 (marked with their color).
// The 8s ignore each other and an 8 is hit only by its own cue (Rules.Collide); cues hit each other.
// Pot your 8 to finish the round: the sooner (in time), the more points. A foul (own cue or own 8 lost, cue potted)
// puts that player's cue and 8 back at the start; a shot that hits nothing is fine here (moving into position).
// First to TargetScore wins. The table has one pocket only: the round's winner moves it before the next round.
// Between rounds every player also picks a premade field piece (Resources/Pieces) and attaches it to the table.
// The host runs the physics and streams ball positions; clients only send their shots.
public partial class MultiplayerGame : MonoBehaviour
{
    public const int MaxPlayers = 8, TargetScore = 10;
    const byte Hello = 1, Lobby = 2, Round = 3, State = 4, Shoot = 5, Toast = 6, RoundEnd = 7, Build = 8, Pick = 9, Place = 10, Done = 11, PocketMsg = 12,
        PoolEnd = 13, PlaceCue = 14;

    // premade field pieces, same order on every machine (same build)
    static TextAsset[] pieces;
    static TextAsset[] Pieces => pieces ??= Resources.LoadAll<TextAsset>("Pieces").OrderBy(t => t.name).ToArray();
    const float NextRoundDelay = 6, SendRate = 1 / 20f;

    public GameUI ui;
    public GameManager game;
    public bool Active => net != null;

    class Player
    {
        public int id, skin, score, roundPoints;
        public bool finished, gone, shooting;
        public float finishTime, shotTime;
        public float pottedAt = -1; // round time when their 8 dropped into the pocket (-1 = not yet): their finish time
        public Ball cue, black;
        public Vector3 cueStart, blackStart;
        public int firstHit = -1;
        public int group; // 8-ball: Rules.GroupOf group (0 = table still open)
        public int piece = -1; // build phase: picked field piece (-1 = not yet), attached (or skipped)
        public bool placed;
        public readonly List<int> potted = new(), lost = new();
    }

    Net net;
    int me;
    bool started, playing, over, building;
    readonly SortedDictionary<int, Player> players = new();
    LevelBuilder.Level level;
    string levelText;
    int round, roundPlayers;
    int pocketBy = -1; // build phase: the round's winner, who moves the pocket once all pieces are on (-1 = done)
    string shownText;  // level text currently built (the build phase previews the hovered piece / pocket)
    int hoverRot, hoverRotShown, hoverX = int.MinValue, hoverZ;
    Vector2Int hoverShift; // where the preview table was moved back to (see BuildInput)
    string hoverText;  // level text with the hovered placement, null = not allowed
    float roundStart, sendTimer, nextRoundAt = -1;
    CueController cueCtl;
    OrbitCamera orbit;
    bool myReady;
    Vector3[] targetPos;
    Quaternion[] targetRot;
    GameObject marker;

    // ---------- lobby ----------

    public async void Host(string levelSource, bool pool = false)
    {
        if (string.IsNullOrEmpty(levelSource)) { ui.ShowMultiplayer("Nessun livello in Resources/Levels"); return; }
        ui.ShowMultiplayer("Creo la partita…");
        levelText = levelSource;
        this.pool = pool;
        net = NewNet();
        try { await net.Host(MaxPlayers); }
        catch (Exception e) { Fail("Impossibile creare la partita: " + e.Message); return; }
        GUIUtility.systemCopyBuffer = net.Code; // paste it with Ctrl+V in the other window
        me = 0;
        players[0] = new Player { id = 0, skin = BallArt.SelectedSkin };
        tableSkin = GameManager.TableSkin;
        BroadcastLobby();
    }

    public async void Join(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) { ui.ShowMultiplayer("Scrivi il codice della partita"); return; }
        ui.ShowMultiplayer("Mi collego…");
        net = NewNet();
        try { await net.Join(code); }
        catch (Exception e) { Debug.LogWarning(e); Fail("Codice non valido o partita non trovata"); }
        // the Connect event sends Hello; the host answers with the lobby
    }

    // unfocused, Unity stops calling Update: no keep-alive, and Relay drops the connection ("not connected") after ~10 s
    Net NewNet()
    {
        Application.runInBackground = true;
        return new Net { onMessage = OnMessage, onConnected = OnConnected, onDisconnected = OnDisconnected };
    }

    void OnConnected(int id)
    {
        if (!net.IsHost) net.Send(0, Net.Msg(Hello, w => w.Write(BallArt.SelectedSkin)));
        else if (started || players.Count >= (pool ? 2 : MaxPlayers)) net.Kick(id); // no joining a match in progress
    }

    void OnDisconnected(int id)
    {
        if (!net.IsHost) { Fail("La partita è stata chiusa dall'host"); return; }
        if (!players.TryGetValue(id, out var p)) return;
        if (!started) { players.Remove(id); BroadcastLobby(); return; }
        p.gone = true;
        if (pool) { if (playing) EndPool(players.Values.First(q => !q.gone).id, $"{Tag(id)} è uscito"); return; }
        foreach (var b in new[] { p.cue, p.black }) if (b) b.gameObject.SetActive(false);
        ToastAll($"{Tag(id)} è uscito");
    }

    void BroadcastLobby()
    {
        foreach (var id in players.Keys.Where(id => id != 0))
            net.Send(id, Net.Msg(Lobby, w => { w.Write(id); w.Write(pool); w.Write(tableSkin); WritePlayers(w); }));
        ShowLobby();
    }

    void ShowLobby() => ui.ShowLobby(net.Code, players.Values.Select(p => (p.skin, PlayerColor(p.id), Name(p.id))).ToList(), net.IsHost,
        !pool || players.Count == 2 ? (Action)StartRound : null, Leave, pool ? "biliardo (8 palle, 2 giocatori)" : "minigolf");

    public void Leave()
    {
        Close();
        game.ExitMultiplayer();
    }

    void Fail(string message)
    {
        Close();
        game.ExitMultiplayer(message);
    }

    void Close()
    {
        net?.Dispose();
        net = null;
        if (level != null) Destroy(level.root.gameObject);
        level = null;
        if (cueCtl) cueCtl.enabled = false;
        players.Clear();
        started = playing = over = building = false;
        if (marker) marker.SetActive(false);
        ShowGhost(null);
        round = 0;
        pocketBy = -1;
        nextRoundAt = -1;
        pool = false;
        if (handGhost) handGhost.SetActive(false);
    }

    void OnDestroy() => net?.Dispose();

    // ---------- messages ----------

    void OnMessage(int from, byte[] data)
    {
        using var r = new BinaryReader(new MemoryStream(data));
        byte type = r.ReadByte();
        if (net.IsHost)
        {
            if (type == Hello && !started)
            {
                players[from] = new Player { id = from, skin = Mathf.Clamp(r.ReadInt32(), 0, BallArt.Skins.Length - 1) };
                BroadcastLobby();
            }
            else if (type == Shoot && playing)
                ApplyShot(from, new Vector3(r.ReadSingle(), 0, r.ReadSingle()).normalized, Mathf.Clamp(r.ReadSingle(), 0, 40),
                    Vector2.ClampMagnitude(new Vector2(r.ReadSingle(), r.ReadSingle()), 0.5f), r.ReadSingle());
            else if (type is Pick or Place or Done or PocketMsg) HostBuild(from, type, r);
            else if (type == PlaceCue && pool && playing) HostPlaceCue(from, new Vector3(r.ReadSingle(), 0, r.ReadSingle()));
            return;
        }
        switch (type)
        {
            case Lobby:
                me = r.ReadInt32();
                pool = r.ReadBoolean();
                tableSkin = r.ReadInt32();
                ReadPlayers(r);
                ShowLobby();
                break;
            case Round: ClientRound(r); break;
            case State: if (playing) ReadState(r); break;
            case Toast: ui.Toast(r.ReadString()); break;
            case Build: ReadBuild(r); break;
            case PoolEnd: ShowPoolEnd(r.ReadInt32(), r.ReadString()); break;
            case RoundEnd:
                over = r.ReadBoolean();
                ReadPlayers(r);
                ShowRoundEnd();
                break;
        }
    }

    void WritePlayers(BinaryWriter w)
    {
        w.Write(players.Count);
        foreach (var p in players.Values)
        {
            w.Write(p.id); w.Write(p.skin); w.Write(p.score); w.Write(p.roundPoints);
            w.Write(p.finished); w.Write(p.gone); w.Write(p.finishTime);
        }
    }

    void ReadPlayers(BinaryReader r)
    {
        int n = r.ReadInt32();
        var seen = new HashSet<int>();
        for (int i = 0; i < n; i++)
        {
            int id = r.ReadInt32();
            if (!players.TryGetValue(id, out var p)) players[id] = p = new Player { id = id };
            p.skin = r.ReadInt32(); p.score = r.ReadInt32(); p.roundPoints = r.ReadInt32();
            p.finished = r.ReadBoolean(); p.gone = r.ReadBoolean(); p.finishTime = r.ReadSingle();
            seen.Add(id);
        }
        foreach (var id in players.Keys.Where(id => !seen.Contains(id)).ToList()) players.Remove(id);
    }

    // ---------- round setup ----------

    void StartRound()
    {
        if (!net.IsHost) return;
        if (pool) { StartPool(); return; }
        started = true;
        round++;
        levelText = LevelBuilder.OnePocket(levelText); // round 1: keep one; later: put it back if the build phase left none
        roundPlayers = players.Values.Count(p => !p.gone);
        BuildLevel();
        var cuePos = level.balls.FirstOrDefault(b => b.number == 0)?.transform.position ?? level.bounds.center + Vector3.up * Ball.Radius;
        var eightPos = level.balls.FirstOrDefault(b => b.number == 8)?.transform.position ?? cuePos;
        foreach (var b in level.balls.Where(b => b.number is 0 or 8).ToList())
        {
            level.balls.Remove(b);
            Destroy(b.gameObject);
        }
        int i = 0;
        foreach (var p in players.Values.Where(p => !p.gone))
        {
            p.finished = p.shooting = false;
            p.roundPoints = 0;
            p.finishTime = 0;
            p.pottedAt = -1;
            p.potted.Clear();
            p.lost.Clear();
            p.cueStart = StartSpot(cuePos, eightPos, i++, roundPlayers);
            p.blackStart = eightPos;
            Spawn(0, p.id, p.cueStart);
            Spawn(8, p.id, p.blackStart);
        }
        var balls = level.balls;
        net.Send(-1, Net.Msg(Round, w =>
        {
            w.Write(round);
            w.Write(levelText);
            WritePlayers(w);
            w.Write(balls.Count);
            foreach (var b in balls)
            {
                var q = b.transform.position;
                w.Write((byte)b.number); w.Write((sbyte)b.owner); w.Write(q.x); w.Write(q.y); w.Write(q.z);
            }
        }));
        BeginRound();
    }

    void ClientRound(BinaryReader r)
    {
        round = r.ReadInt32();
        levelText = r.ReadString();
        ReadPlayers(r);
        BuildLevel();
        foreach (var b in level.balls) Destroy(b.gameObject);
        level.balls.Clear();
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            int number = r.ReadByte(), owner = r.ReadSByte();
            Spawn(number, owner, new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
        }
        targetPos = level.balls.Select(b => b.transform.position).ToArray();
        targetRot = level.balls.Select(b => b.transform.rotation).ToArray();
        if (pool) PoolBalls();
        BeginRound();
    }

    void BuildLevel(string text = null)
    {
        if (level != null) Destroy(level.root.gameObject);
        game.EnterMultiplayer();
        GameManager.ApplyTheme(Worlds.Level(tableSkin, 1)); // the host's table skin = that world's colors
        shownText = text ?? levelText;
        level = LevelBuilder.Build(shownText, "Multiplayer");
        LevelBuilder.Room(level);
    }

    // start spot of player slot i of n: on the circle around the 8 through the level's cue spot, mirrored left/right of the
    // line 8 -> cue spot (Rules.MirrorSlot), so everyone has the same shot at their own 8. Off the felt: nearest free spot.
    Vector3 StartSpot(Vector3 cue, Vector3 eight, int i, int n)
    {
        var d = cue - eight;
        d.y = 0;
        float r = d.magnitude;
        if (r > Ball.Radius * 1.2f)
        {
            float step = 2 * Mathf.Asin(Mathf.Min(1, Ball.Radius * 1.2f / r)) * Mathf.Rad2Deg; // neighbors a bit more than a ball apart
            var q = eight + Quaternion.Euler(0, Rules.MirrorSlot(i, n) * step, 0) * d;
            if (level.Supports(q) && level.TileAt(q) == '.') return new Vector3(q.x, level.GroundAt(q).Value + Ball.Radius, q.z);
        }
        return Spread(cue, i); // shortcut: this one isn't mirrored, only when the circle leaves the felt
    }

    // nearest free table spots around the level's cue position
    // shortcut: ignores modules (a cue may start on a booster/spike next to a crowded start), fine for a handful of players
    Vector3 Spread(Vector3 p, int i)
    {
        float s = Ball.Radius * 2.3f;
        var spots = new List<Vector3>();
        for (int x = -3; x <= 3; x++)
            for (int z = -3; z <= 3; z++)
            {
                var q = p + new Vector3(x * s, 0, z * s);
                if (level.Supports(q) && level.TileAt(q) == '.') spots.Add(new Vector3(q.x, level.GroundAt(q).Value + Ball.Radius, q.z));
            }
        spots.Sort((a, b) => (a - p).sqrMagnitude.CompareTo((b - p).sqrMagnitude));
        return i < spots.Count ? spots[i] : p;
    }

    void Spawn(int number, int owner, Vector3 pos)
    {
        var b = LevelBuilder.SpawnBall(number, pos, level.root);
        b.owner = owner;
        level.balls.Add(b);
        if (owner < 0 || !players.TryGetValue(owner, out var p)) return;
        if (number == 0) p.cue = b; else p.black = b;
        // triangles: always on the 8s (they share a spot), on a cue only when someone else picked the same skin
        bool sameSkin = players.Values.Count(q => q.skin == p.skin && !q.gone) > 1;
        var mat = BallArt.PlayerMaterial(number, p.skin, number == 8 || sameSkin ? owner % BallArt.PlayerColors.Length : -1);
        bool ghost = number == 8 && owner != me; // the other players' 8s: see-through, you can't hit them anyway
        if (ghost) mat = BallArt.Ghost(mat);
        foreach (var rend in b.GetComponentsInChildren<Renderer>())
        {
            rend.sharedMaterial = mat;
            if (ghost) rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        if (number == 0 && net.IsHost) b.onCueHit = hit => { if (p.shooting && p.firstHit < 0) p.firstHit = hit.number; };
    }

    void BeginRound()
    {
        playing = true;
        building = false;
        if (marker) marker.SetActive(false);
        ShowGhost(null);
        nextRoundAt = -1;
        roundStart = Time.time;
        myReady = false;
        var cam = Camera.main;
        if (!cam.TryGetComponent(out orbit)) orbit = cam.gameObject.AddComponent<OrbitCamera>();
        orbit.enabled = true;
        orbit.table = level.bounds;
        var mine = players.TryGetValue(me, out var p) ? p.cue : null;
        orbit.follow = mine ? mine.transform.position : level.bounds.center;
        orbit.Snap();
        orbit.yaw = 90;
        if (!cueCtl)
        {
            cueCtl = new GameObject("MultiplayerCue").AddComponent<CueController>();
            cueCtl.transform.SetParent(transform);
            cueCtl.shoot = RequestShot;
            cueCtl.guide = true;
        }
        cueCtl.guideHit = !pool;
        cueCtl.cue = mine;
        cueCtl.level = level;
        cueCtl.orbit = orbit;
        cueCtl.enabled = false;
        ShowHud();
    }

    void ShowHud() { if (pool) { PoolHud(); return; } ui.ShowMatchHud($"Round {round}", players.Values.Where(p => !p.gone)
        .Select(p => (PlayerColor(p.id), Name(p.id), p.score, p.finished)).ToList(), $"a {TargetScore}",
        "Sinistro + mouse = mira   ·   Destro tenuto + mouse indietro = tiro   ·   imbuca la TUA nera   ·   M = esci"); }

    // ---------- play ----------

    void RequestShot(Vector3 dir, float speed, Vector2 tip, float elevation)
    {
        myReady = false;
        cueCtl.enabled = false;
        if (net.IsHost) ApplyShot(me, dir, speed, tip, elevation);
        else net.Send(0, Net.Msg(Shoot, w => { w.Write(dir.x); w.Write(dir.z); w.Write(speed); w.Write(tip.x); w.Write(tip.y); w.Write(elevation); }));
    }

    void ApplyShot(int id, Vector3 dir, float speed, Vector2 tip, float elevation)
    {
        if (pool) { PoolShoot(id, dir, speed, tip, elevation); return; }
        if (!players.TryGetValue(id, out var p) || !Ready(p)) return;
        PoolPhysics.Shoot(p.cue, dir, speed, tip.x, tip.y, elevation); // Shoot clamps the elevation
        p.shooting = true;
        p.firstHit = -1;
        p.shotTime = Time.time;
    }

    static bool Still(Ball b) => !b || !b.gameObject.activeSelf || (!b.IsMoving && !b.falling && !b.airborne);

    static bool Ready(Player p) => !p.finished && !p.gone && !p.shooting && p.potted.Count + p.lost.Count == 0
                                   && p.cue && p.cue.gameObject.activeSelf && Still(p.cue) && Still(p.black);

    void Update()
    {
        if (net == null) return;
        net.Update();
        if (net == null) return; // a message closed the match
        var kb = Keyboard.current;
        if ((playing || building) && kb != null && kb.mKey.wasPressedThisFrame) { Leave(); return; }

        if (net.IsHost && nextRoundAt > 0 && Time.time >= nextRoundAt) StartBuild();
        if (building)
        {
            if (net.IsHost && players.Values.All(q => q.gone || q.placed) && (pocketBy < 0 || players[pocketBy].gone)) StartRound();
            else BuildInput();
            return;
        }
        if (!playing || level == null) return;

        if (net.IsHost) HostStep();
        else ClientStep();
        if (!playing) return; // the round just ended

        cueCtl.enabled = myReady;
        if (pool) HandInput();
        var mine = players.TryGetValue(me, out var p) ? p.cue : null;
        if (mine && mine.gameObject.activeSelf)
        {
            var q = mine.transform.position;
            orbit.follow = new Vector3(q.x, Mathf.Max(q.y, Ball.Radius), q.z);
        }
    }

    void HostStep()
    {
        if (pool) { PoolHostStep(); return; }
        PoolPhysics.Simulate(level, Mathf.Min(Time.deltaTime, 0.05f));
        foreach (var b in level.balls)
        {
            // the clock stops when the 8 drops into the pocket, not when the balls settle
            if (b.number == 8 && b.pocket != null && players.TryGetValue(b.owner, out var o8) && o8.pottedAt < 0) o8.pottedAt = Time.time - roundStart;
            if (!b.gameObject.activeSelf || b.transform.position.y > -0.6f) continue;
            b.gameObject.SetActive(false);
            if (b.owner >= 0 && players.TryGetValue(b.owner, out var owner))
                (b.pocket != null ? owner.potted : owner.lost).Add(b.number);
        }

        foreach (var p in players.Values.Where(p => !p.finished && !p.gone))
            if ((p.shooting || p.potted.Count + p.lost.Count > 0) && Still(p.cue) && Still(p.black) && Time.time - p.shotTime > 0.2f)
                Evaluate(p);

        myReady = players.TryGetValue(me, out var self) && Ready(self);
        if (Rules.RoundOver(players.Values.Count(p => !p.finished && !p.gone), players.Values.Count(p => p.finished))) { EndRound(); return; }
        SendState();
    }

    // host -> clients, 20 times a second: who can shoot, who finished, the moving parts' clock, the turn (8-ball), every ball
    void SendState()
    {
        sendTimer -= Time.deltaTime;
        if (sendTimer > 0) return;
        sendTimer = SendRate;
        net.Send(-1, Net.Msg(State, w =>
        {
            int ready = 0, done = 0;
            foreach (var p in players.Values)
            {
                if (pool ? PoolReady(p.id) : Ready(p)) ready |= 1 << p.id;
                if (p.finished) done |= 1 << p.id;
            }
            w.Write(ready);
            w.Write(done);
            w.Write(level.time); // clients move gates, timed walls and platforms with the host's clock
            w.Write(turn);
            w.Write(ballInHand);
            w.Write(players.Values.Sum(p => p.group << (2 * p.id)));
            w.Write(level.balls.Count);
            foreach (var b in level.balls)
            {
                var q = b.transform.position;
                var o = b.transform.rotation;
                w.Write(b.gameObject.activeSelf);
                w.Write(q.x); w.Write(q.y); w.Write(q.z);
                w.Write(o.x); w.Write(o.y); w.Write(o.z); w.Write(o.w);
            }
        }), false);
    }

    void Evaluate(Player p)
    {
        // a ball knocked in or out while its owner wasn't shooting: no "nothing hit" foul for them
        var o = Rules.Evaluate(p.shooting ? p.firstHit : 8, p.potted, p.lost, noHitOk: true);
        p.shooting = false;
        p.potted.Clear();
        p.lost.Clear();
        if (o.restart)
        {
            p.pottedAt = -1;
            Respawn(p.cue, p.cueStart);
            Respawn(p.black, p.blackStart);
            SendToast(p.id, o.message + ": si riparte dall'inizio");
        }
        else if (o.win)
        {
            p.finished = true;
            p.finishTime = p.pottedAt >= 0 ? p.pottedAt : Time.time - roundStart;
            foreach (var b in new[] { p.cue, p.black }) b.gameObject.SetActive(false);
            ShowHud(); // before the toast: ShowHud clears the screen
            ToastAll($"{Tag(p.id)} ha finito in {p.finishTime:0.00} s");
        }
    }

    static void Respawn(Ball b, Vector3 pos)
    {
        b.gameObject.SetActive(true);
        b.Stop();
        b.transform.position = pos;
    }

    void ReadState(BinaryReader r)
    {
        int ready = r.ReadInt32(), done = r.ReadInt32();
        level.time = r.ReadSingle();
        int t = r.ReadInt32();
        bool hand = r.ReadBoolean();
        int groups = r.ReadInt32();
        myReady = (ready >> me & 1) == 1;
        bool changed = t != turn || hand != ballInHand;
        turn = t;
        ballInHand = hand;
        foreach (var p in players.Values)
        {
            int g = groups >> (2 * p.id) & 3;
            changed |= g != p.group;
            p.group = g;
        }
        foreach (var p in players.Values)
        {
            bool f = (done >> p.id & 1) == 1;
            changed |= f != p.finished;
            p.finished = f;
        }
        if (changed) ShowHud();
        int n = r.ReadInt32();
        bool potted = false;
        for (int i = 0; i < n; i++)
        {
            bool active = r.ReadBoolean();
            var pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            var rot = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            if (i >= level.balls.Count) continue;
            var b = level.balls[i];
            if (active && (!b.gameObject.activeSelf || (b.transform.position - pos).sqrMagnitude > 4)) b.transform.position = pos; // respawn: jump
            potted |= pool && active != b.gameObject.activeSelf; // 8-ball: the HUD counts the potted balls
            b.gameObject.SetActive(active);
            targetPos[i] = pos;
            targetRot[i] = rot;
        }
        if (potted) ShowHud();
    }

    void ClientStep()
    {
        PoolPhysics.UpdateMoving(level);
        PoolPhysics.ShowMoving(level, Time.deltaTime);
        float k = 1 - Mathf.Exp(-25 * Time.deltaTime); // smooth between 20 Hz snapshots
        for (int i = 0; i < level.balls.Count; i++)
        {
            var t = level.balls[i].transform;
            t.SetPositionAndRotation(Vector3.Lerp(t.position, targetPos[i], k), Quaternion.Slerp(t.rotation, targetRot[i], k));
        }
    }

    void EndRound()
    {
        playing = false;
        // points by finish time (when the 8 dropped): 1st gets one point per player, last gets 1
        int rank = 0;
        foreach (var p in players.Values.Where(p => p.finished).OrderBy(p => p.finishTime))
        {
            p.roundPoints = Mathf.Max(roundPlayers - rank++, 1);
            p.score += p.roundPoints;
        }
        // the one left on the table when everyone else finished: last place, no pot needed
        foreach (var p in players.Values.Where(p => !p.finished && !p.gone && rank > 0))
        {
            p.roundPoints = 1;
            p.score += 1;
            p.finishTime = -1; // standings show "ultimo"
            foreach (var b in new[] { p.cue, p.black }) if (b) b.gameObject.SetActive(false);
        }
        over = players.Values.Any(p => p.score >= TargetScore);
        net.Send(-1, Net.Msg(RoundEnd, w => { w.Write(over); WritePlayers(w); }));
        ShowRoundEnd();
        if (!over) nextRoundAt = Time.time + NextRoundDelay;
    }

    void ShowRoundEnd()
    {
        playing = false;
        if (cueCtl) cueCtl.enabled = false;
        if (orbit) orbit.enabled = false;
        Cursor.lockState = CursorLockMode.None;
        var rows = players.Values.Where(p => !p.gone).OrderByDescending(p => p.score)
            .Select(p => (PlayerColor(p.id), Name(p.id), p.roundPoints, p.score, p.finishTime)).ToList();
        var leader = players.Values.OrderByDescending(p => p.score).First();
        ui.ShowStandings(over ? $"{Name(leader.id)} vince!" : $"Fine round {round}",
            over ? $"Primo a {TargetScore} punti" : "Tra poco si scelgono i pezzi di campo e il vincitore sposta la buca…", rows, over ? Leave : null);
    }

    // ---------- build phase (between rounds) ----------

    void StartBuild()
    {
        nextRoundAt = -1;
        pocketBy = players.Values.Where(p => p.finished && !p.gone).OrderBy(p => p.finishTime).FirstOrDefault()?.id ?? -1;
        foreach (var p in players.Values) { p.piece = -1; p.placed = false; }
        if (pocketBy >= 0) levelText = LevelBuilder.RemovePocket(levelText);
        SendBuild();
    }

    // host -> everyone: the current table, who moves the pocket, who still has a piece to attach
    void SendBuild()
    {
        net.Send(-1, Net.Msg(Build, w =>
        {
            w.Write(levelText);
            w.Write(pocketBy);
            w.Write(players.Count);
            foreach (var p in players.Values) { w.Write(p.id); w.Write(p.piece); w.Write(p.placed); }
        }));
        ShowBuild();
    }

    void ReadBuild(BinaryReader r)
    {
        levelText = r.ReadString();
        pocketBy = r.ReadInt32();
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            int id = r.ReadInt32(), piece = r.ReadInt32();
            bool placed = r.ReadBoolean();
            if (players.TryGetValue(id, out var p)) { p.piece = piece; p.placed = placed; }
        }
        ShowBuild();
    }

    void ShowBuild()
    {
        building = true;
        playing = false;
        if (cueCtl) cueCtl.enabled = false;
        if (orbit) orbit.enabled = false;
        Cursor.lockState = CursorLockMode.None;
        BuildLevel();
        hoverX = int.MinValue; // recompute the hover preview on the new table
        // fixed view of the whole table from the near side, far side up like the level text
        var b = level.bounds;
        var rot = Quaternion.Euler(65, 0, 0);
        Camera.main.transform.SetPositionAndRotation(b.center - rot * Vector3.forward * (Mathf.Max(b.size.x, b.size.z * 1.8f) + 6), rot);

        var p = players[me];
        if (p.piece < 0)
            ui.ShowPiecePicker(Pieces.Select(t => (t.name, t.text)).ToList(), round, i => SendBuildMsg(Pick, w => w.Write(i)));
        else if (!p.placed)
            ui.ShowBuildHud($"{Pieces[p.piece].name}: attaccalo al tavolo", "Clic = attacca (le sponde vicine si aprono)   ·   R / Q = ruota   ·   M = esci",
                () => SendBuildMsg(Done, _ => { }));
        else if (pocketBy == me && PiecesDone)
            ui.ShowBuildHud("Sei arrivato primo: sposta la buca", "Clic su una sponda o sul panno = buca   ·   M = esci", null);
        else ui.ShowBuildHud(PiecesDone ? "Il primo arrivato sta spostando la buca…" : "Aspetto che gli altri attacchino i loro pezzi…", "M = esci", null);
    }

    // everyone still in the match has attached (or skipped) their piece: now the round's winner moves the pocket
    bool PiecesDone => players.Values.All(q => q.gone || q.placed);

    // what player p places next: their piece, then (winner only, once all pieces are on) the pocket
    bool PlacingPocket(Player p) => p.placed && pocketBy == p.id && PiecesDone;

    // a build action: the host applies it at once, a client sends it to the host
    void SendBuildMsg(byte type, Action<BinaryWriter> body)
    {
        var data = Net.Msg(type, body);
        if (!net.IsHost) { net.Send(0, data); return; }
        using var r = new BinaryReader(new MemoryStream(data));
        HostBuild(me, r.ReadByte(), r);
    }

    // the level text with what player p would put on cell next; null = not allowed (or nothing to place)
    string Placement(Player p, Vector2Int cell, int rot) =>
        p.piece >= 0 && !p.placed ? LevelBuilder.AttachPiece(levelText, Pieces[p.piece].text, cell, rot)
        : PlacingPocket(p) ? LevelBuilder.MovePocket(levelText, cell) : null;

    // shortcut: a piece attached on the left/bottom shifts the grid, so a click sent at the same moment by someone else
    // lands off; fine at this pace, send the text version along if it ever matters
    void HostBuild(int id, byte type, BinaryReader r)
    {
        if (!building || !players.TryGetValue(id, out var p)) return;
        if (type == Pick)
        {
            int i = r.ReadInt32();
            if (p.piece >= 0 || i < 0 || i >= Pieces.Length) return;
            p.piece = i;
        }
        else if (type is Place or PocketMsg)
        {
            if ((type == PocketMsg) != PlacingPocket(p)) return; // pieces first, the winner's pocket last
            var text = Placement(p, new Vector2Int(r.ReadInt32(), r.ReadInt32()), r.ReadInt32());
            if (text == null) return;
            levelText = text;
            if (type == PocketMsg) pocketBy = -1;
            else p.placed = true;
        }
        else if (p.piece >= 0) p.placed = true; // Done: skip the piece
        SendBuild();
    }

    // hovered cell: the table is previewed with the piece / pocket on it; red marker = can't go there; left click places
    void BuildInput()
    {
        if (!marker) marker = LevelBuilder.Visual("BuildMarker", PrimitiveType.Cube, Vector3.zero, new Vector3(0.96f, 0.06f, 0.96f), Quaternion.identity, Color.white, transform);
        var p = players[me];
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        float dist = 0;
        var ray = mouse != null ? Camera.main.ScreenPointToRay(mouse.position.ReadValue()) : default;
        bool placing = PlacingPocket(p) || (p.piece >= 0 && !p.placed);
        bool active = placing && mouse != null && new Plane(Vector3.up, 0).Raycast(ray, out dist);
        marker.SetActive(active);
        if (!active)
        {
            if (shownText != levelText) BuildLevel();
            ShowGhost(null);
            hoverX = int.MinValue;
            return;
        }
        if (kb != null && (kb.rKey.wasPressedThisFrame || kb.qKey.wasPressedThisFrame))
            hoverRot = (hoverRot + (kb.qKey.wasPressedThisFrame || kb.shiftKey.isPressed ? 270 : 90)) % 360;

        var cell = LevelBuilder.Level.CellOf(ray.GetPoint(dist));
        bool attaching = p.piece >= 0 && !p.placed;
        ref var shift = ref hoverShift;
        if (cell.x != hoverX || cell.y != hoverZ || hoverRot != hoverRotShown)
        {
            hoverX = cell.x; hoverZ = cell.y; hoverRotShown = hoverRot;
            shift = Vector2Int.zero;
            hoverText = Placement(p, cell, hoverRot);
            BuildLevel(hoverText ?? levelText);
            var pieceCells = attaching ? LevelBuilder.PieceCells(Pieces[p.piece].text, cell, hoverRot) : null;
            // a piece sticking out left / below grows the text grid to the left / below: move the preview back so the table stays under the mouse
            if (hoverText != null && pieceCells != null && pieceCells.Count > 0 && !LevelBuilder.IsOverlay(Pieces[p.piece].text))
                shift = new Vector2Int(Mathf.Min(0, pieceCells.Keys.Min(k => k.x)), Mathf.Min(0, pieceCells.Keys.Min(k => k.y)));
            level.root.position = new Vector3(shift.x, 0, shift.y);
            ShowGhost(hoverText == null ? pieceCells : null);
        }
        bool ok = hoverText != null;
        marker.transform.position = new Vector3(cell.x, level.BaseY(cell - shift) + 0.04f, cell.y);
        var tint = LevelBuilder.Mat(ok ? new Color(0.3f, 0.75f, 1f) : new Color(1f, 0.25f, 0.2f));
        foreach (var rend in marker.GetComponentsInChildren<Renderer>()) rend.sharedMaterial = tint;

        bool overUi = EventSystem.current && EventSystem.current.IsPointerOverGameObject();
        if (ok && !overUi && mouse.leftButton.wasPressedThisFrame)
            SendBuildMsg(PlacingPocket(p) ? PocketMsg : Place, w => { w.Write(cell.x); w.Write(cell.y); w.Write(hoverRot); });
    }

    // piece that can't go there: its cells as see-through red boxes (rails tall, the rest flat), null = hide
    readonly List<GameObject> ghostCells = new();
    void ShowGhost(Dictionary<Vector2Int, char> cells)
    {
        foreach (var g in ghostCells) Destroy(g);
        ghostCells.Clear();
        if (cells == null) return;
        var mat = BallArt.Ghost(LevelBuilder.Mat(new Color(1f, 0.15f, 0.1f)));
        foreach (var kv in cells)
        {
            float h = kv.Value == '#' ? 0.4f : 0.08f, y = level.BaseY(kv.Key);
            var g = LevelBuilder.Visual("Ghost", PrimitiveType.Cube, new Vector3(kv.Key.x, y + h / 2 + 0.01f, kv.Key.y), new Vector3(0.94f, h, 0.94f), Quaternion.identity, Color.white, transform);
            foreach (var r in g.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            ghostCells.Add(g);
        }
    }

    // ---------- helpers ----------

    void SendToast(int id, string text)
    {
        if (id == me) ui.Toast(text);
        else net.Send(id, Net.Msg(Toast, w => w.Write(text)));
    }

    void ToastAll(string text)
    {
        ui.Toast(text);
        net.Send(-1, Net.Msg(Toast, w => w.Write(text)));
    }

    static string Tag(int id) => $"Giocatore {id + 1}";
    string Name(int id) => Tag(id) + (id == me ? " (tu)" : "");
    static Color PlayerColor(int id) => BallArt.PlayerColors[id % BallArt.PlayerColors.Length];
}
