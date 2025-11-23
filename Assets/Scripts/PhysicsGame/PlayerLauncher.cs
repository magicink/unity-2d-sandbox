using UnityEngine;
using UnityEngine.InputSystem;
using Sandbox.StateMachine;
using UnityEngine.InputSystem.EnhancedTouch;

public class PlayerLauncher : AbstractStateMachine<PlayerLauncher.LaunchState>
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

    [Header("Gizmos")]
    [Tooltip("Enable drawing touch gizmos in the Scene/Game view.")]
    [SerializeField] private bool drawTouchGizmos = true;
    [Tooltip("World-space radius of the gizmo circles representing touch start/end points.")]
    [SerializeField] private float gizmoRadius = 0.15f;

    [Header("Grounding")]
    [Tooltip("LayerMask used to detect ground objects. Default tries to use a layer named 'Ground'.")]
    [SerializeField] private LayerMask groundLayerMask = 0;
    [Tooltip("If true, a touch that starts over a ground object will not begin a pull/drag.")]
    [SerializeField] private bool preventStartOverGround = true;

    [Header("Debug")]
    [Tooltip("Enable informational debug logs for PlayerLauncher (touch starts, releases, etc.).")]
    [SerializeField] private bool enableDebugLogs = false;
    [Tooltip("Enable warning logs for PlayerLauncher (duplicate instances, missing references).")]
    [SerializeField] private bool enableWarningLogs = true;

    [Header("Slingshot")]
    [Tooltip("Enable slingshot-style launch when the user releases a drag.")]
    [SerializeField] private bool enableSlingshot = true;
    [Tooltip("Force multiplier applied to the slingshot vector (anchor - release).")]
    [SerializeField] private float slingshotForceMultiplier = 10f;
    [Tooltip("Minimum pull distance (world units) to consider a valid slingshot launch.")]
    [SerializeField] private float minSlingshotDistance = 0.05f;

    // Note: Gizmo fields and follow tracking were moved into DragController.
    // When the player is launched/in-flight the launcher should ignore input
    private bool playerInFlight = false;

    [Header("Provider Refs")]
    [Tooltip("Optional: an input provider instance. Automatically added if not provided.")]
    [SerializeField] private MonoBehaviour inputProviderComponent = null; // assign the component that implements IInputProvider (e.g., UnityInputProvider)
    [Tooltip("Optional: a physics-ground checker component. If unset, the configured layer mask is used.")]
    [SerializeField] private PhysicsGroundChecker groundCheckerMono = null;

    // New abstractions for refactor
    private IPlayerInteractor playerInteractor = null;
    private IGroundChecker groundChecker = null;
    private DragController dragController = null;
    private IInputProvider inputProvider = null;

    // State machine state instances (created in Awake)
    private IdleState idleState;
    private FollowingState followingState;
    private DisabledState disabledState;
    // legacy follow tracking removed; DragController handles follow lifecycle
    // NOTE: The follow lifecycle functions have been moved into FollowingState.

    private void OnEnable()
    {
        // Enforce singleton during enable - keep the first instance and destroy duplicates
        if (Instance != null && Instance != this)
        {
            LogWarning("PlayerLauncher: another instance already exists. Destroying duplicate component.");
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
        if (player != null)
        {
            player.Launched -= OnPlayerLaunched;
            player.Landed -= OnPlayerLanded;
        }
        if (inputProvider != null)
        {
            inputProvider.Begin -= OnInputBegin;
            inputProvider.Move -= OnInputMove;
            inputProvider.End -= OnInputEnd;
            inputProvider.Disable();
        }
    }

    private void Awake()
    {
        // Create states before base Start runs so GetInitialState can return a valid state
        idleState = new IdleState(this);
        followingState = new FollowingState(this);
        disabledState = new DisabledState(this);

        // If another instance is already set and is not this, destroy the duplicate.
        if (Instance != null && Instance != this)
        {
            LogWarning("PlayerLauncher.Awake: Detected duplicate PlayerLauncher. Destroying duplicate component.");
            Destroy(this);
            return;
        }
        Instance = this;
        // Try to auto-assign a default ground mask to the 'Ground' layer (if available)
        if (groundLayerMask == 0)
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer >= 0)
                groundLayerMask = 1 << groundLayer;
        }

        // Create or locate the input provider
        if (inputProvider == null)
        {
            if (inputProviderComponent != null)
                inputProvider = inputProviderComponent as IInputProvider;
            if (inputProvider == null)
            {
                var cmp = GetComponent<UnityInputProvider>();
                if (cmp != null) inputProvider = cmp;
            }
            if (inputProvider == null)
            {
                var created = gameObject.AddComponent<UnityInputProvider>();
                inputProvider = created;
                inputProviderComponent = created;
            }
        }

        // Ground checker: prefer a MonoBehaviour GroundChecker if attached; otherwise fall back to simple layer-masked checker
        if (groundCheckerMono != null)
            groundChecker = groundCheckerMono;
        else
            groundChecker = new SimpleGroundChecker(groundLayerMask);

        // Player interactor
        playerInteractor = new PhysicsGamePlayerInteractor(player);

        // Ensure we have a camera before converting screen coords
        if (worldCamera == null)
            worldCamera = Camera.main;

        // Create drag controller with config mapped from this launcher
        dragController = new DragController(playerInteractor, groundChecker, ScreenToWorld,
            preventStartOverGround, simulateTension, tensionStiffness, maxPullDistance,
            enableSlingshot, slingshotForceMultiplier, minSlingshotDistance);

        // Use controller-driven data; optionally log events for debugging
        dragController.DragStarted += (world) => Log($"Drag started at {world}");
        dragController.DragUpdated += (effective) => { /* no-op; PlayerLauncher uses DragController fields directly */ };
        dragController.DragEnded += (v) => Log($"Drag ended. Payload={v}");
        dragController.DragStartBlocked += (pos) =>
        {
            Log($"PlayerLauncher: blocked drag start over ground at {pos}");
        };
    }

    protected override LaunchState GetInitialState()
    {
        return idleState;
    }

    protected override void Start()
    {
        if (worldCamera == null)
            worldCamera = Camera.main;

        // Subscribe to player's Launched/Landed events so the launcher can disable itself while the player is in flight
        if (player != null)
        {
            player.Launched += OnPlayerLaunched;
            player.Landed += OnPlayerLanded;
            playerInFlight = player.IsAirborne;
        }
        // Subscribe to input provider events so they drive the state machine
        if (inputProvider != null)
        {
            inputProvider.Begin += OnInputBegin;
            inputProvider.Move += OnInputMove;
            inputProvider.End += OnInputEnd;
            inputProvider.Enable();
        }
        // Let the base state machine set the initial state
        base.Start();
        if (playerInFlight && disabledState != null)
            ChangeState(disabledState);
    }

    protected override void Update()
    {
        base.Update();
    }

    private void OnPlayerLaunched()
    {
        playerInFlight = true;
        // Enter a state where the launcher ignores input while the player is airborne.
        if (disabledState != null)
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
        ChangeState(followingState);
        followingState.Begin(screenPosition, touchId);
    }

    private void OnInputMove(Vector2 screenPosition, int touchId)
    {
        if (dragController != null && dragController.IsFollowing)
            followingState.Continue(screenPosition);
    }

    private void OnInputEnd(Vector2 screenPosition, int touchId)
    {
        followingState.End();
    }

    [Header("Tension")]
    [Tooltip("Enable simulated tension while pulling the player away from the start point.")]
    [SerializeField] private bool simulateTension = true;
    [Tooltip("Stiffness of tension; higher values make it harder to pull further.")]
    [SerializeField] private float tensionStiffness = 1f;
    [Tooltip("Maximum allowed raw pull distance in world units; input beyond this is clamped before computing tension.")]
    [SerializeField] private float maxPullDistance = 3f;

    // Tension calculation is now handled by DragController.

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

    // State machine base / skeleton for the player launcher. This is the first step
    // — it adds states and keeps the component behavior intact while enabling a
    // future refactor that moves logic into these states.
    public abstract class LaunchState : StateBase
    {
        protected readonly PlayerLauncher Machine;
        protected LaunchState(PlayerLauncher machine) { Machine = machine; }
    }

                
    public sealed class IdleState : LaunchState
    {
        public IdleState(PlayerLauncher machine) : base(machine) { }
        public override void Enter() { }
        public override void Tick(float deltaTime)
        {
            if (Machine.player == null) return;

            // If the player is airborne, do not start a new pull
            if (Machine.player.IsAirborne || Machine.playerInFlight) return;
            // Idle is now event-driven by IInputProvider; no input polling here
        }
        public override void Exit() { }
    }

    public sealed class FollowingState : LaunchState
    {
        public FollowingState(PlayerLauncher machine) : base(machine) { }
        public override void Enter() { }
        public override void Tick(float deltaTime)
        {
            // Following logic is driven by IInputProvider + DragController; no polling needed here.
            if (Machine.player == null) return;
        }
        
        // Begin following (moved from StartFollowingInternal)
        public void Begin(Vector2 screenPosition, int touchId)
        {
            if (Machine.player == null)
            {
                Machine.LogWarning("PlayerLauncher.BeginFollow: player reference is null.");
                return;
            }
            if (Machine.dragController != null)
            {
                Machine.dragController.Begin(screenPosition, touchId);
            }
        }

        // Continue following (moved from ContinueFollowingInternal)
        public void Continue(Vector2 screenPosition)
        {
            if (Machine.dragController != null)
            {
                Machine.dragController.Continue(screenPosition);
            }
        }

        // End following (moved from EndFollowingInternal)
        public void End(Vector3? overrideVelocity = null)
        {
            if (Machine.dragController != null)
            {
                Machine.dragController.End(null, overrideVelocity);
            }
        }
        public override void Exit() { }
    }

    public sealed class DisabledState : LaunchState
    {
        public DisabledState(PlayerLauncher machine) : base(machine) { }
        public override void Enter()
        {
            // Cancel any active follow when entering disabled state
            // DragController stops following automatically; nothing to clear here.
        }
        public override void Tick(float deltaTime) { /* ignore input while disabled */ }
        public override void Exit() { }
    }

    /// <summary>
    /// Determines if the given world position overlaps any collider on the configured ground layer mask.
    /// </summary>
    // Ground checks are now handled by IGroundChecker implementations (SimpleGroundChecker/PhysicsGroundChecker).
    }
