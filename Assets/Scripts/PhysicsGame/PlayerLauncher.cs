using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;

public class PlayerLauncher : MonoBehaviour
{
    /// <summary>
    /// Singleton instance of the PlayerLauncher. Use this property to access the active launcher in the scene.
    /// </summary>
    public static PlayerLauncher Instance { get; private set; }

    [Header("References")]
    [Tooltip("Reference to the player instance that this launcher controls. Assign a PhysicsGamePlayer in the Inspector.")]
    [SerializeField] private PhysicsGamePlayer player = null;
    [Tooltip("Camera used to convert screen coordinates to world positions. If unset, Camera.main is used.")]
    [SerializeField] private Camera worldCamera = null;

    // PlayerLauncher does not control physics directly anymore - player handles it in its state machine
    // Whether the launcher currently controls the player's movement
    private bool isFollowing = false;
    // The input touch id that controls the player's follow (-1 for mouse, otherwise touchId)
    private int controllingTouchId = -2; // -2 == not assigned
    // PlayerLauncher no longer handles smoothing or movement directly.

    // Note: player field is serialized for Inspector access; don't expose a redundant public accessor.

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Warn in the inspector if the player reference hasn't been assigned.
        if (player == null)
            Debug.LogWarning($"{nameof(PlayerLauncher)}: 'player' reference is not assigned in the Inspector.", this);
        if (worldCamera == null)
            worldCamera = Camera.main;
    }
#endif
    // When running in the Editor, the mouse will be used to simulate a single touch.
    // This is handy for testing touch logic without a touch device.

    private void OnEnable()
    {
        // Enforce singleton during enable - keep the first instance and destroy duplicates
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("PlayerLauncher: another instance already exists. Destroying duplicate component.", this);
            Destroy(this);
            return;
        }
        Instance = this;

        // Ensure enhanced touch support is enabled if the Input System package is active
        EnhancedTouchSupport.Enable();
    }

    private void OnDisable()
    {
        EnhancedTouchSupport.Disable();
        if (Instance == this)
            Instance = null;
    }

    private void Awake()
    {
        // If another instance is already set and is not this, destroy the duplicate. This guards against
        // duplicates in Awake order or when creating/destroying objects at runtime.
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("PlayerLauncher.Awake: Detected duplicate PlayerLauncher. Destroying duplicate component.", this);
            Destroy(this);
            return;
        }
        Instance = this;
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (worldCamera == null)
            worldCamera = Camera.main;

        // Nothing else to initialize here; player tracks its own physics
    }

    // Update is called once per frame
    void Update()
    {
        // PlayerLauncher does not manage player's Rigidbody - the player manages physics when dragging.

        // If the new Input System EnhancedTouch is available, use it
        if (UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0)
        {
            foreach (var t in UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches)
            {
                // If we are not already following, take the first Began touch as the controller
                if (!isFollowing && t.phase == UnityEngine.InputSystem.TouchPhase.Began)
                {
                    BeginFollow(t.screenPosition, (int)t.touchId);
                    continue;
                }

                if (isFollowing && (int)t.touchId == controllingTouchId)
                {
                    if (t.phase == UnityEngine.InputSystem.TouchPhase.Moved || t.phase == UnityEngine.InputSystem.TouchPhase.Stationary)
                    {
                        UpdateFollow(t.screenPosition);
                    }
                    else if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended || t.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                    {
                        EndFollow();
                    }
                }
            }
        }
        else
        {
            // No touches available - treat left mouse button as simulated touch for Editor/useful testing.
            // Note: This is a single-touch simulation only.
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    BeginFollow(mouse.position.ReadValue(), -1);
                }
                else if (mouse.leftButton.wasReleasedThisFrame)
                {
                    EndFollow();
                }
                else if (mouse.leftButton.isPressed)
                {
                    // While the mouse button is pressed, continuously update the follow target.
                    UpdateFollow(mouse.position.ReadValue());
                }
            }
        }
    }

    private void BeginFollow(Vector2 screenPosition, int touchId)
    {
        if (player == null)
        {
            Debug.LogWarning("PlayerLauncher.BeginFollow: player reference is null.", this);
            return;
        }

        controllingTouchId = touchId;
        isFollowing = true;

        Debug.Log($"Touch started: id={touchId} position={screenPosition}");

        // Convert to world position and notify player state machine to begin drag
        var worldPos = ScreenToWorld(screenPosition);
        player?.BeginDragAt(worldPos);
        player?.UpdateDragTarget(worldPos);
    }

    private void UpdateFollow(Vector2 screenPosition)
    {
        if (!isFollowing || player == null)
            return;
        Vector3 targetWorld = ScreenToWorld(screenPosition);
        player?.UpdateDragTarget(targetWorld);
    }

    private void EndFollow()
    {
        if (!isFollowing)
            return;

        // Save the id to be logged before clearing it
        int previousId = controllingTouchId;

        isFollowing = false;
        controllingTouchId = -2;

        Debug.Log($"Touch ended: id={previousId}");
        // Notify the player's state machine that the drag/follow has ended
        player?.EndDragAt();
    }

    private Vector3 ScreenToWorld(Vector2 screenPosition)
    {
        Camera cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("PlayerLauncher.ScreenToWorld: No camera found. Using default ScreenToWorldPoint with z=0.");
            return new Vector3(screenPosition.x, screenPosition.y, 0f);
        }

        // Calculate the z distance from camera to player to provide a proper depth for ScreenToWorldPoint
        float z = Mathf.Abs(cam.transform.position.z - (player != null ? player.transform.position.z : 0f));
        Vector3 sp = new Vector3(screenPosition.x, screenPosition.y, z);
        return cam.ScreenToWorldPoint(sp);
    }
}
