namespace VoxPopuli.Renderer;

using System.Collections.Generic;
using System.Numerics;
using SDL;
using VoxPopuli.Game;
using VoxPopuli.Renderer.UI;

/// <summary>Main renderer implementation using SDL3 GPU API.</summary>
internal sealed unsafe class SdlRenderer : IRenderer
{
    private readonly SdlGpuDevice _gpu;
    private readonly List<ISubRenderer> _subRenderers = new();
    private readonly UIRenderer _uiRenderer;
    private Matrix4x4 _proj;
    private uint _projW, _projH;

    /// <summary>Creates a renderer with the given GPU device and color table.</summary>
    internal SdlRenderer(SdlGpuDevice gpu, ReadOnlySpan<Vector4> colorTable)
    {
        _gpu = gpu;
        _subRenderers.Add(new VoxelChunkRenderer(gpu, colorTable, () => new GreedyChunkMeshBuilder()));
        _uiRenderer = new UIRenderer(gpu, "/System/Library/Fonts/Helvetica.ttc", 16f);
    }

    /// <summary>Draws a frame with the given camera view and world.</summary>
    public void DrawFrame(CameraView view, VoxelWorld world, bool wireframe = false)
    {
        var cmd = _gpu.AcquireCommandBuffer();
        SDL_GPUTexture* swapchain; uint sw, sh;
        SDL3.SDL_WaitAndAcquireGPUSwapchainTexture(cmd, _gpu.Window, &swapchain, &sw, &sh);
        if (swapchain == null) { SDL3.SDL_CancelGPUCommandBuffer(cmd); return; }

        _gpu.ResizeDepthTextureIfNeeded(sw, sh);

        if (sw != _projW || sh != _projH)
        {
            _proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, (float)sw / sh, 0.1f, 2000f);
            _projW = sw; _projH = sh;
        }

        var view_matrix = Matrix4x4.CreateLookAt(view.Eye, view.Target, view.Up);
        var viewProj = view_matrix * _proj;
        var frame = new RenderFrame
        {
            ViewProj = viewProj,
            View = view_matrix,
            Width = sw,
            Height = sh,
            RenderPass = null,
            CommandBuffer = cmd,
            World = world,
            Wireframe = wireframe,
            Frustum = Frustum.FromViewProj(viewProj),
            Gpu = _gpu
        };

        foreach (var sub in _subRenderers)
        {
            sub.PrepareFrame(cmd, in frame);
        }

        _uiRenderer.PrepareFrame(cmd, in frame);

        var colorTarget = new SDL_GPUColorTargetInfo
        {
            texture = swapchain,
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
            store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
            clear_color = new SDL_FColor { r = 0f, g = 0f, b = 0f, a = 1f }
        };
        var depthTarget = new SDL_GPUDepthStencilTargetInfo
        {
            texture = _gpu.DepthTexture,
            clear_depth = 1.0f,
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
            store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
            stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,

            stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
            cycle = true
        };

        var pass = SDL3.SDL_BeginGPURenderPass(cmd, &colorTarget, 1, &depthTarget);
        frame = frame with { RenderPass = pass };

        foreach (var sub in _subRenderers)
        {
            sub.Draw(in frame);
        }

        _uiRenderer.Draw(in frame);

        SDL3.SDL_EndGPURenderPass(pass);
        SDL3.SDL_SubmitGPUCommandBuffer(cmd);
    }

    /// <summary>Gets a frame-scoped UIDrawContext for UI rendering.</summary>
    public UIDrawContext GetUIDrawContext()
    {
        return _uiRenderer.GetUIDrawContext();
    }

    /// <summary>Disposes all sub-renderers and the UI renderer.</summary>
    public void Dispose()
    {
        foreach (var sub in _subRenderers)
        {
            sub.Dispose();
        }
        _uiRenderer.Dispose();
    }
}
