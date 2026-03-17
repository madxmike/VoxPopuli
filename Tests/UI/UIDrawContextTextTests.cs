namespace VoxPopuli.Tests.UI;

using System.Numerics;
using VoxPopuli.Renderer.UI;
using Xunit;

public sealed class UIDrawContextTextTests
{
    [Fact]
    public void DrawText_QueuesOneRequest()
    {
        var quadBuffer = Span<UIQuadVertex>.Empty;
        var textBuffer = new UITextData[256];
        var quadCount = 0;
        var textCount = 0;
        var context = new UIDrawContext(quadBuffer, 1920, 1080, ref quadCount, textBuffer, ref textCount);

        var position = new Vector2(100, 200);
        var color = new Color4(1.0f, 0.5f, 0.25f, 1.0f);
        var text = "Hello World";

        context.DrawText(position, text, color);

        Assert.Equal(1, context.TextData.Length);
        Assert.Equal(position, context.TextData[0].Position);
        Assert.Equal(text, context.TextData[0].Text);
        Assert.Equal(color, context.TextData[0].Color);
    }

    [Fact]
    public void DrawText_Anchor_TopRight_ResolvesPosition()
    {
        var quadBuffer = Span<UIQuadVertex>.Empty;
        var textBuffer = new UITextData[256];
        var quadCount = 0;
        var textCount = 0;
        var context = new UIDrawContext(quadBuffer, 1920, 1080, ref quadCount, textBuffer, ref textCount);

        var offset = new Vector2(-10, 20);
        var text = "Score";
        var color = Color4.White;

        context.DrawText(UIAnchor.TopRight, offset, text, color);

        Assert.Equal(1, context.TextData.Length);
        // TopRight: x = width + offset.X = 1920 + (-10) = 1910
        //           y = offset.Y = 20
        Assert.Equal(new Vector2(1910, 20), context.TextData[0].Position);
    }

    [Fact]
    public void DrawText_Anchor_BottomLeft_ResolvesPosition()
    {
        var quadBuffer = Span<UIQuadVertex>.Empty;
        var textBuffer = new UITextData[256];
        var quadCount = 0;
        var textCount = 0;
        var context = new UIDrawContext(quadBuffer, 1920, 1080, ref quadCount, textBuffer, ref textCount);

        var offset = new Vector2(5, -15);
        var text = "FPS";
        var color = Color4.White;

        context.DrawText(UIAnchor.BottomLeft, offset, text, color);

        Assert.Equal(1, context.TextData.Length);
        // BottomLeft: x = offset.X = 5
        //             y = height + offset.Y = 1080 + (-15) = 1065
        Assert.Equal(new Vector2(5, 1065), context.TextData[0].Position);
    }

    [Fact]
    public void DrawText_Anchor_Center_ResolvesPosition()
    {
        var quadBuffer = Span<UIQuadVertex>.Empty;
        var textBuffer = new UITextData[256];
        var quadCount = 0;
        var textCount = 0;
        var context = new UIDrawContext(quadBuffer, 1920, 1080, ref quadCount, textBuffer, ref textCount);

        var offset = new Vector2(10, 15);
        var text = "Title";
        var color = Color4.White;

        context.DrawText(UIAnchor.Center, offset, text, color);

        Assert.Equal(1, context.TextData.Length);
        // Center: x = width / 2 + offset.X = 1920 / 2 + 10 = 970
        //         y = height / 2 + offset.Y = 1080 / 2 + 15 = 555
        Assert.Equal(new Vector2(970, 555), context.TextData[0].Position);
    }

#if !DEBUG
    [Fact]
    public void DrawText_Overflow_Release_SetsIsTextOverflowed()
    {
        var quadBuffer = Span<UIQuadVertex>.Empty;
        var textBuffer = new UITextData[2];
        var quadCount = 0;
        var textCount = 0;
        var context = new UIDrawContext(quadBuffer, 1920, 1080, ref quadCount, textBuffer, ref textCount);

        // Fill buffer to capacity
        context.DrawText(new Vector2(0, 0), "First", Color4.White);
        context.DrawText(new Vector2(0, 0), "Second", Color4.White);

        // Call DrawText one more time - should set IsTextOverflowed
        context.DrawText(new Vector2(0, 0), "Third", Color4.White);

        Assert.True(context.IsTextOverflowed);
        Assert.Equal(2, context.TextData.Length); // No partial write
    }
#endif
}
