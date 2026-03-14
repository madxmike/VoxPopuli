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

    internal VoxGame(SdlGpuDevice gpu)
    {
        _gpu      = gpu;
        _world    = new VoxelWorld();
        _pool     = new ChunkMeshPool(gpu.Device);
        _renderer = new VoxelRenderer(gpu, _pool);

        // Fill world: solid ground layer (cy=0) type 1, second layer (cy=1) type 2
        _world.SetBlock(0, 0,  0, VoxelWorld.WorldSizeX, 32, VoxelWorld.WorldSizeZ, 1);
        _world.SetBlock(0, 32, 0, VoxelWorld.WorldSizeX, 32, VoxelWorld.WorldSizeZ, 2);

        // Build all chunk meshes synchronously
        var verts   = new VoxelVertex[ChunkMeshPool.VerticesPerSlot];
        var indices = new uint[ChunkMeshPool.IndicesPerSlot];
        var cmd     = gpu.AcquireCommandBuffer();

        for (int i = 0; i < ChunkMeshPool.MaxSlots; i++)
        {
            MeshBuilder.Build(_world.GetPaddedChunkVoxels(i), verts, indices, out int vc, out int ic);
            if (ic == 0) continue;

            int slot = _pool.AllocSlot();
            if (slot == -1) break;
            _pool.UploadMesh(cmd, slot, verts, indices, vc, ic);
            _renderer.AssignSlot(i, slot);
        }

        _renderer.UploadAllChunkOffsets(cmd, _world);
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

        _renderer.Render(cmd, swapchain, sw, sh, _world, view);
        SDL3.SDL_SubmitGPUCommandBuffer(cmd);
    }

    public void Dispose()
    {
        _renderer.Dispose();
        _pool.Dispose();
    }
}
