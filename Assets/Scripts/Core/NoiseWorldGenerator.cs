using System;

public sealed class NoiseWorldGenerator : IWorldGenerator
{
    private readonly float noiseScale;
    private readonly int baseHeight;
    private readonly int heightAmplitude;

    public NoiseWorldGenerator(int seed, float noiseScale, int baseHeight, int heightAmplitude)
    {
        Seed = seed;
        this.noiseScale = Math.Max(0.001f, noiseScale);
        this.baseHeight = Math.Max(0, baseHeight);
        this.heightAmplitude = Math.Max(1, heightAmplitude);
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
        // 엔진 노이즈 함수 대신 순수 C# 값 노이즈를 써서 Core가 독립적으로 컴파일되게 합니다.
        float broadNoise = ValueNoise(sampleX, sampleZ);
        float detailNoise = ValueNoise(sampleX * 2.5f + 17.3f, sampleZ * 2.5f + 42.7f);
        float combinedNoise = (broadNoise * 0.75f) + (detailNoise * 0.25f);

        return baseHeight + (int)Math.Floor((combinedNoise * heightAmplitude) + 0.5f);
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

    private float ValueNoise(float x, float z)
    {
        int x0 = (int)Math.Floor(x);
        int z0 = (int)Math.Floor(z);
        int x1 = x0 + 1;
        int z1 = z0 + 1;

        float tx = SmoothStep(x - x0);
        float tz = SmoothStep(z - z0);

        float a = HashNoise(x0, z0);
        float b = HashNoise(x1, z0);
        float c = HashNoise(x0, z1);
        float d = HashNoise(x1, z1);

        float top = Lerp(a, b, tx);
        float bottom = Lerp(c, d, tx);
        return Lerp(top, bottom, tz);
    }

    private float HashNoise(int x, int z)
    {
        unchecked
        {
            int hash = Seed;
            hash = (hash * 397) ^ x;
            hash = (hash * 397) ^ z;
            hash ^= hash >> 13;
            hash *= 1274126177;
            hash ^= hash >> 16;
            return (hash & 0x7fffffff) / (float)int.MaxValue;
        }
    }

    private static float SmoothStep(float value)
    {
        return value * value * (3f - (2f * value));
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + ((b - a) * t);
    }
}
