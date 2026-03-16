namespace VoxPopuli.Tests.UI;

using System;
using System.Runtime.InteropServices;
using VoxPopuli.Renderer.UI;
using Xunit;

public class UIQuadVertexTests
{
    [Fact]
    public void UIQuadVertex_StructSize_Is24Bytes()
    {
        var size = Marshal.SizeOf<UIQuadVertex>();
        Assert.Equal(24, size);
    }
}
