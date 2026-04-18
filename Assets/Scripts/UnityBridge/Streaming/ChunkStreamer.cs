using System;
using System.Collections.Generic;
using UnityEngine;

// 플레이어 중심 청크를 기준으로 required/unload 집합을 계산하고,
// 이번 프레임에 실제로 적용해야 할 로드/언로드/경계 재빌드 대상을 뽑아내는 스트리밍 상태 객체입니다.
public sealed class ChunkStreamer
{
    // required 집합에서 "이번 프레임에 실제로 로드할 청크"만 추리는 계획기입니다.
    private readonly ChunkLoadPlanner loadPlanner = new();

    // 현재 사용 중인 로드/언로드 반경 정책입니다.
    private IChunkStreamingPolicy loadStreamingPolicy;
    private IChunkStreamingPolicy unloadStreamingPolicy;

    // 현재 required/unload 집합 캐시가 어떤 중심 청크 기준으로 계산됐는지 나타냅니다.
    private ChunkPos? cachedCenterChunk;
    private readonly HashSet<ChunkPos> cachedRequiredChunks = new();
    private readonly HashSet<ChunkPos> cachedRetainedChunks = new();

    // 이전 프레임 유지 집합과 현재 집합의 차이를 계산하기 위한 상태입니다.
    private readonly HashSet<ChunkPos> activeRetainedChunks = new();
    private readonly List<ChunkPos> removedRetainedChunks = new();
    private readonly HashSet<ChunkPos> currentRetainedChunkSetBuffer = new();

    // 스트리밍 갱신 결과를 바깥으로 넘길 때 재사용하는 작업 버퍼입니다.
    private readonly HashSet<ChunkPos> chunksNeedingRebuildBuffer = new();

    // required 집합 안에서 아직 실제로 로드되지 않은 청크 수를 캐시합니다.
    // 이렇게 해두면 매 프레임 cachedRequiredChunks 전체를 다시 순회하지 않고도
    // "아직 더 로드할 청크가 남아 있나?"를 O(1)로 판단할 수 있습니다.
    // 값은 required 집합을 다시 계산할 때 한 번 세고, 이후에는 chunk load/unload에 맞춰 증감만 합니다.
    private int cachedMissingRequiredChunkCount;

    // 월드를 통째로 다시 만들 때 스트리밍 상태만 비웁니다.
    public void Reset()
    {
        loadStreamingPolicy = null;
        unloadStreamingPolicy = null;
        ClearState();
    }

    // world settings가 바뀌거나 스트리밍 모드를 다시 잡아야 할 때 정책과 캐시를 함께 초기화합니다.
    public void RebuildPolicies(VoxelWorldSettings worldSettings)
    {
        loadStreamingPolicy = CreateStreamingPolicy(worldSettings, worldSettings.ViewDistanceInChunks);
        unloadStreamingPolicy = CreateStreamingPolicy(worldSettings, worldSettings.UnloadDistanceInChunks);
        ClearState();
    }

    // 스트리밍 중심, 집합 캐시, delta 계산 버퍼를 모두 초기 상태로 되돌립니다.
    private void ClearState()
    {
        cachedCenterChunk = null;
        cachedRequiredChunks.Clear();
        cachedRetainedChunks.Clear();
        activeRetainedChunks.Clear();
        removedRetainedChunks.Clear();
        chunksNeedingRebuildBuffer.Clear();
        currentRetainedChunkSetBuffer.Clear();
        cachedMissingRequiredChunkCount = 0;
    }

    // required 집합 안의 청크가 실제로 로드 완료됐음을 반영해 missing 개수를 줄입니다.
    public void NotifyChunkLoaded(ChunkPos chunkPos)
    {
        if (cachedRequiredChunks.Contains(chunkPos) && cachedMissingRequiredChunkCount > 0)
        {
            cachedMissingRequiredChunkCount--;
        }
    }

    // required 집합 안의 청크가 언로드됐음을 반영해 missing 개수를 늘립니다.
    public void NotifyChunkUnloaded(ChunkPos chunkPos)
    {
        if (cachedRequiredChunks.Contains(chunkPos))
        {
            cachedMissingRequiredChunkCount++;
        }
    }

    // 현재 중심 청크 기준으로 이번 프레임의 로드/언로드/경계 재빌드 대상을 계산합니다.
    // 실제 청크 생성/삭제/메시 적용은 ChunkManager가 담당하고, 이 객체는 순수하게 스트리밍 상태만 유지합니다.
    public bool Update(
        bool force,
        ChunkPos targetChunk,
        Camera cameraToUse,
        VoxelWorldSettings worldSettings,
        IReadOnlyDictionary<ChunkPos, ChunkData> loadedChunks,
        out IReadOnlyList<ChunkPos> chunksToLoad,
        out IReadOnlyCollection<ChunkPos> chunksToUnload,
        out HashSet<ChunkPos> chunksNeedingRebuild)
    {
        if (loadStreamingPolicy == null || unloadStreamingPolicy == null)
        {
            RebuildPolicies(worldSettings);
        }

        bool centerChanged = !cachedCenterChunk.HasValue || !targetChunk.Equals(cachedCenterChunk.Value);
        RefreshCachedSetsIfNeeded(force, centerChanged, targetChunk, loadedChunks);

        if (!force && !centerChanged && !HasMissingChunks())
        {
            chunksToLoad = Array.Empty<ChunkPos>();
            chunksToUnload = Array.Empty<ChunkPos>();
            chunksNeedingRebuild = chunksNeedingRebuildBuffer;
            chunksNeedingRebuild.Clear();
            return false;
        }

        chunksToUnload = BuildUnloadDelta(force, centerChanged);
        chunksToLoad = BuildLoadPlan(targetChunk, cameraToUse, worldSettings, loadedChunks);

        chunksNeedingRebuild = chunksNeedingRebuildBuffer;
        return true;
    }

    // 중심 청크가 바뀌었거나 강제 갱신일 때만 required/unload 집합 캐시를 다시 계산합니다.
    private void RefreshCachedSetsIfNeeded(
        bool force,
        bool centerChanged,
        ChunkPos targetChunk,
        IReadOnlyDictionary<ChunkPos, ChunkData> loadedChunks)
    {
        if (!force && !centerChanged)
        {
            return;
        }

        loadStreamingPolicy.CollectRequiredChunks(targetChunk, cachedRequiredChunks);
        unloadStreamingPolicy.CollectRequiredChunks(targetChunk, cachedRetainedChunks);
        cachedCenterChunk = targetChunk;
        RecountMissingRequiredChunks(loadedChunks);
    }

    // unload 보호 집합 delta를 계산하고, 사라지는 청크 주변의 재빌드 후보를 함께 모읍니다.
    private IReadOnlyCollection<ChunkPos> BuildUnloadDelta(bool force, bool centerChanged)
    {
        chunksNeedingRebuildBuffer.Clear();

        if (!force && !centerChanged)
        {
            removedRetainedChunks.Clear();
            return removedRetainedChunks;
        }

        CollectRemovedRetainedChunks(cachedRetainedChunks, removedRetainedChunks);
        CollectChunksNeedingRebuildForRemovedChunks(removedRetainedChunks, chunksNeedingRebuildBuffer);
        ReplaceActiveRetainedChunks(cachedRetainedChunks);
        return removedRetainedChunks;
    }

    // required 집합 캐시를 바탕으로 이번 프레임 실제 로드 순서를 계산합니다.
    private IReadOnlyList<ChunkPos> BuildLoadPlan(
        ChunkPos targetChunk,
        Camera cameraToUse,
        VoxelWorldSettings worldSettings,
        IReadOnlyDictionary<ChunkPos, ChunkData> loadedChunks)
    {
        return loadPlanner.BuildLoadPlan(
            cachedRequiredChunks,
            loadedChunks,
            targetChunk,
            cameraToUse,
            worldSettings.MaxChunkLoadsPerFrame,
            worldSettings.LoadPriorityShortlistMultiplier);
    }

    // required 집합 안에 아직 로드되지 않은 청크가 하나라도 있으면 스트리밍을 계속 진행해야 합니다.
    private bool HasMissingChunks()
    {
        return cachedMissingRequiredChunkCount > 0;
    }

    // required 집합이 새로 계산됐을 때만 누락 개수를 다시 셉니다.
    private void RecountMissingRequiredChunks(IReadOnlyDictionary<ChunkPos, ChunkData> loadedChunks)
    {
        int missingCount = 0;
        foreach (ChunkPos chunkPos in cachedRequiredChunks)
        {
            if (!loadedChunks.ContainsKey(chunkPos))
            {
                missingCount++;
            }
        }

        cachedMissingRequiredChunkCount = missingCount;
    }

    // 이전 유지 집합과 현재 집합의 차이를 구해, 이번 프레임에 유지 반경 밖으로 빠진 청크만 뽑습니다.
    private void CollectRemovedRetainedChunks(
        IReadOnlyCollection<ChunkPos> currentRetainedChunks,
        List<ChunkPos> removedChunks)
    {
        removedChunks.Clear();
        currentRetainedChunkSetBuffer.Clear();
        foreach (ChunkPos chunkPos in currentRetainedChunks)
        {
            currentRetainedChunkSetBuffer.Add(chunkPos);
        }

        foreach (ChunkPos chunkPos in activeRetainedChunks)
        {
            if (!currentRetainedChunkSetBuffer.Contains(chunkPos))
            {
                removedChunks.Add(chunkPos);
            }
        }
    }

    // 사라지는 청크의 6방향 이웃은 경계 면 노출 여부가 바뀔 수 있으므로 재빌드 후보에 넣습니다.
    private void CollectChunksNeedingRebuildForRemovedChunks(
        IReadOnlyCollection<ChunkPos> removedChunks,
        HashSet<ChunkPos> chunksNeedingRebuild)
    {
        foreach (ChunkPos chunkPos in removedChunks)
        {
            AddAdjacentChunkPositions(chunkPos, chunksNeedingRebuild);
        }
    }

    // 현재 유지 집합을 다음 프레임 비교 기준으로 교체합니다.
    private void ReplaceActiveRetainedChunks(IReadOnlyCollection<ChunkPos> retainedChunks)
    {
        activeRetainedChunks.Clear();
        foreach (ChunkPos chunkPos in retainedChunks)
        {
            activeRetainedChunks.Add(chunkPos);
        }
    }

    // 한 청크에 맞닿은 6방향 이웃 좌표를 재빌드 후보에 추가합니다.
    private static void AddAdjacentChunkPositions(ChunkPos chunkPos, HashSet<ChunkPos> chunksNeedingRebuild)
    {
        foreach (ChunkPos offset in ChunkPos.OrthogonalOffsets)
        {
            chunksNeedingRebuild.Add(chunkPos + offset);
        }
    }

    // 현재 스트리밍 모드와 반경 설정에 맞는 정책 객체를 만듭니다.
    private static IChunkStreamingPolicy CreateStreamingPolicy(VoxelWorldSettings worldSettings, int horizontalRadius)
    {
        return worldSettings.ActiveStreamingMode == VoxelWorldSettings.StreamingMode.Radial
            ? new RadialStreamingPolicy(horizontalRadius, worldSettings.MinLayerY, worldSettings.MaxLayerY)
            : new SquareStreamingPolicy(horizontalRadius, worldSettings.MinLayerY, worldSettings.MaxLayerY);
    }
}
