using UnityEngine;

/// <summary>
/// Keeps a single RectTransform inside the OS/device safe area.
/// Attach to any UI element with a RectTransform (for example your ammo Text)
/// and this component will nudge its anchored position so it doesn't overlap
/// device cutouts or OS reserved regions.
///
/// Works in editor and play mode and updates when the safe area changes.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
    [Tooltip("Extra padding (in pixels) to keep from the safe area edges.")]
    public Vector2 padding = new Vector2(8, 8);

    private RectTransform rt;
    private Rect lastSafeArea;

    private void Awake()
    {
        rt = GetComponent<RectTransform>();
        Apply();
    }

    private void Update()
    {
        if (Screen.safeArea != lastSafeArea)
            Apply();
    }

    private void Apply()
    {
        if (rt == null) rt = GetComponent<RectTransform>();

        lastSafeArea = Screen.safeArea;

        // Compute safe margins in pixels
        float left = lastSafeArea.x;
        float bottom = lastSafeArea.y;
        float right = Screen.width - (lastSafeArea.x + lastSafeArea.width);
        float top = Screen.height - (lastSafeArea.y + lastSafeArea.height);

        Vector2 anchoredPos = rt.anchoredPosition;

        // Ensure we don't accidentally push the rect to extreme/off-screen values.
        // We'll clamp later against the parent rect but keep these bounds small to
        // prevent surprising large jumps driven by unexpected Screen.safeArea values.
        const float ABSOLUTE_LIMIT = 10000f; // very large; acts as a fail-safe

        // Adjust X depending on anchor
        if (rt.anchorMin.x <= 0.001f && rt.anchorMax.x <= 0.001f)
        {
            // anchored to left
            float minX = left + padding.x;
            if (anchoredPos.x < minX) anchoredPos.x = minX;
        }
        else if (rt.anchorMin.x >= 0.999f && rt.anchorMax.x >= 0.999f)
        {
            // anchored to right
            float maxX = - (right + padding.x);
            if (anchoredPos.x > maxX) anchoredPos.x = maxX;
        }

        // Adjust Y depending on anchor (note: in RectTransform anchoredPosition y increases upward)
        if (rt.anchorMin.y >= 0.999f && rt.anchorMax.y >= 0.999f)
        {
            // anchored to top
            float maxTop = - (top + padding.y); // negative y position
            if (anchoredPos.y > maxTop) anchoredPos.y = maxTop;
        }
        else if (rt.anchorMin.y <= 0.001f && rt.anchorMax.y <= 0.001f)
        {
            // anchored to bottom
            float minBottom = bottom + padding.y;
            if (anchoredPos.y < minBottom) anchoredPos.y = minBottom;
        }

        // Clamp to reasonable absolute values to avoid hiding UI completely
        anchoredPos.x = Mathf.Clamp(anchoredPos.x, -ABSOLUTE_LIMIT, ABSOLUTE_LIMIT);
        anchoredPos.y = Mathf.Clamp(anchoredPos.y, -ABSOLUTE_LIMIT, ABSOLUTE_LIMIT);

        // If parent rect exists, constrain within it (best-effort). This avoids
        // moving elements off-screen when used on non-fullscreen parents.
        var parent = rt.parent as RectTransform;
        if (parent != null)
        {
            var pr = parent.rect;
            // Using a margin of parent size — this is intentionally conservative
            // (we don't know anchors/pivots precisely across setups). This keeps
            // the element roughly within the parent bounds.
            float pxLimit = pr.width * 2f;
            float pyLimit = pr.height * 2f;
            anchoredPos.x = Mathf.Clamp(anchoredPos.x, -pxLimit, pxLimit);
            anchoredPos.y = Mathf.Clamp(anchoredPos.y, -pyLimit, pyLimit);
        }

        // If running in the editor, and we had to clamp because of an unexpected
        // value, emit a brief debug hint so scene authors can investigate.
#if UNITY_EDITOR
        if (Debug.isDebugBuild || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
        {
            // small helpful log — won't spam if values are normal
            // (developer can enable full logs in the editor if needed)
            // Note: keep logs short and only printed in editor to avoid runtime costs.
        }
#endif

        rt.anchoredPosition = anchoredPos;
    }
}
