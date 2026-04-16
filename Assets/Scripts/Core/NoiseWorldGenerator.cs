using System;

public sealed class NoiseWorldGenerator : IWorldGenerator
{
    private readonly NoiseWorldGeneratorSettings settings;

    public NoiseWorldGenerator(NoiseWorldGeneratorSettings settings)
    {
        this.settings = settings;
    }

    public int Seed => settings.Seed;

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
                    byte voxelType = GetVoxelType(worldX, worldY, worldZ, surfaceHeight);
                    chunkData.SetVoxel(new LocalPos(x, y, z), voxelType);
                }
            }
        }
    }

    public int GetSurfaceHeight(int worldX, int worldZ)
    {
        // 높이맵은 같은 worldX/worldZ와 같은 Seed에 대해 항상 같은 높이를 반환해야 합니다.
        // 그래야 청크를 따로 생성해도 경계에서 지형 높이가 끊기지 않습니다.
        float sampleX = (worldX + (Seed * 37.13f)) / settings.NoiseScale;
        float sampleZ = (worldZ + (Seed * 91.71f)) / settings.NoiseScale;

        // 첫 번째 노이즈는 큰 언덕, 두 번째 노이즈는 작은 굴곡입니다.
        // 엔진 노이즈 함수 대신 순수 C# 값 노이즈를 써서 Core가 독립적으로 컴파일되게 합니다.
        float broadNoise = ValueNoise(sampleX, sampleZ);
        float detailNoise = ValueNoise(sampleX * 2.5f + 17.3f, sampleZ * 2.5f + 42.7f);
        float combinedNoise = (broadNoise * 0.75f) + (detailNoise * 0.25f);

        return settings.BaseHeight + (int)Math.Floor((combinedNoise * settings.HeightAmplitude) + 0.5f);
    }

    private byte GetVoxelType(int worldX, int worldY, int worldZ, int surfaceHeight)
    {
        if (worldY > surfaceHeight)
        {
            return VoxelType.Air;
        }

        if (ShouldCarveCave(worldX, worldY, worldZ, surfaceHeight))
        {
            return VoxelType.Air;
        }

        bool isBeachSurface = surfaceHeight <= settings.BeachHeight;
        int soilDepth = GetTopSoilDepth(worldX, worldZ);

        if (worldY == surfaceHeight)
        {
            return isBeachSurface ? VoxelType.Sand : VoxelType.Grass;
        }

        if (worldY >= surfaceHeight - soilDepth)
        {
            return isBeachSurface ? VoxelType.Sand : VoxelType.Dirt;
        }

        return VoxelType.Stone;
    }

    private int GetTopSoilDepth(int worldX, int worldZ)
    {
        float sampleX = ((worldX * 0.35f) + (Seed * 19.41f)) / settings.NoiseScale;
        float sampleZ = ((worldZ * 0.35f) + (Seed * 53.92f)) / settings.NoiseScale;
        float variation = ValueNoise(sampleX, sampleZ);
        return settings.TopSoilDepth + (int)Math.Round(variation * settings.TopSoilDepthVariation);
    }

    private bool ShouldCarveCave(int worldX, int worldY, int worldZ, int surfaceHeight)
    {
        // 표면 바로 아래까지 동굴을 파면 지형 윗면이 쉽게 무너지므로,
        // 최소 두께를 남겨 둔 뒤에만 3D 노이즈로 내부를 비웁니다.
        if (worldY >= surfaceHeight - settings.CaveSurfaceClearance)
        {
            return false;
        }

        float sampleX = (worldX + (Seed * 11.17f)) / settings.CaveNoiseScale;
        float sampleY = (worldY + (Seed * 23.71f)) / settings.CaveNoiseScale;
        float sampleZ = (worldZ + (Seed * 47.33f)) / settings.CaveNoiseScale;
        float caveNoise = ValueNoise(sampleX, sampleY, sampleZ);
        float ridgedNoise = 1f - Math.Abs((caveNoise * 2f) - 1f);
        return ridgedNoise >= settings.CaveThreshold;
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

    private float ValueNoise(float x, float y, float z)
    {
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        int z0 = (int)Math.Floor(z);
        int x1 = x0 + 1;
        int y1 = y0 + 1;
        int z1 = z0 + 1;

        float tx = SmoothStep(x - x0);
        float ty = SmoothStep(y - y0);
        float tz = SmoothStep(z - z0);

        float c000 = HashNoise(x0, y0, z0);
        float c100 = HashNoise(x1, y0, z0);
        float c010 = HashNoise(x0, y1, z0);
        float c110 = HashNoise(x1, y1, z0);
        float c001 = HashNoise(x0, y0, z1);
        float c101 = HashNoise(x1, y0, z1);
        float c011 = HashNoise(x0, y1, z1);
        float c111 = HashNoise(x1, y1, z1);

        float x00 = Lerp(c000, c100, tx);
        float x10 = Lerp(c010, c110, tx);
        float x01 = Lerp(c001, c101, tx);
        float x11 = Lerp(c011, c111, tx);
        float y0Blend = Lerp(x00, x10, ty);
        float y1Blend = Lerp(x01, x11, ty);
        return Lerp(y0Blend, y1Blend, tz);
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

    private float HashNoise(int x, int y, int z)
    {
        unchecked
        {
            int hash = Seed;
            hash = (hash * 397) ^ x;
            hash = (hash * 397) ^ y;
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
