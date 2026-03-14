namespace VoxPopuli.Renderer;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using SDL;

// ── Public API ────────────────────────────────────────────────────────────────

/// <summary>
/// A single vertex in world space. Sequential layout matches the GPU pipeline:
/// float3 position (offset 0) followed by float3 color (offset 12), 24 bytes total.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Vertex
{
    public readonly Vector3 Position;
    public readonly Vector3 Color;
    public Vertex(Vector3 position, Vector3 color) { Position = position; Color = color; }
}

/// <summary>
/// Opaque reference to geometry uploaded to the GPU. The renderer owns the
/// underlying buffer; callers may only pass this handle back to the renderer.
/// </summary>
public readonly record struct MeshHandle(int Id);

/// <summary>
/// A single draw request: which mesh to draw and where to place it in world space.
/// </summary>
public readonly struct MeshInstance
{
    public readonly MeshHandle Mesh;
    public readonly Matrix4x4 Transform;
    public MeshInstance(MeshHandle mesh, Matrix4x4 transform) { Mesh = mesh; Transform = transform; }
}

/// <summary>
/// Renders geometry submitted by the game. The game never sees SDL types,
/// command buffers, or GPU resource handles — only these three methods.
/// </summary>
public interface IRenderer : IDisposable
{
    /// <summary>
    /// Uploads vertex data to the GPU and returns an opaque handle.
    /// The caller owns the handle and must call <see cref="ReleaseMesh"/> when done.
    /// </summary>
    MeshHandle UploadMesh(ReadOnlySpan<Vertex> vertices);

    /// <summary>Releases the GPU buffer associated with <paramref name="handle"/>.</summary>
    void ReleaseMesh(MeshHandle handle);

    /// <summary>
    /// Renders all <paramref name="instances"/> and presents the frame.
    /// Each instance is drawn with its own MVP derived from <see cref="MeshInstance.Transform"/>.
    /// </summary>
    void DrawFrame(ReadOnlySpan<MeshInstance> instances, Matrix4x4 view);
}

// ── Internal implementation ───────────────────────────────────────────────────

/// <summary>
/// SDL3-backed <see cref="IRenderer"/>. Translates safe game-side calls into
/// SDL GPU command buffers, transfer uploads, and render passes.
/// All unsafe code is confined to this class and <see cref="GpuDevice"/>.
/// </summary>
internal sealed unsafe class SdlRenderer : IRenderer
{
    private readonly GpuDevice _gpu;
    private readonly Dictionary<int, MeshBuffer> _meshes = new();
    private int _nextId;

    internal SdlRenderer(string title, int width, int height)
    {
        _gpu = new GpuDevice(title, width, height);
    }

    public MeshHandle UploadMesh(ReadOnlySpan<Vertex> vertices)
    {
        uint size = (uint)(vertices.Length * Marshal.SizeOf<Vertex>());

        var bufInfo = new SDL_GPUBufferCreateInfo
        {
            usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX,
            size = size
        };
        var gpuBuffer = SDL3.SDL_CreateGPUBuffer(_gpu.Device, &bufInfo);

        var xferInfo = new SDL_GPUTransferBufferCreateInfo
        {
            usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
            size = size
        };
        var xfer = SDL3.SDL_CreateGPUTransferBuffer(_gpu.Device, &xferInfo);

        var map = SDL3.SDL_MapGPUTransferBuffer(_gpu.Device, xfer, false);
        Marshal.Copy(MemoryMarshal.AsBytes(vertices).ToArray(), 0, (nint)map, (int)size);
        SDL3.SDL_UnmapGPUTransferBuffer(_gpu.Device, xfer);

        var cmd = _gpu.AcquireCommandBuffer();
        var copyPass = SDL3.SDL_BeginGPUCopyPass(cmd);
        var src = new SDL_GPUTransferBufferLocation { transfer_buffer = xfer, offset = 0 };
        var dst = new SDL_GPUBufferRegion { buffer = gpuBuffer, offset = 0, size = size };
        SDL3.SDL_UploadToGPUBuffer(copyPass, &src, &dst, false);
        SDL3.SDL_EndGPUCopyPass(copyPass);
        SDL3.SDL_SubmitGPUCommandBuffer(cmd);
        SDL3.SDL_ReleaseGPUTransferBuffer(_gpu.Device, xfer);

        var handle = new MeshHandle(++_nextId);
        _meshes[handle.Id] = new MeshBuffer(gpuBuffer, vertices.Length);
        return handle;
    }

    public void ReleaseMesh(MeshHandle handle)
    {
        if (_meshes.Remove(handle.Id, out var buf))
            buf.Dispose(_gpu.Device);
    }

    public void DrawFrame(ReadOnlySpan<MeshInstance> instances, Matrix4x4 view)
    {
        var cmd = _gpu.AcquireCommandBuffer();
        SDL_GPUTexture* swapchain; uint sw, sh;
        SDL3.SDL_WaitAndAcquireGPUSwapchainTexture(cmd, _gpu.Window, &swapchain, &sw, &sh);
        // Swapchain texture can be null when the window is minimised or occluded.
        if (swapchain == null) { SDL3.SDL_CancelGPUCommandBuffer(cmd); return; }

        _gpu.ResizeDepthTextureIfNeeded(sw, sh);

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
            // Depth values don't need to be read back after the pass.
            store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
            stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,
            stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
            cycle = true
        };

        var pass = SDL3.SDL_BeginGPURenderPass(cmd, &colorTarget, 1, &depthTarget);
        SDL3.SDL_BindGPUGraphicsPipeline(pass, _gpu.Pipeline);

        foreach (var instance in instances)
        {
            if (!_meshes.TryGetValue(instance.Mesh.Id, out var buf)) continue;
            var mvp = BuildMVP(instance.Transform, view, sw, sh);
            fixed (float* mvpPtr = mvp)
                SDL3.SDL_PushGPUVertexUniformData(cmd, 0, (nint)mvpPtr, 64);
            var vbBinding = new SDL_GPUBufferBinding { buffer = buf.GpuBuffer, offset = 0 };
            SDL3.SDL_BindGPUVertexBuffers(pass, 0, &vbBinding, 1);
            SDL3.SDL_DrawGPUPrimitives(pass, (uint)buf.VertexCount, 1, 0, 0);
        }

        SDL3.SDL_EndGPURenderPass(pass);
        SDL3.SDL_SubmitGPUCommandBuffer(cmd);
    }

    public void Dispose()
    {
        foreach (var buf in _meshes.Values)
            buf.Dispose(_gpu.Device);
        _meshes.Clear();
        _gpu.Dispose();
    }

    /// <summary>
    /// Builds a column-major MVP matrix for the vertex shader uniform buffer.
    /// Projection: 45° vertical FOV, near=0.01, far=100.
    /// View: camera fixed at Z=+2.5 looking toward the origin.
    /// The model transform comes from <paramref name="model"/> (System.Numerics row-major),
    /// transposed to column-major before multiplication.
    /// </summary>
    private static float[] BuildMVP(Matrix4x4 model, Matrix4x4 view, uint w, uint h)
    {
        float f = 1.0f / MathF.Tan(MathF.PI / 8f);
        float aspect = (float)w / h;
        float near = 0.01f, far = 100f;

        float[] proj = new float[16];
        proj[0]  = f / aspect;
        proj[5]  = f;
        proj[10] = (near + far) / (near - far);
        proj[11] = -1f;
        proj[14] = (2f * near * far) / (near - far);

        // View: System.Numerics is row-major; do NOT transpose — CreateLookAt output
        // maps directly to column-major when used as-is in this multiply convention.
        float[] v =
        [
            view.M11, view.M12, view.M13, view.M14,
            view.M21, view.M22, view.M23, view.M24,
            view.M31, view.M32, view.M33, view.M34,
            view.M41, view.M42, view.M43, view.M44,
        ];

        float[] m =
        [
            model.M11, model.M12, model.M13, model.M14,
            model.M21, model.M22, model.M23, model.M24,
            model.M31, model.M32, model.M33, model.M34,
            model.M41, model.M42, model.M43, model.M44,
        ];

        return Mul(Mul(proj, v), m);
    }

    // Column-major 4×4 matrix multiply: r[j,i] = Σ a[k,i] * b[j,k]
    private static float[] Mul(float[] a, float[] b)
    {
        var r = new float[16];
        for (int j = 0; j < 4; j++)
            for (int i = 0; i < 4; i++)
                for (int k = 0; k < 4; k++)
                    r[j * 4 + i] += a[k * 4 + i] * b[j * 4 + k];
        return r;
    }

    // ── Nested private type ───────────────────────────────────────────────────

    /// <summary>
    /// Wraps a single GPU vertex buffer. Lifetime is managed by <see cref="SdlRenderer"/>;
    /// disposal requires the device pointer because SDL buffers aren't self-releasing.
    /// </summary>
    private sealed class MeshBuffer
    {
        internal SDL_GPUBuffer* GpuBuffer { get; }
        internal int VertexCount { get; }

        internal MeshBuffer(SDL_GPUBuffer* gpuBuffer, int vertexCount)
        {
            GpuBuffer = gpuBuffer;
            VertexCount = vertexCount;
        }

        internal void Dispose(SDL_GPUDevice* device) =>
            SDL3.SDL_ReleaseGPUBuffer(device, GpuBuffer);
    }
}
