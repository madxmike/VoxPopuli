namespace VoxPopuli.Renderer;

using System;
using SDL;

internal interface ISubRenderer : IDisposable
{
    unsafe void PrepareFrame(SDL_GPUCommandBuffer* cmd, in RenderFrame frame);
    void Draw(in RenderFrame frame);
}
