using UnityEngine;

public class DisabledState : LaunchState
{
    public DisabledState(PlayerLauncher machine) : base(machine) { }

    public override void Enter()
    {
        // Cancel any active follow when entering disabled state
        if (Machine.DragController != null)
            Machine.DragController.End();
    }
    public override void Tick(float deltaTime) { /* ignore input while disabled */ }
    public override void Exit() { }
}
