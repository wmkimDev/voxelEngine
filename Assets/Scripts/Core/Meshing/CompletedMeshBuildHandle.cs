public sealed class CompletedMeshBuildHandle : IMeshBuildHandle
{
    private readonly ChunkMeshData meshData;

    public CompletedMeshBuildHandle(ChunkMeshData meshData)
    {
        this.meshData = meshData;
    }

    public bool IsCompleted => true;

    public ChunkMeshData Complete()
    {
        return meshData;
    }
}
