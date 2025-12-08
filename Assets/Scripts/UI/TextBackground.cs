using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Adds/maintains a simple Image background behind a UI Text element and
/// keeps it sized with a pixel padding. Attach this component to the GameObject
/// that has a UnityEngine.UI.Text component.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Text))]
public class TextBackground : MonoBehaviour
{
    [Tooltip("Padding in pixels applied horizontally/vertically around the text.")]
    public Vector2 padding = new Vector2(32, 24);

    [Tooltip("When enabled, padding scales proportionally with the Text font size using referenceFontSize as the baseline.")]
    public bool scalePaddingWithFontSize = true;

    [Tooltip("Font size used as the baseline when scaling padding with font size.")]
    public float referenceFontSize = 36f;

    [Tooltip("Extra multiplier applied after font-based scaling so padding can feel chunkier.")]
    public float paddingScale = 1.2f;

    [Tooltip("If true, the Text alignment will be forced to MiddleCenter so the text sits in the middle of the background.")]
    public bool centerTextInBackground = true;

    [Tooltip("If true, the Text RectTransform will be resized to its preferred width/height so centering is accurate.")]
    public bool resizeTextToPreferred = true;

    [Tooltip("Background colour (alpha controls opacity)")]
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.85f);

    [Tooltip("If true, the component will create a background Image child automatically.")]
    public bool autoCreateBackground = true;

    [Tooltip("When enabled the component will emit short editor logs when it updates the background. Helpful for debugging layout issues.")]
    public bool debug = false;

    private Text text;
    private RectTransform textRect;
    private RectTransform bgRect;
    private Image bgImage;

    private Vector2 lastTextSize = Vector2.zero;
    private Vector2 lastPadding = Vector2.zero;
    private Color lastColor = Color.clear;
    private Vector2 lastAnchoredPosition = Vector2.zero;
    private Vector2 lastAnchorMin = Vector2.zero;
    private Vector2 lastAnchorMax = Vector2.zero;
    private Vector2 lastPivot = Vector2.zero;
    private int lastFontSize = -1;
    private bool lastScalePaddingWithFontSize = true;
    private float lastReferenceFontSize = -1f;
    private float lastPaddingScale = -1f;
    private bool lastCenterTextInBackground = true;
    private TextAnchor lastAlignment = TextAnchor.UpperLeft;
    private bool lastResizeTextToPreferred = true;

    private const string backgroundName = "TextBackground_auto";

    private void Awake()
    {
        text = GetComponent<Text>();
        textRect = GetComponent<RectTransform>();
        EnsureBackgroundExists();
        ApplyImmediate();
    }

    private void Update()
    {
        // Update when relevant values change
        if (textRect == null) return;

        var size = textRect.rect.size;
        var anchoredPos = textRect.anchoredPosition;
        var anchorMin = textRect.anchorMin;
        var anchorMax = textRect.anchorMax;
        var pivot = textRect.pivot;
        var fontSize = text != null ? text.fontSize : -1;
        var alignment = text != null ? text.alignment : TextAnchor.UpperLeft;

        if (size != lastTextSize ||
            padding != lastPadding ||
            backgroundColor != lastColor ||
            anchoredPos != lastAnchoredPosition ||
            anchorMin != lastAnchorMin ||
            anchorMax != lastAnchorMax ||
            pivot != lastPivot ||
            fontSize != lastFontSize ||
            scalePaddingWithFontSize != lastScalePaddingWithFontSize ||
            !Mathf.Approximately(referenceFontSize, lastReferenceFontSize) ||
            !Mathf.Approximately(paddingScale, lastPaddingScale) ||
            centerTextInBackground != lastCenterTextInBackground ||
            (centerTextInBackground && alignment != TextAnchor.MiddleCenter) ||
            resizeTextToPreferred != lastResizeTextToPreferred)
        {
            ApplyImmediate();
        }
    }

    private void OnValidate()
    {
        // Let designers tweak padding/color in the editor and see updates immediately
        if (!isActiveAndEnabled) return;

        if (text == null) text = GetComponent<Text>();
        if (textRect == null) textRect = GetComponent<RectTransform>();

        EnsureBackgroundExists();
        ApplyImmediate();
    }

    private void EnsureBackgroundExists()
    {
        if (!autoCreateBackground) return;

        if (bgImage != null && bgRect != null) return;

        // try to find an existing child image
        for (int i = 0; i < transform.childCount; i++)
        {
            var ch = transform.GetChild(i);
            var img = ch.GetComponent<Image>();
            if (img != null && ch.name == backgroundName)
            {
                bgImage = img;
                bgRect = img.GetComponent<RectTransform>();
                break;
            }
        }

        if (bgImage == null && autoCreateBackground)
        {
            var parentTransform = transform.parent;

            // Create background as a sibling (behind) the text so it doesn't cover the
            // text when both are rendered. If there is no parent, fallback to creating
            // it as a child of the text GameObject.
            var go = new GameObject(backgroundName, typeof(RectTransform), typeof(Image));

            bgRect = go.GetComponent<RectTransform>();
            bgImage = go.GetComponent<Image>();

            // Ensure the image has a usable sprite so the color will be visible
            // and disable raycast targeting so it doesn't block events.
            if (bgImage.sprite == null)
            {
                // Avoid calling GetBuiltinResource (it logs errors when the resource is missing
                // in some Unity/editor setups). Create a lightweight runtime sprite fallback
                // instead and reuse it across instances to reduce allocations.
                bgImage.sprite = RuntimeFallbackSprite.Instance.Sprite;
                bgImage.type = Image.Type.Simple;
            }

            bgImage.raycastTarget = false;

            if (parentTransform != null)
            {
                // place under the same parent so we can set sibling order
                int textIndex = transform.GetSiblingIndex();
                go.transform.SetParent(parentTransform, false);

                // insert background at the text's index (so it's before the text)
                go.transform.SetSiblingIndex(textIndex);

                // ensure the text is explicitly above the background
                transform.SetSiblingIndex(textIndex + 1);
            }
            else
            {
                // fallback: make it a child (older behavior)
                go.transform.SetParent(transform, false);
                // ensure background is drawn behind the parent's content by placing
                // it as the first child
                go.transform.SetSiblingIndex(0);
            }
        }
    }

    private void ApplyImmediate()
    {
        if (textRect == null || bgRect == null || bgImage == null) return;

        // Ensure layout is up-to-date so textRect.rect reflects the actual rendered
        // size after any Canvas / Layout updates. This prevents the background from
        // being sized incorrectly at startup.
        Canvas.ForceUpdateCanvases();
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(textRect);

        // Match anchors/pivot/pos to the text rect so relative layout is preserved.
        // Also ensure local scale is normalized so sizeDelta behaves predictably.
        bgRect.anchorMin = textRect.anchorMin;
        bgRect.anchorMax = textRect.anchorMax;
        bgRect.pivot = textRect.pivot;
        bgRect.anchoredPosition = textRect.anchoredPosition;
        bgRect.localScale = Vector3.one;

        // Prefer the text's measured size so the background wraps the visible glyphs
        var textSize = textRect.rect.size;
        if (text != null)
        {
            float preferredW = LayoutUtility.GetPreferredWidth(textRect);
            float preferredH = LayoutUtility.GetPreferredHeight(textRect);
            if (preferredW > 0f && preferredH > 0f)
            {
                textSize = new Vector2(Mathf.Max(textSize.x, preferredW), Mathf.Max(textSize.y, preferredH));
            }
        }

        // Optionally resize the Text rect so centering and overflow behave predictably
        if (resizeTextToPreferred && textRect != null)
        {
            textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textSize.x);
            textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, textSize.y);
        }

        // Optionally scale padding so the background grows with font size changes
        var effectivePadding = padding;
        if (scalePaddingWithFontSize && text != null)
        {
            float baseline = Mathf.Max(1f, referenceFontSize);
            float scale = text.fontSize > 0 ? (float)text.fontSize / baseline : 1f;
            effectivePadding *= scale * paddingScale;
        }
        else
        {
            effectivePadding *= paddingScale;
        }

        // Make size slightly larger using padding
        var desired = new Vector2(textSize.x + effectivePadding.x * 2f, textSize.y + effectivePadding.y * 2f);
        bgRect.sizeDelta = desired;

        bgImage.color = backgroundColor;

        if (debug)
        {
            Debug.Log($"[TextBackground] Updated background for '{name}': textSize={textSize} padding={effectivePadding} bgSize={desired}");
        }

        if (centerTextInBackground && text != null && text.alignment != TextAnchor.MiddleCenter)
        {
            text.alignment = TextAnchor.MiddleCenter;
        }

        // Ensure the background GameObject is placed directly before the text in the
        // hierarchy so the text renders on top in the Canvas.
        if (bgRect != null && bgRect.transform.parent == transform.parent)
        {
            int textIndex = transform.GetSiblingIndex();
            int bgIndex = bgRect.transform.GetSiblingIndex();
            if (bgIndex >= textIndex)
            {
                // Move background before text
                bgRect.transform.SetSiblingIndex(Mathf.Max(0, textIndex - 1));
            }
            if (debug)
            {
                Debug.Log($"[TextBackground] sibling order: parent={transform.parent?.name ?? "(none)"} bgIndex={bgRect.transform.GetSiblingIndex()} textIndex={transform.GetSiblingIndex()}");
            }
        }

        lastTextSize = textSize;
        lastPadding = padding;
        lastColor = backgroundColor;
        lastAnchoredPosition = textRect.anchoredPosition;
        lastAnchorMin = textRect.anchorMin;
        lastAnchorMax = textRect.anchorMax;
        lastPivot = textRect.pivot;
        lastFontSize = text != null ? text.fontSize : -1;
        lastScalePaddingWithFontSize = scalePaddingWithFontSize;
        lastReferenceFontSize = referenceFontSize;
        lastPaddingScale = paddingScale;
        lastCenterTextInBackground = centerTextInBackground;
        lastAlignment = text != null ? text.alignment : TextAnchor.UpperLeft;
        lastResizeTextToPreferred = resizeTextToPreferred;
    }

    // Expose a helper so other code can force a refresh when text content changes
    public void RefreshNow()
    {
        EnsureBackgroundExists();
        ApplyImmediate();
    }
}
