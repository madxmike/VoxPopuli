namespace VoxPopuli.Renderer;

using System.Runtime.InteropServices;

/// <summary>
/// A single voxel mesh vertex packed into 8 bytes.
/// X, Y, Z are local chunk coordinates (0-31); Face is the face index (0-5).
/// Color is packed as 0xRRGGBB00.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct VoxelVertex
{
    public readonly byte X, Y, Z, Face;
    public readonly uint Color;

    public VoxelVertex(byte x, byte y, byte z, byte face, uint color)
    {
        X = x; Y = y; Z = z; Face = face; Color = color;
    }
}
