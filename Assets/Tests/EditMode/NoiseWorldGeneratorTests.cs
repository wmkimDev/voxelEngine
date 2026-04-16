using NUnit.Framework;

public sealed class NoiseWorldGeneratorTests
{
    private const int ChunkSize = 16;

    // beachHeight보다 낮은 지형은 grass 대신 sand가 표면과 얕은 지층에 배치돼야 합니다.
    [Test]
    public void Generate_UsesSandLayersForBeachHeights()
    {
        var generator = new NoiseWorldGenerator(new NoiseWorldGeneratorSettings(
            seed: 12345,
            noiseScale: 1000f,
            baseHeight: 6,
            heightAmplitude: 1,
            beachHeight: 100,
            topSoilDepth: 3,
            topSoilDepthVariation: 0,
            caveNoiseScale: 18f,
            caveThreshold: 1f,
            caveSurfaceClearance: 4));
        var chunk = new ChunkData(ChunkSize);

        generator.Generate(new ChunkPos(0, 0, 0), chunk);

        int surfaceY = FindSurfaceY(chunk, 0, 0);
        Assert.That(surfaceY, Is.GreaterThanOrEqualTo(0));
        Assert.That(chunk.GetVoxel(new LocalPos(0, surfaceY, 0)), Is.EqualTo(VoxelType.Sand));
        Assert.That(chunk.GetVoxel(new LocalPos(0, surfaceY - 1, 0)), Is.EqualTo(VoxelType.Sand));
        Assert.That(chunk.GetVoxel(new LocalPos(0, surfaceY - 2, 0)), Is.EqualTo(VoxelType.Sand));
        Assert.That(chunk.GetVoxel(new LocalPos(0, surfaceY - 3, 0)), Is.EqualTo(VoxelType.Sand));
        Assert.That(chunk.GetVoxel(new LocalPos(0, surfaceY - 4, 0)), Is.EqualTo(VoxelType.Stone));
    }

    // caveThreshold를 0으로 두면 표면 보호 두께 아래는 모두 동굴로 비워집니다.
    // 이 테스트는 caveSurfaceClearance가 실제로 표면 근처 지층을 남기는지 확인합니다.
    [Test]
    public void Generate_PreservesSurfaceLayersAboveCaveClearance()
    {
        var generator = new NoiseWorldGenerator(new NoiseWorldGeneratorSettings(
            seed: 12345,
            noiseScale: 1000f,
            baseHeight: 10,
            heightAmplitude: 1,
            beachHeight: 0,
            topSoilDepth: 3,
            topSoilDepthVariation: 0,
            caveNoiseScale: 12f,
            caveThreshold: 0f,
            caveSurfaceClearance: 3));
        var chunk = new ChunkData(ChunkSize);

        generator.Generate(new ChunkPos(0, 0, 0), chunk);

        int surfaceY = FindSurfaceY(chunk, 0, 0);
        Assert.That(surfaceY, Is.GreaterThanOrEqualTo(4));
        Assert.That(chunk.GetVoxel(new LocalPos(0, surfaceY, 0)), Is.EqualTo(VoxelType.Grass));
        Assert.That(chunk.GetVoxel(new LocalPos(0, surfaceY - 1, 0)), Is.Not.EqualTo(VoxelType.Air));
        Assert.That(chunk.GetVoxel(new LocalPos(0, surfaceY - 2, 0)), Is.Not.EqualTo(VoxelType.Air));
        Assert.That(chunk.GetVoxel(new LocalPos(0, surfaceY - 3, 0)), Is.Not.EqualTo(VoxelType.Air));
        Assert.That(chunk.GetVoxel(new LocalPos(0, surfaceY - 4, 0)), Is.EqualTo(VoxelType.Air));
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
