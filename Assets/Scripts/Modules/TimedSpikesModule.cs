using UnityEngine;

// Timed spikes (! purple, & green = opposite phase) settings and look. Read by PoolPhysics; default = PoolPhysics constant.
public class TimedSpikesModule : MonoBehaviour
{
    public float period = PoolPhysics.TimedPeriod; // seconds up, then as long down (same clock as the timed walls)
    public bool inverted;                          // green: down while the purple ones are up, starts down
    public Transform tips;                         // the spikes: scaled flat when down (PoolPhysics.ShowMoving)
    [HideInInspector] public bool shownUp;         // last state the look played a sound for
}
