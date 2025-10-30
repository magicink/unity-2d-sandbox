using UnityEngine;
using UnityEngine.Events;

namespace Sandbox.Debugging
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public class ConeDetection2D : MonoBehaviour
    {
        [Header("Filter")]
        [Tooltip("Only react to colliders on these layers (default: Player layer if present).")]
        [SerializeField] private LayerMask onlyWith = 0;

        [Tooltip("Optional tag check in addition to layer mask.")]
        [SerializeField] private bool useTagCheck = false;

        [SerializeField] private string playerTag = "Player";

        [Tooltip("Apply collider callback/include layer overrides to only the specified mask (Unity 2022.2+).")]
        [SerializeField] private bool restrictColliderCallbacksToMask = true;

        [Header("Events")] 
        public UnityEvent onPlayerEnter;
        public UnityEvent onPlayerExit;

        private Collider2D col;

        private void Reset()
        {
            col = GetComponent<Collider2D>();
            if (col) col.isTrigger = true;

            // Default mask to Player layer if it exists; otherwise current GameObject layer
            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer >= 0)
                onlyWith = (1 << playerLayer);
            else
                onlyWith = (1 << gameObject.layer);

            ApplyMaskToCollider();
        }

        private void OnEnable() { ApplyMaskToCollider(); }
        private void OnValidate() { ApplyMaskToCollider(); }

        private void ApplyMaskToCollider()
        {
            if (col == null) col = GetComponent<Collider2D>();
            if (col == null) return;
            if (!restrictColliderCallbacksToMask) return;

            // On supported Unity versions, narrow callbacks to just the designated layers.
#if UNITY_2022_2_OR_NEWER
            try
            {
                col.isTrigger = true;
                // These properties exist on modern Unity versions of Collider2D
                col.includeLayers = onlyWith;
                col.callbackLayers = onlyWith;
            }
            catch (System.Exception)
            {
                // Ignore if not supported on this version
            }
#endif
        }

        private bool Matches(Collider2D other)
        {
            var go = other.attachedRigidbody ? other.attachedRigidbody.gameObject : other.gameObject;
            bool layerOk = (onlyWith.value & (1 << go.layer)) != 0;
            bool tagOk = !useTagCheck || string.IsNullOrEmpty(playerTag) || go.CompareTag(playerTag);
            return layerOk && tagOk;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (Matches(other))
            {
                onPlayerEnter?.Invoke();
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (Matches(other))
            {
                onPlayerExit?.Invoke();
            }
        }
    }
}
