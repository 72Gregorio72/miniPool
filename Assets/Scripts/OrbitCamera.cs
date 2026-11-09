using UnityEngine;
using UnityEngine.InputSystem;

// Orbit around the cue ball: left mouse drag turns (left/right = aim), wheel zooms,
// arrows fine-turn (Shift = finer), T toggles the top view of the whole table. Ctrl + left drag turns only the camera (the aim stays).
// F toggles free flight (WASD move, Q/E down/up, Shift fast, mouse drag look); F again returns to the aim you left.
public class OrbitCamera : MonoBehaviour
{
    public Vector3 follow;       // what to orbit (set by the game: the cue ball while aiming)
    public Bounds table;
    public float yaw = 90, pitch = 15, distance = 6;
    public float followSharpness = 4; // higher = tighter follow of the cue ball
    public bool Free { get; private set; }

    Vector3 pivot;
    bool top;
    float savedPitch, savedDistance, savedYaw, savedFreePitch, blend = 1;
    float look; // camera yaw on top of the aim (Ctrl + left drag); a normal drag glides it back to 0

    public Vector3 AimDirection => Quaternion.Euler(0, yaw, 0) * Vector3.forward;

    public void Snap() { pivot = follow; look = 0; }

    public bool Top { get => top; set { if (value != top) ToggleTop(); } }

    void LateUpdate()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        bool fine = kb != null && kb.shiftKey.isPressed;
        float dt = Time.unscaledDeltaTime;

        if (mouse != null)
            Cursor.lockState = mouse.leftButton.isPressed || mouse.rightButton.isPressed ? CursorLockMode.Locked : CursorLockMode.None;
        if (kb != null && kb.fKey.wasPressedThisFrame) ToggleFree();

        if (Free)
        {
            if (mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed))
            {
                var d = mouse.delta.ReadValue() * 0.2f * GameManager.Sensitivity;
                yaw += d.x;
                pitch = Mathf.Clamp(pitch - d.y, -89, 89);
            }
            var rot = Quaternion.Euler(pitch, yaw, 0);
            var move = Vector3.zero;
            if (kb != null)
            {
                move = rot * new Vector3(
                    (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0), 0,
                    (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0));
                move.y += (kb.eKey.isPressed ? 1 : 0) - (kb.qKey.isPressed ? 1 : 0);
            }
            transform.SetPositionAndRotation(transform.position + move * (fine ? 20f : 6f) * dt, rot);
            return;
        }

        // right button is the shot (CueController): no orbiting while it is held
        if (mouse != null)
        {
            if (mouse.leftButton.isPressed && !mouse.rightButton.isPressed)
            {
                var d = mouse.delta.ReadValue() * (fine ? 0.02f : 0.2f) * GameManager.Sensitivity;
                if (kb != null && kb.ctrlKey.isPressed) look += d.x;
                else
                {
                    yaw += d.x;
                    if (look != 0) { look = 0; blend = 0; } // back behind the cue
                }
                pitch = Mathf.Clamp(pitch - d.y, 2, 89);
            }
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0) distance = Mathf.Clamp(distance * (scroll > 0 ? 0.9f : 1.1f), 1.2f, 40);
        }
        if (kb != null)
        {
            float turn = (kb.rightArrowKey.isPressed ? 1 : 0) - (kb.leftArrowKey.isPressed ? 1 : 0);
            yaw += turn * (fine ? 2f : 30f) * dt;
            if (kb.tKey.wasPressedThisFrame) ToggleTop();
        }

        var target = top ? table.center : follow;
        // soft follow: lags a fast-rolling ball a little instead of snapping to it
        pivot = Vector3.Lerp(pivot, target, 1 - Mathf.Exp(-followSharpness * dt));
        var orbitRot = Quaternion.Euler(pitch, yaw + look, 0);
        var pos = pivot - orbitRot * Vector3.forward * distance;
        // glide back from free flight instead of jumping
        blend = Mathf.MoveTowards(blend, 1, dt * 2);
        float k = blend >= 1 ? 1 : 1 - Mathf.Exp(-10 * dt);
        transform.SetPositionAndRotation(Vector3.Lerp(transform.position, pos, k), Quaternion.Slerp(transform.rotation, orbitRot, k));
        transform.position += Juice.ShakeOffset(dt);
    }

    void ToggleFree()
    {
        Free = !Free;
        if (Free)
        {
            savedYaw = yaw;
            savedFreePitch = pitch;
            yaw += look; // fly off from where the camera looks
            look = 0;
        }
        else
        {
            yaw = savedYaw;
            pitch = savedFreePitch;
            blend = 0;
        }
    }

    void ToggleTop()
    {
        top = !top;
        if (top)
        {
            savedPitch = pitch;
            savedDistance = distance;
            pitch = 89;
            distance = Mathf.Max(table.size.x, table.size.z * 1.8f);
        }
        else
        {
            pitch = savedPitch;
            distance = savedDistance;
        }
    }
}
