namespace VoxPopuli.Tests;
using VoxPopuli.Game;
using VoxPopuli.Renderer;
using Xunit;
using Xunit.Abstractions;

public sealed class FlatPlaneDebugTest(ITestOutputHelper output)
{
    [Fact]
    public void FlatPlane_TopFace_ShouldBeOneQuad()
    {
        var world = new VoxelWorld();
        for (int x = 0; x < 4; x++)
            for (int z = 0; z < 4; z++)
                world.SetVoxel(x, 5, z, 1);

        var builder = new GreedyChunkMeshBuilder();
        var buf = new VoxelVertex[VoxelVertex.MaxVerticesPerChunk];
        int verts = builder.Build(world, 0, buf);
        output.WriteLine($"Total verts: {verts}, quads: {verts/6}");
        // Dump unique Y=6 (top face) quads
        for (int i = 0; i < verts; i += 6)
        {
            output.WriteLine($"  quad[{i/6}]: q0={buf[i].Position} q1={buf[i+1].Position} q2={buf[i+2].Position} q3={buf[i+3].Position}");
        }
    }
}
