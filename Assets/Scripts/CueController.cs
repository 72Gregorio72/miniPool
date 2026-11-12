using UnityEngine;
using UnityEngine.InputSystem;

// Aim = camera direction (OrbitCamera). Hold right mouse and pull the mouse back (down) to load power, release to shoot,
// Esc cancels. While holding right, also hold left: the mouse moves the cue tip on the ball (up follow, down draw, sides english).
// W / S raise / lower the cue butt (elevation): with side english a raised cue curves the ball (swerve, massé).
public class CueController : MonoBehaviour
{
    public float maxSpeed = 40f, pullPixels = 300f, tipPixels = 150f;
    public Ball cue;
    public OrbitCamera orbit;
    public LevelBuilder.Level level;
    public System.Action onShot;
    public System.Action<Vector3, float, Vector2, float> shoot; // multiplayer: send the shot (dir, speed, tip, elevation) instead of applying it here
    public bool guide; // multiplayer: aim lines from a run-ahead of the shot (PoolPhysics.Predict), spin and raised cue included
    public bool guideHit = true; // also the yellow line of the ball that gets hit (off in 8-ball: too easy)
    public GameUI ui; // draws the cue-ball diagram (tip spot, elevation) and the power bar (GameUI.SetCue)

    LineRenderer cueLine, hitLine;
    readonly System.Collections.Generic.List<Vector3> cuePath = new(), hitPath = new();
    float guideAt;
    (Vector3, float, Vector2, float, Vector3) guideKey;

    Transform stick;
    /// <summary>The cue model (the HUD's power bar shows a copy).</summary>
    public Transform Stick => stick;
    bool pulling;
    public bool Pulling => pulling; // onboarding hints (GameManager.Hint)
    float power;
    Vector2 tip; // in ball radii, max 0.5 (beyond that a real cue miscues)
    float elevation; // degrees the butt is raised, 0..PoolPhysics.MaxElevation

    const float CueLength = 1.47f * 7, Tilt = 6f;

    void Awake()
    {
        stick = LevelBuilder.Visual("Cue", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.21f, CueLength, 0.21f), Quaternion.identity, new Color(0.85f, 0.7f, 0.45f), transform).transform;
    }

    void OnEnable() => Show(true);

    void OnDisable()
    {
        pulling = false;
        power = 0;
        Show(false);
    }

    void Show(bool on)
    {
        if (stick) stick.gameObject.SetActive(on);
        if (cueLine) hitLine.enabled = (cueLine.enabled = on && guide) && guideHit;
        if (ui) ui.SetCue(on, tip, power, elevation);
    }

    static LineRenderer Line(string name, Color c, Transform parent)
    {
        var lr = new GameObject(name).AddComponent<LineRenderer>();
        lr.transform.SetParent(parent, false);
        // in builds the shader is there thanks to Resources/BuildShaders (BuildGame)
        lr.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = c };
        lr.widthMultiplier = 0.05f;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return lr;
    }

    // re-runs the shot ahead when the aim, power, tip or elevation changed (at most 20 times a second: it's a full simulation)
    void UpdateGuide(Vector3 dir)
    {
        if (!cueLine)
        {
            cueLine = Line("CueGuide", new Color(1, 1, 1, 1), transform);
            hitLine = Line("HitGuide", new Color(1f, 0.85f, 0.2f), transform);
        }
        cueLine.enabled = true;
        hitLine.enabled = guideHit;
        // not pulling yet: show a medium shot, so spin and massé curves already read
        float speed = Mathf.Pow(pulling ? Mathf.Max(power, 0.05f) : 0.6f, 2) * maxSpeed;
        var key = (dir, speed, tip, elevation, cue.transform.position);
        if (key == guideKey || Time.time - guideAt < 0.05f) return;
        guideKey = key;
        guideAt = Time.time;
        PoolPhysics.Predict(level, cue, dir, speed, tip, elevation, cuePath, hitPath);
        cueLine.positionCount = cuePath.Count;
        cueLine.SetPositions(cuePath.ToArray());
        hitLine.positionCount = hitPath.Count;
        hitLine.SetPositions(hitPath.ToArray());
    }

    void Update()
    {
        if (cue == null || orbit == null || level == null) return;
        var mouse = Mouse.current;
        var kb = Keyboard.current;

        if (orbit.Free) { pulling = false; power = 0; Show(false); return; }

        if (kb != null && kb.escapeKey.wasPressedThisFrame) { pulling = false; power = 0; }
        if (kb != null)
            elevation = Mathf.Clamp(elevation + ((kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0)) * 40 * Time.deltaTime, PoolPhysics.MinElevation, PoolPhysics.MaxElevation);

        if (mouse != null)
        {
            if (mouse.rightButton.wasPressedThisFrame) { pulling = true; power = 0; }
            if (pulling)
            {
                var d = mouse.delta.ReadValue();
                if (mouse.leftButton.isPressed) tip = Vector2.ClampMagnitude(tip + d / tipPixels, 0.5f);
                else power = Mathf.Clamp01(power - d.y / pullPixels);
            }
            if (pulling && mouse.rightButton.wasReleasedThisFrame)
            {
                pulling = false;
                if (power > 0.01f)
                {
                    Sfx.Play(Sfx.Kind.Cue, power * power); // same curve as the shot speed
                    // squared curve: fine control on soft shots, still a full-power break
                    if (shoot != null) shoot(orbit.AimDirection, power * power * maxSpeed, tip, elevation);
                    else PoolPhysics.Shoot(cue, orbit.AimDirection, power * power * maxSpeed, tip.x, tip.y, elevation);
                    power = 0;
                    tip = Vector2.zero;
                    onShot?.Invoke();
                    return;
                }
            }
        }

        Show(true); // after the input: the HUD shows this frame's tip and power
        PlaceStick(orbit.AimDirection);
        if (guide) UpdateGuide(orbit.AimDirection);
    }

    void PlaceStick(Vector3 dir)
    {
        // stick behind the ball, tilted, tip offset by the english, pulled back with power
        var from = cue.transform.position;
        // below 0 (jump shot) the cue doesn't rotate around the ball: it keeps a slight downward tilt and drops, the tip low on the ball
        float tilt = Mathf.Max(Tilt, elevation) * Mathf.Deg2Rad;
        var axis = (dir * Mathf.Cos(tilt) + Vector3.down * Mathf.Sin(tilt)).normalized; // butt raised
        var side = Vector3.Cross(Vector3.up, dir).normalized;
        var faceUp = Vector3.Cross(axis, side);
        float drop = Mathf.Max(0, elevation / PoolPhysics.MinElevation) * 0.7f * Ball.Radius;
        var tipPos = from + (side * tip.x + faceUp * tip.y) * Ball.Radius - axis * (Mathf.Sqrt(Ball.Radius * Ball.Radius - drop * drop) + 0.05f + power * 1.5f) + Vector3.down * drop; // tip stays on the ball's surface
        stick.SetPositionAndRotation(tipPos - axis * CueLength / 2, Quaternion.FromToRotation(Vector3.up, axis));
    }
}
