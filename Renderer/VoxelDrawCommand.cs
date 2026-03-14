namespace VoxPopuli.Renderer;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
internal struct VoxelDrawCommand  // 20 bytes — matches SDL_GPUIndexedIndirectDrawCommand
{
    public uint NumIndices;
    public uint NumInstances;   // always 1
    public uint FirstIndex;
    public int  VertexOffset;
    public uint FirstInstance;  // chunk index — shader reads ChunkOffsets[FirstInstance]
}
