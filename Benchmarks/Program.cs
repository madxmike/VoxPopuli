using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using VoxPopuli.Game;
using VoxPopuli.Renderer;

BenchmarkRunner.Run<MeshBuilderBenchmarks>();

[MemoryDiagnoser]
public class MeshBuilderBenchmarks
{
    private VoxelWorld _world = null!;
    private readonly VoxelVertex[] _output = new VoxelVertex[VoxelVertex.MaxVerticesPerChunk];
    private readonly GreedyChunkMeshBuilder _greedy = new();

    [GlobalSetup]
    public void Setup()
    {
        _world = new VoxelWorld();
        TerrainGenerator.GenerateWorld(_world, seed: 42);
    }

    [Benchmark]
    public int Greedy()
    {
        int total = 0;
        for (int i = 0; i < VoxelWorld.MAX_CHUNKS; i++)
        {
            total += _greedy.Build(_world, i, _output);
        }
        return total;
    }
}
