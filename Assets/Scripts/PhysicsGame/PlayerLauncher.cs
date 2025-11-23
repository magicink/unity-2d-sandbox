using System;
using UnityEngine;
using Sandbox.StateMachine;

/// <summary>
/// Coordinates player drag/launch flow by wiring an InputProvider, GroundChecker,
/// PlayerInteractor and DragController together. States are implemented in separate files.
/// </summary>
public class PlayerLauncher : AbstractStateMachine<LaunchState>
{
    [Header("References")]
    [SerializeField] private PhysicsGamePlayer player = null;
    [SerializeField] private MonoBehaviour inputProviderComponent = null; // assign a UnityInputProvider or other IInputProvider implementation
    [SerializeField] private PhysicsGroundChecker physicsGroundChecker = null;
    [SerializeField] private LayerMask groundLayerMask = 0;
    [SerializeField] private Camera worldCamera = null;

    [Header("Debug & Gizmos")]
    [SerializeField] private bool drawTouchGizmos = true;
    [SerializeField] private float gizmoRadius = 0.1f;
    [SerializeField] private bool enableDebugLogs = false;
    [SerializeField] private bool enableWarningLogs = true;

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

        // Build player interactor for the configured player
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

        // Optional: subscribe to drag events for gizmo visibility or logging
        dragController.DragStartBlocked += (pos) => {
            Log($"Drag start blocked at {pos}");
        };
    }

    private void OnEnable()
    {
        // Wire Player events
        if (player != null)
        {
            player.Landed += OnPlayerLanded;
            player.Launched += OnPlayerLaunched;
        }

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
    }

    private void OnDisable()
    {
        if (player != null)
        {
            player.Landed -= OnPlayerLanded;
            player.Launched -= OnPlayerLaunched;
        }

        if (inputProvider != null)
        {
            inputProvider.Begin -= OnInputBegin;
            inputProvider.Move -= OnInputMove;
            inputProvider.End -= OnInputEnd;
        }
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
        // Return to Idle state after landing
        if (idleState != null)
            ChangeState(idleState);
    }

    private void OnInputBegin(Vector2 screenPosition, int touchId)
    {
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
        return (player != null && !player.IsAirborne && !playerInFlight);
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

