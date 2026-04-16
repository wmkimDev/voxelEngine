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

    [Test, Performance]
    public void Naive_BuildNoiseTerrainWithNeighbors()
    {
        RunBenchmark("Naive", new NaiveMeshBuilder());
    }

    [Test, Performance]
    public void Greedy_BuildNoiseTerrainWithNeighbors()
    {
        RunBenchmark("Greedy", new GreedyMeshBuilder());
    }

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
