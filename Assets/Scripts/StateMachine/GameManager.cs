using System;
using UnityEngine;
using UnityEngine.Events;

namespace Sandbox.StateMachine
{
    public class GameManager : AbstractStateMachine<GameManager.GameStateBase>
    {
        [SerializeField] private GamePhase initialPhase = GamePhase.Active;
        [SerializeField] private bool manageTimeScale = true;
        [SerializeField] private float idleTimeScale = 0f;
        [SerializeField] private float activeTimeScale = 1f;
        [SerializeField] private float pausedTimeScale = 0f;
        [SerializeField] private float gameOverTimeScale = 0f;

        [Header("Phase Events")]
        [SerializeField] private PhaseEventSet idleEvents = new PhaseEventSet();
        [SerializeField] private PhaseEventSet activeEvents = new PhaseEventSet();
        [SerializeField] private PhaseEventSet pausedEvents = new PhaseEventSet();
        [SerializeField] private PhaseEventSet gameOverEvents = new PhaseEventSet();
        [SerializeField] private PhaseTransitionEvent onPhaseChanged = new PhaseTransitionEvent();

        public GamePhase CurrentPhase { get; private set; } = GamePhase.Idle;

        private IdleState idleState;
        private ActiveState activeState;
        private PausedState pausedState;
        private GameOverState gameOverState;

        protected virtual void Awake()
        {
            idleState = new IdleState(this);
            activeState = new ActiveState(this);
            pausedState = new PausedState(this);
            gameOverState = new GameOverState(this);
        }

        protected override GameStateBase GetInitialState()
        {
            return initialPhase switch
            {
                GamePhase.Active => activeState,
                GamePhase.Paused => pausedState,
                GamePhase.GameOver => gameOverState,
                _ => idleState,
            };
        }

        public bool IsIdle => ReferenceEquals(CurrentState, idleState);
        public bool IsGameActive => ReferenceEquals(CurrentState, activeState);
        public bool IsPaused => ReferenceEquals(CurrentState, pausedState);
        public bool IsGameOver => ReferenceEquals(CurrentState, gameOverState);

        public void StartGame() => ChangeState(activeState);

        public void PauseGame()
        {
            if (!IsGameActive)
            {
                return;
            }

            ChangeState(pausedState);
        }

        public void ResumeGame()
        {
            if (!IsPaused)
            {
                return;
            }

            ChangeState(activeState);
        }

        public void TogglePause()
        {
            if (IsPaused)
            {
                ResumeGame();
            }
            else if (IsGameActive)
            {
                ChangeState(pausedState);
            }
        }

        public void EndGame() => ChangeState(gameOverState);

        public void ResetGame() => ChangeState(idleState);

        internal void ApplyManagedTimeScale(float value)
        {
            if (!manageTimeScale)
            {
                return;
            }

            Time.timeScale = Mathf.Max(0f, value);
        }

        protected override void OnStateChanged(GameStateBase previousState, GameStateBase newState)
        {
            CurrentPhase = newState?.Phase ?? GamePhase.Idle;
            onPhaseChanged?.Invoke(previousState?.Phase ?? GamePhase.Idle, CurrentPhase);
            base.OnStateChanged(previousState, newState);
        }

        public enum GamePhase
        {
            Idle,
            Active,
            Paused,
            GameOver
        }

        public abstract class GameStateBase : StateBase
        {
            protected GameManager Manager { get; }

            protected GameStateBase(GameManager manager)
            {
                Manager = manager;
            }

            public override void Enter()
            {
                base.Enter();
                Manager.NotifyPhaseEntered(Phase);
            }

            public override void Exit()
            {
                base.Exit();
                Manager.NotifyPhaseExited(Phase);
            }

            public abstract GamePhase Phase { get; }
        }

        private sealed class IdleState : GameStateBase
        {
            public IdleState(GameManager manager) : base(manager) { }

            public override GamePhase Phase => GamePhase.Idle;

            public override void Enter()
            {
                Manager.ApplyManagedTimeScale(Manager.idleTimeScale);
                base.Enter();
            }
        }

        private sealed class ActiveState : GameStateBase
        {
            public ActiveState(GameManager manager) : base(manager) { }

            public override GamePhase Phase => GamePhase.Active;

            public override void Enter()
            {
                Manager.ApplyManagedTimeScale(Manager.activeTimeScale);
                base.Enter();
            }
        }

        private sealed class PausedState : GameStateBase
        {
            public PausedState(GameManager manager) : base(manager) { }

            public override GamePhase Phase => GamePhase.Paused;

            public override void Enter()
            {
                Manager.ApplyManagedTimeScale(Manager.pausedTimeScale);
                base.Enter();
            }
        }

        private sealed class GameOverState : GameStateBase
        {
            public GameOverState(GameManager manager) : base(manager) { }

            public override GamePhase Phase => GamePhase.GameOver;

            public override void Enter()
            {
                Manager.ApplyManagedTimeScale(Manager.gameOverTimeScale);
                base.Enter();
            }
        }

        [Serializable]
        public class PhaseTransitionEvent : UnityEvent<GamePhase, GamePhase> { }

        [Serializable]
        private class PhaseEventSet
        {
            [SerializeField] private UnityEvent onEntered = new UnityEvent();
            [SerializeField] private UnityEvent onExited = new UnityEvent();

            public void InvokeEntered() => onEntered?.Invoke();
            public void InvokeExited() => onExited?.Invoke();
        }

        private void NotifyPhaseEntered(GamePhase phase)
        {
            GetPhaseEvents(phase)?.InvokeEntered();
        }

        private void NotifyPhaseExited(GamePhase phase)
        {
            GetPhaseEvents(phase)?.InvokeExited();
        }

        private PhaseEventSet GetPhaseEvents(GamePhase phase)
        {
            return phase switch
            {
                GamePhase.Active => activeEvents,
                GamePhase.Paused => pausedEvents,
                GamePhase.GameOver => gameOverEvents,
                _ => idleEvents,
            };
        }
    }
}
