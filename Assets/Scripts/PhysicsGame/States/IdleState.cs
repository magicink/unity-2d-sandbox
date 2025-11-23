using UnityEngine;

public class IdleState : LaunchState
{
    public IdleState(PlayerLauncher machine) : base(machine) { }
    public override void Enter() { }
    public override void Tick(float deltaTime)
    {
        // Guard: do not start new pull if player is airborne
        if (Machine.Player == null) return;
        if (!Machine.CanStartPull()) return;
        // Input now comes from IInputProvider; Idle state does not poll InputSystem directly
    }
    public override void Exit() { }
}
