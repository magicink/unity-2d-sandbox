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

    // Gizmo storage for touch start and end locations
    private Vector3 gizmoStartWorld = Vector3.zero;
    private Vector3 gizmoEndWorld = Vector3.zero;
    private Vector3 lastFollowWorldPosition = Vector3.zero;
    private bool hasGizmoStart = false;
    private bool hasGizmoEnd = false;
    private Vector3 rawFollowWorldPosition = Vector3.zero;
    // When the player is launched/in-flight the launcher should ignore input
    private bool playerInFlight = false;

    // State machine state instances (created in Awake)
    private IdleState idleState;
    private FollowingState followingState;
    private DisabledState disabledState;
    private bool isFollowing = false;
    private int controllingTouchId = -2; // -2 == not assigned
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

    [Header("Tension")]
    [Tooltip("Enable simulated tension while pulling the player away from the start point.")]
    [SerializeField] private bool simulateTension = true;
    [Tooltip("Stiffness of tension; higher values make it harder to pull further.")]
    [SerializeField] private float tensionStiffness = 1f;
    [Tooltip("Maximum allowed raw pull distance in world units; input beyond this is clamped before computing tension.")]
    [SerializeField] private float maxPullDistance = 3f;

    private Vector3 ApplyTension(Vector3 rawWorld)
    {
        if (!simulateTension || !hasGizmoStart)
            return rawWorld;

        Vector3 dir = rawWorld - gizmoStartWorld;
        float distance = dir.magnitude;
        if (distance <= 0f) return rawWorld;

        float clamped = Mathf.Min(distance, Mathf.Max(0.0001f, maxPullDistance));

        float effectiveDistance = clamped;
        if (tensionStiffness > 0f)
        {
            // Non-linear diminishing return: effective distance = d / (1 + k * d)
            effectiveDistance = clamped / (1f + tensionStiffness * clamped);
        }

        Vector3 dirNorm = dir / distance;
        return gizmoStartWorld + dirNorm * effectiveDistance;
    }

    private void OnDrawGizmos()
    {
        if (!drawTouchGizmos) return;

        // Draw start point
        if (hasGizmoStart)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(gizmoStartWorld, gizmoRadius);
        }

        // Draw end point
        if (hasGizmoEnd)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(gizmoEndWorld, gizmoRadius);
        }

        // Draw raw pointer/drag location in blue so that you can see the user's input vs the tensioned output
        if (rawFollowWorldPosition != Vector3.zero && isFollowing)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(rawFollowWorldPosition, gizmoRadius * 0.6f);
        }

        // Draw line between start and end (if end exists), otherwise line to current follow position
        if (hasGizmoStart && hasGizmoEnd)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(gizmoStartWorld, gizmoEndWorld);
        }
        else if (hasGizmoStart && isFollowing)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(gizmoStartWorld, lastFollowWorldPosition);
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

            if (UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0)
            {
                foreach (var t in UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches)
                {
                        if (t.phase == UnityEngine.InputSystem.TouchPhase.Began)
                        {
                            Machine.ChangeState(Machine.followingState);
                            Machine.followingState.Begin(t.screenPosition, (int)t.touchId);
                            return;
                        }
                }
            }
            else
            {
                var mouse = Mouse.current;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                {
                    Machine.ChangeState(Machine.followingState);
                    Machine.followingState.Begin(mouse.position.ReadValue(), -1);
                }
            }
        }
        public override void Exit() { }
    }

    public sealed class FollowingState : LaunchState
    {
        public FollowingState(PlayerLauncher machine) : base(machine) { }
        public override void Enter() { }
        public override void Tick(float deltaTime)
        {
            if (!Machine.isFollowing || Machine.player == null) return;

            if (UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0)
            {
                foreach (var t in UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches)
                {
                        if ((int)t.touchId == Machine.controllingTouchId)
                    {
                        if (t.phase == UnityEngine.InputSystem.TouchPhase.Moved || t.phase == UnityEngine.InputSystem.TouchPhase.Stationary)
                        {
                            Machine.followingState.Continue(t.screenPosition);
                        }
                        else if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended || t.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                        {
                            Machine.followingState.End();
                        }
                        return;
                    }
                }
            }
            else
            {
                var mouse = Mouse.current;
                if (mouse != null)
                {
                    if (mouse.leftButton.wasReleasedThisFrame)
                        Machine.followingState.End();
                    else if (mouse.leftButton.isPressed)
                        Machine.followingState.Continue(mouse.position.ReadValue());
                }
            }
        }
        
        // Begin following (moved from StartFollowingInternal)
        public void Begin(Vector2 screenPosition, int touchId)
        {
            if (Machine.player == null)
            {
                Machine.LogWarning("PlayerLauncher.BeginFollow: player reference is null.");
                return;
            }

            Machine.controllingTouchId = touchId;
            Machine.isFollowing = true;

            Machine.Log($"Touch started: id={touchId} position={screenPosition}");

            var worldPos = Machine.ScreenToWorld(screenPosition);
            if (Machine.preventStartOverGround && Machine.IsOverGroundObject(worldPos))
            {
                Machine.Log($"PlayerLauncher.BeginFollow: blocked pull start because touch began over ground at {worldPos}");
                Machine.controllingTouchId = -2;
                Machine.isFollowing = false;
                return;
            }

            Machine.player?.BeginDragAt(worldPos);
            Machine.rawFollowWorldPosition = worldPos;
            Vector3 effectiveTarget = Machine.ApplyTension(Machine.rawFollowWorldPosition);
            Machine.lastFollowWorldPosition = effectiveTarget;
            Machine.player?.UpdateDragTarget(effectiveTarget);

            Machine.hasGizmoStart = true;
            Machine.hasGizmoEnd = false;
            Machine.gizmoStartWorld = worldPos;
        }

        // Continue following (moved from ContinueFollowingInternal)
        public void Continue(Vector2 screenPosition)
        {
            if (!Machine.isFollowing || Machine.player == null) return;
            Vector3 rawWorld = Machine.ScreenToWorld(screenPosition);
            Machine.rawFollowWorldPosition = rawWorld;
            Vector3 effectiveWorld = Machine.ApplyTension(rawWorld);
            Machine.lastFollowWorldPosition = effectiveWorld;
            Machine.player?.UpdateDragTarget(effectiveWorld);
        }

        // End following (moved from EndFollowingInternal)
        public void End(Vector3? overrideVelocity = null)
        {
            if (!Machine.isFollowing) return;
            int previousId = Machine.controllingTouchId;
            Machine.isFollowing = false;
            Machine.controllingTouchId = -2;

            Machine.Log($"PlayerLauncher.EndFollow: Touch ended: id={previousId}");
            Machine.Log("PlayerLauncher.EndFollow: calling player.EndDragAt()");

            if (Machine.player != null)
            {
                Vector3 anchor = Machine.gizmoStartWorld;
                Vector3 release = Machine.rawFollowWorldPosition != Vector3.zero ? Machine.rawFollowWorldPosition : Machine.player.transform.position;

                bool releaseOverGround = false;
                try
                {
                    if (Machine.player.IsGrounded)
                        releaseOverGround = true;
                    else
                        releaseOverGround = Machine.IsOverGroundObject(release);
                }
                catch (System.Exception)
                {
                    releaseOverGround = false;
                }
                if (releaseOverGround)
                {
                    Camera cam = Machine.worldCamera != null ? Machine.worldCamera : Camera.main;
                    float zDepth = 0f;
                    if (cam != null && Machine.player != null)
                        zDepth = Mathf.Abs(cam.transform.position.z - Machine.player.transform.position.z);

                    Machine.player.RespawnToWorldOrigin(cam, zDepth);
                    Machine.player.EndDragAt(Vector3.zero);

                    Machine.gizmoEndWorld = Machine.player.transform.position;
                    Machine.hasGizmoEnd = true;
                    Machine.rawFollowWorldPosition = Vector3.zero;
                    Machine.ChangeState(Machine.idleState);
                    return;
                }

                Vector3 launchVec = Vector3.zero;
                if (Machine.enableSlingshot)
                {
                    Vector3 effective = Machine.simulateTension ? Machine.lastFollowWorldPosition : release;
                    float pullDistance = (effective - anchor).magnitude;
                    if (pullDistance >= Machine.minSlingshotDistance)
                    {
                        if (Machine.simulateTension)
                        {
                            launchVec = (anchor - effective) * Machine.slingshotForceMultiplier;
                        }
                        else
                        {
                            launchVec = (anchor - release) * Machine.slingshotForceMultiplier;
                        }
                    }
                }

                if (Machine.enableSlingshot && launchVec != Vector3.zero)
                    Machine.player?.EndDragAt(launchVec);
                else
                    Machine.player?.EndDragAt(overrideVelocity.HasValue ? overrideVelocity.Value : (Vector3?)null);

                if (Machine.player != null && Machine.player.IsAirborne)
                    Machine.ChangeState(Machine.disabledState);
                else
                    Machine.ChangeState(Machine.idleState);
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
            Machine.isFollowing = false;
            Machine.controllingTouchId = -2;
        }
        public override void Tick(float deltaTime) { /* ignore input while disabled */ }
        public override void Exit() { }
    }

    /// <summary>
    /// Determines if the given world position overlaps any collider on the configured ground layer mask.
    /// </summary>
    private bool IsOverGroundObject(Vector3 worldPosition)
    {
        if (groundLayerMask == 0)
            return false;
        Vector2 point = new Vector2(worldPosition.x, worldPosition.y);
        Collider2D c = Physics2D.OverlapPoint(point, groundLayerMask.value);
        return c != null;
    }
}
