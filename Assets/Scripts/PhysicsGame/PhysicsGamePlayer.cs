using System;
using UnityEngine;
using Sandbox.StateMachine;

public class PhysicsGamePlayer : AbstractStateMachine<PhysicsGamePlayer.PhysicsState>
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

    // Drag state fields
    private Rigidbody2D rb2D;
    private bool isDragging = false;
    private Vector3 dragTarget = Vector3.zero;
    private Vector3 lastDragPosition = Vector3.zero;
    private float lastDragTime = 0f;
    private bool previousSimulatedState = true;
    private Vector3 followVelocity = Vector3.zero;

    private void Awake()
    {
        idleState = new IdleState(this);
        dragState = new DragState(this);

        TryGetComponent(out rb2D);
        if (rb2D != null)
            previousSimulatedState = rb2D.simulated;
    }

    // Update is called once per frame
    protected override void Update()
    {
        base.Update();

        // Check if the player is off-screen below the camera viewport and optionally respawn
        if (respawnIfBelowViewport && !isDragging)
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 viewportPos = cam.WorldToViewportPoint(transform.position);
                if (viewportPos.y < 0f + viewportBottomMargin)
                {
                    RespawnToViewportCenter(cam, viewportPos.z);
                }
            }
        }
    }

    private void RespawnToViewportCenter(Camera cam, float zDepth)
    {
        // Calculate world position at the center of the camera viewport using the object's depth
        Vector3 centerViewport = new Vector3(0.5f, 0.5f, zDepth);
        Vector3 spawnWorld = cam.ViewportToWorldPoint(centerViewport);

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
        }

        // Reset drag-related bookkeeping so the state machine has coherent values
        lastDragPosition = spawnWorld;
        lastDragTime = Time.time;
    }

    protected override PhysicsState GetInitialState()
    {
        return idleState;
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

        BeginDrag();
    }

    public void UpdateDragTarget(Vector3 worldPosition)
    {
        if (!isDragging) return;
        dragTarget = worldPosition;
    }

    public void EndDragAt()
    {
        if (!isDragging) return;
        isDragging = false;

        // compute throw velocity from last position/time
        Vector3 currentPos = transform.position;
        float dt = Time.time - lastDragTime;
        Vector3 velocity = Vector3.zero;
        if (dt > 0f)
            velocity = (currentPos - lastDragPosition) / dt * throwVelocityMultiplier;

        if (rb2D != null)
        {
            rb2D.simulated = previousSimulatedState;
#if UNITY_2023_1_OR_NEWER
            rb2D.linearVelocity = velocity;
#else
            rb2D.velocity = velocity;
#endif
        }

        EndDrag();
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
