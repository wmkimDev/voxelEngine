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
    // 새로 로드된 청크가 "아직 한 번도 메시를 만든 적 없음" 상태일 때 대기하는 큐입니다.
    // 일반 재빌드보다 우선순위가 높아서, 이번 프레임 예산이 있으면 먼저 처리합니다.
    private readonly HashSet<ChunkPos> pendingInitialBuildChunks = new();

    // initial build 큐를 이번 프레임에 안전하게 순회하기 위한 스냅샷 버퍼입니다.
    // HashSet을 순회하면서 동시에 수정할 수 없어서, 처리 시작 전에 List로 복사해 둡니다.
    private readonly List<ChunkPos> initialBuildQueueSnapshot = new();

    // 이미 한 번은 메시가 있었던 청크들의 일반 재빌드 대기 큐입니다.
    // 로드/언로드 경계 변화나 편집 후 이웃 갱신 같은 요청이 여기로 들어옵니다.
    private readonly HashSet<ChunkPos> pendingRebuildChunks = new();

    // rebuild 큐를 이번 프레임에 안전하게 순회하기 위한 스냅샷 버퍼입니다.
    // initial build와 같은 이유로, 처리 시작 전에 HashSet 내용을 List로 옮겨 둡니다.
    private readonly List<ChunkPos> rebuildQueueSnapshot = new();

    public bool HasPendingBuilds => pendingInitialBuildChunks.Count > 0 || pendingRebuildChunks.Count > 0;

    // 월드를 통째로 다시 만들거나 정리할 때 큐 상태를 모두 비웁니다.
    // "어떤 청크를 빌드해야 하는가"라는 보류 상태만 지우고, 실제 청크 데이터나 렌더러는 건드리지 않습니다.
    public void Clear()
    {
        pendingInitialBuildChunks.Clear();
        initialBuildQueueSnapshot.Clear();
        pendingRebuildChunks.Clear();
        rebuildQueueSnapshot.Clear();
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
        pendingInitialBuildChunks.Add(chunkPos);
    }

    // 이미 존재하는 청크의 일반 재빌드를 예약합니다.
    // 단, 같은 청크가 아직 "첫 메시 생성"도 끝나지 않은 상태라면 일반 재빌드보다 초기 빌드가 우선이므로 무시합니다.
    public void QueueRebuild(ChunkPos chunkPos)
    {
        if (pendingInitialBuildChunks.Contains(chunkPos))
        {
            return;
        }

        pendingRebuildChunks.Add(chunkPos);
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
            initialBuildQueueSnapshot,
            maxBuildCount,
            processChunk);
        int remainingBudget = Math.Max(0, maxBuildCount - initialBuildsPerformed);
        int rebuildsPerformed = ProcessQueue(
            pendingRebuildChunks,
            rebuildQueueSnapshot,
            remainingBudget,
            processChunk);

        return initialBuildsPerformed + rebuildsPerformed;
    }

    // 하나의 큐(initial 또는 rebuild)를 스냅샷으로 꺼내 예산만큼 처리합니다.
    // HashSet을 직접 순회하면서 동시에 수정할 수 없기 때문에, 이번 프레임 처리분은 List 스냅샷으로 복사합니다.
    // 예산을 넘긴 나머지 청크는 다시 원래 큐로 되돌려 다음 프레임에 이어서 처리합니다.
    private static int ProcessQueue(
        HashSet<ChunkPos> pendingChunks,
        List<ChunkPos> queueSnapshot,
        int maxBuildCount,
        Func<ChunkPos, bool> processChunk)
    {
        if (maxBuildCount <= 0 || pendingChunks.Count == 0)
        {
            return 0;
        }

        queueSnapshot.Clear();
        foreach (ChunkPos chunkPos in pendingChunks)
        {
            queueSnapshot.Add(chunkPos);
        }

        pendingChunks.Clear();

        int buildCount = Math.Min(maxBuildCount, queueSnapshot.Count);
        int buildsPerformed = 0;
        for (int i = 0; i < buildCount; i++)
        {
            if (processChunk(queueSnapshot[i]))
            {
                buildsPerformed++;
            }
        }

        for (int i = buildCount; i < queueSnapshot.Count; i++)
        {
            pendingChunks.Add(queueSnapshot[i]);
        }

        queueSnapshot.Clear();
        return buildsPerformed;
    }
}
