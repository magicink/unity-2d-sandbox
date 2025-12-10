using System;
using UnityEngine;

namespace Sandbox.StateMachine
{
    public interface IState
    {
        void Enter();
        void Exit();
        void Tick(float deltaTime);
        void FixedTick(float fixedDeltaTime);
        void LateTick(float deltaTime);
    }

    public abstract class StateBase : IState
    {
        public virtual void Enter() { }
        public virtual void Exit() { }
        public virtual void Tick(float deltaTime) { }
        public virtual void FixedTick(float fixedDeltaTime) { }
        public virtual void LateTick(float deltaTime) { }
    }

    public abstract class AbstractStateMachine<TState> : MonoBehaviour where TState : class, IState
    {
        public TState CurrentState { get; private set; }

        public event Action<TState, TState> StateChanged;

        protected virtual void Start()
        {
            // If a state was already selected (e.g., pooled objects started a flow before Start), don't overwrite it.
            if (CurrentState != null)
            {
                return;
            }

            var initialState = GetInitialState();
            if (initialState != null)
            {
                ChangeState(initialState);
            }
        }

        protected virtual void Update()
        {
            CurrentState?.Tick(Time.deltaTime);
        }

        protected virtual void FixedUpdate()
        {
            CurrentState?.FixedTick(Time.fixedDeltaTime);
        }

        protected virtual void LateUpdate()
        {
            CurrentState?.LateTick(Time.deltaTime);
        }

        public void ChangeState(TState newState)
        {
            if (ReferenceEquals(CurrentState, newState))
            {
                return;
            }

            var previousState = CurrentState;
            previousState?.Exit();

            CurrentState = newState;
            CurrentState?.Enter();

            OnStateChanged(previousState, CurrentState);
        }

        public void ClearState()
        {
            if (CurrentState == null)
            {
                return;
            }

            ChangeState(null);
        }

        protected virtual void OnStateChanged(TState previousState, TState newState)
        {
            StateChanged?.Invoke(previousState, newState);
        }

        protected abstract TState GetInitialState();
    }
}
