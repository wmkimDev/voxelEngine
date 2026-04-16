using System.Text;
using TMPro;
using UnityEngine;

public sealed class VoxelPerformanceHud : MonoBehaviour
{
    [SerializeField] private ChunkManager chunkManager;
    [SerializeField] private TMP_Text statsText;
    [SerializeField] private float refreshInterval = 0.2f;

    private readonly StringBuilder builder = new();
    private float refreshTimer;
    private float fpsAverage;

    private void Awake()
    {
        if (statsText == null)
        {
            statsText = GetComponentInChildren<TMP_Text>();
        }
    }

    private void Start()
    {
        if (chunkManager == null)
        {
            chunkManager = FindFirstObjectByType<ChunkManager>();
        }

        RefreshText();
    }

    private void Update()
    {
        float currentFps = 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime);
        fpsAverage = fpsAverage <= 0f
            ? currentFps
            : Mathf.Lerp(fpsAverage, currentFps, 0.15f);

        refreshTimer += Time.unscaledDeltaTime;
        if (refreshTimer < refreshInterval)
        {
            return;
        }

        refreshTimer = 0f;
        RefreshText();
    }

    private void RefreshText()
    {
        if (statsText == null)
        {
            return;
        }

        builder.Clear();
        builder.AppendLine("Voxel Performance");
        builder.Append("FPS: ").Append(fpsAverage.ToString("F1")).AppendLine();

        if (chunkManager == null)
        {
            builder.AppendLine("ChunkManager: not found");
            statsText.text = builder.ToString();
            return;
        }

        builder.Append("Mesh Builder: ").Append(chunkManager.ActiveMeshBuilderName).AppendLine();
        builder.Append("Loaded Chunks: ").Append(chunkManager.LoadedChunkCount).AppendLine();
        builder.Append("Chunk Loads / Update: ").Append(chunkManager.LastChunkLoadsPerformed).AppendLine();
        builder.Append("Last Mesh Rebuild: ")
            .Append(VoxelPerformanceStats.LastMeshRebuildMilliseconds.ToString("F3"))
            .AppendLine(" ms");
        builder.Append("Avg Mesh Rebuild: ")
            .Append(VoxelPerformanceStats.AverageMeshRebuildMilliseconds.ToString("F3"))
            .AppendLine(" ms");
        builder.Append("Last Vertices: ").Append(VoxelPerformanceStats.LastVertexCount).AppendLine();
        builder.Append("Last Triangles: ").Append(VoxelPerformanceStats.LastTriangleCount).AppendLine();
        builder.Append("Last Quads: ").Append(VoxelPerformanceStats.LastQuadCount).AppendLine();

        statsText.text = builder.ToString();
    }
}
