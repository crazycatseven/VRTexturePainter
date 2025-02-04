using System;
using UnityEngine;

public static class MenuEvents
{
    public static event Action<float> OnBrushSizeChanged;
    public static event Action<Texture2D> OnBrushTextureChanged;
    public static event Action<Color> OnBrushColorChanged;

    public static void TriggerBrushSizeChanged(float size)
    {
        OnBrushSizeChanged?.Invoke(size);
    }

    public static void TriggerBrushTextureChanged(Texture2D texture)
    {
        OnBrushTextureChanged?.Invoke(texture);
    }

    public static void TriggerBrushColorChanged(Color color)
    {
        OnBrushColorChanged?.Invoke(color);
    }
} 