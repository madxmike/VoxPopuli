namespace VoxPopuli.Renderer.UI;

using System;
using System.Numerics;

/// <summary>Anchor points for UI positioning.</summary>
internal enum UIAnchor
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Center,
    TopCenter,
    BottomCenter,
}

/// <summary>Frame-scoped quad writer. Writes vertices directly into a caller-supplied buffer.</summary>
/// <remarks>ref struct enforces frame-scoped usage — cannot be stored on the heap or escape the frame.</remarks>
internal ref struct UIDrawContext
{
    private Span<UIQuadVertex> _buffer;
    private ref int _countRef;
    private Span<UITextData> _textBuffer;
    private ref int _textCountRef;
    private uint _width;
    private uint _height;
    private bool _isOverflowed;
    private bool _isTextOverflowed;

    /// <summary>Constructs a new UIDrawContext with the specified buffer and screen dimensions.</summary>
    /// <param name="buffer">Span of UIQuadVertex to write vertices into.</param>
    /// <param name="width">Screen width in pixels.</param>
    /// <param name="height">Screen height in pixels.</param>
    /// <param name="countRef">Reference to the renderer's vertex count to increment directly.</param>
    /// <param name="textBuffer">Span of UITextData to write text draw requests into.</param>
    /// <param name="textCountRef">Reference to the renderer's text count to increment directly.</param>
    internal UIDrawContext(Span<UIQuadVertex> buffer, uint width, uint height, ref int countRef, Span<UITextData> textBuffer, ref int textCountRef)
    {
        _buffer = buffer;
        _countRef = ref countRef;
        _textBuffer = textBuffer;
        _textCountRef = ref textCountRef;
        _width = width;
        _height = height;
        _isOverflowed = false;
        _isTextOverflowed = false;
    }

    /// <summary>Writes a quad with the specified screen-space position and size.</summary>
    /// <param name="position">Top-left position in screen-space pixels.</param>
    /// <param name="size">Width and height of the quad.</param>
    /// <param name="color">RGBA color of the quad.</param>
    /// <remarks>Writes 6 vertices (2 triangles, clockwise winding: TL, TR, BR, TL, BR, BL).</remarks>
    internal void DrawRect(Vector2 position, Vector2 size, Color4 color)
    {
#if DEBUG
        if (_countRef + 6 > _buffer.Length)
        {
            throw new UIRenderingException("Vertex buffer overflow");
        }
#else
        if (_countRef + 6 > _buffer.Length)
        {
            _isOverflowed = true;
            return;
        }
#endif

        float x = position.X;
        float y = position.Y;
        float w = size.X;
        float h = size.Y;

        // TL, TR, BR, TL, BR, BL (clockwise winding)
        _buffer[_countRef + 0] = new UIQuadVertex { Position = new Vector2(x, y), Color = color };       // TL
        _buffer[_countRef + 1] = new UIQuadVertex { Position = new Vector2(x + w, y), Color = color };   // TR
        _buffer[_countRef + 2] = new UIQuadVertex { Position = new Vector2(x + w, y + h), Color = color }; // BR
        _buffer[_countRef + 3] = new UIQuadVertex { Position = new Vector2(x, y), Color = color };       // TL
        _buffer[_countRef + 4] = new UIQuadVertex { Position = new Vector2(x + w, y + h), Color = color }; // BR
        _buffer[_countRef + 5] = new UIQuadVertex { Position = new Vector2(x, y + h), Color = color };   // BL

        _countRef += 6;
    }

    /// <summary>Writes a quad with anchor-based positioning.</summary>
    /// <param name="anchor">Anchor point for positioning.</param>
    /// <param name="offset">Pixel offset from the anchor position.</param>
    /// <param name="size">Width and height of the quad.</param>
    /// <param name="color">RGBA color of the quad.</param>
    internal void DrawRect(UIAnchor anchor, Vector2 offset, Vector2 size, Color4 color)
    {
        Vector2 position = ResolveAnchor(anchor, offset, size, _width, _height);
        DrawRect(position, size, color);
    }

    /// <summary>Queues a text draw request at the specified position.</summary>
    /// <param name="position">Top-left position in screen-space pixels.</param>
    /// <param name="text">The text string to render.</param>
    /// <param name="color">RGBA color of the text.</param>
    internal void DrawText(Vector2 position, string text, Color4 color)
    {
#if DEBUG
        if (_textCountRef + 1 > _textBuffer.Length)
        {
            throw new UIRenderingException("Text buffer overflow");
        }
#else
        if (_textCountRef + 1 > _textBuffer.Length)
        {
            _isTextOverflowed = true;
            return;
        }
#endif

        _textBuffer[_textCountRef] = new UITextData(position, color, text);
        _textCountRef++;
    }

    /// <summary>Queues a text draw request with anchor-based positioning.</summary>
    /// <param name="anchor">Anchor point for positioning.</param>
    /// <param name="offset">Pixel offset from the anchor position.</param>
    /// <param name="text">The text string to render.</param>
    /// <param name="color">RGBA color of the text.</param>
    internal void DrawText(UIAnchor anchor, Vector2 offset, string text, Color4 color)
    {
        Vector2 position = ResolveAnchor(anchor, offset, Vector2.Zero, _width, _height);
        DrawText(position, text, color);
    }

    /// <summary>Returns a read-only span of the vertices written this frame.</summary>
    internal readonly ReadOnlySpan<UIQuadVertex> Vertices => _buffer[.._countRef];

    /// <summary>Returns a read-only span of the text data written this frame.</summary>
    internal readonly ReadOnlySpan<UITextData> TextData => _textBuffer[.._textCountRef];

    /// <summary>True if DrawRect was called when the buffer was full (release only).</summary>
    internal bool IsOverflowed => _isOverflowed;

    /// <summary>True if DrawText was called when the text buffer was full (release only).</summary>
    internal bool IsTextOverflowed => _isTextOverflowed;

    /// <summary>Resolves an anchor + offset to a top-left pixel position.</summary>
    /// <param name="anchor">Anchor point.</param>
    /// <param name="offset">Pixel offset from the anchor.</param>
    /// <param name="size">Size of the quad being positioned.</param>
    /// <param name="width">Screen width in pixels.</param>
    /// <param name="height">Screen height in pixels.</param>
    /// <returns>Top-left pixel position in screen space.</returns>
    private static Vector2 ResolveAnchor(UIAnchor anchor, Vector2 offset, Vector2 size, uint width, uint height)
    {
        return anchor switch
        {
            UIAnchor.TopLeft => new Vector2(offset.X, offset.Y),
            UIAnchor.TopRight => new Vector2(width - size.X + offset.X, offset.Y),
            UIAnchor.BottomLeft => new Vector2(offset.X, height - size.Y + offset.Y),
            UIAnchor.BottomRight => new Vector2(width - size.X + offset.X, height - size.Y + offset.Y),
            UIAnchor.Center => new Vector2((width - size.X) / 2 + offset.X, (height - size.Y) / 2 + offset.Y),
            UIAnchor.TopCenter => new Vector2((width - size.X) / 2 + offset.X, offset.Y),
            UIAnchor.BottomCenter => new Vector2((width - size.X) / 2 + offset.X, height - size.Y + offset.Y),
            _ => throw new ArgumentException($"Unknown anchor: {anchor}")
        };
    }
}
