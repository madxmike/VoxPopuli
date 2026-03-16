namespace VoxPopuli.Tests.UI;

using System.Numerics;
using VoxPopuli.Renderer.UI;
using Xunit;

public sealed class UIDrawContextTests
{
    [Fact]
    public void DrawRect_WritesExactly6Vertices()
    {
        var buffer = new UIQuadVertex[4096 * 6];
        var count = 0;
        var context = new UIDrawContext(buffer, 1920, 1080, ref count);

        context.DrawRect(new Vector2(0, 0), new Vector2(100, 50), Color4.White);

        Assert.Equal(6, context.Vertices.Length);
    }

    [Fact]
    public void DrawRect_Anchor_TopLeft_MatchesExpectedPosition()
    {
        var buffer = new UIQuadVertex[4096 * 6];
        var count = 0;
        var context = new UIDrawContext(buffer, 1920, 1080, ref count);

        context.DrawRect(UIAnchor.TopLeft, new Vector2(10, 20), new Vector2(100, 50), Color4.White);

        var vertices = context.Vertices;
        Assert.Equal(new Vector2(10, 20), vertices[0].Position);
    }

    [Fact]
    public void DrawRect_Anchor_BottomRight_MatchesExpectedPosition()
    {
        var buffer = new UIQuadVertex[4096 * 6];
        var count = 0;
        var context = new UIDrawContext(buffer, 1920, 1080, ref count);

        context.DrawRect(UIAnchor.BottomRight, new Vector2(-10, -20), new Vector2(100, 50), Color4.White);

        var vertices = context.Vertices;
        // BottomRight: x = width - size.X + offset.X = 1920 - 100 + (-10) = 1810
        //              y = height - size.Y + offset.Y = 1080 - 50 + (-20) = 1010
        Assert.Equal(new Vector2(1810, 1010), vertices[0].Position);
    }

    [Fact]
    public void DrawRect_Anchor_BottomLeft_MatchesExpectedPosition()
    {
        var buffer = new UIQuadVertex[4096 * 6];
        var count = 0;
        var context = new UIDrawContext(buffer, 1920, 1080, ref count);

        context.DrawRect(UIAnchor.BottomLeft, new Vector2(10, -20), new Vector2(100, 50), Color4.White);

        var vertices = context.Vertices;
        // BottomLeft: x = offset.X = 10
        //             y = height - size.Y + offset.Y = 1080 - 50 + (-20) = 1010
        Assert.Equal(new Vector2(10, 1010), vertices[0].Position);
    }

    [Fact]
    public void DrawRect_Anchor_Center_MatchesExpectedPosition()
    {
        var buffer = new UIQuadVertex[4096 * 6];
        var count = 0;
        var context = new UIDrawContext(buffer, 1920, 1080, ref count);

        context.DrawRect(UIAnchor.Center, new Vector2(5, 10), new Vector2(100, 50), Color4.White);

        var vertices = context.Vertices;
        // Center: x = (width - size.X) / 2 + offset.X = (1920 - 100) / 2 + 5 = 915
        //         y = (height - size.Y) / 2 + offset.Y = (1080 - 50) / 2 + 10 = 525
        Assert.Equal(new Vector2(915, 525), vertices[0].Position);
    }

    [Fact]
    public void DrawRect_Anchor_TopCenter_MatchesExpectedPosition()
    {
        var buffer = new UIQuadVertex[4096 * 6];
        var count = 0;
        var context = new UIDrawContext(buffer, 1920, 1080, ref count);

        context.DrawRect(UIAnchor.TopCenter, new Vector2(-5, 20), new Vector2(100, 50), Color4.White);

        var vertices = context.Vertices;
        // TopCenter: x = (width - size.X) / 2 + offset.X = (1920 - 100) / 2 + (-5) = 905
        //            y = offset.Y = 20
        Assert.Equal(new Vector2(905, 20), vertices[0].Position);
    }

    [Fact]
    public void DrawRect_Anchor_BottomCenter_MatchesExpectedPosition()
    {
        var buffer = new UIQuadVertex[4096 * 6];
        var count = 0;
        var context = new UIDrawContext(buffer, 1920, 1080, ref count);

        context.DrawRect(UIAnchor.BottomCenter, new Vector2(0, -10), new Vector2(100, 50), Color4.White);

        var vertices = context.Vertices;
        // BottomCenter: x = (width - size.X) / 2 + offset.X = (1920 - 100) / 2 + 0 = 910
        //               y = height - size.Y + offset.Y = 1080 - 50 + (-10) = 1020
        Assert.Equal(new Vector2(910, 1020), vertices[0].Position);
    }

#if !DEBUG
    [Fact]
    public void DrawRect_Overflow_Release_SetsIsOverflowed()
    {
        var buffer = new UIQuadVertex[12]; // Capacity for 2 quads (2 * 6 = 12)
        var count = 0;
        var context = new UIDrawContext(buffer, 1920, 1080, ref count);

        // Fill buffer to capacity
        context.DrawRect(new Vector2(0, 0), new Vector2(100, 50), Color4.White);
        context.DrawRect(new Vector2(0, 0), new Vector2(100, 50), Color4.White);

        // Call DrawRect one more time - should set IsOverflowed
        context.DrawRect(new Vector2(0, 0), new Vector2(100, 50), Color4.White);

        Assert.True(context.IsOverflowed);
        Assert.Equal(12, context.Vertices.Length); // No partial write
    }
#endif
}
