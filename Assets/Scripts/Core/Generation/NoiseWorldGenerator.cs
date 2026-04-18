using System;

public sealed class NoiseWorldGenerator : IWorldGenerator
{
    // 월드 좌표를 seed와 함께 샘플 공간으로 옮기는 오프셋입니다.
    private static class WorldNoise
    {
        public const float WorldOffsetX = 37.13f;
        public const float WorldOffsetZ = 91.71f;
    }

    // 평원/구릉 실루엣을 만드는 샘플 계수입니다.
    private static class PlainsNoise
    {
        public const float PlainBroadScale = 0.78f;
        public const float PlainMidScale = 1.65f;
        public const float PlainDetailScale = 3.1f;
        public const float PlainMidOffsetX = 17.3f;
        public const float PlainMidOffsetZ = 42.7f;
        public const float PlainDetailOffsetX = -51.2f;
        public const float PlainDetailOffsetZ = 11.8f;
        public const float PlainBroadWeight = 0.62f;
        public const float PlainMidWeight = 0.28f;
        public const float PlainDetailWeight = 0.10f;
        public const float PlainHeightScale = 0.20f;
        public const float PlainHeightBias = 0.10f;
    }

    // 산악 지대 마스크, 산 덩어리, 능선, 골짜기 형태를 만드는 샘플 계수입니다.
    private static class MountainNoise
    {
        public const float MountainRegionScale = 0.15f;
        public const float MountainRegionOffsetX = -73.1f;
        public const float MountainRegionOffsetZ = 41.7f;
        public const float MountainRegionMin = 0.52f;
        public const float MountainRegionMax = 0.80f;

        public const float MountainMassScaleA = 0.34f;
        public const float MountainMassScaleB = 0.58f;
        public const float MountainMassOffsetAX = -25.4f;
        public const float MountainMassOffsetAZ = 61.7f;
        public const float MountainMassOffsetBX = 12.7f;
        public const float MountainMassOffsetBZ = -44.9f;
        public const float MountainMassWeightA = 0.58f;
        public const float MountainMassWeightB = 0.42f;
        public const float FoothillShapeScale = 0.24f;
        public const float FoothillShapeBias = 0.06f;
        public const float MountainMassShapeScale = 0.72f;
        public const float MountainMassShapeBias = 0.22f;

        public const float RidgePrimaryScale = 0.74f;
        public const float RidgeSecondaryScale = 1.32f;
        public const float RidgePrimaryOffsetX = 89.4f;
        public const float RidgePrimaryOffsetZ = 12.6f;
        public const float RidgeSecondaryOffsetX = -31.8f;
        public const float RidgeSecondaryOffsetZ = 57.1f;
        public const float RidgePrimaryWeight = 0.70f;
        public const float RidgeSecondaryWeight = 0.30f;
        public const float RidgeHeightScale = 0.85f;
        public const float RidgeHeightBias = 0.10f;

        public const float ValleyScale = 0.52f;
        public const float ValleyOffsetX = -141.4f;
        public const float ValleyOffsetZ = 19.6f;
        public const float ValleyDepthScale = 0.18f;

        public const float MountainPlainBlendStrength = 0.45f;
    }

    // 표토 두께 변화를 주는 샘플 계수입니다.
    private static class SoilNoise
    {
        public const float SoilScale = 0.35f;
        public const float SoilOffsetX = 19.41f;
        public const float SoilOffsetZ = 53.92f;
    }

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
        float sampleX = (worldX + (Seed * WorldNoise.WorldOffsetX)) / settings.NoiseScale;
        float sampleZ = (worldZ + (Seed * WorldNoise.WorldOffsetZ)) / settings.NoiseScale;

        // 평원은 완전히 평평하지 않고, 완만한 구릉이 길게 이어지도록 저주파와 중주파를 섞습니다.
        float plainBroadNoise = ValueNoise(sampleX * PlainsNoise.PlainBroadScale, sampleZ * PlainsNoise.PlainBroadScale);
        float plainMidNoise = ValueNoise(
            (sampleX * PlainsNoise.PlainMidScale) + PlainsNoise.PlainMidOffsetX,
            (sampleZ * PlainsNoise.PlainMidScale) + PlainsNoise.PlainMidOffsetZ);
        float plainDetailNoise = ValueNoise(
            (sampleX * PlainsNoise.PlainDetailScale) + PlainsNoise.PlainDetailOffsetX,
            (sampleZ * PlainsNoise.PlainDetailScale) + PlainsNoise.PlainDetailOffsetZ);
        float plainsNoise =
            (plainBroadNoise * PlainsNoise.PlainBroadWeight) +
            (plainMidNoise * PlainsNoise.PlainMidWeight) +
            (plainDetailNoise * PlainsNoise.PlainDetailWeight);
        float plainsHeight = settings.HeightAmplitude *
            ((plainsNoise * PlainsNoise.PlainHeightScale) + PlainsNoise.PlainHeightBias);

        // 산맥 지역은 더 넓고 부드럽게 열리게 해서, 평원에서 산악 지대로 넘어가는 구간이 덜 딱딱하게 보이게 합니다.
        float mountainRegionNoise = ValueNoise(
            (sampleX * MountainNoise.MountainRegionScale) + MountainNoise.MountainRegionOffsetX,
            (sampleZ * MountainNoise.MountainRegionScale) + MountainNoise.MountainRegionOffsetZ);
        float mountainRegion = SmoothClampRange(
            mountainRegionNoise,
            MountainNoise.MountainRegionMin,
            MountainNoise.MountainRegionMax);
        float foothillRegion = mountainRegion * mountainRegion;
        float coreMountainRegion = foothillRegion * mountainRegion;

        // 산 덩어리는 두 개의 큰 저주파 층을 섞어 한쪽으로만 기울지 않게 만듭니다.
        float mountainMassNoiseA = ValueNoise(
            (sampleX * MountainNoise.MountainMassScaleA) + MountainNoise.MountainMassOffsetAX,
            (sampleZ * MountainNoise.MountainMassScaleA) + MountainNoise.MountainMassOffsetAZ);
        float mountainMassNoiseB = ValueNoise(
            (sampleX * MountainNoise.MountainMassScaleB) + MountainNoise.MountainMassOffsetBX,
            (sampleZ * MountainNoise.MountainMassScaleB) + MountainNoise.MountainMassOffsetBZ);
        float mountainMassShape =
            (mountainMassNoiseA * MountainNoise.MountainMassWeightA) +
            (mountainMassNoiseB * MountainNoise.MountainMassWeightB);
        float foothillHeight = settings.HeightAmplitude * foothillRegion *
            ((mountainMassShape * MountainNoise.FoothillShapeScale) + MountainNoise.FoothillShapeBias);
        float mountainMassHeight = settings.HeightAmplitude * coreMountainRegion *
            ((mountainMassShape * MountainNoise.MountainMassShapeScale) + MountainNoise.MountainMassShapeBias);

        // 능선은 그대로 ridged noise를 쓰되, 거친 톱니 느낌을 줄이기 위해 살짝 부드럽게 눌러줍니다.
        float ridgePrimaryNoise = ValueNoise(
            (sampleX * MountainNoise.RidgePrimaryScale) + MountainNoise.RidgePrimaryOffsetX,
            (sampleZ * MountainNoise.RidgePrimaryScale) + MountainNoise.RidgePrimaryOffsetZ);
        float ridgeSecondaryNoise = ValueNoise(
            (sampleX * MountainNoise.RidgeSecondaryScale) + MountainNoise.RidgeSecondaryOffsetX,
            (sampleZ * MountainNoise.RidgeSecondaryScale) + MountainNoise.RidgeSecondaryOffsetZ);
        float ridgePrimary = SoftRidgedNoise(ridgePrimaryNoise);
        float ridgeSecondary = SoftRidgedNoise(ridgeSecondaryNoise);
        float ridgeShape =
            (ridgePrimary * MountainNoise.RidgePrimaryWeight) +
            (ridgeSecondary * MountainNoise.RidgeSecondaryWeight);
        float mountainRidgeHeight = settings.HeightAmplitude * coreMountainRegion *
            ((ridgeShape * MountainNoise.RidgeHeightScale) + MountainNoise.RidgeHeightBias);

        // 산맥 사이에 골짜기가 더 자연스럽게 생기도록 약한 valley term을 넣습니다.
        float valleyNoise = ValueNoise(
            (sampleX * MountainNoise.ValleyScale) + MountainNoise.ValleyOffsetX,
            (sampleZ * MountainNoise.ValleyScale) + MountainNoise.ValleyOffsetZ);
        float valleyDepth = settings.HeightAmplitude * coreMountainRegion *
            ((1f - valleyNoise) * MountainNoise.ValleyDepthScale);

        // 산지로 갈수록 평원 기복을 줄이되 완전히 사라지지 않게 해서, 전환부가 더 자연스럽게 이어집니다.
        float blendedPlainsHeight = plainsHeight * (1f - (foothillRegion * MountainNoise.MountainPlainBlendStrength));
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
        float sampleX = ((worldX * SoilNoise.SoilScale) + (Seed * SoilNoise.SoilOffsetX)) / settings.NoiseScale;
        float sampleZ = ((worldZ * SoilNoise.SoilScale) + (Seed * SoilNoise.SoilOffsetZ)) / settings.NoiseScale;
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
