namespace VoxPopuli.Renderer;

using System;
using VoxPopuli.Game;

internal interface IRenderer : IDisposable
{
    void DrawFrame(CameraView camera, VoxelWorld world);
}
