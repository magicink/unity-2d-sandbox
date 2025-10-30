using UnityEngine;
using Sandbox.StateMachine;

namespace Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerStateMachine : AbstractStateMachine<PlayerStateMachine.PlayerState>
    {
        [Header("Config")]
        [Tooltip("Minimum movement speed to be considered moving.")]
        [SerializeField] private float moveThreshold = 0.001f;

        private Animator animator;
        private Rigidbody2D rb2D;
        private SpriteRenderer spriteRenderer;

        private Vector3 lastPosition;
        private bool hasLastPosition;

        private int hashIsMoving;

        private IdleState idleState;
        private MoveState moveState;

        internal float CurrentSpeed { get; private set; }

        private void Awake()
        {
            TryGetComponent(out animator);
            TryGetComponent(out rb2D);
            TryGetComponent(out spriteRenderer);

            hashIsMoving = Animator.StringToHash("IsMoving");

            idleState = new IdleState(this);
            moveState = new MoveState(this);
        }

        protected override PlayerState GetInitialState()
        {
            return idleState;
        }

        protected override void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                base.Update();
                return;
            }

            if (rb2D != null)
            {
                CurrentSpeed = rb2D.linearVelocity.magnitude;
            }
            else
            {
                Vector3 pos = transform.position;
                if (hasLastPosition)
                {
                    CurrentSpeed = (pos - lastPosition).magnitude / dt;
                }
                lastPosition = pos;
                hasLastPosition = true;
            }

            base.Update();
        }

        internal bool IsMovingAboveThreshold()
        {
            return CurrentSpeed > moveThreshold;
        }

        internal void SetIsMoving(bool value)
        {
            if (animator == null) return;
            animator.SetBool(hashIsMoving, value);
        }

        public abstract class PlayerState : StateBase
        {
            protected readonly PlayerStateMachine Machine;
            protected PlayerState(PlayerStateMachine machine) { Machine = machine; }
        }

        public sealed class IdleState : PlayerState
        {
            public IdleState(PlayerStateMachine machine) : base(machine) { }

            public override void Enter()
            {
                Machine.SetIsMoving(false);
            }

            public override void Tick(float deltaTime)
            {
                if (Machine.IsMovingAboveThreshold())
                {
                    Machine.ChangeState(Machine.moveState);
                }
            }
        }

        public sealed class MoveState : PlayerState
        {
            public MoveState(PlayerStateMachine machine) : base(machine) { }

            public override void Enter()
            {
                Machine.SetIsMoving(true);
            }

            public override void Tick(float deltaTime)
            {
                if (!Machine.IsMovingAboveThreshold())
                {
                    Machine.ChangeState(Machine.idleState);
                }
            }
        }
    }
}
