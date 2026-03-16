namespace VoxPopuli.Renderer.UI;

using System.Numerics;
using System.Runtime.InteropServices;

/// <summary>Per-vertex data for UI quads. Plain data struct with no methods.</summary>
/// <remarks>Layout: Position (offset 0, 8 bytes) + Color (offset 8, 16 bytes) = 24 bytes total.</remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct UIQuadVertex
{
    /// <summary>Screen-space position in pixels, origin top-left.</summary>
    public Vector2 Position;

    /// <summary>RGBA color with components in 0–1 range.</summary>
    public Color4 Color;
}
