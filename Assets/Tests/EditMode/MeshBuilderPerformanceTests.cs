using System.Diagnostics;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;

public sealed class MeshBuilderPerformanceTests
{
    private const string CaseName = "NoiseTerrainWithNeighbors";
    private const int ChunkSize = 16;
    private const int Seed = 12345;
    private const float NoiseScale = 10f;
    private const int BaseHeight = 3;
    private const int HeightAmplitude = 8;
    private const int WarmupCount = 3;
    private const int MeasurementCount = 10;
    private const int StopwatchSamples = 10;

    // 같은 노이즈 입력에서 Naive 메셔의 기준 성능을 측정합니다.
    // 이후 Greedy나 Job 기반 구현과 비교할 때 출발점이 되는 테스트입니다.
    [Test, Performance]
    public void Naive_BuildNoiseTerrainWithNeighbors()
    {
        RunBenchmark("Naive", new NaiveMeshBuilder());
    }

    // 같은 노이즈 입력에서 Greedy 메셔의 성능을 측정합니다.
    // 면 병합이 실제로 얼마나 비용과 메시 크기를 줄이는지 비교할 때 사용합니다.
    [Test, Performance]
    public void Greedy_BuildNoiseTerrainWithNeighbors()
    {
        RunBenchmark("Greedy", new GreedyMeshBuilder());
    }

    // Greedy 메셔가 같은 지형에서 Naive보다 적은 쿼드를 만드는지 검증합니다.
    // 성능 자체보다 "면 병합이 실제로 일어났는가"를 확인하는 정확성 테스트입니다.
    [Test]
    public void Greedy_ReducesQuadCountOnNoiseTerrain()
    {
        ChunkNeighborhood neighborhood = CreateNoiseTerrainWithNeighbors();
        MeshStats naive = GetMeshStats(new NaiveMeshBuilder().Schedule(neighborhood).Complete());
        MeshStats greedy = GetMeshStats(new GreedyMeshBuilder().Schedule(neighborhood).Complete());

        Assert.Less(greedy.Quads, naive.Quads);

        float reduction = 100f * (1f - (greedy.Quads / (float)naive.Quads));
        UnityEngine.Debug.Log(
            $"[Mesh Benchmark] Greedy reduction | case {CaseName} | " +
            $"naive quads {naive.Quads} | greedy quads {greedy.Quads} | reduced {reduction:F1}%");
    }

    // 공용 FaceTopology 테이블의 winding 순서가 저장된 normal 방향과 일치하는지 검증합니다.
    // face 규칙이 뒤집히면 Naive/Greedy/Job 메셔가 모두 잘못될 수 있으므로 별도로 막아둡니다.
    [Test]
    public void FaceTopology_WindingMatchesStoredNormals()
    {
        Vec3 p0 = new Vec3(0, 0, 0);
        Vec3 pU = new Vec3(1, 0, 0);
        Vec3 pV = new Vec3(0, 1, 0);
        Vec3 pUV = new Vec3(1, 1, 0);

        for (int i = 0; i < 6; i++)
        {
            FaceDirection direction = (FaceDirection)i;
            Vec3 a = GetWindingCorner(direction, 0, p0, pU, pV, pUV);
            Vec3 b = GetWindingCorner(direction, 1, p0, pU, pV, pUV);
            Vec3 c = GetWindingCorner(direction, 2, p0, pU, pV, pUV);

            Vec3 expectedNormal = FaceTopology.GetNormal(direction);
            Vec3 actualNormal = Vec3.Cross(b - a, c - a);

            Assert.Greater(
                Vec3.Dot(actualNormal, expectedNormal),
                0f,
                $"FaceTopology winding does not match normal for {direction}.");
        }
    }

    // Greedy가 실제로 만든 삼각형의 winding이 각 정점 normal과 같은 방향을 보는지 확인합니다.
    // 화면에서 면이 투명하게 보이거나 한쪽에서만 보이는 회귀를 잡기 위한 테스트입니다.
    [Test]
    public void Greedy_TriangleWindingMatchesNormals()
    {
        ChunkMeshData meshData = new GreedyMeshBuilder()
            .Schedule(CreateNoiseTerrainWithNeighbors())
            .Complete();

        for (int i = 0; i < meshData.Triangles.Count; i += 3)
        {
            int aIndex = meshData.Triangles[i + 0];
            int bIndex = meshData.Triangles[i + 1];
            int cIndex = meshData.Triangles[i + 2];

            Vec3 a = meshData.Vertices[aIndex];
            Vec3 b = meshData.Vertices[bIndex];
            Vec3 c = meshData.Vertices[cIndex];
            Vec3 expectedNormal = meshData.Normals[aIndex];
            Vec3 actualNormal = Vec3.Cross(b - a, c - a);

            Assert.Greater(
                Vec3.Dot(actualNormal, expectedNormal),
                0f,
                $"Triangle winding does not match normal at triangle index {i / 3}.");
        }
    }

    private static void RunBenchmark(string label, IMeshBuilder builder)
    {
        ChunkNeighborhood neighborhood = CreateNoiseTerrainWithNeighbors();

        LogBenchmarkCondition(label);

        Measure.Method(() =>
            {
                ChunkMeshData meshData = builder.Schedule(neighborhood).Complete();
                Consume(meshData);
            })
            .WarmupCount(WarmupCount)
            .MeasurementCount(MeasurementCount)
            .Run();

        ChunkMeshData result = builder.Schedule(neighborhood).Complete();
        MeshTiming timing = MeasureWithStopwatch(builder, neighborhood);
        LogMeshStats(label, result, timing);
    }

    private static ChunkNeighborhood CreateNoiseTerrainWithNeighbors()
    {
        var generator = new NoiseWorldGenerator(Seed, NoiseScale, BaseHeight, HeightAmplitude);
        var center = GenerateChunk(generator, new ChunkPos(0, 0, 0));

        // 실제 스트리밍에서는 옆 청크가 존재하므로, 벤치마크도 6방향 이웃을 함께 넘깁니다.
        // 그래야 청크 경계 면을 불필요하게 만들지 않고 실제 월드와 가까운 조건이 됩니다.
        return new ChunkNeighborhood(
            center,
            GenerateChunk(generator, new ChunkPos(1, 0, 0)),
            GenerateChunk(generator, new ChunkPos(-1, 0, 0)),
            GenerateChunk(generator, new ChunkPos(0, 1, 0)),
            GenerateChunk(generator, new ChunkPos(0, -1, 0)),
            GenerateChunk(generator, new ChunkPos(0, 0, 1)),
            GenerateChunk(generator, new ChunkPos(0, 0, -1)));
    }

    private static ChunkData GenerateChunk(IWorldGenerator generator, ChunkPos chunkPos)
    {
        var chunk = new ChunkData(ChunkSize);
        generator.Generate(chunkPos, chunk);
        return chunk;
    }

    private static void Consume(ChunkMeshData meshData)
    {
        // JIT/최적화가 결과 생성을 통째로 무시하지 못하도록 Count 값을 읽습니다.
        Assert.GreaterOrEqual(meshData.Vertices.Count, 0);
    }

    private static void LogBenchmarkCondition(string label)
    {
        UnityEngine.Debug.Log(
            $"[Mesh Benchmark] {label} | case {CaseName} | chunk {ChunkSize}^3 | " +
            $"seed {Seed} | warmup {WarmupCount} | samples {MeasurementCount}");
    }

    private static MeshTiming MeasureWithStopwatch(IMeshBuilder builder, ChunkNeighborhood neighborhood)
    {
        double min = double.MaxValue;
        double max = 0;
        double total = 0;

        for (int i = 0; i < StopwatchSamples; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            ChunkMeshData meshData = builder.Schedule(neighborhood).Complete();
            stopwatch.Stop();

            Consume(meshData);

            double elapsed = stopwatch.Elapsed.TotalMilliseconds;
            min = System.Math.Min(min, elapsed);
            max = System.Math.Max(max, elapsed);
            total += elapsed;
        }

        return new MeshTiming(total / StopwatchSamples, min, max);
    }

    private static void LogMeshStats(string label, ChunkMeshData meshData, MeshTiming timing)
    {
        MeshStats stats = GetMeshStats(meshData);

        UnityEngine.Debug.Log(
            $"[Mesh Benchmark] {label} | avg {timing.AverageMilliseconds:F3} ms | " +
            $"min {timing.MinMilliseconds:F3} ms | max {timing.MaxMilliseconds:F3} ms | " +
            $"vertices {stats.Vertices} | triangles {stats.Triangles} | quads {stats.Quads}");
    }

    private static MeshStats GetMeshStats(ChunkMeshData meshData)
    {
        return new MeshStats(
            meshData.Vertices.Count,
            meshData.Triangles.Count / 3,
            meshData.Triangles.Count / 6);
    }

    private static Vec3 GetWindingCorner(
        FaceDirection direction,
        int cornerIndex,
        Vec3 p0,
        Vec3 pU,
        Vec3 pV,
        Vec3 pUV)
    {
        return FaceTopology.GetWindingCornerIndex(direction, cornerIndex) switch
        {
            0 => p0,
            1 => pU,
            2 => pV,
            _ => pUV
        };
    }

    private readonly struct MeshTiming
    {
        // Stopwatch로 여러 번 직접 측정한 시간 요약입니다.
        // Unity Performance Testing 결과는 XML/Test Runner에 저장되고,
        // 이 값은 콘솔에서 Naive/Greedy를 빠르게 비교하기 위해 따로 출력합니다.
        public readonly double AverageMilliseconds;
        public readonly double MinMilliseconds;
        public readonly double MaxMilliseconds;

        public MeshTiming(double averageMilliseconds, double minMilliseconds, double maxMilliseconds)
        {
            AverageMilliseconds = averageMilliseconds;
            MinMilliseconds = minMilliseconds;
            MaxMilliseconds = maxMilliseconds;
        }
    }

    private readonly struct MeshStats
    {
        // 빌더가 만든 메시 크기 요약입니다.
        // Greedy가 실제로 면 수를 줄였는지 vertices/triangles/quads로 확인합니다.
        public readonly int Vertices;
        public readonly int Triangles;
        public readonly int Quads;

        public MeshStats(int vertices, int triangles, int quads)
        {
            Vertices = vertices;
            Triangles = triangles;
            Quads = quads;
        }
    }
}
