using System.Collections.Generic;
using UnityEngine;

// "이번 프레임에 어떤 청크를 로드할지"를 계산하는 전용 계획기입니다.
public sealed class ChunkLoadPlanner
{
    private readonly ChunkLoadScheduler loadScheduler = new();
    private readonly ChunkStreamingPriorityEvaluator streamingPriorityEvaluator = new();
    private readonly List<ChunkPos> chunksToLoadBuffer = new();
    private readonly List<ChunkPos> loadPriorityShortlistBuffer = new();
    private readonly HashSet<ChunkPos> visibleChunkPositionsBuffer = new();
    private readonly Dictionary<ChunkPos, float> forwardPriorityScoresBuffer = new();

    // requiredChunks 중 아직 로드되지 않은 청크를 골라
    // "거리 shortlist -> visible/forward tie-break" 규칙으로 이번 프레임 로드 계획을 만듭니다.
    // 반환값은 내부 재사용 버퍼이므로, 호출자는 바로 순회만 하고 보관하지 않아야 합니다.
    public IReadOnlyList<ChunkPos> BuildLoadPlan(
        IReadOnlyCollection<ChunkPos> requiredChunks,
        IReadOnlyDictionary<ChunkPos, ChunkData> loadedChunks,
        ChunkPos centerChunk,
        Camera cameraToUse,
        int maxChunkLoadsPerFrame,
        int shortlistMultiplier)
    {
        chunksToLoadBuffer.Clear();
        foreach (ChunkPos chunkPos in requiredChunks)
        {
            if (!loadedChunks.ContainsKey(chunkPos))
            {
                chunksToLoadBuffer.Add(chunkPos);
            }
        }

        int shortlistCount = Mathf.Min(
            chunksToLoadBuffer.Count,
            maxChunkLoadsPerFrame * shortlistMultiplier);

        loadPriorityShortlistBuffer.Clear();
        loadPriorityShortlistBuffer.AddRange(chunksToLoadBuffer);
        loadScheduler.SelectTopByVisibilityAndDistanceInPlace(
            loadPriorityShortlistBuffer,
            centerChunk,
            preferredChunkPositions: null,
            forwardPriorityScores: null,
            shortlistCount);

        streamingPriorityEvaluator.CollectVisibleChunkPositions(
            cameraToUse,
            loadPriorityShortlistBuffer,
            visibleChunkPositionsBuffer);
        streamingPriorityEvaluator.CollectForwardPriorityScores(
            cameraToUse,
            loadPriorityShortlistBuffer,
            forwardPriorityScoresBuffer);
        loadScheduler.SelectTopByVisibilityAndDistanceInPlace(
            loadPriorityShortlistBuffer,
            centerChunk,
            visibleChunkPositionsBuffer,
            forwardPriorityScoresBuffer,
            maxChunkLoadsPerFrame);

        return loadPriorityShortlistBuffer;
    }
}
