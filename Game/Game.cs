namespace VoxPopuli.Game;

using System.Numerics;
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

    internal void DeleteClosestChunk(Vector3 eye, Vector3 target)
    {
        var dir = Vector3.Normalize(target - eye);
        int ci = _world.RaycastChunk(eye, dir);
        if (ci >= 0) _world.DeleteChunk(ci);
    }

    internal void ToggleWireframe() => _wireframe = !_wireframe;

    internal void MarkAllChunksDirty() => _world.MarkAllChunksDirty();

    internal VoxelWorld World => _world;

    internal CameraView Tick(CameraInput input)
    {
        var view = _camera.Update(input);
        _renderer.DrawFrame(view, _world, _wireframe);
        return view;
    }

    public void Dispose() { }
}
