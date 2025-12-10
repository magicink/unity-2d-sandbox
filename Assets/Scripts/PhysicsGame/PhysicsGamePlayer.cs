using System;
using UnityEngine;
using Sandbox.StateMachine;
using Sandbox.Pooling;

public class PhysicsGamePlayer : AbstractStateMachine<PhysicsGamePlayer.PhysicsState>
    , InstancePool.IPooledInstance
{
    private IdleState idleState;
    private DragState dragState;

    [Header("Drag Settings")]
    [Tooltip("Smoothing time used when following a drag target (0 = instant)")]
    [SerializeField] private float followSmoothing = 0f;
    [Tooltip("Multiplier applied to the computed release velocity when physics is re-enabled")]
    [SerializeField] private float throwVelocityMultiplier = 1f;

    [Header("Respawn Settings")]
    [Tooltip("Enable respawning the player when it falls below the bottom of the camera viewport.")]
    [SerializeField] private bool respawnIfBelowViewport = true;
    [Tooltip("Extra vertical margin in viewport space to consider a fall (negative values respawn sooner).")]
    [SerializeField] private float viewportBottomMargin = 0f;

    [Header("Grounding")]
    [Tooltip("If true, landing requires the player to be in contact with a ground object (layered with 'Ground' by default).")]
    [SerializeField] private bool requireContactToLand = true;
    [Tooltip("LayerMask used to detect ground objects. Default tries to use a layer named 'Ground'.")]
    [SerializeField] private LayerMask groundLayerMask = 0;

    // Drag state fields
    private Rigidbody2D rb2D;
    private bool isDragging;
    private Vector3 dragTarget = Vector3.zero;
    // World-space X coordinate where the drag started. Used to detect "pulled to the right" cases.
    private float dragStartWorldX = 0f;
    private Vector3 lastDragPosition = Vector3.zero;
    private float lastDragTime = 0f;
    private bool previousSimulatedState = true;
    private Vector3 followVelocity = Vector3.zero;
    // Launch/airborne detection events
    public event Action Launched;
    public event Action Landed;
    private bool isAirborne;
    private int groundContactCount = 0;
    public bool IsGrounded => groundContactCount > 0;
    public bool IsAirborne => isAirborne;
    [Tooltip("Minimum speed under which the player is considered to have landed (world units/sec).")]
    [SerializeField] private float landingSpeedThreshold = 0.1f;
    [Header("Drag Behavior")]
    [Tooltip("If true, always re-enable Rigidbody2D.simulated when drag ends. Useful if you want the player to resume physics regardless of previous simulated state.")]
    [SerializeField] private bool forceReenableSimulatedOnEnd = true;

    private void Awake()
    {
        idleState = new IdleState(this);
        dragState = new DragState(this);

        TryGetComponent(out rb2D);
        if (rb2D != null)
            previousSimulatedState = rb2D.simulated;

        // If groundLayerMask is unset, try to auto-assign a layer named 'Ground'
        if (groundLayerMask == 0)
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer >= 0)
                groundLayerMask = 1 << groundLayer;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (groundLayerMask == 0)
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer >= 0)
                groundLayerMask = 1 << groundLayer;
        }
    }
#endif

    // Update is called once per frame
    protected override void Update()
    {
        base.Update();

        // NOTE: automatic respawn when falling below the camera viewport was removed
        // to avoid resetting the player's position to the world origin. Any manual
        // respawn behavior should be explicitly invoked (e.g., via an interactor
        // or external manager) instead of automatic on-update resets.
    }

    public void RespawnToWorldOrigin(Camera cam, float zDepth)
    {
        // Respawn the player at world origin (0,0), keeping the same z-depth so layering remains correct.
        Vector3 spawnWorld = new Vector3(0f, 0f, zDepth);

        // Move the transform and reset velocities. Use the Rigidbody2D if available.
        transform.position = spawnWorld;
        if (rb2D != null)
        {
            rb2D.position = spawnWorld;
#if UNITY_2023_1_OR_NEWER
            rb2D.linearVelocity = Vector2.zero;
#else
            rb2D.velocity = Vector2.zero;
#endif
            rb2D.angularVelocity = 0f;
            // If we set a notable velocity, mark player as launched/in-air
            float sqr = rb2D.linearVelocity.sqrMagnitude;
            if (sqr > landingSpeedThreshold * landingSpeedThreshold)
            {
                isAirborne = true;
                Launched?.Invoke();
            }
        }

        // Reset drag-related bookkeeping so the state machine has coherent values
        lastDragPosition = spawnWorld;
        lastDragTime = Time.time;
        // clear any contact state because we're moving the player
        groundContactCount = 0;
    }

    // InstancePool.IPooledInstance - lifecycle hooks used when this player is pooled
    public void OnCreatedForPool(InstancePool pool)
    {
        // nothing special to do on creation for now
    }

    public void OnTakenFromPool()
    {
        // Prepare the player for immediate use: stop physics and clear airborne/contact
        isDragging = false;
        isAirborne = false;
        groundContactCount = 0;
        followVelocity = Vector3.zero;
        lastDragPosition = transform.position;
        lastDragTime = Time.time;

        if (rb2D != null)
        {
            rb2D.simulated = false;
#if UNITY_2023_1_OR_NEWER
            rb2D.linearVelocity = Vector2.zero;
#else
            rb2D.velocity = Vector2.zero;
#endif
            rb2D.angularVelocity = 0f;
        }
    }

    public void OnReturnedToPool()
    {
        // When returned, ensure the player's simulation is disabled and velocities cleared
        isDragging = false;
        isAirborne = false;
        groundContactCount = 0;

        if (rb2D != null)
        {
#if UNITY_2023_1_OR_NEWER
            rb2D.linearVelocity = Vector2.zero;
#else
            rb2D.velocity = Vector2.zero;
#endif
            rb2D.angularVelocity = 0f;
            rb2D.simulated = false;
        }
    }

    protected override PhysicsState GetInitialState()
    {
        return idleState;
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate();

        // If airborne, check for landing condition (velocity low enough => landed)
        if (rb2D != null && isAirborne && rb2D.simulated)
        {
            float speedSqr = rb2D.linearVelocity.sqrMagnitude;
            bool belowSpeed = speedSqr < landingSpeedThreshold * landingSpeedThreshold;
            bool contactOk = !requireContactToLand || IsGrounded;
            if (belowSpeed && contactOk)
            {
                isAirborne = false;
                Landed?.Invoke();
            }
        }
    }

    // Simple API to change states from other objects (like PlayerLauncher)
    public void BeginDrag() => ChangeState(dragState);
    public void EndDrag() => ChangeState(idleState);

    // API used by PlayerLauncher: begin updating the player using a world-space target
    public void BeginDragAt(Vector3 worldPosition)
    {
        isDragging = true;
        dragTarget = worldPosition;
        lastDragPosition = transform.position;
        // remember where the drag started in world-space X so EndDragAt can detect a rightward pull
        dragStartWorldX = transform.position.x;
        lastDragTime = Time.time;

        if (rb2D != null)
        {
            previousSimulatedState = rb2D.simulated;
            rb2D.simulated = false;
#if UNITY_2023_1_OR_NEWER
            rb2D.linearVelocity = Vector2.zero;
#else
            rb2D.velocity = Vector2.zero;
#endif
            rb2D.angularVelocity = 0f;
        }

        // During drag we are actively controlled and not considered airborne
        isAirborne = false;

        Debug.Log($"PhysicsGamePlayer.BeginDragAt: player={name} startPos={transform.position} target={worldPosition}");

        BeginDrag();
    }

    public void UpdateDragTarget(Vector3 worldPosition)
    {
        if (!isDragging) return;
        Debug.Log($"PhysicsGamePlayer.UpdateDragTarget: player={name} transform={transform.position} newTarget={worldPosition}");
        dragTarget = worldPosition;
    }

    public void EndDragAt(Vector3? overrideVelocity = null)
    {
        if (!isDragging) return;
        isDragging = false;

        // compute throw velocity from last position/time unless an override velocity was provided
        Vector3 currentPos = transform.position;
        float dt = Time.time - lastDragTime;
        Vector3 velocity = Vector3.zero;
        if (dt > 0f)
            velocity = (currentPos - lastDragPosition) / dt * throwVelocityMultiplier;

        if (overrideVelocity.HasValue)
        {
            velocity = overrideVelocity.Value;
        }

        // If the player was dragged to the right of the original drag start point, we should
        // not apply any launch force and instead let the player just drop straight down.
        // Detect this by comparing final position against the saved drag start X.
        // We zero the computed/override velocity so no horizontal or vertical impulse is applied.
        if (currentPos.x > dragStartWorldX)
        {
            velocity.x = 0f;
            velocity.y = 0f;
        }

        if (rb2D != null)
        {
            Debug.Log($"PhysicsGamePlayer.EndDragAt: previousSimulatedState={previousSimulatedState} rb2D.simulated(before)={rb2D.simulated}");
            // Re-enable simulation either by force or per saved state
            rb2D.simulated = forceReenableSimulatedOnEnd ? true : previousSimulatedState;
#if UNITY_2023_1_OR_NEWER
            rb2D.linearVelocity = velocity;
            Debug.Log($"PhysicsGamePlayer.EndDragAt: rb2D.simulated(after)={rb2D.simulated} linear/velocity= {rb2D.linearVelocity}");
#else
            rb2D.velocity = velocity;
            Debug.Log($"PhysicsGamePlayer.EndDragAt: rb2D.simulated(after)={rb2D.simulated} velocity= {rb2D.velocity}");
#endif
            rb2D.angularVelocity = 0f;
        }

        // If we just re-enabled simulation with a non-trivial velocity, mark the player launched
        if (rb2D != null)
        {
            float sqr = velocity.sqrMagnitude;
            if (sqr > landingSpeedThreshold * landingSpeedThreshold)
            {
                isAirborne = true;
                Launched?.Invoke();
            }
            else
            {
                // If velocity is small, consider the contact state: if we're touching ground or we don't require contact, mark landed
                if (!requireContactToLand || IsGrounded)
                {
                    isAirborne = false;
                    Landed?.Invoke();
                }
                else
                {
                    // Small velocity but not touching ground - remain airborne
                    isAirborne = true;
                }
            }
        }

        EndDrag();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision == null) return;
        var go = collision.gameObject;
        if (go != null && IsGroundObject(go))
        {
            groundContactCount++;
            // if we are airborne and we meet landing criteria, mark landed
            if (isAirborne && rb2D != null)
            {
                float speedSqr = rb2D.linearVelocity.sqrMagnitude;
                if (!requireContactToLand || speedSqr < landingSpeedThreshold * landingSpeedThreshold)
                {
                    isAirborne = false;
                    Landed?.Invoke();
                }
            }
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision == null) return;
        var go = collision.gameObject;
        if (go != null && IsGroundObject(go))
        {
            groundContactCount = Mathf.Max(0, groundContactCount - 1);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null) return;
        var go = other.gameObject;
        if (go != null && IsGroundObject(go))
        {
            groundContactCount++;
            if (isAirborne && rb2D != null)
            {
                float speedSqr = rb2D.linearVelocity.sqrMagnitude;
                if (!requireContactToLand || speedSqr < landingSpeedThreshold * landingSpeedThreshold)
                {
                    isAirborne = false;
                    Landed?.Invoke();
                }
            }
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other == null) return;
        var go = other.gameObject;
        if (go != null && IsGroundObject(go))
        {
            groundContactCount = Mathf.Max(0, groundContactCount - 1);
        }
    }

    /// <summary>
    /// Determines whether the specified GameObject counts as ground using the configured LayerMask or tag.
    /// </summary>
    private bool IsGroundObject(GameObject go)
    {
        if (go == null) return false;
        if (groundLayerMask != 0)
        {
            if ((groundLayerMask.value & (1 << go.layer)) != 0)
                return true;
        }
        return false;
    }

    // Base state type for this player's state machine
    public abstract class PhysicsState : StateBase
    {
        protected readonly PhysicsGamePlayer Machine;
        protected PhysicsState(PhysicsGamePlayer machine) { Machine = machine; }
    }

    public sealed class IdleState : PhysicsState
    {
        public IdleState(PhysicsGamePlayer machine) : base(machine) { }
        public override void Enter() { }
        public override void Tick(float deltaTime) { }
    }

    public sealed class DragState : PhysicsState
    {
        public DragState(PhysicsGamePlayer machine) : base(machine) { }
        public override void Enter() { }
        public override void Tick(float deltaTime)
        {
            if (!Machine.isDragging) return;

            // Keep old values to compute velocity if needed
            float now = Time.time;

            if (Machine.followSmoothing <= 0f)
            {
                Machine.transform.position = Machine.dragTarget;
            }
            else
            {
                Vector3 newPos = Vector3.SmoothDamp(Machine.transform.position, Machine.dragTarget, ref Machine.followVelocity, Machine.followSmoothing);
                Machine.transform.position = newPos;
            }

            // Update last drag position/time AFTER moving so EndDragAt computes velocity over the last frame
            Machine.lastDragPosition = Machine.transform.position;
            Machine.lastDragTime = now;
        }
    }
}
