using System.Collections.Generic;

public sealed class ChunkLoadScheduler
{
    // 스케줄러는 "무엇을 로드할지"가 아니라 "어떤 순서로 로드할지"만 결정합니다.
    // 이 책임을 분리해두면 나중에 우선순위 큐, 시간 예산, 비동기 Job으로 바꾸기 쉽습니다.
    public List<ChunkPos> SortByDistance(IEnumerable<ChunkPos> chunkPositions, ChunkPos centerChunk)
    {
        return SortByVisibilityAndDistance(chunkPositions, centerChunk, preferredChunkPositions: null);
    }

    // preferredChunkPositions에 들어 있는 청크를 먼저 로드하고,
    // 같은 그룹 안에서는 기존처럼 가까운 청크를 우선으로 정렬합니다.
    public List<ChunkPos> SortByVisibilityAndDistance(
        IEnumerable<ChunkPos> chunkPositions,
        ChunkPos centerChunk,
        HashSet<ChunkPos> preferredChunkPositions)
    {
        var sorted = new List<ChunkPos>(chunkPositions);

        // 카메라 프러스텀 안쪽 청크를 먼저 만들면 플레이어가 실제로 보는 방향이 먼저 채워집니다.
        // 같은 우선순위 그룹 안에서는 가까운 청크를 먼저 만들어 기존 거리 기반 감각을 유지합니다.
        sorted.Sort((a, b) =>
        {
            int visibilityCompare = CompareVisibilityPriority(a, b, preferredChunkPositions);
            if (visibilityCompare != 0)
            {
                return visibilityCompare;
            }

            return GetDistanceSquared(a, centerChunk).CompareTo(GetDistanceSquared(b, centerChunk));
        });

        return sorted;
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

    private static int GetDistanceSquared(ChunkPos chunkPos, ChunkPos centerChunk)
    {
        int dx = chunkPos.X - centerChunk.X;
        int dy = chunkPos.Y - centerChunk.Y;
        int dz = chunkPos.Z - centerChunk.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }
}
