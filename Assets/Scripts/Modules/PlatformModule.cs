using UnityEngine;

// Moving platform (H) settings. Read by PoolPhysics / LevelBuilder.Module; default = PoolPhysics constant.
public class PlatformModule : MonoBehaviour
{
    public float speed = PoolPhysics.PlatformSpeed; // tiles/s (read when built)
}
