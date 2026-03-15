namespace VoxPopuli.Renderer;

using System;
using System.Numerics;
using VoxPopuli.Game;

internal sealed class CpuChunkMeshBuilder : IChunkMeshBuilder
{
    // 6 faces: (normal direction, 4 quad corner offsets in CCW winding order)
    private static readonly (Vector3 normal, Vector3[] corners)[] Faces =
    [
        (new Vector3( 0,  0,  1), [new(0,0,1), new(1,0,1), new(1,1,1), new(0,1,1)]), // +Z
        (new Vector3( 0,  0, -1), [new(1,0,0), new(0,0,0), new(0,1,0), new(1,1,0)]), // -Z
        (new Vector3( 1,  0,  0), [new(1,0,1), new(1,0,0), new(1,1,0), new(1,1,1)]), // +X
        (new Vector3(-1,  0,  0), [new(0,0,0), new(0,0,1), new(0,1,1), new(0,1,0)]), // -X
        (new Vector3( 0,  1,  0), [new(0,1,1), new(1,1,1), new(1,1,0), new(0,1,0)]), // +Y
        (new Vector3( 0, -1,  0), [new(0,0,0), new(1,0,0), new(1,0,1), new(0,0,1)]), // -Y
    ];

    public int Build(VoxelWorld world, int chunkIndex, Span<VoxelVertex> output)
    {
        var origin = VoxelWorld.ChunkOrigin(chunkIndex);
        int ox = (int)origin.X, oy = (int)origin.Y, oz = (int)origin.Z;
        int count = 0;

        var chunk = world.GetChunk(chunkIndex);

        for (int z = 0; z < Chunk.SIZE; z++)
        for (int y = 0; y < Chunk.SIZE; y++)
        for (int x = 0; x < Chunk.SIZE; x++)
        {
            byte typeId = chunk.Get(x, y, z);
            if (typeId == 0) continue;

            var worldBase = new Vector3(ox + x, oy + y, oz + z);

            foreach (var (normal, corners) in Faces)
            {
                // Skip face if neighbor is solid (face culling)
                if (world.GetVoxel(ox + x + (int)normal.X, oy + y + (int)normal.Y, oz + z + (int)normal.Z) != 0)
                    continue;

                // Emit quad as 2 triangles (CCW), 6 vertices: (v0,v1,v2) + (v0,v2,v3)
                var v0 = worldBase + corners[0];
                var v1 = worldBase + corners[1];
                var v2 = worldBase + corners[2];
                var v3 = worldBase + corners[3];

                output[count++] = new VoxelVertex(v0, typeId);
                output[count++] = new VoxelVertex(v1, typeId);
                output[count++] = new VoxelVertex(v2, typeId);
                output[count++] = new VoxelVertex(v0, typeId);
                output[count++] = new VoxelVertex(v2, typeId);
                output[count++] = new VoxelVertex(v3, typeId);
            }
        }

        return count;
    }
}
