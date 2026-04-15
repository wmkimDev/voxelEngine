using System.Collections.Generic;

public sealed class ChunkLoadScheduler
{
    // 스케줄러는 "무엇을 로드할지"가 아니라 "어떤 순서로 로드할지"만 결정합니다.
    // 이 책임을 분리해두면 나중에 우선순위 큐, 시간 예산, 비동기 Job으로 바꾸기 쉽습니다.
    public List<ChunkPos> SortByDistance(IEnumerable<ChunkPos> chunkPositions, ChunkPos centerChunk)
    {
        var sorted = new List<ChunkPos>(chunkPositions);

        // 가까운 청크를 먼저 만들면 이동 중에 플레이어 주변부터 채워집니다.
        // 아직 비동기/Job이 없으므로 이 정렬 뒤의 생성 작업은 모두 메인 스레드에서 실행됩니다.
        sorted.Sort((a, b) =>
            GetDistanceSquared(a, centerChunk).CompareTo(GetDistanceSquared(b, centerChunk)));

        return sorted;
    }

    private static int GetDistanceSquared(ChunkPos chunkPos, ChunkPos centerChunk)
    {
        int dx = chunkPos.X - centerChunk.X;
        int dy = chunkPos.Y - centerChunk.Y;
        int dz = chunkPos.Z - centerChunk.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }
}
