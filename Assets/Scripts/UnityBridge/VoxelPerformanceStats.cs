using UnityEngine;

public static class VoxelPerformanceStats
{
    public static double LastMeshRebuildMilliseconds { get; private set; }
    public static double AverageMeshRebuildMilliseconds { get; private set; }
    public static int LastVertexCount { get; private set; }
    public static int LastTriangleCount { get; private set; }
    public static int LastQuadCount { get; private set; }

    private static int sampleCount;

    public static void RecordMeshRebuild(double rebuildMilliseconds, ChunkMeshData meshData)
    {
        LastMeshRebuildMilliseconds = rebuildMilliseconds;
        LastVertexCount = meshData.Vertices.Count;
        LastTriangleCount = meshData.Triangles.Count / 3;
        LastQuadCount = meshData.Triangles.Count / 6;

        sampleCount++;
        if (sampleCount == 1)
        {
            AverageMeshRebuildMilliseconds = rebuildMilliseconds;
            return;
        }

        AverageMeshRebuildMilliseconds +=
            (rebuildMilliseconds - AverageMeshRebuildMilliseconds) / sampleCount;
    }

    public static void Reset()
    {
        LastMeshRebuildMilliseconds = 0d;
        AverageMeshRebuildMilliseconds = 0d;
        LastVertexCount = 0;
        LastTriangleCount = 0;
        LastQuadCount = 0;
        sampleCount = 0;
    }
}
