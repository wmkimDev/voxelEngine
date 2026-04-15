public interface IWorldGenerator
{
    int Seed { get; }

    void Generate(ChunkPos chunkPos, IChunkDataStore chunkData);
}
