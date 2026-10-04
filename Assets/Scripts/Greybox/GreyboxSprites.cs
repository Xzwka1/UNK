using UnityEngine;

/// <summary>
/// Provides a runtime-generated 1x1 unit white square sprite for greybox visuals.
/// Scale a transform by (width, height) to get a rectangle of exactly that size in world units.
/// </summary>
public static class GreyboxSprites
{
    private static Sprite square;

    public static Sprite Square
    {
        get
        {
            if (square == null)
            {
                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                tex.hideFlags = HideFlags.HideAndDontSave;

                // pixelsPerUnit = 1 -> the sprite is exactly 1x1 world unit.
                square = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                square.hideFlags = HideFlags.HideAndDontSave;
            }
            return square;
        }
    }
}
