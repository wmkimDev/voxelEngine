using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;

public sealed class NaiveMeshBuilderPerformanceTests
{
    private const string CaseName = "SolidChunkWithCenteredAirRoom";
    private const int ChunkSize = 8;
    private const int CarvedRoomMin = 2;
    private const int CarvedRoomMaxExclusive = 6;
    private const int WarmupCount = 3;
    private const int MeasurementCount = 10;

    [Test, Performance]
    public void BuildSolidChunkWithCenteredAirRoom()
    {
        ChunkNeighborhood neighborhood = CreateSolidChunkWithCarvedRoom();
        var builder = new NaiveMeshBuilder();

        LogBenchmarkCondition();

        Measure.Method(() =>
            {
                ChunkMeshData meshData = builder.Schedule(neighborhood).Complete();
                Consume(meshData);
            })
            .WarmupCount(WarmupCount)
            .MeasurementCount(MeasurementCount)
            .Run();

        ChunkMeshData result = builder.Schedule(neighborhood).Complete();
        LogMeshStats("Naive", result);
    }

    private static ChunkNeighborhood CreateSolidChunkWithCarvedRoom()
    {
        var center = new ChunkData(ChunkSize);

        for (int z = 0; z < ChunkSize; z++)
        {
            for (int y = 0; y < ChunkSize; y++)
            {
                for (int x = 0; x < ChunkSize; x++)
                {
                    center.SetVoxel(new LocalPos(x, y, z), VoxelType.Stone);
                }
            }
        }

        for (int z = CarvedRoomMin; z < CarvedRoomMaxExclusive; z++)
        {
            for (int y = CarvedRoomMin; y < CarvedRoomMaxExclusive; y++)
            {
                for (int x = CarvedRoomMin; x < CarvedRoomMaxExclusive; x++)
                {
                    center.SetVoxel(new LocalPos(x, y, z), VoxelType.Air);
                }
            }
        }

        return new ChunkNeighborhood(center, null, null, null, null, null, null);
    }

    private static void Consume(ChunkMeshData meshData)
    {
        // JIT/최적화가 결과 생성을 통째로 무시하지 못하도록 Count 값을 읽습니다.
        Assert.GreaterOrEqual(meshData.Vertices.Count, 0);
    }

    private static void LogBenchmarkCondition()
    {
        Debug.Log(
            $"[Mesh Benchmark] Naive | case {CaseName} | chunk {ChunkSize}^3 | " +
            $"warmup {WarmupCount} | samples {MeasurementCount}");
    }

    private static void LogMeshStats(string label, ChunkMeshData meshData)
    {
        int vertices = meshData.Vertices.Count;
        int triangles = meshData.Triangles.Count / 3;
        int quads = meshData.Triangles.Count / 6;

        Debug.Log(
            $"[Mesh Benchmark] {label} | vertices {vertices} | triangles {triangles} | quads {quads}");
    }
}
