using UnityEngine;

// Timed wall (L) settings. Read by PoolPhysics / LevelBuilder.Module; default = PoolPhysics constant.
public class TimedWallModule : MonoBehaviour
{
    public float period = PoolPhysics.TimedPeriod; // seconds up, then as long down (read when built)
}
