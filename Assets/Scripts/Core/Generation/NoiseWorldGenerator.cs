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

        // 먼저 "평원용 높이"를 만듭니다.
        // 이 값은 전체적으로 완만하고 낮게 유지해서, 산맥 지역이 아닌 곳에서는
        // 초원/평지/언덕처럼 읽히도록 의도적으로 진폭을 작게 둡니다.
        float plainBroadNoise = ValueNoise(sampleX * 0.95f, sampleZ * 0.95f);
        float plainDetailNoise = ValueNoise((sampleX * 2.8f) + 17.3f, (sampleZ * 2.8f) + 42.7f);
        float plainsNoise = (plainBroadNoise * 0.82f) + (plainDetailNoise * 0.18f);
        float plainsHeight = settings.HeightAmplitude * ((plainsNoise * 0.16f) + 0.08f);

        // 아주 저주파 노이즈로 "산맥이 등장하는 지역"을 먼저 정합니다.
        // 문턱값을 지나기 전까지는 거의 평원처럼 남기고,
        // 문턱을 넘은 뒤에는 산맥 형태가 빠르게 강해지도록 마스크를 더 날카롭게 만듭니다.
        float mountainRegionNoise = ValueNoise((sampleX * 0.18f) - 73.1f, (sampleZ * 0.18f) + 41.7f);
        float mountainRegion = SmoothClampRange(mountainRegionNoise, 0.6f, 0.82f);
        mountainRegion *= mountainRegion * mountainRegion;

        // 산맥 지역은 저주파 "산 덩어리"와 중주파 "능선"을 함께 써서,
        // 멀리서도 산맥처럼 보이는 큰 실루엣과 가까이서 읽히는 골짜기/능선을 동시에 만듭니다.
        float mountainMassNoise = ValueNoise((sampleX * 0.42f) - 25.4f, (sampleZ * 0.42f) + 61.7f);
        float mountainMassHeight = settings.HeightAmplitude * mountainRegion * ((mountainMassNoise * 0.5f) + 0.35f) * 0.7f;

        float ridgePrimaryNoise = ValueNoise((sampleX * 0.82f) + 89.4f, (sampleZ * 0.82f) + 12.6f);
        float ridgeSecondaryNoise = ValueNoise((sampleX * 1.74f) - 31.8f, (sampleZ * 1.74f) + 57.1f);
        float ridgePrimary = 1f - Math.Abs((ridgePrimaryNoise * 2f) - 1f);
        float ridgeSecondary = 1f - Math.Abs((ridgeSecondaryNoise * 2f) - 1f);
        float mountainRidgeHeight = settings.HeightAmplitude * mountainRegion * ((ridgePrimary * 1.1f) + (ridgeSecondary * 0.45f));

        // 산맥 지역에선 평원 진폭을 줄이고, 대신 큰 산 실루엣이 분명히 읽히게 합니다.
        float blendedPlainsHeight = plainsHeight * (1f - (mountainRegion * 0.7f));
        float finalHeight = blendedPlainsHeight + mountainMassHeight + mountainRidgeHeight;

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
