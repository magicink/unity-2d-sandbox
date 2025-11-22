using System.Collections.Generic;
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
    [SerializeField] private bool autoDetectTileWidth = true;
    [SerializeField] private int initialTiles = 3;
    [SerializeField] private float spawnBuffer = 1f;
    [SerializeField] private float despawnBuffer = 1f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    // Internal state
    private readonly Dictionary<int, GameObject> spawned = new Dictionary<int, GameObject>();
    private Camera cam;
    private float halfCameraWidth;
    private float halfCameraHeight;
    private float baseOffsetX = 0f;

    private void Awake()
    {
        cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("GroundRepeater: No camera assigned and Camera.main is null. GroundRepeater will not spawn tiles.", this);
            enabled = false;
            return;
        }

        if (pool == null)
        {
            Debug.LogWarning("GroundRepeater: No InstancePool assigned.", this);
            enabled = false;
            return;
        }

        pool.EnsureInitialized();

        if (poolParent == null)
        {
            poolParent = transform;
        }

        // Attempt to auto-detect tile width if configured or if not set
        if (autoDetectTileWidth || tileWidth <= 0f)
        {
            // Attempt to derive tile width from the pool's prefab sprite bounds
            var prefab = pool.Prefab;
            if (prefab != null)
            {
                if (prefab.TryGetComponent<SpriteRenderer>(out var sr))
                {
                    tileWidth = sr.bounds.size.x;
                }
                else if (prefab.TryGetComponent<Renderer>(out var r))
                {
                    tileWidth = r.bounds.size.x;
                }
            }
        }

        if (tileWidth <= 0f)
        {
            tileWidth = 1f; // safe fallback
            if (debugLogs)
            {
                Debug.LogWarning($"GroundRepeater: Couldn't derive tileWidth from prefab; using fallback {tileWidth}.", this);
            }
        }

        // compute half camera extents for orthographic camera
        if (cam.orthographic)
        {
            halfCameraHeight = cam.orthographicSize;
            halfCameraWidth = halfCameraHeight * cam.aspect;
        }
        else
        {
            // For perspective, approximate using FOV and distance to plane at camera.z
            // Fall back to a single-tile spawn fallback
            halfCameraWidth = cam.pixelWidth / 100f * 0.5f;
            halfCameraHeight = cam.pixelHeight / 100f * 0.5f;
        }

        // initial spawn
        SpawnInitialTiles();
    }

    private void OnValidate()
    {
        if (tileWidth < 0f)
        {
            tileWidth = 0f;
        }

        // When fields change in the editor, optionally auto-detect tile width
        if (autoDetectTileWidth)
        {
            AutoDetectTileWidth();
        }
    }

    [ContextMenu("Auto-detect Tile Width")]
    private void AutoDetectTileWidth()
    {
        if (pool == null || pool.Prefab == null)
        {
            if (debugLogs)
            {
                Debug.LogWarning("GroundRepeater: Cannot auto-detect tileWidth because pool or pool.Prefab is null.", this);
            }
            return;
        }

        var prefab = pool.Prefab;
        float detected = 0f;

        // Try to find SpriteRenderer(s) and compute combined bounds
        var renderers = prefab.GetComponentsInChildren<Renderer>(true);
        if (renderers != null && renderers.Length > 0)
        {
            Bounds combined = new Bounds(prefab.transform.position, Vector3.zero);
            bool first = true;
            foreach (var r in renderers)
            {
                if (first)
                {
                    combined = r.bounds;
                    first = false;
                }
                else
                {
                    combined.Encapsulate(r.bounds);
                }
            }

            detected = combined.size.x;
        }

        // If no renderers, try to find a BoxCollider2D
        if (detected <= 0f)
        {
            if (prefab.TryGetComponent<BoxCollider2D>(out var bc))
            {
                var scale = prefab.transform.lossyScale;
                detected = bc.size.x * Mathf.Abs(scale.x);
            }
            else
            {
                // Try children colliders
                var colliders = prefab.GetComponentsInChildren<BoxCollider2D>(true);
                if (colliders != null && colliders.Length > 0)
                {
                    var maxX = 0f;
                    foreach (var cc in colliders)
                    {
                        var scale = cc.transform.lossyScale;
                        maxX = Mathf.Max(maxX, cc.size.x * Mathf.Abs(scale.x));
                    }
                    detected = maxX;
                }
            }
        }

        if (detected > 0f)
        {
            tileWidth = detected;
            if (debugLogs)
            {
                Debug.Log($"GroundRepeater: auto-detected tileWidth = {tileWidth}", this);
            }
        }
    }

    private void LateUpdate()
    {
        if (!enabled || cam == null || pool == null)
        {
            return;
        }

        // Recompute camera extents in case the viewport changed
        if (cam.orthographic)
        {
            halfCameraHeight = cam.orthographicSize;
            halfCameraWidth = halfCameraHeight * cam.aspect;
        }

        UpdateTiles();
    }

    private void SpawnInitialTiles()
    {
        if (initialTiles <= 0)
        {
            initialTiles = 1;
        }

        var camX = cam.transform.position.x;
        float anchorX = poolParent != null ? poolParent.position.x : 0f;
        int centerIndex = Mathf.RoundToInt((camX - anchorX) / tileWidth);
        int start = centerIndex - (initialTiles / 2);
        int end = start + initialTiles - 1;

        for (int i = start; i <= end; i++)
        {
            EnsureTileAtIndex(i);
        }
    }

    private void UpdateTiles()
    {
        var camPos = cam.transform.position;
        float left = camPos.x - halfCameraWidth;
        float right = camPos.x + halfCameraWidth;
        float anchorX = poolParent != null ? poolParent.position.x : 0f;

        // Expand spawn/despawn by buffers
        int leftIndex = Mathf.FloorToInt((left - anchorX - spawnBuffer) / tileWidth);
        int rightIndex = Mathf.CeilToInt((right - anchorX + spawnBuffer) / tileWidth);

        // Ensure tiles in range
        for (int i = leftIndex; i <= rightIndex; i++)
        {
            EnsureTileAtIndex(i);
        }

        // Despawn tiles that are beyond despawn buffer to the left or right
        var toRemove = new List<int>();
        float despawnLeft = left - despawnBuffer;
        float despawnRight = right + despawnBuffer;

        foreach (var kvp in spawned)
        {
            var index = kvp.Key;
            var obj = kvp.Value;
            float objX = obj.transform.position.x;

            if (objX + (tileWidth * 0.5f) < despawnLeft || objX - (tileWidth * 0.5f) > despawnRight)
            {
                toRemove.Add(index);
            }
        }

        foreach (var i in toRemove)
        {
            ReturnTile(i);
        }
    }

    private void EnsureTileAtIndex(int index)
    {
        if (spawned.ContainsKey(index))
        {
            return;
        }

        float anchorX = poolParent != null ? poolParent.position.x : 0f;
        Vector3 position = new Vector3(anchorX + index * tileWidth + baseOffsetX, poolParent.position.y, poolParent.position.z);
        var instance = pool.Get(position, Quaternion.identity, poolParent, true);
        if (instance != null)
        {
            spawned[index] = instance;
            if (debugLogs)
            {
                Debug.Log($"GroundRepeater: spawned index {index} at {position}", this);
            }
        }
        else if (debugLogs)
        {
            Debug.LogWarning($"GroundRepeater: pool.Get() returned null for index {index}");
        }
    }

    private void ReturnTile(int index)
    {
        if (!spawned.TryGetValue(index, out var obj))
        {
            return;
        }

        pool.Return(obj);
        spawned.Remove(index);
        if (debugLogs)
        {
            Debug.Log($"GroundRepeater: returned tile at index {index}", this);
        }
    }

    private void OnDisable()
    {
        // Return all spawned tiles when disabled
        foreach (var kvp in spawned)
        {
            if (kvp.Value != null)
            {
                pool.Return(kvp.Value);
            }
        }

        spawned.Clear();
    }
}
