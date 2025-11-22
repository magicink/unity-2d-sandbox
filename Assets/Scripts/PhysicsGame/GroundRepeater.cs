using UnityEngine;
using Sandbox.Pooling;

/// <summary>
/// Minimal GroundRepeater class that exposes the same inspector fields as before,
/// but intentionally contains no runtime logic. This keeps the same API surface
/// for designers while removing behavior.
/// </summary>
public class GroundRepeater : MonoBehaviour
{
    [Header("Pool")]
    [SerializeField] private InstancePool pool;
    [SerializeField] private Transform poolParent;

    [Header("Camera & Tile")]
    [SerializeField] private Camera targetCamera;
    [Tooltip("Width of each ground tile in world units (set to the prefab's width)")]
    [SerializeField] private float tileWidth = 10f;
    [SerializeField] private int initialTiles = 3;
    [SerializeField] private float spawnBuffer = 1f;
    [SerializeField] private float despawnBuffer = 1f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;
}
