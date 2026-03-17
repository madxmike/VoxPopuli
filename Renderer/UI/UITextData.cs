namespace VoxPopuli.Renderer.UI;

using System.Numerics;

/// <summary>Plain data struct queued by DrawText, consumed by TextRenderer.Draw.</summary>
internal readonly struct UITextData
{
    public Vector2 Position { get; }
    public Color4 Color { get; }
    public string Text { get; }

    public UITextData(Vector2 position, Color4 color, string text)
    {
        Position = position;
        Color = color;
        Text = text;
    }
}
