namespace VoxPopuli.Renderer;

using System.Numerics;
using SDL;
using VoxPopuli.Game;

/// <summary>Frame data passed to sub-renderers for a single frame.</summary>
internal unsafe readonly struct RenderFrame
{
    /// <summary>Combined view-projection matrix.</summary>
    public required Matrix4x4 ViewProj { get; init; }
    /// <summary>View matrix only.</summary>
    public required Matrix4x4 View { get; init; }
    /// <summary>Render target width.</summary>
    public required uint Width { get; init; }
    /// <summary>Render target height.</summary>
    public required uint Height { get; init; }
    /// <summary>Render pass handle.</summary>
    public required SDL_GPURenderPass* RenderPass { get; init; }
    /// <summary>Command buffer handle.</summary>
    public required SDL_GPUCommandBuffer* CommandBuffer { get; init; }
    /// <summary>The voxel world being rendered.</summary>
    public VoxelWorld? World { get; init; }
    /// <summary>Whether to render in wireframe mode.</summary>
    public bool Wireframe { get; init; }
    /// <summary>Frustum for culling.</summary>
    public Frustum Frustum { get; init; }
    /// <summary>GPU device for resource management.</summary>
    public required SdlGpuDevice Gpu { get; init; }
}
