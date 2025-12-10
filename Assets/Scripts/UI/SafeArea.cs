using UnityEngine;

/// <summary>
/// Simple SafeArea helper — attach to a GameObject with a RectTransform to
/// automatically constrain that RectTransform to the device safe area.
///
/// Typical usage:
/// - Add this component to a top-level UI container (Panel/Empty GameObject)
///   and place your top-left UI elements as children inside it.
/// - It will update anchors to match Screen.safeArea so children laid out
///   using anchors/positions will remain inside the visible cutout area on
///   mobile devices.
///
/// This script works in both Play and Editor (ExecuteAlways) and updates if
/// the safe area changes (device rotation, resolution change, etc.).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class SafeArea : MonoBehaviour
{
    private Rect lastSafeArea = new Rect(0, 0, 0, 0);
    private RectTransform rectTransform;

    [Tooltip("When true, the RectTransform will be stretched to exactly the safe area.")]
    public bool stretchToSafeArea = true;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        Apply();
    }

    private void Update()
    {
        // In editor or on device the safe area may change (rotation / cutout change).
        if (Screen.safeArea != lastSafeArea)
            Apply();
    }

    private void Apply()
    {
        if (rectTransform == null)
            rectTransform = GetComponent<RectTransform>();

        var safeArea = Screen.safeArea;
        lastSafeArea = safeArea;

        // Convert safe area from absolute pixels to normalized anchors
        Vector2 anchorMin = safeArea.position;
        Vector2 anchorMax = safeArea.position + safeArea.size;

        anchorMin.x /= Screen.width;
        anchorMin.y /= Screen.height;
        anchorMax.x /= Screen.width;
        anchorMax.y /= Screen.height;

        // Apply anchors
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;

        // When stretching we zero out offsets so it exactly matches the safe area.
        if (stretchToSafeArea)
        {
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }
    }
}
