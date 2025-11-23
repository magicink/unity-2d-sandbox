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
        if (groundLayerMask == 0)
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer >= 0)
                groundLayerMask = 1 << groundLayer;
        }
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

        // If another instance is already set and is not this, destroy the duplicate. This guards against
        // duplicates in Awake order or when creating/destroying objects at runtime.
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("PlayerLauncher.Awake: Detected duplicate PlayerLauncher. Destroying duplicate component.", this);
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

    // Editor-only validation and auto-assignment done in top-level OnValidate to avoid duplicate definitions
    // Start is called once before the first execution of Update after the MonoBehaviour is created
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
        // Let the base state machine assign the initial state
        base.Start();
        // If the player is already airborne at start, enter DisabledState
        if (playerInFlight && disabledState != null)
            ChangeState(disabledState);
    }

    // Update is called once per frame
    protected override void Update()
    {
        // Let AbstractStateMachine run the current state's Tick.
        base.Update();
    }
    

    private void StartFollowingInternal(Vector2 screenPosition, int touchId)
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
        // Prevent beginning a pull if the touch began over a ground object (configurable)
        if (preventStartOverGround && IsOverGroundObject(worldPos))
        {
            Debug.Log($"PlayerLauncher.BeginFollow: blocked pull start because touch began over ground at {worldPos}", this);
            // reset follow state
            controllingTouchId = -2;
            isFollowing = false;
            return;
        }
        player?.BeginDragAt(worldPos);
        // Set both raw-follow and the initial effective-follow position during Begin
        rawFollowWorldPosition = worldPos;
        Vector3 effectiveTarget = ApplyTension(rawFollowWorldPosition);
        lastFollowWorldPosition = effectiveTarget;
        player?.UpdateDragTarget(effectiveTarget);

        // Capture the start location for debugging gizmos
        hasGizmoStart = true;
        hasGizmoEnd = false;
        gizmoStartWorld = worldPos;
        // lastFollowWorldPosition is already set to the effective target; do not overwrite with the raw worldPos
        // Enter the Following state so the state machine takes over updates
        ChangeState(followingState);
    }

    private void ContinueFollowingInternal(Vector2 screenPosition)
    {
        if (!isFollowing || player == null)
            return;
        Vector3 rawWorld = ScreenToWorld(screenPosition);
        rawFollowWorldPosition = rawWorld;
        Vector3 effectiveWorld = ApplyTension(rawWorld);
        lastFollowWorldPosition = effectiveWorld;
        player?.UpdateDragTarget(effectiveWorld);
    }

    private void EndFollowingInternal(Vector3? overrideVelocity = null)
    {
        if (!isFollowing)
            return;

        // Save the id to be logged before clearing it
        int previousId = controllingTouchId;

        isFollowing = false;
        controllingTouchId = -2;

        Debug.Log($"PlayerLauncher.EndFollow: Touch ended: id={previousId}");
        // Notify the player's state machine that the drag/follow has ended
        Debug.Log("PlayerLauncher.EndFollow: calling player.EndDragAt()", this);
        // Compute slingshot velocity if enabled
        if (player != null)
        {
            Vector3 anchor = gizmoStartWorld;
            Vector3 release = rawFollowWorldPosition != Vector3.zero ? rawFollowWorldPosition : (player != null ? player.transform.position : Vector3.zero);

            // If the player is grounded or release happened over a ground object, just reset to the origin
            bool releaseOverGround = false;
            try
            {
                if (player.IsGrounded)
                    releaseOverGround = true;
                else
                    releaseOverGround = IsOverGroundObject(release);
            }
            catch (System.Exception)
            {
                // In case player is null or IsGrounded throws, default to false
                releaseOverGround = false;
            }
                if (releaseOverGround)
            {
                Camera cam = worldCamera != null ? worldCamera : Camera.main;
                float zDepth = 0f;
                if (cam != null && player != null)
                    zDepth = Mathf.Abs(cam.transform.position.z - player.transform.position.z);

                // Reset player position to origin and clear velocities
                player.RespawnToWorldOrigin(cam, zDepth);
                player.EndDragAt(Vector3.zero);

                // Update gizmo end/clear rawFollow and exit early to avoid slingshot computation
                gizmoEndWorld = player.transform.position;
                hasGizmoEnd = true;
                rawFollowWorldPosition = Vector3.zero;
                // After respawn, go back to idle state so input can begin again
                ChangeState(idleState);
                return;
            }

            // Compute slingshot vector (if enabled). Use the effective target when using simulated tension
            Vector3 launchVec = Vector3.zero;
            if (enableSlingshot)
            {
                Vector3 effective = simulateTension ? lastFollowWorldPosition : release;
                float pullDistance = (effective - anchor).magnitude;
                if (pullDistance >= minSlingshotDistance)
                {
                    if (simulateTension)
                    {
                        // Use the effective target so that tension reduces the resulting velocity
                        launchVec = (anchor - effective) * slingshotForceMultiplier;
                    }
                    else
                    {
                        // Use raw input for direct slingshot force
                        launchVec = (anchor - release) * slingshotForceMultiplier;
                    }
                }
            }

            if (enableSlingshot && launchVec != Vector3.zero)
                player?.EndDragAt(launchVec);
            else
                player?.EndDragAt();

            }

            // Transition states depending on whether the player is airborne after release
            if (player != null && player.IsAirborne)
            {
                // The player will be in flight — disable input until landed
                ChangeState(disabledState);
            }
            else
            {
                ChangeState(idleState);
            }

        // Capture the final location for debugging gizmos
        gizmoEndWorld = lastFollowWorldPosition != Vector3.zero ? lastFollowWorldPosition : (player != null ? player.transform.position : Vector3.zero);
        hasGizmoEnd = true;
        // clear raw follow so future touches don't reuse stale data
        rawFollowWorldPosition = Vector3.zero;
        // odd but safe: set hasGizmoStart false so next Begin cleans it
        // keep hasGizmoStart true so the start dot remains visible unless Begin sets it otherwise
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
            Debug.LogWarning("PlayerLauncher.ScreenToWorld: No camera found. Using default ScreenToWorldPoint with z=0.");
            return new Vector3(screenPosition.x, screenPosition.y, 0f);
        }

        // Calculate the z distance from camera to player to provide a proper depth for ScreenToWorldPoint
        float z = Mathf.Abs(cam.transform.position.z - (player != null ? player.transform.position.z : 0f));
        Vector3 sp = new Vector3(screenPosition.x, screenPosition.y, z);
        return cam.ScreenToWorldPoint(sp);
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
                        Machine.StartFollowingInternal(t.screenPosition, (int)t.touchId);
                        return;
                    }
                }
            }
            else
            {
                var mouse = Mouse.current;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                {
                    Machine.StartFollowingInternal(mouse.position.ReadValue(), -1);
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
                            Machine.ContinueFollowingInternal(t.screenPosition);
                        }
                        else if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended || t.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                        {
                            Machine.EndFollowingInternal();
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
                        Machine.EndFollowingInternal();
                    else if (mouse.leftButton.isPressed)
                        Machine.ContinueFollowingInternal(mouse.position.ReadValue());
                }
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
