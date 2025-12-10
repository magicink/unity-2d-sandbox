using UnityEngine;

/// <summary>
/// Provides a single reusable runtime fallback sprite used by TextBackground when
/// a project's UI sprite is unavailable. Keeps allocation to a minimum.
/// </summary>
public class RuntimeFallbackSprite
{
    private static RuntimeFallbackSprite instance;
    public static RuntimeFallbackSprite Instance
    {
        get
        {
            if (instance == null) instance = new RuntimeFallbackSprite();
            return instance;
        }
    }

    public Sprite Sprite { get; }

    private RuntimeFallbackSprite()
    {
        var tex = new Texture2D(1, 1, TextureFormat.ARGB32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        tex.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

        Sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
        Sprite.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
    }
}
