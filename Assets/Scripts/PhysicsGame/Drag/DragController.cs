using System;
using UnityEngine;

public class DragController
{
    public event Action<Vector3> DragStarted;      // world start
    public event Action<Vector3> DragUpdated;      // world effective
    public event Action<Vector3?> DragEnded;       // optional launch velocity
    public event Action<Vector3> DragStartBlocked; // world start blocked by ground

    public bool IsFollowing { get; private set; }

    public Vector3 GizmoStartWorld { get; private set; }
    public Vector3 GizmoEndWorld { get; private set; }
    public bool HasGizmoStart { get; private set; }
    public bool HasGizmoEnd { get; private set; }
    public Vector3 LastFollowWorldPosition { get; private set; }
    public Vector3 RawFollowWorldPosition { get; private set; }

    private readonly IPlayerInteractor playerInteractor;
    private readonly IGroundChecker groundChecker;
    private readonly Func<Vector2, Vector3> screenToWorld;

    // Config
    private readonly bool preventStartOverGround;
    private readonly bool simulateTension;
    private readonly float tensionStiffness;
    private readonly float maxPullDistance;
    private readonly bool enableSlingshot;
    private readonly float slingshotForceMultiplier;
    private readonly float minSlingshotDistance;

    public bool EnableSlingshot => enableSlingshot;

    public DragController(
        IPlayerInteractor playerInteractor,
        IGroundChecker groundChecker,
        Func<Vector2, Vector3> screenToWorld,
        bool preventStartOverGround = true,
        bool simulateTension = true,
        float tensionStiffness = 1f,
        float maxPullDistance = 3f,
        bool enableSlingshot = true,
        float slingshotForceMultiplier = 10f,
        float minSlingshotDistance = 0.05f
    )
    {
        this.playerInteractor = playerInteractor;
        this.groundChecker = groundChecker;
        this.screenToWorld = screenToWorld;
        this.preventStartOverGround = preventStartOverGround;
        this.simulateTension = simulateTension;
        this.tensionStiffness = tensionStiffness;
        this.maxPullDistance = maxPullDistance;
        this.enableSlingshot = enableSlingshot;
        this.slingshotForceMultiplier = slingshotForceMultiplier;
        this.minSlingshotDistance = minSlingshotDistance;

        IsFollowing = false;
        HasGizmoStart = false;
        HasGizmoEnd = false;
    }

    private Vector3 ApplyTension(Vector3 rawWorld)
    {
        if (!simulateTension || !HasGizmoStart)
            return rawWorld;

        Vector3 dir = rawWorld - GizmoStartWorld;
        float distance = dir.magnitude;
        if (distance <= 0f) return rawWorld;

        float clamped = Mathf.Min(distance, Mathf.Max(0.0001f, maxPullDistance));

        float effectiveDistance = clamped;
        if (tensionStiffness > 0f)
        {
            effectiveDistance = clamped / (1f + tensionStiffness * clamped);
        }

        Vector3 dirNorm = dir / distance;
        return GizmoStartWorld + dirNorm * effectiveDistance;
    }

    public void Begin(Vector2 screenPosition, int touchId)
    {
        if (playerInteractor == null) return;

        Vector3 worldPos = screenToWorld(screenPosition);

        if (preventStartOverGround && groundChecker != null && groundChecker.IsOverGroundObject(worldPos))
        {
            DragStartBlocked?.Invoke(worldPos);
            return;
        }

        playerInteractor.BeginDragAt(worldPos);

        RawFollowWorldPosition = worldPos;
        Vector3 effectiveTarget = ApplyTension(RawFollowWorldPosition);
        LastFollowWorldPosition = effectiveTarget;
        playerInteractor.UpdateDragTarget(effectiveTarget);

        HasGizmoStart = true;
        HasGizmoEnd = false;
        GizmoStartWorld = worldPos;
        IsFollowing = true;

        DragStarted?.Invoke(worldPos);
    }

    public void Continue(Vector2 screenPosition)
    {
        if (!IsFollowing || playerInteractor == null) return;
        Vector3 rawWorld = screenToWorld(screenPosition);
        RawFollowWorldPosition = rawWorld;
        Vector3 effectiveWorld = ApplyTension(rawWorld);
        LastFollowWorldPosition = effectiveWorld;
        playerInteractor.UpdateDragTarget(effectiveWorld);
        DragUpdated?.Invoke(effectiveWorld);
    }

    public void End(Vector2? screenPosition = null, Vector3? overrideVelocity = null)
    {
        if (!IsFollowing) return;
        IsFollowing = false;

        Vector3 anchor = GizmoStartWorld;
        Vector3 release = RawFollowWorldPosition != Vector3.zero ? RawFollowWorldPosition : (playerInteractor != null ? Vector3.zero : Vector3.zero);

        bool releaseOverGround = false;
        try
        {
            if (playerInteractor.IsGrounded)
                releaseOverGround = true;
            else if (groundChecker != null)
                releaseOverGround = groundChecker.IsOverGroundObject(release);
        }
        catch (Exception)
        {
            releaseOverGround = false;
        }

        if (releaseOverGround)
        {
            Camera cam = Camera.main;
            float zDepth = 0f;
            if (cam != null)
                zDepth = Mathf.Abs(cam.transform.position.z - (playerInteractor != null ? 0f : cam.transform.position.z));

            playerInteractor.RespawnToWorldOrigin(cam, zDepth);
            playerInteractor.EndDragAt(Vector3.zero);

            HasGizmoEnd = true;
            GizmoEndWorld = Vector3.zero; // Player position unknown here
            RawFollowWorldPosition = Vector3.zero;
            DragEnded?.Invoke(Vector3.zero);
            return;
        }

        Vector3 launchVec = Vector3.zero;
        if (enableSlingshot)
        {
            Vector3 effective = simulateTension ? LastFollowWorldPosition : release;
            float pullDistance = (effective - anchor).magnitude;
            if (pullDistance >= minSlingshotDistance)
            {
                if (simulateTension)
                    launchVec = (anchor - effective) * slingshotForceMultiplier;
                else
                    launchVec = (anchor - release) * slingshotForceMultiplier;
            }
        }

        if (enableSlingshot && launchVec != Vector3.zero)
            playerInteractor.EndDragAt(launchVec);
        else
            playerInteractor.EndDragAt(overrideVelocity.HasValue ? overrideVelocity.Value : (Vector3?)null);

        DragEnded?.Invoke(launchVec != Vector3.zero ? (Vector3?)launchVec : null);
    }
    }
