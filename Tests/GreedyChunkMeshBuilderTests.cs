namespace VoxPopuli.Tests;

using System;
using System.Collections.Generic;
using VoxPopuli.Game;
using VoxPopuli.Renderer;
using Xunit;

public sealed class GreedyChunkMeshBuilderTests
{
    private readonly GreedyChunkMeshBuilder _greedy = new();
    private readonly VoxelVertex[] _output = new VoxelVertex[VoxelVertex.MaxVerticesPerChunk];

    [Fact]
    public void EmptyChunk_Returns0()
    {
        var world = new VoxelWorld();
        Assert.Equal(0, _greedy.Build(world, 0, _output, CancellationToken.None));
    }

    [Fact]
    public void SingleExposedVoxel_Returns36Vertices()
    {
        var world = new VoxelWorld();
        world.SetVoxel(5, 5, 5, 1);
        Assert.Equal(36, _greedy.Build(world, 0, _output, CancellationToken.None));
    }

    [Fact]
    public void FullySurroundedVoxel_EmitsNoFacesForCenter()
    {
        var world = new VoxelWorld();
        world.SetVoxel(5, 5, 5, 1);
        foreach (var (dx, dy, dz) in new[] { (1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1) })
        {
            world.SetVoxel(5+dx, 5+dy, 5+dz, 1);
        }
        Assert.Equal(180, _greedy.Build(world, 0, _output, CancellationToken.None));
    }

    [Fact]
    public void CrossChunkCulling_BorderFaceNotEmitted()
    {
        var world = new VoxelWorld();
        world.SetVoxel(31, 5, 5, 1);
        world.SetVoxel(32, 5, 5, 1);
        Assert.Equal(30, _greedy.Build(world, 0, _output, CancellationToken.None));
    }

    [Fact]
    public void TwoAdjacentVoxels_FewerVerticesThanSingleVoxelPair()
    {
        var world = new VoxelWorld();
        world.SetVoxel(5, 5, 5, 1);
        world.SetVoxel(6, 5, 5, 1);
        int count = _greedy.Build(world, 0, _output, CancellationToken.None);
        // Two isolated voxels would be 72; merged faces reduce this
        Assert.True(count < 72);
    }

    [Fact]
    public void WorldSpacePositions_OffsetByChunkOrigin()
    {
        int ci = VoxelWorld.ChunkIndex(1, 0, 0);
        var world = new VoxelWorld();
        world.SetVoxel(32, 0, 0, 1);
        int count = _greedy.Build(world, ci, _output, CancellationToken.None);
        Assert.True(count > 0);
        for (int i = 0; i < count; i++)
        {
            Assert.True(_output[i].Position.X >= 32f);
        }
    }

    internal static HashSet<string> ToTriangleSet(ReadOnlySpan<VoxelVertex> verts)
    {
        var set = new HashSet<string>();
        for (int i = 0; i + 2 < verts.Length; i += 3)
        {
            var pts = new[] { Fmt(verts[i]), Fmt(verts[i+1]), Fmt(verts[i+2]) };
            Array.Sort(pts, StringComparer.Ordinal);
            set.Add(string.Join("|", pts));
        }
        return set;
    }

    private static string Fmt(VoxelVertex v) =>
        $"{v.Position.X:F2},{v.Position.Y:F2},{v.Position.Z:F2},{v.TypeId}";
}
