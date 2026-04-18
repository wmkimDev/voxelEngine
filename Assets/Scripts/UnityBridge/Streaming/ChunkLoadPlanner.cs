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

        BuildDistanceShortlist(centerChunk, maxChunkLoadsPerFrame, shortlistMultiplier);
        RankShortlistByVisibilityAndForward(cameraToUse, centerChunk, maxChunkLoadsPerFrame);

        return loadPriorityShortlistBuffer;
    }

    // 아직 로드되지 않은 청크들 중에서 거리 기준으로만 1차 shortlist를 만듭니다.
    private void BuildDistanceShortlist(
        ChunkPos centerChunk,
        int maxChunkLoadsPerFrame,
        int shortlistMultiplier)
    {
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
    }

    // 거리 shortlist만 대상으로, 카메라에 보이는지와 바라보는 방향 점수를 반영해 최종 순위를 다시 매깁니다.
    private void RankShortlistByVisibilityAndForward(
        Camera cameraToUse,
        ChunkPos centerChunk,
        int maxChunkLoadsPerFrame)
    {
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
    }
}
