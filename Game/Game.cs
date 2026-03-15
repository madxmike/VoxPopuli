namespace VoxPopuli.Game;

using VoxPopuli.Renderer;

internal sealed class VoxGame : IDisposable
{
    private readonly IRenderer _renderer;
    private readonly VoxelWorld _world;
    private readonly GodCamera _camera = new();
    private bool _wireframe;

    internal VoxGame(IRenderer renderer)
    {
        _renderer = renderer;
        _world = new VoxelWorld();
        TerrainGenerator.GenerateWorld(_world, seed: 42);
    }

    internal void TriggerEdit()
    {
        _world.SetBlock(240, 0, 240, 32, 32, 32, 0);
    }

    internal void ToggleWireframe() => _wireframe = !_wireframe;

    internal void Tick(CameraInput input)
    {
        var view = _camera.Update(input);
        _renderer.DrawFrame(view, _world, _wireframe);
    }

    public void Dispose() { }
}
