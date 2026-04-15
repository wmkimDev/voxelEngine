public interface IMeshBuilder
{
    IMeshBuildHandle Schedule(ChunkNeighborhood neighborhood);
}

public interface IMeshBuildHandle
{
    bool IsCompleted { get; }

    ChunkMeshData Complete();
}
