using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;

public class PlayerLauncher : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Reference to the player instance that this launcher controls. Assign a PhysicsGamePlayer in the Inspector.")]
    [SerializeField] private PhysicsGamePlayer player = null;
    [Tooltip("Camera used to convert screen coordinates to world positions. If unset, Camera.main is used.")]
    [SerializeField] private Camera worldCamera = null;
    [Tooltip("Optional smoothing for follow motion (0 = instant)")]
    [SerializeField] private float followSmoothing = 0f;

    // Cached player's Rigidbody2D
    private Rigidbody2D playerBody;
    private bool previousSimulatedState = true;
    // Whether the launcher currently controls the player's movement
    private bool isFollowing = false;
    // The input touch id that controls the player's follow (-1 for mouse, otherwise touchId)
    private int controllingTouchId = -2; // -2 == not assigned
    private Vector3 followVelocity = Vector3.zero;

    // Public accessor for other scripts (read-only)
    public PhysicsGamePlayer Player => player;

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
    private bool mouseTouchActive = false;
    private Vector2 lastMousePosition = Vector2.zero;

    private void OnEnable()
    {
        // Ensure enhanced touch support is enabled if the Input System package is active
        EnhancedTouchSupport.Enable();
    }

    private void OnDisable()
    {
        EnhancedTouchSupport.Disable();
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (worldCamera == null)
            worldCamera = Camera.main;

        if (player != null)
            playerBody = player.GetComponent<Rigidbody2D>();

        if (playerBody != null)
            previousSimulatedState = playerBody.simulated;
    }

    // Update is called once per frame
    void Update()
    {
        // Simple helper - ensure we have the player's Rigidbody cached
        if (playerBody == null && player != null)
            playerBody = player.GetComponent<Rigidbody2D>();

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
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                mouseTouchActive = true;
                lastMousePosition = mouse.position.ReadValue();
                BeginFollow(lastMousePosition, -1);
            }
            else if (mouse != null && mouse.leftButton.wasReleasedThisFrame)
            {
                Vector2 mousePos = mouse.position.ReadValue();
                mouseTouchActive = false;
                EndFollow();
            }
            else if (mouseTouchActive && mouse != null)
            {
                Vector2 mousePos = mouse.position.ReadValue();
                if (mousePos != lastMousePosition)
                {
                    // Optional: log movement if needed
                    // Debug.Log($"Simulated touch moved: id=-1 position={mousePos}");
                    lastMousePosition = mousePos;
                    UpdateFollow(mousePos);
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

        // Disable physics on player body while dragging
        if (playerBody != null)
        {
            previousSimulatedState = playerBody.simulated;
            playerBody.simulated = false;
#if UNITY_2023_1_OR_NEWER
            playerBody.linearVelocity = Vector2.zero;
#else
            playerBody.velocity = Vector2.zero;
#endif
            playerBody.angularVelocity = 0f;
        }
        

        var worldPos = ScreenToWorld(screenPosition);
        player.transform.position = worldPos;
    }

    private void UpdateFollow(Vector2 screenPosition)
    {
        if (!isFollowing || player == null)
            return;

        Vector3 targetWorld = ScreenToWorld(screenPosition);
        if (followSmoothing <= 0f)
        {
            player.transform.position = targetWorld;
        }
        else
        {
            player.transform.position = Vector3.SmoothDamp(player.transform.position, targetWorld, ref followVelocity, followSmoothing);
        }
    }

    private void EndFollow()
    {
        if (!isFollowing)
            return;

        // Save the id to be logged before clearing it
        int previousId = controllingTouchId;

        isFollowing = false;
        controllingTouchId = -2;

        if (playerBody != null)
        {
            playerBody.simulated = previousSimulatedState;
        }
        Debug.Log($"Touch ended: id={previousId}");
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
