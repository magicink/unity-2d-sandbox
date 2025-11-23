using UnityEngine;

public class PhysicsGamePlayerInteractor : IPlayerInteractor
    {
    private readonly PhysicsGamePlayer player;
    public PhysicsGamePlayerInteractor(PhysicsGamePlayer player)
    {
        this.player = player;
    }

    public bool IsAirborne => player?.IsAirborne ?? false;
    public bool IsGrounded => player?.IsGrounded ?? false;

    public void BeginDragAt(Vector3 world) => player?.BeginDragAt(world);
    public void UpdateDragTarget(Vector3 world) => player?.UpdateDragTarget(world);

    public void EndDragAt(Vector3? launchVelocity) => player?.EndDragAt(launchVelocity);

    public void RespawnToWorldOrigin(Camera cam, float zDepth)
    {
        player?.RespawnToWorldOrigin(cam, zDepth);
    }
    }
