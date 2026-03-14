namespace VoxPopuli.Renderer;

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SDL;
using VoxPopuli.Game;

internal sealed class MeshWorker : IDisposable
{
    private readonly struct MeshUploadRequest
    {
        internal readonly int ChunkIndex;
        internal readonly VoxelVertex[] Verts;
        internal readonly uint[] Indices;
        internal readonly int VertCount;
        internal readonly int IdxCount;
        internal MeshUploadRequest(int ci, VoxelVertex[] v, uint[] i, int vc, int ic)
        { ChunkIndex = ci; Verts = v; Indices = i; VertCount = vc; IdxCount = ic; }
    }

    private readonly VoxelWorld _world;
    private readonly ChunkMeshPool _pool;
    private readonly ConcurrentQueue<int> _workQueue = new();
    private readonly ConcurrentQueue<MeshUploadRequest> _doneQueue = new();
    private readonly ConcurrentDictionary<int, byte> _inFlight = new();
    private readonly List<int> _updatedChunks = new();
    private volatile bool _stop;
    private readonly Thread _thread;

    internal MeshWorker(VoxelWorld world, ChunkMeshPool pool)
    {
        _world = world;
        _pool  = pool;
        _thread = new Thread(WorkerLoop) { IsBackground = true, Name = "MeshWorker" };
        _thread.Start();
    }

    internal void EnqueueChunk(int chunkIndex)
    {
        if (_inFlight.TryAdd(chunkIndex, 0))
            _workQueue.Enqueue(chunkIndex);
    }

    internal unsafe ReadOnlySpan<int> FlushUploads(SDL_GPUCommandBuffer* cmd, VoxelRenderer renderer)
    {
        _updatedChunks.Clear();
        while (_doneQueue.TryDequeue(out var req))
        {
            int slot = _pool.AllocSlot();
            if (slot == -1) { Console.WriteLine("[MeshWorker] Pool full, skipping chunk " + req.ChunkIndex); continue; }
            _pool.UploadMesh(cmd, slot, req.Verts, req.Indices, req.VertCount, req.IdxCount);
            renderer.AssignSlot(req.ChunkIndex, slot);
            _updatedChunks.Add(req.ChunkIndex);
        }
        return CollectionsMarshal.AsSpan(_updatedChunks);
    }

    private void WorkerLoop()
    {
        var verts   = new VoxelVertex[ChunkMeshPool.VerticesPerSlot];
        var indices = new uint[ChunkMeshPool.IndicesPerSlot];

        while (!_stop)
        {
            if (!_workQueue.TryDequeue(out int ci))
            { Thread.Sleep(1); continue; }

            var padded = _world.GetPaddedChunkVoxels(ci);
            MeshBuilder.Build(padded, verts, indices, out int vc, out int ic);

            if (ic > 0)
            {
                var vertsCopy   = new VoxelVertex[vc];
                var indicesCopy = new uint[ic];
                verts.AsSpan(0, vc).CopyTo(vertsCopy);
                indices.AsSpan(0, ic).CopyTo(indicesCopy);
                _doneQueue.Enqueue(new MeshUploadRequest(ci, vertsCopy, indicesCopy, vc, ic));
            }

            _inFlight.TryRemove(ci, out _);
        }
    }

    public void Dispose()
    {
        _stop = true;
        _thread.Join(2000);
    }
}
