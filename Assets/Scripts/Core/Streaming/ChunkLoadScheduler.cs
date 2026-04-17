using System.Collections.Generic;

public sealed class ChunkLoadScheduler
{
    private readonly List<ChunkPos> bestChunkPositionsBuffer = new();

    // 스케줄러는 "무엇을 로드할지"가 아니라 "어떤 순서로 로드할지"만 결정합니다.
    // 이 책임을 분리해두면 나중에 우선순위 큐, 시간 예산, 비동기 Job으로 바꾸기 쉽습니다.
    public List<ChunkPos> SortByDistance(IEnumerable<ChunkPos> chunkPositions, ChunkPos centerChunk)
    {
        return SortByVisibilityAndDistance(chunkPositions, centerChunk, preferredChunkPositions: null, forwardPriorityScores: null);
    }

    // preferredChunkPositions에 들어 있는 청크를 먼저 로드하고,
    // 같은 그룹 안에서는 카메라가 바라보는 방향에 더 가까운 청크를 먼저, 그래도 같으면 가까운 청크를 우선으로 정렬합니다.
    public List<ChunkPos> SortByVisibilityAndDistance(
        IEnumerable<ChunkPos> chunkPositions,
        ChunkPos centerChunk,
        HashSet<ChunkPos> preferredChunkPositions,
        Dictionary<ChunkPos, float> forwardPriorityScores)
    {
        var sorted = new List<ChunkPos>(chunkPositions);
        SortByVisibilityAndDistanceInPlace(sorted, centerChunk, preferredChunkPositions, forwardPriorityScores);
        return sorted;
    }

    public void SortByVisibilityAndDistanceInPlace(
        List<ChunkPos> chunkPositions,
        ChunkPos centerChunk,
        HashSet<ChunkPos> preferredChunkPositions,
        Dictionary<ChunkPos, float> forwardPriorityScores)
    {
        chunkPositions.Sort((a, b) => CompareChunkPriority(a, b, centerChunk, preferredChunkPositions, forwardPriorityScores));
    }

    public void SelectTopByVisibilityAndDistanceInPlace(
        List<ChunkPos> chunkPositions,
        ChunkPos centerChunk,
        HashSet<ChunkPos> preferredChunkPositions,
        Dictionary<ChunkPos, float> forwardPriorityScores,
        int maxCount)
    {
        if (maxCount <= 0)
        {
            chunkPositions.Clear();
            return;
        }

        if (chunkPositions.Count <= maxCount)
        {
            SortByVisibilityAndDistanceInPlace(chunkPositions, centerChunk, preferredChunkPositions, forwardPriorityScores);
            return;
        }

        bestChunkPositionsBuffer.Clear();

        foreach (ChunkPos candidate in chunkPositions)
        {
            if (bestChunkPositionsBuffer.Count < maxCount)
            {
                bestChunkPositionsBuffer.Add(candidate);
                continue;
            }

            int worstIndex = 0;
            for (int i = 1; i < bestChunkPositionsBuffer.Count; i++)
            {
                if (CompareChunkPriority(
                        bestChunkPositionsBuffer[worstIndex],
                        bestChunkPositionsBuffer[i],
                        centerChunk,
                        preferredChunkPositions,
                        forwardPriorityScores) < 0)
                {
                    worstIndex = i;
                }
            }

            if (CompareChunkPriority(candidate, bestChunkPositionsBuffer[worstIndex], centerChunk, preferredChunkPositions, forwardPriorityScores) < 0)
            {
                bestChunkPositionsBuffer[worstIndex] = candidate;
            }
        }

        chunkPositions.Clear();
        chunkPositions.AddRange(bestChunkPositionsBuffer);
        chunkPositions.Sort((a, b) => CompareChunkPriority(a, b, centerChunk, preferredChunkPositions, forwardPriorityScores));
    }

    private static int CompareVisibilityPriority(
        ChunkPos a,
        ChunkPos b,
        HashSet<ChunkPos> preferredChunkPositions)
    {
        if (preferredChunkPositions == null || preferredChunkPositions.Count == 0)
        {
            return 0;
        }

        bool aPreferred = preferredChunkPositions.Contains(a);
        bool bPreferred = preferredChunkPositions.Contains(b);
        if (aPreferred == bPreferred)
        {
            return 0;
        }

        return aPreferred ? -1 : 1;
    }

    private static int CompareForwardPriority(
        ChunkPos a,
        ChunkPos b,
        Dictionary<ChunkPos, float> forwardPriorityScores)
    {
        if (forwardPriorityScores == null || forwardPriorityScores.Count == 0)
        {
            return 0;
        }

        float aScore = forwardPriorityScores.TryGetValue(a, out float foundAScore) ? foundAScore : float.NegativeInfinity;
        float bScore = forwardPriorityScores.TryGetValue(b, out float foundBScore) ? foundBScore : float.NegativeInfinity;
        return bScore.CompareTo(aScore);
    }

    private static int GetDistanceSquared(ChunkPos chunkPos, ChunkPos centerChunk)
    {
        int dx = chunkPos.X - centerChunk.X;
        int dy = chunkPos.Y - centerChunk.Y;
        int dz = chunkPos.Z - centerChunk.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    private static int CompareChunkPriority(
        ChunkPos a,
        ChunkPos b,
        ChunkPos centerChunk,
        HashSet<ChunkPos> preferredChunkPositions,
        Dictionary<ChunkPos, float> forwardPriorityScores)
    {
        // 먼저 플레이어 주변부터 안정적으로 채우기 위해 거리 가까운 청크를 1순위로 둡니다.
        // 같은 거리권 안에서만 "현재 화면에 보이는가"와 "정면에 가까운가"를 tie-breaker로 사용합니다.
        int distanceCompare = GetDistanceSquared(a, centerChunk).CompareTo(GetDistanceSquared(b, centerChunk));
        if (distanceCompare != 0)
        {
            return distanceCompare;
        }

        int visibilityCompare = CompareVisibilityPriority(a, b, preferredChunkPositions);
        if (visibilityCompare != 0)
        {
            return visibilityCompare;
        }

        int forwardCompare = CompareForwardPriority(a, b, forwardPriorityScores);
        if (forwardCompare != 0)
        {
            return forwardCompare;
        }

        int xCompare = a.X.CompareTo(b.X);
        if (xCompare != 0)
        {
            return xCompare;
        }

        int yCompare = a.Y.CompareTo(b.Y);
        if (yCompare != 0)
        {
            return yCompare;
        }

        return a.Z.CompareTo(b.Z);
    }
}
