using UnityEngine;

public static class VoxelPerformanceStats
{
    public static int LastChunkLoadsPerformed { get; private set; }
    public static int LastChunkRebuildsPerformed { get; private set; }
    public static double LastMeshRebuildMilliseconds { get; private set; }
    public static double AverageMeshRebuildMilliseconds { get; private set; }
    public static int LastVertexCount { get; private set; }
    public static int LastTriangleCount { get; private set; }
    public static int LastQuadCount { get; private set; }

    private static int sampleCount;

    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void RecordChunkLoadCount(int loadCount)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        LastChunkLoadsPerformed = loadCount;
#endif
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void RecordChunkRebuildCount(int rebuildCount)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        LastChunkRebuildsPerformed = rebuildCount;
#endif
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void RecordMeshRebuild(double rebuildMilliseconds, ChunkMeshData meshData)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
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
#endif
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void Reset()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        LastChunkLoadsPerformed = 0;
        LastChunkRebuildsPerformed = 0;
        LastMeshRebuildMilliseconds = 0d;
        AverageMeshRebuildMilliseconds = 0d;
        LastVertexCount = 0;
        LastTriangleCount = 0;
        LastQuadCount = 0;
        sampleCount = 0;
#endif
    }
}
