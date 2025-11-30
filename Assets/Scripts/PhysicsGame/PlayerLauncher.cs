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
    [SerializeField] private PhysicsGamePlayer player;
    [Tooltip("Optional: assign the Player Pool Manager InstancePool here to spawn players from the pool on pull")]
    [SerializeField] private InstancePool playerPool;
    [SerializeField] private MonoBehaviour inputProviderComponent; // assign a UnityInputProvider or other IInputProvider implementation
    [SerializeField] private PhysicsGroundChecker physicsGroundChecker;
    [SerializeField] private LayerMask groundLayerMask = 0;
    [SerializeField] private Camera worldCamera;

    [Header("Debug & Gizmos")]
    [SerializeField] private bool drawTouchGizmos = true;
    [SerializeField] private float gizmoRadius = 0.1f;
    [SerializeField] private bool enableDebugLogs;
    [SerializeField] private bool enableWarningLogs = true;
    [Tooltip("Dev only: automatically simulate a single drag/launch sequence on start for testing")]
    [SerializeField] private bool runTestOnStart;

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

    private bool playerInFlight;
    // Track pooled instances so each player can be returned to the pool when it lands
    // (supports multiple simultaneous pooled players).
    private readonly System.Collections.Generic.HashSet<PhysicsGamePlayer> pooledPlayers = new System.Collections.Generic.HashSet<PhysicsGamePlayer>();

    // Keep per-player event handler references so we can properly unsubscribe later
    private readonly System.Collections.Generic.Dictionary<PhysicsGamePlayer, (Action launched, Action landed)> playerHandlers = new System.Collections.Generic.Dictionary<PhysicsGamePlayer, (Action, Action)>();

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
        // Unsubscribe from all tracked players (current + pooled instances).
        // Iterate over a copy of the keys so UnsubscribeFromPlayerEvents can modify
        // the `playerHandlers` dictionary safely without invalidating the enumerator.
        var handlerKeys = new System.Collections.Generic.List<PhysicsGamePlayer>(playerHandlers.Keys);
        foreach (var p in handlerKeys)
            UnsubscribeFromPlayerEvents(p);
        playerHandlers.Clear();

        // If any pooled players are still around, attempt to return them cleanly
        if (playerPool != null && pooledPlayers.Count > 0)
        {
            // Iterate over a copy in case returning to the pool triggers callbacks
            // that modify the pooledPlayers collection.
            var pooled = new System.Collections.Generic.List<PhysicsGamePlayer>(pooledPlayers);
            foreach (var p in pooled)
            {
                try
                {
                    playerPool.Return(p.gameObject);
                }
                catch (Exception) { }
            }
            pooledPlayers.Clear();
        }

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

    // Note: Per-player events use OnPlayerLaunchedFor/OnPlayerLandedFor which are
    // subscribed dynamically in SubscribeToPlayerEvents. These parameterless
    // helpers were previously unused and have been removed.

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

            // Don't return previous pooled players here — we want to allow multiple
            // pooled instances on screen simultaneously. The individual instance will
            // be returned when it lands (per-player handlers below).

            // Place the newly spawned instance at the world position corresponding to the screen touch
            Vector3 spawnWorld = ScreenToWorld(screenPosition);
            // Use the no-expand variant so we never create extra instances beyond
            // what the pool currently contains. If the pool is depleted this will
            // return null and we will not start a new pull/launch.
            var go = playerPool.GetNoExpand(spawnWorld, Quaternion.identity);
            if (go != null)
            {
                var pg = go.GetComponent<PhysicsGamePlayer>();
                if (pg != null)
                {
                    player = pg;
                    Debug.Log($"[POOL] PlayerLauncher: obtained pooled player instance '{player.name}' from pool");
                    Log($"PlayerLauncher: obtained pooled player instance '{player.name}' from pool");
                    // Track that this instance came from the pool so it can be returned
                    // when it lands. Also create a dedicated interactor and per-instance
                    // event handlers so older players will remain and be cleaned up
                    // independently of newer ones.
                    pooledPlayers.Add(player);
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
        // Allow starting pull if there's an available player or the pool has available instances
        bool havePlayerReady = (player != null && !player.IsAirborne) || (player == null && playerPool != null && playerPool.AvailableCount > 0);
        return (havePlayerReady && !playerInFlight);
    }

    private void SubscribeToPlayerEvents(PhysicsGamePlayer p)
    {
        if (p == null) return;
        // Create and store per-instance delegates so we can unsubscribe precisely
        // for each player instance later. These will forward to per-player handlers
        // which are aware of the source instance.
        Action launchedHandler = () => OnPlayerLaunchedFor(p);
        Action landedHandler = () => OnPlayerLandedFor(p);
        p.Launched += launchedHandler;
        p.Landed += landedHandler;
        playerHandlers[p] = (launchedHandler, landedHandler);
    }

    private void UnsubscribeFromPlayerEvents(PhysicsGamePlayer p)
    {
        if (p == null) return;
        if (playerHandlers.TryGetValue(p, out var handlers))
        {
            p.Launched -= handlers.launched;
            p.Landed -= handlers.landed;
            playerHandlers.Remove(p);
        }
    }

    // Per-player landing handler so we can correctly return the landed instance
    // to the pool (if pooled) and only update the global player/interactor state
    // if this instance is the currently tracked `player`.
    private void OnPlayerLandedFor(PhysicsGamePlayer p)
    {
        // If it's pooled, return that particular instance.
        if (p != null && playerPool != null && pooledPlayers.Contains(p))
        {
            try
            {
                UnsubscribeFromPlayerEvents(p);
                playerPool.Return(p.gameObject);
                Log($"PlayerLauncher: returned pooled player '{p.name}' to pool on landing");
            }
            catch (Exception ex)
            {
                LogWarning($"PlayerLauncher: Failed to return pooled player '{p?.name}' to pool: {ex.Message}");
            }

            pooledPlayers.Remove(p);
        }

        // If this landed instance was the currently-selected player, clear the
        // PlayerLauncher references so another pull can be started.
        if (player == p)
        {
            player = null;
            playerInteractor = null;
            if (dragController != null)
                dragController.SetPlayerInteractor(null);

            // Return to Idle state
            if (idleState != null)
                ChangeState(idleState);
        }
    }

    // Per-player launched handler — only act if the launched player is the currently
    // selected one. Other players launching should not change the launcher's state.
    private void OnPlayerLaunchedFor(PhysicsGamePlayer p)
    {
        if (player != p) return;

        playerInFlight = true;
        if (dragController != null && dragController.IsFollowing)
            dragController.End();

        ChangeState(disabledState);
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

