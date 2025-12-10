using UnityEngine;
using UnityEngine.InputSystem;

namespace Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerMovement : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("Optional reference to an Input Action that supplies a Vector2 move value (new Input System).")]
        [SerializeField] private InputActionReference moveActionReference;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private bool alignWithCameraForward;

        private InputAction moveAction;
        private bool ownsAction;
        private Vector2 moveInput;
        private Rigidbody2D cachedBody;
        private SpriteRenderer cachedSprite;

        private void Awake()
        {
            TryGetComponent(out cachedBody);
            TryGetComponent(out cachedSprite);
        }

        private void OnEnable()
        {
            BindInput();
        }

        private void OnDisable()
        {
            UnbindInput();

            if (cachedBody != null)
            {
                cachedBody.linearVelocity = Vector2.zero;
            }
        }

        private void Update()
        {
            if (cachedBody == null)
            {
                MoveTransform(Time.deltaTime);
            }
        }

        private void FixedUpdate()
        {
            if (cachedBody != null)
            {
                cachedBody.linearVelocity = moveInput * moveSpeed;
            }
        }
        private void BindInput()
        {
            moveAction = moveActionReference != null ? moveActionReference.action : null;

            if (moveAction == null)
            {
                moveAction = new InputAction("Move", InputActionType.Value, null, null, null, "Vector2");
                moveAction.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/w")
                    .With("Down", "<Keyboard>/s")
                    .With("Left", "<Keyboard>/a")
                    .With("Right", "<Keyboard>/d");
                moveAction.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/upArrow")
                    .With("Down", "<Keyboard>/downArrow")
                    .With("Left", "<Keyboard>/leftArrow")
                    .With("Right", "<Keyboard>/rightArrow");

                ownsAction = true;
            }
            else
            {
                ownsAction = false;
            }

            moveAction.performed += OnMovePerformed;
            moveAction.canceled += OnMoveCanceled;

            if (!moveAction.enabled)
            {
                moveAction.Enable();
            }
        }

        private void UnbindInput()
        {
            if (moveAction == null)
            {
                return;
            }

            moveAction.performed -= OnMovePerformed;
            moveAction.canceled -= OnMoveCanceled;

            if (ownsAction)
            {
                moveAction.Disable();
                moveAction.Dispose();
            }

            moveAction = null;
            ownsAction = false;
            moveInput = Vector2.zero;
        }

        private void OnMovePerformed(InputAction.CallbackContext context)
        {
            moveInput = context.ReadValue<Vector2>();
            moveInput = Vector2.ClampMagnitude(moveInput, 1f);

            if (alignWithCameraForward && Camera.main != null)
            {
                Vector3 forward = Camera.main.transform.forward;
                Vector3 right = Camera.main.transform.right;

                forward.z = 0f;
                right.z = 0f;
                forward.Normalize();
                right.Normalize();

                // right and forward are Vector3 with z cleared; assigning the result to
                // a Vector2 performs the appropriate conversion so an explicit cast isn't needed.
                Vector2 aligned = right * moveInput.x + forward * moveInput.y;
                moveInput = Vector2.ClampMagnitude(aligned, 1f);
            }

            UpdateFacing();
        }

        private void OnMoveCanceled(InputAction.CallbackContext context)
        {
            moveInput = Vector2.zero;
        }

        private void MoveTransform(float deltaTime)
        {
            if (moveInput.sqrMagnitude < float.Epsilon)
            {
                return;
            }

            // Multiply the scalar values first to avoid two vector-scale temporaries
            // (vector * float * float would create two intermediate Vector3 results).
            float scale = moveSpeed * deltaTime;
            Vector3 delta = new Vector3(moveInput.x * scale, moveInput.y * scale, 0f);
            transform.position += delta;
        }

        private void UpdateFacing()
        {
            if (cachedSprite == null)
            {
                return;
            }

            const float epsilon = 0.001f;
            if (moveInput.x > epsilon)
            {
                // Moving right: face right
                cachedSprite.flipX = false;
            }
            else if (moveInput.x < -epsilon)
            {
                // Moving left: flip horizontally (mirror across Y axis)
                cachedSprite.flipX = true;
            }
        }

        
    }
}
