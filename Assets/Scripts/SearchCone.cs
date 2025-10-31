using System.Collections.Generic;
using UnityEngine;

namespace Sandbox.Debugging
{
    [ExecuteAlways]
    [RequireComponent(typeof(PolygonCollider2D))]
    [DisallowMultipleComponent]
    public sealed class SearchCone : MonoBehaviour
    {
        [Header("Cone")]
        [Tooltip("Length of the cone (radius of the arc).")]
        [Min(0f)] public float length = 5f;

        [Tooltip("Full angle of the cone in degrees.")]
        [Range(0f, 360f)] public float angle = 60f;

        [Tooltip("Number of segments used to draw/build the arc.")]
        [Range(2, 256)] public int arcSegments = 32;

        [Tooltip("Extra local Z rotation (degrees) of the cone shape.")]
        public float rotationOffset = 0f;

        public enum Facing { Right, Up, Custom }

        [Header("Facing")] 
        [Tooltip("Base direction the cone points in local space.")]
        public Facing facing = Facing.Right;

        [Tooltip("Used when Facing is Custom (local space XY).")]
        public Vector2 customFacing = Vector2.right;

        [Header("Display")]
        [Tooltip("When enabled, draws only while the GameObject is selected.")]
        public bool drawWhenSelectedOnly = false;

        [Tooltip("Line color for the gizmo.")]
        public Color lineColor = Color.green;

        [Header("Collider")]
        [Tooltip("Override segment count for collider; -1 to use arcSegments.")]
        [SerializeField] private int overrideSegments = -1;

        [Tooltip("If true, set collider to IsTrigger on sync.")]
        [SerializeField] private bool setAsTrigger = true;

        [Tooltip("Regenerate collider automatically when values change (Editor only).")]
        [SerializeField] private bool autoUpdate = true;

        private PolygonCollider2D pc;

        // Change tracking (Editor)
        private float lastAngle = -1f;
        private float lastLength = -1f;
        private float lastRotationOffset = float.NaN;
        private int lastSegs = -1;
        private Facing lastFacing = (Facing)(-1);
        private Vector2 lastCustomFacing = new Vector2(float.NaN, float.NaN);

        private void Reset()
        {
            pc = GetComponent<PolygonCollider2D>();
            if (pc != null) pc.isTrigger = true;
            Rebuild();
            CacheCurrent();
        }

        private void Awake()
        {
            if (Application.isPlaying) return; // do not update in Play Mode
            if (autoUpdate) { Rebuild(); CacheCurrent(); }
        }

        private void OnEnable()
        {
            if (Application.isPlaying) return; // do not update in Play Mode
            if (autoUpdate) { Rebuild(); CacheCurrent(); }
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            if (Application.isPlaying) return; // do not update while playing in Editor
            if (autoUpdate) { Rebuild(); CacheCurrent(); }
#endif
        }

        private void Update()
        {
#if UNITY_EDITOR
            if (Application.isPlaying || !autoUpdate) return; // edit-time only
            if (pc == null) pc = GetComponent<PolygonCollider2D>();

            int segs = overrideSegments > 0 ? overrideSegments : Mathf.Clamp(arcSegments, 2, 256);
            if (!Mathf.Approximately(lastAngle, angle)
                || !Mathf.Approximately(lastLength, length)
                || !Mathf.Approximately(lastRotationOffset, rotationOffset)
                || segs != lastSegs
                || facing != lastFacing
                || (lastCustomFacing - customFacing).sqrMagnitude > 1e-6f)
            {
                Rebuild();
                CacheCurrent();
            }
#endif
        }

        [ContextMenu("Rebuild Collider")]
        public void Rebuild()
        {
            if (pc == null) pc = GetComponent<PolygonCollider2D>();
            if (pc == null) return;

            float a = Mathf.Max(0f, angle);
            float len = Mathf.Max(0f, length);
            int segs = overrideSegments > 0 ? overrideSegments : Mathf.Clamp(arcSegments, 2, 256);
            if (a <= 0f || len <= 0f || segs < 2)
            {
                // Degenerate triangle
                pc.pathCount = 1;
                pc.SetPath(0, new Vector2[] { Vector2.zero, Vector2.right, Vector2.right });
                if (setAsTrigger) pc.isTrigger = true;
                return;
            }

            float half = Mathf.Min(a, 359.9f) * 0.5f;
            float step = a / segs;

            Vector2 baseDir = GetBaseDirectionLocal();

            var pts = new List<Vector2>(segs + 2);
            pts.Add(Vector2.zero);
            for (int i = 0; i <= segs; i++)
            {
                float ang = -half + i * step + rotationOffset;
                Vector2 dir = Rotate(baseDir, ang).normalized;
                pts.Add(dir * len);
            }

            pc.pathCount = 1;
            pc.SetPath(0, pts);
            if (setAsTrigger) pc.isTrigger = true;
        }

        private void CacheCurrent()
        {
            lastAngle = angle;
            lastLength = length;
            lastRotationOffset = rotationOffset;
            lastSegs = overrideSegments > 0 ? overrideSegments : Mathf.Clamp(arcSegments, 2, 256);
            lastFacing = facing;
            lastCustomFacing = customFacing;
        }

        private Vector2 GetBaseDirectionLocal()
        {
            switch (facing)
            {
                case Facing.Up: return Vector2.up;
                case Facing.Custom:
                    var v = customFacing;
                    if (v.sqrMagnitude < 1e-6f) v = Vector2.right;
                    return v.normalized;
                case Facing.Right:
                default: return Vector2.right;
            }
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad);
            float s = Mathf.Sin(rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        // Gizmo rendering
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
            if (length <= 0f || angle <= 0f) return;

            Gizmos.color = lineColor;

            Vector3 origin = transform.position;
            Vector3 baseDir = GetWorldBaseDirection();
            if (Mathf.Abs(rotationOffset) > 0.0001f)
            {
                baseDir = (Quaternion.AngleAxis(rotationOffset, transform.forward) * baseDir).normalized;
            }

            float half = Mathf.Max(0f, angle) * 0.5f;
            int segs = Mathf.Clamp(arcSegments, 2, 256);
            float step = angle / segs;

            Quaternion qStart = Quaternion.AngleAxis(-half, Vector3.forward);
            Vector3 startDir = (qStart * baseDir).normalized;

            Vector3 prev = origin + startDir * length;
            Gizmos.DrawLine(origin, prev);

            for (int i = 1; i <= segs; i++)
            {
                float a = -half + i * step;
                Quaternion q = Quaternion.AngleAxis(a, Vector3.forward);
                Vector3 dir = (q * baseDir).normalized;
                Vector3 next = origin + dir * length;
                Gizmos.DrawLine(prev, next);
                prev = next;
            }

            Gizmos.DrawLine(origin, prev);
        }

        private Vector3 GetWorldBaseDirection()
        {
            switch (facing)
            {
                case Facing.Up:
                    return transform.up;
                case Facing.Custom:
                {
                    Vector3 local = new Vector3(customFacing.x, customFacing.y, 0f);
                    if (local.sqrMagnitude < 1e-6f) local = Vector3.right;
                    return (transform.rotation * local).normalized;
                }
                case Facing.Right:
                default:
                    return transform.right;
            }
        }
    }
}
