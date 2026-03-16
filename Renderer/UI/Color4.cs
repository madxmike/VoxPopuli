namespace VoxPopuli.Renderer.UI;

/// <summary>Immutable RGBA color with components in 0–1 range.</summary>
internal readonly struct Color4
{
    /// <summary>Red component.</summary>
    public readonly float R;
    /// <summary>Green component.</summary>
    public readonly float G;
    /// <summary>Blue component.</summary>
    public readonly float B;
    /// <summary>Alpha component.</summary>
    public readonly float A;

    /// <summary>Creates a new Color4 with the specified components.</summary>
    /// <param name="r">Red component (0–1).</param>
    /// <param name="g">Green component (0–1).</param>
    /// <param name="b">Blue component (0–1).</param>
    /// <param name="a">Alpha component (0–1).</param>
    internal Color4(float r, float g, float b, float a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    /// <summary>Opaque white color (1,1,1,1).</summary>
    public static readonly Color4 White = new(1f, 1f, 1f, 1f);

    /// <summary>Opaque black color (0,0,0,1).</summary>
    public static readonly Color4 Black = new(0f, 0f, 0f, 1f);

    /// <summary>Transparent color (0,0,0,0).</summary>
    public static readonly Color4 Transparent = new(0f, 0f, 0f, 0f);
}
