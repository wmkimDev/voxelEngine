using System;
using System.Collections.Generic;

// "초기 메싱 큐 + 일반 재빌드 큐" 상태를 분리한 도우미입니다.
// 역할은 단순합니다:
// 1. 새로 로드된 청크의 첫 메시 생성 요청을 모은다.
// 2. 기존 청크의 일반 재빌드 요청을 모은다.
// 3. 한 프레임 예산 안에서 "초기 메싱 먼저, 일반 재빌드 나중" 순서로 꺼내 준다.
// 실제 RebuildMesh 호출 자체는 여기서 하지 않고, ChunkManager가 넘겨준 processChunk 콜백으로 위임합니다.
public sealed class ChunkMeshBuildQueue
{
    private sealed class PendingChunkQueue
    {
        private const int MinimumSparseCompactionStaleCount = 32;

        // "현재 아직 처리 안 된 청크"를 O(1)로 확인하기 위한 집합입니다.
        private readonly HashSet<ChunkPos> pendingChunkSet = new();

        // 실제 처리 순서를 유지하는 리스트입니다.
        // 항목을 한 번 넣고 nextIndex를 전진시키며 소비합니다.
        private readonly List<ChunkPos> orderedChunks = new();

        // 다음 프레임에 어디부터 이어서 처리할지 가리키는 인덱스입니다.
        private int nextIndex;

        public int Count => pendingChunkSet.Count;

        public bool Contains(ChunkPos chunkPos)
        {
            return pendingChunkSet.Contains(chunkPos);
        }

        public void Clear()
        {
            pendingChunkSet.Clear();
            orderedChunks.Clear();
            nextIndex = 0;
        }

        public void Remove(ChunkPos chunkPos)
        {
            // orderedChunks 중간 삭제는 비용이 크기 때문에, remove 시점엔 pending set에서만 빼고
            // 리스트 안엔 tombstone처럼 남겨 둡니다. 대신 stale 항목이 너무 많아지면 중간 compaction을 돌립니다.
            pendingChunkSet.Remove(chunkPos);
            CompactIfSparse();
            CompactIfFullyConsumed();
        }

        public void Enqueue(ChunkPos chunkPos)
        {
            if (!pendingChunkSet.Add(chunkPos))
            {
                return;
            }

            orderedChunks.Add(chunkPos);
        }

        public int ProcessFrameBudget(int maxBuildCount, Func<ChunkPos, bool> processChunk)
        {
            if (maxBuildCount <= 0 || pendingChunkSet.Count == 0)
            {
                return 0;
            }

            int buildsPerformed = 0;
            while (nextIndex < orderedChunks.Count && buildsPerformed < maxBuildCount)
            {
                ChunkPos chunkPos = orderedChunks[nextIndex];
                nextIndex++;

                // 중간에 제거된 청크는 pending set에서 빠져 있으므로 그냥 건너뜁니다.
                if (!pendingChunkSet.Remove(chunkPos))
                {
                    continue;
                }

                if (processChunk(chunkPos))
                {
                    buildsPerformed++;
                }
            }

            CompactIfFullyConsumed();
            return buildsPerformed;
        }

        private void CompactIfSparse()
        {
            if (pendingChunkSet.Count == 0)
            {
                orderedChunks.Clear();
                nextIndex = 0;
                return;
            }

            int remainingEntries = orderedChunks.Count - nextIndex;
            int staleEntryCount = remainingEntries - pendingChunkSet.Count;
            if (staleEntryCount < MinimumSparseCompactionStaleCount || staleEntryCount < pendingChunkSet.Count)
            {
                return;
            }

            int writeIndex = 0;
            for (int readIndex = nextIndex; readIndex < orderedChunks.Count; readIndex++)
            {
                ChunkPos chunkPos = orderedChunks[readIndex];
                if (!pendingChunkSet.Contains(chunkPos))
                {
                    continue;
                }

                orderedChunks[writeIndex] = chunkPos;
                writeIndex++;
            }

            orderedChunks.RemoveRange(writeIndex, orderedChunks.Count - writeIndex);
            nextIndex = 0;
        }

        private void CompactIfFullyConsumed()
        {
            // nextIndex가 리스트 끝까지 도달했다는 건 현재 OrderedList를 모두 소비했다는 뜻입니다.
            // 그 시점엔 남은 대기 항목이 orderedChunks 앞쪽에 없으므로 통째로 비우고
            // 다음 enqueue부터 새 큐처럼 다시 시작합니다.
            if (nextIndex < orderedChunks.Count)
            {
                return;
            }

            orderedChunks.Clear();
            nextIndex = 0;
        }
    }

    // 새로 로드된 청크가 "아직 한 번도 메시를 만든 적 없음" 상태일 때 대기하는 큐입니다.
    // 일반 재빌드보다 우선순위가 높아서, 이번 프레임 예산이 있으면 먼저 처리합니다.
    private readonly PendingChunkQueue pendingInitialBuildChunks = new();

    // 이미 한 번은 메시가 있었던 청크들의 일반 재빌드 대기 큐입니다.
    // 로드/언로드 경계 변화나 편집 후 이웃 갱신 같은 요청이 여기로 들어옵니다.
    private readonly PendingChunkQueue pendingRebuildChunks = new();

    public bool HasPendingBuilds => pendingInitialBuildChunks.Count > 0 || pendingRebuildChunks.Count > 0;

    // 월드를 통째로 다시 만들거나 정리할 때 큐 상태를 모두 비웁니다.
    // "어떤 청크를 빌드해야 하는가"라는 보류 상태만 지우고, 실제 청크 데이터나 렌더러는 건드리지 않습니다.
    public void Clear()
    {
        pendingInitialBuildChunks.Clear();
        pendingRebuildChunks.Clear();
    }

    // 언로드된 청크나 더 이상 유효하지 않은 청크를 큐에서 제거합니다.
    // 초기 빌드/일반 재빌드 두 큐 어디에 들어 있어도 함께 정리합니다.
    public void Remove(ChunkPos chunkPos)
    {
        pendingInitialBuildChunks.Remove(chunkPos);
        pendingRebuildChunks.Remove(chunkPos);
    }

    // 새로 로드된 청크의 "첫 메시 생성"을 예약합니다.
    // 같은 청크가 일반 재빌드 큐에도 들어 있었다면, 첫 메시 생성이 더 중요하므로 재빌드 대기열에서 빼고
    // 초기 빌드 큐로 승격시킵니다.
    public void QueueInitialBuild(ChunkPos chunkPos)
    {
        pendingRebuildChunks.Remove(chunkPos);
        pendingInitialBuildChunks.Enqueue(chunkPos);
    }

    // 이미 존재하는 청크의 일반 재빌드를 예약합니다.
    // 단, 같은 청크가 아직 "첫 메시 생성"도 끝나지 않은 상태라면 일반 재빌드보다 초기 빌드가 우선이므로 무시합니다.
    public void QueueRebuild(ChunkPos chunkPos)
    {
        if (pendingInitialBuildChunks.Contains(chunkPos))
        {
            return;
        }

        pendingRebuildChunks.Enqueue(chunkPos);
    }

    // 이번 프레임에 처리할 빌드 예산을 소비합니다.
    // 규칙은 "초기 빌드 먼저, 남은 예산으로 일반 재빌드"입니다.
    // processChunk는 실제로 해당 청크를 RebuildMesh 하는 바깥 로직이고,
    // 여기서는 큐 순서와 예산만 관리합니다.
    public int ProcessFrameBudget(int maxBuildCount, Func<ChunkPos, bool> processChunk)
    {
        if (maxBuildCount <= 0 || !HasPendingBuilds)
        {
            return 0;
        }

        int initialBuildsPerformed = ProcessQueue(
            pendingInitialBuildChunks,
            maxBuildCount,
            processChunk);
        int remainingBudget = Math.Max(0, maxBuildCount - initialBuildsPerformed);
        int rebuildsPerformed = ProcessQueue(
            pendingRebuildChunks,
            remainingBudget,
            processChunk);

        return initialBuildsPerformed + rebuildsPerformed;
    }

    // 하나의 큐(initial 또는 rebuild)에서 nextIndex부터 예산만큼 이어서 처리합니다.
    // 매 프레임 스냅샷을 다시 만들지 않고, OrderedList 상태를 그대로 유지합니다.
    private static int ProcessQueue(
        PendingChunkQueue pendingChunks,
        int maxBuildCount,
        Func<ChunkPos, bool> processChunk)
    {
        return pendingChunks.ProcessFrameBudget(maxBuildCount, processChunk);
    }
}
