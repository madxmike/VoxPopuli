namespace VoxPopuli.Renderer;

using System.Numerics;
using SDL;
using VoxPopuli.Game;

internal unsafe readonly struct RenderFrame
{
    public required Matrix4x4 ViewProj { get; init; }
    public required Matrix4x4 View { get; init; }
    public required uint Width { get; init; }
    public required uint Height { get; init; }
    public required SDL_GPURenderPass* RenderPass { get; init; }
    public required SDL_GPUCommandBuffer* CommandBuffer { get; init; }
    public VoxelWorld? World { get; init; }
    public bool Wireframe { get; init; }
    public Frustum Frustum { get; init; }
}
