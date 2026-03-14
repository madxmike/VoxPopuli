namespace VoxPopuli.Game;

using SDL;
using VoxPopuli.Renderer;

internal sealed unsafe class VoxGame : IDisposable
{
    private readonly SdlGpuDevice _gpu;
    private readonly VoxelWorld _world;
    private readonly ChunkMeshPool _pool;
    private readonly VoxelRenderer _renderer;
    private readonly MeshWorker _meshWorker;
    private readonly GodCamera _camera = new();

    internal VoxGame(SdlGpuDevice gpu)
    {
        _gpu      = gpu;
        _world    = new VoxelWorld();
        _pool     = new ChunkMeshPool(gpu.Device);
        _renderer = new VoxelRenderer(gpu, _pool);

        // Fill world — marks chunks dirty, no mesh build here
        _world.SetBlock(0, 0,  0, VoxelWorld.WorldSizeX, 32, VoxelWorld.WorldSizeZ, 1);
        _world.SetBlock(0, 32, 0, VoxelWorld.WorldSizeX, 32, VoxelWorld.WorldSizeZ, 2);

        _meshWorker = new MeshWorker(_world, _pool);
    }

    internal void TriggerEdit()
    {
        // Remove a 32x32x32 region near world center
        _world.SetBlock(240, 0, 240, 32, 32, 32, 0);
    }

    internal void Tick(CameraInput input)
    {
        // Drain dirty chunks → enqueue for async meshing
        foreach (var ci in _world.DrainDirtyChunks())
            _meshWorker.EnqueueChunk(ci);

        var view = _camera.Update(input);
        var cmd  = _gpu.AcquireCommandBuffer();

        SDL_GPUTexture* swapchain;
        uint sw, sh;
        SDL3.SDL_WaitAndAcquireGPUSwapchainTexture(cmd, _gpu.Window, &swapchain, &sw, &sh);
        if (swapchain == null) { SDL3.SDL_CancelGPUCommandBuffer(cmd); return; }

        _renderer.Render(cmd, swapchain, sw, sh, _world, view, _meshWorker);
        SDL3.SDL_SubmitGPUCommandBuffer(cmd);
    }

    public void Dispose()
    {
        _meshWorker.Dispose();
        _renderer.Dispose();
        _pool.Dispose();
    }
}
