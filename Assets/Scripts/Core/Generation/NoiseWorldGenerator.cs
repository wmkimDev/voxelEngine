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
        int worldBaseX = chunkPos.X * chunkSize;
        int worldBaseY = chunkPos.Y * chunkSize;
        int worldBaseZ = chunkPos.Z * chunkSize;
        int[,] surfaceHeights = new int[chunkSize, chunkSize];
        int[,] soilDepths = new int[chunkSize, chunkSize];

        BuildColumnMaps(chunkSize, worldBaseX, worldBaseZ, surfaceHeights, soilDepths);

        for (int z = 0; z < chunkSize; z++)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                int surfaceHeight = surfaceHeights[x, z];
                int soilDepth = soilDepths[x, z];

                for (int y = 0; y < chunkSize; y++)
                {
                    int worldY = worldBaseY + y;
                    byte voxelType = GetVoxelType(worldY, surfaceHeight, soilDepth);
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

        // 평원은 완전히 평평하지 않고, 완만한 구릉이 길게 이어지도록 저주파와 중주파를 섞습니다.
        float plainBroadNoise = ValueNoise(sampleX * 0.78f, sampleZ * 0.78f);
        float plainMidNoise = ValueNoise((sampleX * 1.65f) + 17.3f, (sampleZ * 1.65f) + 42.7f);
        float plainDetailNoise = ValueNoise((sampleX * 3.1f) - 51.2f, (sampleZ * 3.1f) + 11.8f);
        float plainsNoise = (plainBroadNoise * 0.62f) + (plainMidNoise * 0.28f) + (plainDetailNoise * 0.10f);
        float plainsHeight = settings.HeightAmplitude * ((plainsNoise * 0.20f) + 0.10f);

        // 산맥 지역은 더 넓고 부드럽게 열리게 해서, 평원에서 산악 지대로 넘어가는 구간이 덜 딱딱하게 보이게 합니다.
        float mountainRegionNoise = ValueNoise((sampleX * 0.15f) - 73.1f, (sampleZ * 0.15f) + 41.7f);
        float mountainRegion = SmoothClampRange(mountainRegionNoise, 0.52f, 0.80f);
        float foothillRegion = mountainRegion * mountainRegion;
        float coreMountainRegion = foothillRegion * mountainRegion;

        // 산 덩어리는 두 개의 큰 저주파 층을 섞어 한쪽으로만 기울지 않게 만듭니다.
        float mountainMassNoiseA = ValueNoise((sampleX * 0.34f) - 25.4f, (sampleZ * 0.34f) + 61.7f);
        float mountainMassNoiseB = ValueNoise((sampleX * 0.58f) + 12.7f, (sampleZ * 0.58f) - 44.9f);
        float mountainMassShape = (mountainMassNoiseA * 0.58f) + (mountainMassNoiseB * 0.42f);
        float foothillHeight = settings.HeightAmplitude * foothillRegion * ((mountainMassShape * 0.24f) + 0.06f);
        float mountainMassHeight = settings.HeightAmplitude * coreMountainRegion * ((mountainMassShape * 0.72f) + 0.22f);

        // 능선은 그대로 ridged noise를 쓰되, 거친 톱니 느낌을 줄이기 위해 살짝 부드럽게 눌러줍니다.
        float ridgePrimaryNoise = ValueNoise((sampleX * 0.74f) + 89.4f, (sampleZ * 0.74f) + 12.6f);
        float ridgeSecondaryNoise = ValueNoise((sampleX * 1.32f) - 31.8f, (sampleZ * 1.32f) + 57.1f);
        float ridgePrimary = SoftRidgedNoise(ridgePrimaryNoise);
        float ridgeSecondary = SoftRidgedNoise(ridgeSecondaryNoise);
        float ridgeShape = (ridgePrimary * 0.70f) + (ridgeSecondary * 0.30f);
        float mountainRidgeHeight = settings.HeightAmplitude * coreMountainRegion * ((ridgeShape * 0.85f) + 0.10f);

        // 산맥 사이에 골짜기가 더 자연스럽게 생기도록 약한 valley term을 넣습니다.
        float valleyNoise = ValueNoise((sampleX * 0.52f) - 141.4f, (sampleZ * 0.52f) + 19.6f);
        float valleyDepth = settings.HeightAmplitude * coreMountainRegion * ((1f - valleyNoise) * 0.18f);

        // 산지로 갈수록 평원 기복을 줄이되 완전히 사라지지 않게 해서, 전환부가 더 자연스럽게 이어집니다.
        float blendedPlainsHeight = plainsHeight * (1f - (foothillRegion * 0.45f));
        float finalHeight = blendedPlainsHeight + foothillHeight + mountainMassHeight + mountainRidgeHeight - valleyDepth;

        return settings.BaseHeight + (int)Math.Floor(finalHeight + 0.5f);
    }

    private void BuildColumnMaps(
        int chunkSize,
        int worldBaseX,
        int worldBaseZ,
        int[,] surfaceHeights,
        int[,] soilDepths)
    {
        for (int z = 0; z < chunkSize; z++)
        {
            int worldZ = worldBaseZ + z;
            for (int x = 0; x < chunkSize; x++)
            {
                int worldX = worldBaseX + x;
                surfaceHeights[x, z] = GetSurfaceHeight(worldX, worldZ);
                soilDepths[x, z] = GetTopSoilDepth(worldX, worldZ);
            }
        }
    }

    private byte GetVoxelType(int worldY, int surfaceHeight, int soilDepth)
    {
        if (worldY > surfaceHeight)
        {
            return VoxelType.Air;
        }

        if (worldY == surfaceHeight)
        {
            return VoxelType.Grass;
        }

        if (worldY >= surfaceHeight - soilDepth)
        {
            return VoxelType.Dirt;
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

    private static float SoftRidgedNoise(float value)
    {
        float ridged = 1f - Math.Abs((value * 2f) - 1f);
        return SmoothStep(ridged);
    }

    private static float SmoothClampRange(float value, float min, float max)
    {
        if (value <= min)
        {
            return 0f;
        }

        if (value >= max)
        {
            return 1f;
        }

        return SmoothStep((value - min) / (max - min));
    }
}
