using NUnit.Framework;

public sealed class NoiseWorldGeneratorTests
{
    private const int ChunkSize = 16;

    [Test]
    public void Generate_UsesGrassDirtAndStoneLayers()
    {
        var generator = new NoiseWorldGenerator(new NoiseWorldGeneratorSettings(
            seed: 12345,
            noiseScale: 1000f,
            baseHeight: 6,
            heightAmplitude: 1,
            topSoilDepth: 3,
            topSoilDepthVariation: 0));
        var chunk = new ChunkData(ChunkSize);

        generator.Generate(new ChunkPos(0, 0, 0), chunk);

        int surfaceY = FindSurfaceY(chunk, 0, 0);
        Assert.That(surfaceY, Is.GreaterThanOrEqualTo(0));
        Assert.That(chunk.GetVoxel(new LocalPos(0, surfaceY, 0)), Is.EqualTo(VoxelType.Grass));
        Assert.That(chunk.GetVoxel(new LocalPos(0, surfaceY - 1, 0)), Is.EqualTo(VoxelType.Dirt));
        Assert.That(chunk.GetVoxel(new LocalPos(0, surfaceY - 2, 0)), Is.EqualTo(VoxelType.Dirt));
        Assert.That(chunk.GetVoxel(new LocalPos(0, surfaceY - 3, 0)), Is.EqualTo(VoxelType.Dirt));
        Assert.That(chunk.GetVoxel(new LocalPos(0, surfaceY - 4, 0)), Is.EqualTo(VoxelType.Stone));
    }

    // 가까운 평지 위에 멀리 큰 산맥이 드러나게 하려면, 장거리 샘플링 시 높이 편차가 충분히 커야 합니다.
    [Test]
    public void GetSurfaceHeight_ProducesBroadMountainRanges()
    {
        var generator = new NoiseWorldGenerator(new NoiseWorldGeneratorSettings(
            seed: 12345,
            noiseScale: 26f,
            baseHeight: 18,
            heightAmplitude: 38,
            topSoilDepth: 4,
            topSoilDepthVariation: 2));

        int minHeight = int.MaxValue;
        int maxHeight = int.MinValue;

        for (int x = 0; x <= 2048; x += 32)
        {
            int sampledHeight = generator.GetSurfaceHeight(x, 512);
            if (sampledHeight < minHeight)
            {
                minHeight = sampledHeight;
            }

            if (sampledHeight > maxHeight)
            {
                maxHeight = sampledHeight;
            }
        }

        Assert.That(maxHeight - minHeight, Is.GreaterThanOrEqualTo(20));
    }

    // 평원과 산맥을 확실히 구분하려면, 장거리 샘플에서 낮은 구간과 높은 구간이
    // 같은 비율로 섞이지 않고 충분히 벌어져 있어야 합니다.
    [Test]
    public void GetSurfaceHeight_SeparatesPlainsFromMountainRanges()
    {
        var generator = new NoiseWorldGenerator(new NoiseWorldGeneratorSettings(
            seed: 12345,
            noiseScale: 34f,
            baseHeight: 18,
            heightAmplitude: 32,
            topSoilDepth: 4,
            topSoilDepthVariation: 2));

        int plainSamples = 0;
        int mountainSamples = 0;

        for (int x = 0; x <= 4096; x += 32)
        {
            int sampledHeight = generator.GetSurfaceHeight(x, 768);
            if (sampledHeight <= 28)
            {
                plainSamples++;
            }

            if (sampledHeight >= 48)
            {
                mountainSamples++;
            }
        }

        Assert.That(plainSamples, Is.GreaterThan(8));
        Assert.That(mountainSamples, Is.GreaterThan(8));
    }

    private static int FindSurfaceY(ChunkData chunk, int x, int z)
    {
        for (int y = chunk.Size - 1; y >= 0; y--)
        {
            if (chunk.GetVoxel(new LocalPos(x, y, z)) != VoxelType.Air)
            {
                return y;
            }
        }

        return -1;
    }
}
