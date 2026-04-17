using System.Collections.Generic;

public sealed class ChunkLoadScheduler
{
    // 스케줄러는 "무엇을 로드할지"가 아니라 "어떤 순서로 로드할지"만 결정합니다.
    // 이 책임을 분리해두면 나중에 우선순위 큐, 시간 예산, 비동기 Job으로 바꾸기 쉽습니다.
    public List<ChunkPos> SortByDistance(IEnumerable<ChunkPos> chunkPositions, ChunkPos centerChunk)
    {
        return SortByVisibilityAndDistance(chunkPositions, centerChunk, preferredChunkPositions: null, screenPriorityScores: null);
    }

    // preferredChunkPositions에 들어 있는 청크를 먼저 로드하고,
    // 같은 그룹 안에서는 화면 중앙에 더 가까운 청크를 먼저, 그래도 같으면 가까운 청크를 우선으로 정렬합니다.
    public List<ChunkPos> SortByVisibilityAndDistance(
        IEnumerable<ChunkPos> chunkPositions,
        ChunkPos centerChunk,
        HashSet<ChunkPos> preferredChunkPositions,
        Dictionary<ChunkPos, float> screenPriorityScores)
    {
        var sorted = new List<ChunkPos>(chunkPositions);
        SortByVisibilityAndDistanceInPlace(sorted, centerChunk, preferredChunkPositions, screenPriorityScores);
        return sorted;
    }

    public void SortByVisibilityAndDistanceInPlace(
        List<ChunkPos> chunkPositions,
        ChunkPos centerChunk,
        HashSet<ChunkPos> preferredChunkPositions,
        Dictionary<ChunkPos, float> screenPriorityScores)
    {
        // 카메라 프러스텀 안쪽 청크를 먼저 만들고,
        // 그 안에서는 화면 중앙에 더 가까운 청크를 우선해 회전 시 시야 중심부터 월드가 채워지게 합니다.
        // 그래도 우선순위가 같으면 가까운 청크를 먼저 만들어 기존 거리 기반 감각을 유지합니다.
        chunkPositions.Sort((a, b) =>
        {
            int visibilityCompare = CompareVisibilityPriority(a, b, preferredChunkPositions);
            if (visibilityCompare != 0)
            {
                return visibilityCompare;
            }

            int screenCompare = CompareScreenPriority(a, b, screenPriorityScores);
            if (screenCompare != 0)
            {
                return screenCompare;
            }

            return GetDistanceSquared(a, centerChunk).CompareTo(GetDistanceSquared(b, centerChunk));
        });
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

    private static int CompareScreenPriority(
        ChunkPos a,
        ChunkPos b,
        Dictionary<ChunkPos, float> screenPriorityScores)
    {
        if (screenPriorityScores == null || screenPriorityScores.Count == 0)
        {
            return 0;
        }

        float aScore = screenPriorityScores.TryGetValue(a, out float foundAScore) ? foundAScore : float.NegativeInfinity;
        float bScore = screenPriorityScores.TryGetValue(b, out float foundBScore) ? foundBScore : float.NegativeInfinity;
        return bScore.CompareTo(aScore);
    }

    private static int GetDistanceSquared(ChunkPos chunkPos, ChunkPos centerChunk)
    {
        int dx = chunkPos.X - centerChunk.X;
        int dy = chunkPos.Y - centerChunk.Y;
        int dz = chunkPos.Z - centerChunk.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }
}
