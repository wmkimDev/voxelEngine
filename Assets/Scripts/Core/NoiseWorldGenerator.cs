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

        // 3D 동굴 노이즈를 바로 쓰면 월드 전역에 스파게티처럼 퍼지기 쉽습니다.
        // 먼저 아주 저주파 2D 마스크로 "이 지역은 동굴이 날 만한가"를 한 번 더 거른 뒤,
        // 그 안에서만 실제 동굴을 뚫어 동굴 출현 구역 자체를 드물게 만듭니다.
        float caveRegionX = (worldX + (Seed * 5.19f)) / (settings.CaveNoiseScale * 3.6f);
        float caveRegionZ = (worldZ + (Seed * 9.47f)) / (settings.CaveNoiseScale * 3.6f);
        float caveRegionNoise = ValueNoise(caveRegionX, caveRegionZ);
        float caveRegionMask = SmoothClampRange(caveRegionNoise, 0.76f, 0.92f);
        caveRegionMask *= caveRegionMask;
        if (caveRegionMask <= 0f)
        {
            return false;
        }

        float sampleX = (worldX + (Seed * 11.17f)) / settings.CaveNoiseScale;
        float sampleY = (worldY + (Seed * 23.71f)) / settings.CaveNoiseScale;
        float sampleZ = (worldZ + (Seed * 47.33f)) / settings.CaveNoiseScale;
        float caveNoise = ValueNoise(sampleX, sampleY, sampleZ);
        float ridgedNoise = 1f - Math.Abs((caveNoise * 2f) - 1f);

        // 지역 마스크가 약한 곳은 사실상 더 높은 threshold가 필요하도록 만들어,
        // "가끔 보이는 큰 동굴 구역" 위주로 남기고 잔가닥 동굴은 줄입니다.
        float effectiveThreshold = settings.CaveThreshold + ((1f - caveRegionMask) * 0.08f);
        return ridgedNoise >= effectiveThreshold;
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

    private static float Clamp01(float value)
    {
        return Math.Max(0f, Math.Min(1f, value));
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
