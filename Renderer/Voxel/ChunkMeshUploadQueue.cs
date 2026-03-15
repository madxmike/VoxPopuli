namespace VoxPopuli.Renderer;

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using SDL;
using VoxPopuli.Game;

internal sealed unsafe class ChunkMeshUploadQueue
{
    private readonly SdlGpuDevice _gpu;
    private readonly Func<IChunkMeshBuilder> _meshBuilderFactory;
    private readonly CancellationTokenSource?[] _inFlight = new CancellationTokenSource?[VoxelWorld.MAX_CHUNKS];
    private readonly ConcurrentQueue<MeshResult> _completed = new();
    private readonly ConcurrentBag<VoxelVertex[]> _scratchPool = new();

    private readonly struct MeshResult
    {
        internal readonly int ChunkIndex;
        internal readonly VoxelVertex[] Vertices;
        internal readonly int VertexCount;
        internal MeshResult(int chunkIndex, VoxelVertex[] vertices, int vertexCount)
        {
            ChunkIndex = chunkIndex;
            Vertices = vertices;
            VertexCount = vertexCount;
        }
    }

    internal ChunkMeshUploadQueue(SdlGpuDevice gpu, Func<IChunkMeshBuilder> meshBuilderFactory)
    {
        _gpu = gpu;
        _meshBuilderFactory = meshBuilderFactory;
    }

    internal bool IsInFlight(int chunkIndex) => _inFlight[chunkIndex] != null;

    internal void Reschedule(VoxelWorld world, int chunkIndex, uint[] vertexCounts)
    {
        vertexCounts[chunkIndex] = 0;
        if (_inFlight[chunkIndex] is { } existing)
        {
            existing.Cancel();
            existing.Dispose();
            _inFlight[chunkIndex] = null;
        }
        Schedule(world, chunkIndex);
    }

    internal void Schedule(VoxelWorld world, int chunkIndex)
    {
        var cts = new CancellationTokenSource();
        _inFlight[chunkIndex] = cts;
        var ct = cts.Token;
        if (!_scratchPool.TryTake(out VoxelVertex[]? scratch))
        {
            scratch = new VoxelVertex[VoxelVertex.MaxVerticesPerChunk];
        }
        Task.Run(() =>
        {
            try
            {
                var builder = _meshBuilderFactory();
                int count = builder.Build(world, chunkIndex, scratch.AsSpan(), ct);
                _inFlight[chunkIndex] = null;
                cts.Dispose();
                _completed.Enqueue(new MeshResult(chunkIndex, scratch, count));
            }
            catch (OperationCanceledException)
            {
                _scratchPool.Add(scratch);
                _inFlight[chunkIndex] = null;
            }
        }, ct);
    }

    internal void UploadPending(SDL_GPUCommandBuffer* cmd, SDL_GPUBuffer*[] vertexBuffers, uint[] vertexCounts)
    {
        while (_completed.TryDequeue(out MeshResult result))
        {
            int i = result.ChunkIndex;
            int count = result.VertexCount;

            if (count == 0)
            {
                if (vertexBuffers[i] != null)
                {
                    SDL3.SDL_ReleaseGPUBuffer(_gpu.Device, vertexBuffers[i]);
                    vertexBuffers[i] = null;
                }
                vertexCounts[i] = 0;
            }
            else
            {
                if (vertexBuffers[i] != null)
                {
                    SDL3.SDL_ReleaseGPUBuffer(_gpu.Device, vertexBuffers[i]);
                }

                uint byteSize = (uint)(count * 16);
                var bufInfo = new SDL_GPUBufferCreateInfo
                {
                    usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX,
                    size = byteSize
                };
                vertexBuffers[i] = SDL3.SDL_CreateGPUBuffer(_gpu.Device, &bufInfo);
                if (vertexBuffers[i] == null)
                {
                    throw new Exception(SDL3.SDL_GetError());
                }

                var tempTransferInfo = new SDL_GPUTransferBufferCreateInfo
                {
                    usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
                    size = byteSize
                };
                var tempTransfer = SDL3.SDL_CreateGPUTransferBuffer(_gpu.Device, &tempTransferInfo);
                if (tempTransfer == null)
                {
                    throw new Exception(SDL3.SDL_GetError());
                }

                var mapped = (VoxelVertex*)SDL3.SDL_MapGPUTransferBuffer(_gpu.Device, tempTransfer, false);
                result.Vertices.AsSpan(0, count).CopyTo(new Span<VoxelVertex>(mapped, count));
                SDL3.SDL_UnmapGPUTransferBuffer(_gpu.Device, tempTransfer);

                var copyPass = SDL3.SDL_BeginGPUCopyPass(cmd);
                var src = new SDL_GPUTransferBufferLocation { transfer_buffer = tempTransfer, offset = 0 };
                var dst = new SDL_GPUBufferRegion { buffer = vertexBuffers[i], offset = 0, size = byteSize };
                SDL3.SDL_UploadToGPUBuffer(copyPass, &src, &dst, false);
                SDL3.SDL_EndGPUCopyPass(copyPass);
                SDL3.SDL_ReleaseGPUTransferBuffer(_gpu.Device, tempTransfer);

                vertexCounts[i] = (uint)count;
            }

            _scratchPool.Add(result.Vertices);
        }
    }
}
