namespace VoxPopuli.Tests;

using System;
using System.Collections.Generic;
using System.Numerics;
using VoxPopuli.Game;
using VoxPopuli.Renderer;
using Xunit;

public sealed class GreedyChunkMeshBuilderTests
{
    private readonly CpuChunkMeshBuilder _naive = new();
    private readonly GreedyChunkMeshBuilder _greedy = new();
    private readonly VoxelVertex[] _naiveOut = new VoxelVertex[VoxelVertex.MaxVerticesPerChunk];
    private readonly VoxelVertex[] _greedyOut = new VoxelVertex[VoxelVertex.MaxVerticesPerChunk];

    // For a single isolated voxel, greedy and naive must produce identical vertex sets (order may differ)
    [Fact]
    public void SingleVoxel_MatchesNaiveBuilder_AllFaces()
    {
        var world = new VoxelWorld();
        world.SetVoxel(5, 5, 5, 1);

        int nc = _naive.Build(world, 0, _naiveOut);
        int gc = _greedy.Build(world, 0, _greedyOut);

        Assert.Equal(nc, gc);
        AssertSameVertexSets(_naiveOut.AsSpan(0, nc), _greedyOut.AsSpan(0, gc));
    }

    [Fact]
    public void EmptyChunk_Returns0()
    {
        var world = new VoxelWorld();
        Assert.Equal(0, _greedy.Build(world, 0, _greedyOut));
    }

    [Fact]
    public void CrossChunkCulling_MatchesNaive()
    {
        var world = new VoxelWorld();
        world.SetVoxel(31, 5, 5, 1);
        world.SetVoxel(32, 5, 5, 1);

        int nc = _naive.Build(world, 0, _naiveOut);
        int gc = _greedy.Build(world, 0, _greedyOut);

        Assert.Equal(nc, gc);
        AssertSameVertexSets(_naiveOut.AsSpan(0, nc), _greedyOut.AsSpan(0, gc));
    }

    // Two adjacent same-type voxels: greedy should emit fewer vertices (merged quads)
    [Fact]
    public void TwoAdjacentVoxels_FewerVerticesThanNaive()
    {
        var world = new VoxelWorld();
        world.SetVoxel(5, 5, 5, 1);
        world.SetVoxel(6, 5, 5, 1);

        int nc = _naive.Build(world, 0, _naiveOut);
        int gc = _greedy.Build(world, 0, _greedyOut);

        Assert.True(gc < nc, $"Expected greedy ({gc}) < naive ({nc})");
    }

    private static void AssertSameVertexSets(ReadOnlySpan<VoxelVertex> a, ReadOnlySpan<VoxelVertex> b)
    {
        // Compare as sets of triangles (each 3 vertices), order-independent
        var triA = ToTriangleSet(a);
        var triB = ToTriangleSet(b);
        Assert.Equal(triA.Count, triB.Count);
        foreach (var t in triA)
            Assert.Contains(t, triB);
    }

    private static HashSet<string> ToTriangleSet(ReadOnlySpan<VoxelVertex> verts)
    {
        var set = new HashSet<string>();
        for (int i = 0; i + 2 < verts.Length; i += 3)
        {
            // Normalize triangle by sorting its 3 vertices so order doesn't matter
            var pts = new[] { Fmt(verts[i]), Fmt(verts[i+1]), Fmt(verts[i+2]) };
            Array.Sort(pts, StringComparer.Ordinal);
            set.Add(string.Join("|", pts));
        }
        return set;
    }

    private static string Fmt(VoxelVertex v) =>
        $"{v.Position.X:F2},{v.Position.Y:F2},{v.Position.Z:F2},{v.TypeId}";
}
