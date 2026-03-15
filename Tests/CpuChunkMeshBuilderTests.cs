namespace VoxPopuli.Tests;

using VoxPopuli.Game;
using VoxPopuli.Renderer;
using Xunit;

public sealed class CpuChunkMeshBuilderTests
{
    private readonly CpuChunkMeshBuilder _builder = new();
    private readonly VoxelVertex[] _output = new VoxelVertex[VoxelVertex.MaxVerticesPerChunk];

    [Fact]
    public void EmptyChunk_Returns0()
    {
        var world = new VoxelWorld();
        Assert.Equal(0, _builder.Build(world, 0, _output));
    }

    [Fact]
    public void SingleExposedVoxel_Returns36Vertices()
    {
        // Voxel at (5,5,5) in chunk 0 — all 6 neighbors are air
        var world = new VoxelWorld();
        world.SetVoxel(5, 5, 5, 1);

        Assert.Equal(36, _builder.Build(world, 0, _output));
    }

    [Fact]
    public void FullySurroundedVoxel_EmitsNoFaces()
    {
        // Center voxel at (5,5,5) with all 6 neighbors solid.
        // Center has 0 exposed faces; each of the 6 neighbors has 5 exposed faces (4 outer + 0 toward center).
        // Wait — each neighbor has 1 face toward center (occluded) and 5 outer faces exposed.
        // Total expected = 6 neighbors × 5 faces × 6 vertices = 180.
        // If center contributed any faces, count would be > 180.
        var world = new VoxelWorld();
        world.SetVoxel(5, 5, 5, 1); // center — fully surrounded
        foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
            world.SetVoxel(5 + dx, 5 + dy, 5 + dz, 1);

        int count = _builder.Build(world, 0, _output);

        // Center contributes 0; 6 neighbors each have 5 exposed faces = 180 vertices
        Assert.Equal(180, count);
    }

    [Fact]
    public void WorldSpacePositions_OffsetByChunkOrigin()
    {
        // Chunk at cx=1,cy=0,cz=0 has origin (32,0,0)
        int ci = VoxelWorld.ChunkIndex(1, 0, 0);
        var world = new VoxelWorld();
        world.SetVoxel(32, 0, 0, 1); // local (0,0,0) in that chunk

        int count = _builder.Build(world, ci, _output);

        Assert.True(count > 0);
        for (int i = 0; i < count; i++)
            Assert.True(_output[i].Position.X >= 32f);
    }

    [Fact]
    public void CrossChunkCulling_BorderFaceNotEmitted()
    {
        // Voxel at world (31,5,5) in chunk 0; its +X neighbor at (32,5,5) is in chunk cx=1 and is solid.
        // The +X face of (31,5,5) must be culled.
        var world = new VoxelWorld();
        world.SetVoxel(31, 5, 5, 1);
        world.SetVoxel(32, 5, 5, 1); // solid neighbor in adjacent chunk

        // Without cross-chunk neighbor: 6 faces = 36 vertices.
        // With solid +X neighbor culled: 5 faces = 30 vertices.
        Assert.Equal(30, _builder.Build(world, 0, _output));
    }
}
