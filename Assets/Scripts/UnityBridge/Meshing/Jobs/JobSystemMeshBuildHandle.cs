using Unity.Collections;
using Unity.Jobs;

public sealed class JobSystemMeshBuildHandle : IMeshBuildHandle
{
    private JobHandle handle;
    private JobMeshBuildBuffers buffers;
    private readonly JobMeshBuildBufferPool bufferPool;
    private readonly bool hasGreedyScratchBuffers;
    private ChunkMeshData completedMeshData;
    private bool hasCompleted;

    public JobSystemMeshBuildHandle(
        JobHandle handle,
        JobMeshBuildBuffers buffers,
        JobMeshBuildBufferPool bufferPool,
        bool hasGreedyScratchBuffers)
    {
        this.handle = handle;
        this.buffers = buffers;
        this.bufferPool = bufferPool;
        this.hasGreedyScratchBuffers = hasGreedyScratchBuffers;
    }

    public bool IsCompleted => hasCompleted || handle.IsCompleted;

    public ChunkMeshData Complete()
    {
        return Complete(null);
    }

    public ChunkMeshData Complete(ChunkMeshData reusableMeshData)
    {
        if (hasCompleted)
        {
            if (reusableMeshData != null && !ReferenceEquals(reusableMeshData, completedMeshData))
            {
                reusableMeshData.CopyFrom(completedMeshData);
                return reusableMeshData;
            }

            return completedMeshData;
        }

        handle.Complete();

        // Job이 만든 NativeList 결과를 기존 렌더 경로가 그대로 쓸 수 있도록
        // Core의 ChunkMeshData로 한 번 옮겨 담습니다.
        ChunkMeshData meshData = reusableMeshData ?? new ChunkMeshData();
        meshData.ResetAndEnsureCapacity(
            buffers.Vertices.Length,
            buffers.Triangles.Length,
            buffers.Normals.Length,
            buffers.Uvs.Length);

        for (int i = 0; i < buffers.Vertices.Length; i++)
        {
            meshData.Vertices.Add(buffers.Vertices[i]);
        }

        for (int i = 0; i < buffers.Triangles.Length; i++)
        {
            meshData.Triangles.Add(buffers.Triangles[i]);
        }

        for (int i = 0; i < buffers.Normals.Length; i++)
        {
            meshData.Normals.Add(buffers.Normals[i]);
        }

        for (int i = 0; i < buffers.Uvs.Length; i++)
        {
            meshData.Uvs.Add(buffers.Uvs[i]);
        }

        DisposeNativeData();
        completedMeshData = meshData;
        hasCompleted = true;
        return completedMeshData;
    }

    private void DisposeNativeData()
    {
        // 완료된 버퍼는 다음 Schedule에서 다시 쓸 수 있도록 풀에 반납합니다.
        // 풀이 이미 정리된 상태면 Release 쪽에서 바로 Dispose합니다.
        bufferPool.Release(buffers, hasGreedyScratchBuffers);
    }
}
