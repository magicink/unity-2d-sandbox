using UnityEngine;

public interface IPlayerInteractor
{
    void BeginDragAt(Vector3 world);
    void UpdateDragTarget(Vector3 world);
    void EndDragAt(Vector3? launchVelocity);
    void RespawnToWorldOrigin(Camera cam, float zDepth);
    bool IsAirborne { get; }
    bool IsGrounded { get; }
    }
