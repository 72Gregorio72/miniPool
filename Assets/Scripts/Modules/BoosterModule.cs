using UnityEngine;

// Booster (> < ^ V) settings. Read by PoolPhysics / LevelBuilder.Module; default = PoolPhysics constant.
public class BoosterModule : MonoBehaviour
{
    public float speed = PoolPhysics.BoostSpeed; // minimum speed it sends the ball off with
}
