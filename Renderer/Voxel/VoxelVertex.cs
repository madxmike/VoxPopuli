namespace VoxPopuli.Renderer;

using System.Numerics;
using System.Runtime.InteropServices;

/// <summary>GPU vertex format for voxel rendering. Packed into 16 bytes for memory efficiency.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct VoxelVertex
{
    /// <summary>World-space position.</summary>
    public readonly Vector3 Position;
    /// <summary>Bits [29:0] = voxel type, bits [31:30] = AO (0-3).</summary>
    public readonly uint TypeId;

    /// <summary>Creates a vertex with position and type, AO defaults to 0.</summary>
    public VoxelVertex(Vector3 position, uint typeId) { Position = position; TypeId = typeId; }

    /// <summary>Creates a vertex with position, type, and ambient occlusion.</summary>
    public VoxelVertex(Vector3 position, uint typeId, int ao)
    {
        Position = position;
        TypeId = (typeId & 0x3FFFFFFFu) | ((uint)(ao & 3) << 30);
    }

    /// <summary>Upper bound on vertices per chunk if every voxel face is visible.</summary>
    public const int MaxVerticesPerChunk = 32 * 32 * 32 * 6 * 6;
}
