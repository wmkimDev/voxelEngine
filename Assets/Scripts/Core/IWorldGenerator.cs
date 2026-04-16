public interface IWorldGenerator
{
    int Seed { get; }

    void Generate(ChunkPos chunkPos, IChunkDataStore chunkData);

    int GetSurfaceHeight(int worldX, int worldZ);
}
