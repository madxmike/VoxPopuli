namespace VoxPopuli.Game;

using System.Numerics;
using SDL;
using VoxPopuli.Renderer;

internal sealed unsafe class VoxGame : IDisposable
{
    private readonly SdlGpuDevice _gpu;
    private readonly VoxelWorld _world;
    private readonly ChunkMeshPool _pool;
    private readonly VoxelRenderer _renderer;
    private readonly GodCamera _camera = new();
    private readonly int _slot;

    internal VoxGame(SdlGpuDevice gpu)
    {
        _gpu    = gpu;
        _world  = new VoxelWorld();
        _pool   = new ChunkMeshPool(gpu.Device);
        _renderer = new VoxelRenderer(gpu, _pool);

        _world.SetBlock(0, 0, 0, 32, 32, 32, 1);

        var verts   = new VoxelVertex[ChunkMeshPool.VerticesPerSlot];
        var indices = new uint[ChunkMeshPool.IndicesPerSlot];
        MeshBuilder.Build(_world.GetPaddedChunkVoxels(0), verts, indices, out int vc, out int ic);

        _slot = _pool.AllocSlot();
        var cmd = gpu.AcquireCommandBuffer();
        _pool.UploadMesh(cmd, _slot, verts, indices, vc, ic);
        SDL3.SDL_SubmitGPUCommandBuffer(cmd);
    }

    internal void Tick(CameraInput input)
    {
        var view = _camera.Update(input);
        var cmd  = _gpu.AcquireCommandBuffer();

        SDL_GPUTexture* swapchain;
        uint sw, sh;
        SDL3.SDL_WaitAndAcquireGPUSwapchainTexture(cmd, _gpu.Window, &swapchain, &sw, &sh);
        if (swapchain == null) { SDL3.SDL_CancelGPUCommandBuffer(cmd); return; }

        _renderer.Render(cmd, swapchain, sw, sh, _slot, VoxelWorld.ChunkOrigin(0), view);
        SDL3.SDL_SubmitGPUCommandBuffer(cmd);
    }

    public void Dispose()
    {
        _renderer.Dispose();
        _pool.Dispose();
    }
}
