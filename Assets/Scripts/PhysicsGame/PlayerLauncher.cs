using System;
using UnityEngine;
using Sandbox.StateMachine;
using Sandbox.Pooling;

/// <summary>
/// Coordinates player drag/launch flow by wiring an InputProvider, GroundChecker,
/// PlayerInteractor and DragController together. States are implemented in separate files.
/// </summary>
public class PlayerLauncher : AbstractStateMachine<LaunchState>
{
    [Header("References")]
    [SerializeField] private PhysicsGamePlayer player = null;
    [Tooltip("Optional: assign the Player Pool Manager InstancePool here to spawn players from the pool on pull")]
    [SerializeField] private InstancePool playerPool = null;
    [SerializeField] private MonoBehaviour inputProviderComponent = null; // assign a UnityInputProvider or other IInputProvider implementation
    [SerializeField] private PhysicsGroundChecker physicsGroundChecker = null;
    [SerializeField] private LayerMask groundLayerMask = 0;
    [SerializeField] private Camera worldCamera = null;

    [Header("Debug & Gizmos")]
    [SerializeField] private bool drawTouchGizmos = true;
    [SerializeField] private float gizmoRadius = 0.1f;
    [SerializeField] private bool enableDebugLogs = false;
    [SerializeField] private bool enableWarningLogs = true;
    [Tooltip("Dev only: automatically simulate a single drag/launch sequence on start for testing")]
    [SerializeField] private bool runTestOnStart = false;

    [Header("Tension")]
    [SerializeField] private bool simulateTension = true;
    [SerializeField] private float tensionStiffness = 1f;
    [SerializeField] private float maxPullDistance = 3f;
    [SerializeField] private bool preventStartOverGround = true;

    [Header("Slingshot")]
    [SerializeField] private bool enableSlingshot = true;
    [SerializeField] private float slingshotForceMultiplier = 10f;
    [SerializeField] private float minSlingshotDistance = 0.05f;

    // Runtime objects
    private IInputProvider inputProvider;
    private IPlayerInteractor playerInteractor;
    private IGroundChecker groundChecker;
    private DragController dragController;

    private IdleState idleState;
    private FollowingState followingState;
    private DisabledState disabledState;

    private bool playerInFlight = false;
    // If the current player was spawned from the pool we should return it on landing
    private bool playerFromPool = false;

    // Public read-only accessors used by state classes
    public PhysicsGamePlayer Player => player;
    public DragController DragController => dragController;
    public bool PlayerInFlight => playerInFlight;

    protected override LaunchState GetInitialState()
    {
        return idleState;
    }

    private void Awake()
    {
        // Build states
        idleState = new IdleState(this);
        followingState = new FollowingState(this);
        disabledState = new DisabledState(this);

        // Build player interactor for the configured player (if assigned in inspector)
        playerInteractor = player != null ? new PhysicsGamePlayerInteractor(player) as IPlayerInteractor : null;

        // Resolve ground checker (PhysicsGroundChecker preferred, fallback to Simple)
        if (physicsGroundChecker != null)
            groundChecker = physicsGroundChecker as IGroundChecker;
        else
            groundChecker = new SimpleGroundChecker(groundLayerMask);

        // Create drag controller
        dragController = new DragController(
            playerInteractor,
            groundChecker,
            ScreenToWorld,
            preventStartOverGround: preventStartOverGround,
            simulateTension: simulateTension,
            tensionStiffness: tensionStiffness,
            maxPullDistance: maxPullDistance,
            enableSlingshot: enableSlingshot,
            slingshotForceMultiplier: slingshotForceMultiplier,
            minSlingshotDistance: minSlingshotDistance
        );

        // Ensure the drag controller uses the new interactor instance
        if (dragController != null)
            dragController.SetPlayerInteractor(playerInteractor);
        // Optional: subscribe to drag events for gizmo visibility or logging
        dragController.DragStartBlocked += (pos) => {
            Log($"Drag start blocked at {pos}");
        };
    }

    private void OnEnable()
    {
        // Wire Player events if a player is present
        if (player != null)
            SubscribeToPlayerEvents(player);

        // Ensure input provider exists (fall back to UnityInputProvider if needed)
        if (inputProviderComponent != null)
        {
            inputProvider = inputProviderComponent as IInputProvider;
        }
        else
        {
            // Try to find an IInputProvider component
            inputProvider = GetComponent<IInputProvider>();
            if (inputProvider == null)
            {
                // Add UnityInputProvider if none provided
                var uip = GetComponent<UnityInputProvider>();
                if (uip == null)
                    uip = gameObject.AddComponent<UnityInputProvider>();
                inputProvider = uip as IInputProvider;
            }
        }

        if (inputProvider != null)
        {
            inputProvider.Begin += OnInputBegin;
            inputProvider.Move += OnInputMove;
            inputProvider.End += OnInputEnd;
        }

        if (Application.isPlaying && runTestOnStart)
        {
            StartCoroutine(TestDragCoroutine());
        }
    }

    private void OnDisable()
    {
        if (player != null)
            UnsubscribeFromPlayerEvents(player);

        if (inputProvider != null)
        {
            inputProvider.Begin -= OnInputBegin;
            inputProvider.Move -= OnInputMove;
            inputProvider.End -= OnInputEnd;
        }
    }

    private System.Collections.IEnumerator TestDragCoroutine()
    {
        // Wait a short time to allow scene to initialize
        yield return new WaitForSeconds(0.2f);

        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        OnInputBegin(center, -1);
        yield return new WaitForSeconds(0.05f);
        OnInputMove(center + new Vector2(-100f, -80f), -1);
        yield return new WaitForSeconds(0.1f);
        OnInputEnd(center + new Vector2(-100f, -80f), -1);
    }

    private void OnPlayerLaunched()
    {
        playerInFlight = true;
        // When player launches, ensure we are not following any input
        if (dragController != null && dragController.IsFollowing)
            dragController.End();

        ChangeState(disabledState);
    }

    private void OnPlayerLanded()
    {
        playerInFlight = false;
        // If the player instance came from the pool, return it and clear references
        if (playerFromPool && player != null && playerPool != null)
        {
            playerPool.Return(player.gameObject);
            UnsubscribeFromPlayerEvents(player);
            player = null;
            playerInteractor = null;
            // clear drag controller's interactor so it won't try to control a returned object
            if (dragController != null)
                dragController.SetPlayerInteractor(null);
            playerFromPool = false;
        }

        // Return to Idle state after landing
        if (idleState != null)
            ChangeState(idleState);
    }

    private void OnInputBegin(Vector2 screenPosition, int touchId)
    {
        // If a pool is configured, always try to acquire a fresh player when the user starts pulling.
        // This ensures a new instance is used per pull and avoids stale references.
        if (playerPool != null)
        {
            // Reset the camera to (0,0) when dragging starts so the player is visible
            if (worldCamera != null)
            {
                var camTransform = worldCamera.transform;
                camTransform.position = new Vector3(0f, 0f, camTransform.position.z);
            }

            // If we already have a pooled player, return it first so we can get a fresh one
            if (player != null && playerFromPool)
            {
                try
                {
                    UnsubscribeFromPlayerEvents(player);
                    playerPool.Return(player.gameObject);
                    Debug.Log($"[POOL] PlayerLauncher: returned previous pooled player '{player.name}' to pool");
                    Log($"PlayerLauncher: returned previous pooled player '{player.name}' to pool");
                }
                catch (Exception ex)
                {
                    LogWarning($"PlayerLauncher: Failed to return previous pooled player: {ex.Message}");
                }

                player = null;
                playerInteractor = null;
                if (dragController != null)
                    dragController.SetPlayerInteractor(null);
                playerFromPool = false;
            }

            // Place the newly spawned instance at the world position corresponding to the screen touch
            Vector3 spawnWorld = ScreenToWorld(screenPosition);
            var go = playerPool.Get(spawnWorld, Quaternion.identity);
            if (go != null)
            {
                var pg = go.GetComponent<PhysicsGamePlayer>();
                if (pg != null)
                {
                    player = pg;
                    Debug.Log($"[POOL] PlayerLauncher: obtained pooled player instance '{player.name}' from pool");
                    Log($"PlayerLauncher: obtained pooled player instance '{player.name}' from pool");
                    playerFromPool = true;
                    playerInteractor = new PhysicsGamePlayerInteractor(player) as IPlayerInteractor;
                    SubscribeToPlayerEvents(player);
                    // Tell the drag controller about the newly created interactor so future drag commands act on it
                    if (dragController != null)
                    {
                        dragController.SetPlayerInteractor(playerInteractor);
                        // proactively start the drag on the player so it's ready to follow immediately
                        try
                        {
                            playerInteractor?.BeginDragAt(spawnWorld);
                            // Also start the DragController following right away so subsequent Move events
                            // in the same frame will be handled (avoids races where BeginFollow runs later).
                            if (dragController != null)
                            {
                                Log("PlayerLauncher: starting drag on pooled player immediately");
                                dragController.Begin(screenPosition, touchId);
                            }
                        }
                        catch (Exception) { }
                    }
                }
                else
                {
                    // pool returned a GameObject that does not have a PhysicsGamePlayer
                    LogWarning("PlayerLauncher: Obtained object from pool did not contain a PhysicsGamePlayer component.");
                }
            }
        }

        if (player == null || player.IsAirborne || playerInFlight) return;
        if (!CanStartPull()) return;
        ChangeState(followingState);
        BeginFollow(screenPosition, touchId);
    }

    private void OnInputMove(Vector2 screenPosition, int touchId)
    {
        if (dragController != null && dragController.IsFollowing)
            ContinueFollow(screenPosition);
    }

    private void OnInputEnd(Vector2 screenPosition, int touchId)
    {
        EndFollow();
    }

    // Public API for states and external callers to start/continue/end a follow
    public void BeginFollow(Vector2 screenPosition, int touchId)
    {
        if (dragController == null) return;
        dragController.Begin(screenPosition, touchId);
    }

    public void ContinueFollow(Vector2 screenPosition)
    {
        if (dragController == null) return;
        dragController.Continue(screenPosition);
    }

    public void EndFollow(Vector3? overrideVelocity = null)
    {
        if (dragController == null) return;
        dragController.End(null, overrideVelocity);
    }

    public bool CanStartPull()
    {
        // Allow starting pull if there's an available player or a pool configured
        bool havePlayerReady = (player != null && !player.IsAirborne) || (player == null && playerPool != null);
        return (havePlayerReady && !playerInFlight);
    }

    private void SubscribeToPlayerEvents(PhysicsGamePlayer p)
    {
        if (p == null) return;
        p.Landed += OnPlayerLanded;
        p.Launched += OnPlayerLaunched;
    }

    private void UnsubscribeFromPlayerEvents(PhysicsGamePlayer p)
    {
        if (p == null) return;
        p.Landed -= OnPlayerLanded;
        p.Launched -= OnPlayerLaunched;
    }

    private void OnDrawGizmos()
    {
        if (!drawTouchGizmos) return;

        if (dragController == null) return;
        var hasStart = dragController.HasGizmoStart;
        var hasEnd = dragController.HasGizmoEnd;
        var startWorld = dragController.GizmoStartWorld;
        var endWorld = dragController.GizmoEndWorld;
        var rawWorld = dragController.RawFollowWorldPosition;
        var lastWorld = dragController.LastFollowWorldPosition;
        var currentlyFollowing = dragController.IsFollowing;

        // Draw start point
        if (hasStart)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(startWorld, gizmoRadius);
        }

        // Draw end point
        if (hasEnd)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(endWorld, gizmoRadius);
        }

        // Draw raw pointer/drag location in blue so that you can see the user's input vs the tensioned output
        if (rawWorld != Vector3.zero && currentlyFollowing)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(rawWorld, gizmoRadius * 0.6f);
        }

        // Draw line between start and end (if end exists), otherwise line to current follow position
        if (hasStart && hasEnd)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(startWorld, endWorld);
        }
        else if (hasStart && currentlyFollowing)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(startWorld, lastWorld);
        }
    }

    private Vector3 ScreenToWorld(Vector2 screenPosition)
    {
        Camera cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null)
        {
            LogWarning("PlayerLauncher.ScreenToWorld: No camera found. Using default ScreenToWorldPoint with z=0.");
            return new Vector3(screenPosition.x, screenPosition.y, 0f);
        }

        // Calculate the z distance from camera to player to provide a proper depth for ScreenToWorldPoint
        float z = Mathf.Abs(cam.transform.position.z - (player != null ? player.transform.position.z : 0f));
        Vector3 sp = new Vector3(screenPosition.x, screenPosition.y, z);
        return cam.ScreenToWorldPoint(sp);
    }

    private void Log(string message)
    {
        if (enableDebugLogs)
            Debug.Log(message, this);
    }

    private void LogWarning(string message)
    {
        if (enableWarningLogs)
            Debug.LogWarning(message, this);
    }

}

