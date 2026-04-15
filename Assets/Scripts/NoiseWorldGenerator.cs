using UnityEngine;

public sealed class NoiseWorldGenerator : IWorldGenerator
{
    private readonly float noiseScale;
    private readonly int baseHeight;
    private readonly int heightAmplitude;

    public NoiseWorldGenerator(int seed, float noiseScale, int baseHeight, int heightAmplitude)
    {
        Seed = seed;
        this.noiseScale = Mathf.Max(0.001f, noiseScale);
        this.baseHeight = Mathf.Max(0, baseHeight);
        this.heightAmplitude = Mathf.Max(1, heightAmplitude);
    }

    public int Seed { get; }

    public void Generate(ChunkPos chunkPos, IChunkDataStore chunkData)
    {
        int chunkSize = chunkData.Size;

        for (int z = 0; z < chunkSize; z++)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                int worldX = (chunkPos.X * chunkSize) + x;
                int worldZ = (chunkPos.Z * chunkSize) + z;
                int surfaceHeight = GetSurfaceHeight(worldX, worldZ);

                for (int y = 0; y < chunkSize; y++)
                {
                    int worldY = (chunkPos.Y * chunkSize) + y;
                    byte voxelType = GetVoxelType(worldY, surfaceHeight);
                    chunkData.SetVoxel(new LocalPos(x, y, z), voxelType);
                }
            }
        }
    }

    private int GetSurfaceHeight(int worldX, int worldZ)
    {
        // 높이맵은 같은 worldX/worldZ와 같은 Seed에 대해 항상 같은 높이를 반환해야 합니다.
        // 그래야 청크를 따로 생성해도 경계에서 지형 높이가 끊기지 않습니다.
        float sampleX = (worldX + (Seed * 37.13f)) / noiseScale;
        float sampleZ = (worldZ + (Seed * 91.71f)) / noiseScale;

        // 첫 번째 노이즈는 큰 언덕, 두 번째 노이즈는 작은 굴곡입니다.
        // 여러 노이즈를 섞으면 완전히 평평한 Perlin 패턴보다 자연스러운 지형이 됩니다.
        float broadNoise = Mathf.PerlinNoise(sampleX, sampleZ);
        float detailNoise = Mathf.PerlinNoise(sampleX * 2.5f + 17.3f, sampleZ * 2.5f + 42.7f);
        float combinedNoise = (broadNoise * 0.75f) + (detailNoise * 0.25f);

        return baseHeight + Mathf.RoundToInt(combinedNoise * heightAmplitude);
    }

    private static byte GetVoxelType(int worldY, int surfaceHeight)
    {
        if (worldY > surfaceHeight)
        {
            return VoxelType.Air;
        }

        if (worldY == surfaceHeight)
        {
            return VoxelType.Grass;
        }

        if (worldY >= surfaceHeight - 2)
        {
            return VoxelType.Dirt;
        }

        return VoxelType.Stone;
    }
}
