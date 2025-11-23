using UnityEngine;

public class FollowingState : LaunchState
{
    public FollowingState(PlayerLauncher machine) : base(machine) { }
    public override void Enter() { }
    public override void Tick(float deltaTime)
    {
        if (Machine.Player == null) return;
        // Following logic handled by DragController and input events; no InputSystem polling here
    }

    public void Begin(Vector2 screenPosition, int touchId)
    {
        if (!Machine.CanStartPull()) return;
        Machine.BeginFollow(screenPosition, touchId);
    }

    public void Continue(Vector2 screenPosition)
    {
        Machine.ContinueFollow(screenPosition);
    }

    public void End(Vector3? overrideVelocity = null)
    {
        Machine.EndFollow(overrideVelocity);
    }

    public override void Exit() { }
}
