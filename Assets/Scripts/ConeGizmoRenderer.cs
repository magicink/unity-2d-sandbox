using UnityEngine;

namespace Sandbox.Debugging
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ConeGizmoRenderer : MonoBehaviour
    {
        [Header("Cone")]
        [Tooltip("Length of the cone (radius of the arc).")]
        [Min(0f)] public float length = 5f;

        [Tooltip("Full angle of the cone in degrees.")]
        [Range(0f, 360f)] public float angle = 60f;

        [Tooltip("Number of line segments used to draw the arc.")]
        [Range(4, 128)] public int arcSegments = 32;

        [Tooltip("Extra rotation of the cone around the GameObject's local Z axis in degrees.")]
        public float rotationOffset = 0f;

        [Header("Display")] 
        [Tooltip("When enabled, draws only while the GameObject is selected.")]
        public bool drawWhenSelectedOnly = false;

        [Tooltip("Line color for the gizmo.")]
        public Color lineColor = Color.green;

        public enum Facing
        {
            Right,
            Up,
            Custom
        }

        [Header("Facing")] 
        [Tooltip("Base direction the cone points in local space.")]
        public Facing facing = Facing.Right;

        [Tooltip("Used when Facing is Custom (local space XY).")]
        public Vector2 customFacing = Vector2.right;

        private void OnDrawGizmos()
        {
            if (drawWhenSelectedOnly) return;
            DrawCone();
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawWhenSelectedOnly) return;
            DrawCone();
        }

        private void DrawCone()
        {
            if (length <= 0f || angle <= 0f)
            {
                return;
            }

            Gizmos.color = lineColor;

            Vector3 origin = transform.position;
            Vector3 baseDir = GetBaseDirection();
            if (Mathf.Abs(rotationOffset) > 0.0001f)
            {
                baseDir = (Quaternion.AngleAxis(rotationOffset, transform.forward) * baseDir).normalized;
            }

            float half = Mathf.Max(0f, angle) * 0.5f;
            int segs = Mathf.Clamp(arcSegments, 2, 256);
            float step = angle / segs;

            // Starting boundary direction (left side when looking along +Z)
            Quaternion qStart = Quaternion.AngleAxis(-half, Vector3.forward);
            Vector3 startDir = (qStart * baseDir).normalized;

            Vector3 prev = origin + startDir * length;

            // Side from tip to first arc point
            Gizmos.DrawLine(origin, prev);

            // Arc
            for (int i = 1; i <= segs; i++)
            {
                float a = -half + i * step;
                Quaternion q = Quaternion.AngleAxis(a, Vector3.forward);
                Vector3 dir = (q * baseDir).normalized;
                Vector3 next = origin + dir * length;
                Gizmos.DrawLine(prev, next);
                prev = next;
            }

            // Closing side from tip to last arc point
            Gizmos.DrawLine(origin, prev);
        }

        private Vector3 GetBaseDirection()
        {
            switch (facing)
            {
                case Facing.Up:
                    return transform.up;
                case Facing.Custom:
                {
                    Vector3 local = new Vector3(customFacing.x, customFacing.y, 0f);
                    if (local.sqrMagnitude < 1e-6f)
                    {
                        local = Vector3.right;
                    }
                    return (transform.rotation * local).normalized;
                }
                case Facing.Right:
                default:
                    return transform.right;
            }
        }
    }
}
