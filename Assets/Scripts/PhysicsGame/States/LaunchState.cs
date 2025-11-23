using Sandbox.StateMachine;

public abstract class LaunchState : StateBase
{
    protected readonly PlayerLauncher Machine;
    protected LaunchState(PlayerLauncher machine) { Machine = machine; }
}
