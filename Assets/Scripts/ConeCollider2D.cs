using System.Collections.Generic;
using UnityEngine;

namespace Sandbox.Debugging
{
    [ExecuteAlways]
    [RequireComponent(typeof(PolygonCollider2D))]
    [DisallowMultipleComponent]
    public class ConeCollider2D : MonoBehaviour
    {
        [SerializeField] private ConeGizmoRenderer source;

        [Tooltip("Override segment count for collider; -1 to use source.arcSegments.")]
        [SerializeField] private int overrideSegments = -1;

        [Tooltip("If true, set collider to IsTrigger on sync.")]
        [SerializeField] private bool setAsTrigger = true;

        [Tooltip("Regenerate collider automatically when values change.")]
        [SerializeField] private bool autoUpdate = true;

        
        // Cache of last applied values for change detection
        private float lastAngle = -1f;
        private float lastLength = -1f;
        private float lastRotationOffset = float.NaN;
        private int lastSegs = -1;
        private ConeGizmoRenderer.Facing lastFacing = (ConeGizmoRenderer.Facing)(-1);
        private Vector2 lastCustomFacing = new Vector2(float.NaN, float.NaN);
private PolygonCollider2D pc;

        private void Reset()
        {
            pc = GetComponent<PolygonCollider2D>();
            source = GetComponent<ConeGizmoRenderer>();
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
            if (source == null) source = GetComponent<ConeGizmoRenderer>();
            if (pc == null) pc = GetComponent<PolygonCollider2D>();
            if (source == null || pc == null) return;

            int segs = overrideSegments > 0 ? overrideSegments : Mathf.Clamp(source.arcSegments, 2, 256);
            if (!Mathf.Approximately(lastAngle, source.angle)
                || !Mathf.Approximately(lastLength, source.length)
                || !Mathf.Approximately(lastRotationOffset, source.rotationOffset)
                || segs != lastSegs
                || source.facing != lastFacing
                || (lastCustomFacing - source.customFacing).sqrMagnitude > 1e-6f)
            {
                Rebuild();
                CacheCurrent();
            }
#endif
        }

        private void CacheCurrent()
        {
            if (source == null) return;
            lastAngle = source.angle;
            lastLength = source.length;
            lastRotationOffset = source.rotationOffset;
            lastSegs = overrideSegments > 0 ? overrideSegments : Mathf.Clamp(source.arcSegments, 2, 256);
            lastFacing = source.facing;
            lastCustomFacing = source.customFacing;
        }


        [ContextMenu("Rebuild Collider")]
        public void Rebuild()
        {
            if (pc == null) pc = GetComponent<PolygonCollider2D>();
            if (source == null) source = GetComponent<ConeGizmoRenderer>();
            if (pc == null || source == null) return;

            float angle = Mathf.Max(0f, source.angle);
            float length = Mathf.Max(0f, source.length);
            int segs = overrideSegments > 0 ? overrideSegments : Mathf.Clamp(source.arcSegments, 2, 256);
            if (angle <= 0f || length <= 0f || segs < 2)
            {
                // Collapse to a degenerate triangle
                pc.pathCount = 1;
                pc.SetPath(0, new Vector2[] { Vector2.zero, Vector2.right, Vector2.right });
                return;
            }

            float half = Mathf.Min(angle, 359.9f) * 0.5f;
            float step = angle / segs;
            float offset = source.rotationOffset;

            Vector2 baseDir = GetBaseDirectionLocal(source);

            var pts = new List<Vector2>(segs + 2);
            pts.Add(Vector2.zero);
            for (int i = 0; i <= segs; i++)
            {
                float a = -half + i * step + offset;
                Vector2 dir = Rotate(baseDir, a).normalized;
                pts.Add(dir * length);
            }

            pc.pathCount = 1;
            
            CacheCurrent();
pc.SetPath(0, pts);
            if (setAsTrigger) pc.isTrigger = true;
        }

        private static Vector2 GetBaseDirectionLocal(ConeGizmoRenderer src)
        {
            switch (src.facing)
            {
                case ConeGizmoRenderer.Facing.Up: return Vector2.up;
                case ConeGizmoRenderer.Facing.Custom:
                    var v = src.customFacing;
                    if (v.sqrMagnitude < 1e-6f) v = Vector2.right;
                    return v.normalized;
                case ConeGizmoRenderer.Facing.Right:
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
    }
}
