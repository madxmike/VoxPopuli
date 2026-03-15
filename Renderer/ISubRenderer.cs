namespace VoxPopuli.Renderer;

using System;
using SDL;

/// <summary>Sub-renderer interface for preparing and drawing rendering passes.</summary>
internal interface ISubRenderer : IDisposable
{
    /// <summary>Prepares frame data before the render pass begins.</summary>
    unsafe void PrepareFrame(SDL_GPUCommandBuffer* cmd, in RenderFrame frame);
    /// <summary>Draws the sub-renderer during the render pass.</summary>
    void Draw(in RenderFrame frame);
}
