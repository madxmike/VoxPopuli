namespace VoxPopuli.Renderer;

/// <summary>
/// Builds a greedy-free voxel mesh from a 34³ padded chunk array (32³ owned + 1-voxel ghost border).
/// The padded index formula is: (x+1) + (y+1)*34 + (z+1)*34*34.
/// Only faces adjacent to air (type 0) are emitted, culling interior faces automatically.
/// </summary>
public static class MeshBuilder
{
    // One color per voxel type (index = type ID). 0xRRGGBB00 format.
    public static readonly uint[] TypeColors =
    [
        0x00000000, // 0 = air (unused)
        0xCC4444_00, // 1 = red
        0x44CC44_00, // 2 = green
        0x4444CC_00, // 3 = blue
        0xCCCC44_00, // 4 = yellow
        0x44CCCC_00, // 5 = cyan
        0xCC44CC_00, // 6 = magenta
        0xCC8844_00, // 7 = orange
        0x888888_00, // 8 = grey
    ];

    // Face neighbor offsets: +X, -X, +Y, -Y, +Z, -Z
    private static readonly (int dx, int dy, int dz)[] Neighbors =
    [
        ( 1,  0,  0),
        (-1,  0,  0),
        ( 0,  1,  0),
        ( 0, -1,  0),
        ( 0,  0,  1),
        ( 0,  0, -1),
    ];

    // 4 corners per face as (dx,dy,dz) offsets from voxel origin.
    // Wound CCW when viewed from outside.
    private static readonly (byte ox, byte oy, byte oz)[,] FaceCorners = new (byte, byte, byte)[6, 4]
    {
        { (1,0,0), (1,1,0), (1,1,1), (1,0,1) }, // +X
        { (0,0,1), (0,1,1), (0,1,0), (0,0,0) }, // -X
        { (0,1,0), (0,1,1), (1,1,1), (1,1,0) }, // +Y
        { (1,0,0), (1,0,1), (0,0,1), (0,0,0) }, // -Y
        { (1,0,1), (1,1,1), (0,1,1), (0,0,1) }, // +Z
        { (0,0,0), (0,1,0), (1,1,0), (1,0,0) }, // -Z
    };

    /// <summary>
    /// Populates <paramref name="verts"/> and <paramref name="indices"/> from a 34³ padded voxel array.
    /// Truncates gracefully if the output spans are too small to hold all geometry.
    /// </summary>
    /// <param name="padded">34³ = 39304 bytes; index = (x+1)+(y+1)*34+(z+1)*1156.</param>
    /// <param name="verts">Output vertex buffer; must be sized to at least the expected face count * 4.</param>
    /// <param name="indices">Output index buffer; must be sized to at least the expected face count * 6.</param>
    /// <param name="vertCount">Number of vertices written.</param>
    /// <param name="idxCount">Number of indices written.</param>
    public static void Build(
        ReadOnlySpan<byte> padded,
        Span<VoxelVertex> verts,
        Span<uint> indices,
        out int vertCount,
        out int idxCount)
    {
        vertCount = 0;
        idxCount = 0;

        for (int z = 0; z < 32; z++)
        for (int y = 0; y < 32; y++)
        for (int x = 0; x < 32; x++)
        {
            byte type = padded[(x + 1) + (y + 1) * 34 + (z + 1) * 1156];
            if (type == 0) continue;

            uint color = type < TypeColors.Length ? TypeColors[type] : 0x888888_00;

            for (int face = 0; face < 6; face++)
            {
                var (dx, dy, dz) = Neighbors[face];
                byte neighbor = padded[(x + 1 + dx) + (y + 1 + dy) * 34 + (z + 1 + dz) * 1156];
                if (neighbor != 0) continue;

                // Truncate gracefully if spans are full
                if (vertCount + 4 > verts.Length || idxCount + 6 > indices.Length) return;

                uint baseIdx = (uint)vertCount;
                for (int c = 0; c < 4; c++)
                {
                    var (ox, oy, oz) = FaceCorners[face, c];
                    verts[vertCount++] = new VoxelVertex(
                        (byte)(x + ox), (byte)(y + oy), (byte)(z + oz),
                        (byte)face, color);
                }

                indices[idxCount++] = baseIdx;
                indices[idxCount++] = baseIdx + 1;
                indices[idxCount++] = baseIdx + 2;
                indices[idxCount++] = baseIdx;
                indices[idxCount++] = baseIdx + 2;
                indices[idxCount++] = baseIdx + 3;
            }
        }
    }
}
