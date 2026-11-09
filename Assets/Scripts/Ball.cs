using UnityEngine;

// Ball state for PoolPhysics (no Rigidbody).
public class Ball : MonoBehaviour
{
    public const float Radius = 0.25f;

    public int number;          // 0 = cue, 8 = black
    public int owner = -1;      // multiplayer: player that owns this cue / 8 (-1 = shared)
    public Vector3 v, w;        // linear and angular velocity
    public bool falling;
    public bool airborne;       // launched by a fan or rolled off a step down, lands where it comes down
    public Vector2Int? cell;    // last tile it rolled on (modules trigger on entering a tile)
    public bool boostArmed;     // entered a booster, pushes once it reaches the center
    public LevelBuilder.Pocket pocket; // set while dropping into a pocket
    public System.Action<Ball> onCueHit; // set on the cue ball only
    public float beltSpeed;             // speed of the last belt it was on
    public bool onBelt, carried;        // on a conveyor this step / carried somewhere by it over the last frame

    public bool IsMoving => v != Vector3.zero || w != Vector3.zero || carried;

    public void Stop()
    {
        v = w = Vector3.zero;
        falling = airborne = onBelt = carried = boostArmed = false;
        cell = null;
        pocket = null;
    }
}
