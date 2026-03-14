namespace VoxPopuli.Game;

using VoxPopuli.Renderer;

internal sealed class VoxGame : IDisposable
{
    private readonly SdlRenderer _renderer;
    private readonly VoxelWorld _world;
    private readonly GodCamera _camera = new();

    internal VoxGame(SdlRenderer renderer)
    {
        _renderer = renderer;
        _world    = new VoxelWorld();
        TerrainGenerator.GenerateWorld(_world, seed: 42);
    }

    internal void TriggerEdit()
    {
        _world.SetBlock(240, 0, 240, 32, 32, 32, 0);
    }

    internal void Tick(CameraInput input)
    {
        var view = _camera.Update(input);
        _renderer.DrawFrame(ReadOnlySpan<MeshInstance>.Empty, view);
    }

    public void Dispose() { }
}
