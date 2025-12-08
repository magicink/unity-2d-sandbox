using UnityEngine;
using UnityEngine.UI;

public class PhysicsGameUi : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Assign the PlayerLauncher that drives ammo state")]
    [SerializeField] private PlayerLauncher launcher;

    [Tooltip("Text element used to display remaining ammo")]
    [SerializeField] private Text ammoText;

    private bool subbed = false;

    private void OnEnable()
    {
        TrySubscribe();
    }

    private void OnDisable()
    {
        if (launcher != null && subbed)
        {
            launcher.AmmoChanged -= OnAmmoChanged;
            subbed = false;
        }
    }

    private void Start()
    {
        // if not explicitly wired in the inspector, try to find the scene's PlayerLauncher
        if (launcher == null)
            launcher = FindObjectOfType<PlayerLauncher>();

        TrySubscribe();
        RefreshUi();
        // Ensure Ammo text is inside safe area for canvas-based UI
        TryApplySafeAreaToAmmoText();

        // Recovery guard: try to ensure the ammoText is visible if something moved/disabled it
        if (ensureAmmoTextVisible)
            EnsureAmmoTextVisible();
    }

    private void TrySubscribe()
    {
        if (launcher != null && !subbed)
        {
            launcher.AmmoChanged += OnAmmoChanged;
            subbed = true;
        }
    }

    private void OnAmmoChanged(int remaining, int max)
    {
        RefreshUi();
    }

    private void RefreshUi()
    {
        if (ammoText == null || launcher == null) return;
        ammoText.text = $"Ammo: {launcher.RemainingAmmo}/{launcher.MaxAmmo}";
    }

    // Fallback OnGUI display so the ammo counter is visible even if no Canvas/Text
    //
    // NOTE: For Canvas-based UI elements (the serialized 'ammoText'), prefer using
    // the SafeArea component (Assets/Scripts/UI/SafeArea.cs) — attach SafeArea to a
    // top-level UI container or panel, then position your Text element at the
    // top-left inside that container so it sits inside the device safe area.
    private GUIStyle cachedGuiStyle;
    [Tooltip("Padding in pixels to the safe area's left/top when drawing the OnGUI fallback")]
    [SerializeField] private int guiSafePadding = 10;
    [Header("Ammo Text Background")]
    [Tooltip("If true, the component will add/maintain a black background behind the serialized ammoText (Canvas UI).")]
    [SerializeField] private bool addBackgroundToAmmoText = true;

    [Tooltip("Padding in pixels applied around the ammoText for the background (x = horizontal, y = vertical)")]
    [SerializeField] private Vector2 ammoTextBackgroundPadding = new Vector2(8, 6);

    [Tooltip("Background color for the Canvas text background")]
    [SerializeField] private Color ammoTextBackgroundColor = new Color(0f, 0f, 0f, 0.85f);
        private Texture2D cachedGuiBackgroundTexture;
        [Tooltip("Color used for the OnGUI fallback background box")]
        [SerializeField] private Color guiBackgroundColor = new Color(0f, 0f, 0f, 0.85f);
    [Header("Ammo Text Safe Area")]
    [Tooltip("If true, the component will ensure the serialized ammoText stays inside the device safe area at runtime by adding a SafeAreaFitter if needed.")]
    [SerializeField] private bool autoApplySafeAreaToAmmoText = false; // disabled by default to avoid surprising scene changes
    [Tooltip("If true, PhysicsGameUi will ensure the ammoText is visible and sane at Start (activate it, clamp position and restore a readable color/size). Useful if runtime helpers moved it off-screen).")]
    [SerializeField] private bool ensureAmmoTextVisible = true;
    [Header("HUD Font Scaling")]
    [Tooltip("If true, scale the HUD font size proportionally to the screen height at runtime.")]
    [SerializeField] private bool autoScaleHudFont = true;

    [Tooltip("Base font size used when the screen height equals ReferenceScreenHeight")]
    [SerializeField] private int hudBaseFontSize = 36;

    [Tooltip("Reference screen height (in pixels) that hudBaseFontSize corresponds to")]
    [SerializeField] private float referenceScreenHeight = 800f;

    [Tooltip("Minimum allowed HUD font size regardless of scaling")]
    [SerializeField] private int hudMinFontSize = 18;

    [Tooltip("Maximum allowed HUD font size regardless of scaling")]
    [SerializeField] private int hudMaxFontSize = 120;

    // Cache the last applied size so we don't reassign every frame
    private int lastAppliedHudFontSize = -1;
    private int lastScreenHeight = -1;
    private Rect cachedGuiRect = new Rect(10, 10, 320, 40);

    private void EnsureGuiStyle()
    {
        if (cachedGuiStyle != null) return;
        cachedGuiStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 36,
            alignment = TextAnchor.UpperLeft
        };
        cachedGuiStyle.normal.textColor = Color.white;

        // make a 1x1 background texture that we can tint when drawing a background rect
        if (cachedGuiBackgroundTexture == null)
        {
            cachedGuiBackgroundTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            cachedGuiBackgroundTexture.hideFlags = HideFlags.DontSave;
            cachedGuiBackgroundTexture.SetPixel(0, 0, guiBackgroundColor);
            cachedGuiBackgroundTexture.Apply();
        }
    }

    private void OnGUI()
    {
        // Only show the OnGUI fallback if we don't have a working Text ui assigned/active
        if (launcher == null) return;
        if (ammoText != null && ammoText.gameObject != null && ammoText.gameObject.activeInHierarchy && ammoText.enabled) return;

        var remaining = launcher.RemainingAmmo;
        var max = launcher.MaxAmmo;

        EnsureGuiStyle();
        // Position the GUI fallback inside the device safe area (if available)
        try
        {
            var safe = Screen.safeArea;

            // GUI uses top-left origin for Y, while safeArea origin is bottom-left.
            float x = safe.x + guiSafePadding;
            float y = Screen.height - (safe.y + safe.height) + guiSafePadding; // top edge inside safe area

            cachedGuiRect.x = x;
            cachedGuiRect.y = y;
        }
        catch
        {
            // If any platform doesn't support safeArea or an error occurs, fall back to default cached rect
        }

        // Ensure the OnGUI font size matches our HUD scaling
        if (autoScaleHudFont)
        {
            int desired = ComputeDesiredHudFontSize();
            if (cachedGuiStyle != null && cachedGuiStyle.fontSize != desired)
                cachedGuiStyle.fontSize = desired;
        }

        // Use the cached style/rect to avoid allocating GUIStyle/Rect each frame
        // Use the cached style/rect to avoid allocating GUIStyle/Rect each frame
        // Draw a background box slightly larger than the text (respect guiSafePadding)
        if (cachedGuiBackgroundTexture != null)
        {
            var content = new GUIContent($"Ammo: {remaining}/{max}");
            var contentSize = cachedGuiStyle.CalcSize(content);
            var bgRect = new Rect(cachedGuiRect.x - guiSafePadding, cachedGuiRect.y - guiSafePadding,
                contentSize.x + guiSafePadding * 2, contentSize.y + guiSafePadding * 2);

            GUI.DrawTexture(bgRect, cachedGuiBackgroundTexture);
        }

        GUI.Label(cachedGuiRect, $"Ammo: {remaining}/{max}", cachedGuiStyle);
    }
    private void TryApplySafeAreaToAmmoText()
    {
        if (ammoText == null) return;

        // Optionally add a background to the canvas Text element (independent of safe-area auto-apply)
        if (addBackgroundToAmmoText)
        {
            var bg = ammoText.GetComponent<TextBackground>();
            if (bg == null)
            {
                bg = ammoText.gameObject.AddComponent<TextBackground>();
            }

            bg.padding = ammoTextBackgroundPadding;
            bg.backgroundColor = ammoTextBackgroundColor;
            bg.RefreshNow();
        }

        if (!autoApplySafeAreaToAmmoText) return;

        // If any parent already has a SafeArea component, assume it's handled
        if (ammoText.GetComponentInParent<SafeArea>() != null) return;

        // If the text already has a SafeAreaFitter attached, nothing to do
        var fitter = ammoText.GetComponent<SafeAreaFitter>();
        if (fitter == null)
        {
            fitter = ammoText.gameObject.AddComponent<SafeAreaFitter>();
            // Convert guiSafePadding (int) to fitter padding vector
            fitter.padding = new Vector2(guiSafePadding, guiSafePadding);
        }
    }

    private void Update()
    {
        // Detect resolution changes and update HUD font size if needed
        if (!autoScaleHudFont) return;

        if (Screen.height != lastScreenHeight)
        {
            ApplyHudFontScaling();
            lastScreenHeight = Screen.height;
        }
    }

    private int ComputeDesiredHudFontSize()
    {
        var scale = referenceScreenHeight > 0 ? (Screen.height / referenceScreenHeight) : 1f;
        var size = Mathf.RoundToInt(hudBaseFontSize * scale);
        return Mathf.Clamp(size, hudMinFontSize, hudMaxFontSize);
    }

    private void ApplyHudFontScaling()
    {
        if (!autoScaleHudFont) return;

        int desired = ComputeDesiredHudFontSize();

        // Apply to the UnityEngine.UI.Text element (if assigned)
        if (ammoText != null)
        {
            // Only update when changed to avoid forcing layout every frame
            if (ammoText.fontSize != desired)
                ammoText.fontSize = desired;
        }

        // Keep cached GUI style in sync — created lazily in EnsureGuiStyle
        if (cachedGuiStyle != null && cachedGuiStyle.fontSize != desired)
            cachedGuiStyle.fontSize = desired;

        lastAppliedHudFontSize = desired;
    }

    private void EnsureAmmoTextVisible()
    {
        if (ammoText == null) return;

        // Ensure the object is active and the component is enabled
        if (!ammoText.gameObject.activeInHierarchy)
            ammoText.gameObject.SetActive(true);
        if (!ammoText.enabled)
            ammoText.enabled = true;

        // Make sure color is readable
        if (ammoText.color.a <= 0.01f)
            ammoText.color = Color.white;

        // Ensure font size is in a sane range
        if (autoScaleHudFont)
        {
            ApplyHudFontScaling();
        }
        else if (ammoText.fontSize < 6 || ammoText.fontSize > 1024)
        {
            ammoText.fontSize = Mathf.Clamp(hudBaseFontSize, hudMinFontSize, hudMaxFontSize);
        }

        // Basic sanity check on RectTransform — make sure it's near top-left if anchored there
        var rt = ammoText.GetComponent<RectTransform>();
        if (rt != null)
        {
            var ap = rt.anchoredPosition;
            if (Mathf.Abs(ap.x) > 2000f || Mathf.Abs(ap.y) > 2000f)
            {
                rt.anchoredPosition = new Vector2(10f, -10f);
            }
        }
    }
    // (no additional Unity Start/Update methods — we use our own Start and OnGUI above)
}
