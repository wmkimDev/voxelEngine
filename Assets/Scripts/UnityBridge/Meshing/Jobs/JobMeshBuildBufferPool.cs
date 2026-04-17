using System;
using System.Collections.Generic;
using Unity.Collections;

// Job 메셔용 Native 버퍼를 크기별로 재사용하는 풀입니다.
// Schedule마다 새 TempJob 버퍼를 만들지 않고, 완료된 handle이 반납한 Persistent 버퍼를 다시 빌려 씁니다.
public sealed class JobMeshBuildBufferPool : IDisposable
{
    private readonly Dictionary<int, Stack<JobMeshBuildBuffers>> basicBuffers = new();
    private readonly Dictionary<int, Stack<JobMeshBuildBuffers>> greedyBuffers = new();
    private bool disposed;

    public JobMeshBuildBuffers Rent(ChunkNeighborhood neighborhood, bool includeGreedyScratchBuffers)
    {
        // Schedule 시점에 필요한 크기의 버퍼를 빌립니다.
        // 같은 size 버퍼가 풀에 남아 있으면 재사용하고, 없으면 새 Persistent 버퍼를 만듭니다.
        // 빌린 직후 현재 neighborhood 데이터로 내용을 다시 채우고 결과 리스트를 비웁니다.
        int size = neighborhood.Size;
        Stack<JobMeshBuildBuffers> buffersBySize = GetBuffersBySize(size, includeGreedyScratchBuffers);
        JobMeshBuildBuffers buffers = buffersBySize.Count > 0
            ? buffersBySize.Pop()
            : JobMeshBuildBuffers.Create(size, includeGreedyScratchBuffers, Allocator.Persistent);
        buffers.Populate(neighborhood);
        return buffers;
    }

    public void Release(JobMeshBuildBuffers buffers, bool includeGreedyScratchBuffers)
    {
        // JobSystemMeshBuildHandle.Complete()가 끝난 뒤 호출됩니다.
        // 즉 "한 번의 메시 빌드에서 버퍼 사용이 완전히 끝난 시점"에 풀로 되돌립니다.
        // 풀이 이미 Dispose된 뒤라면 재사용하지 않고 즉시 실제 Native 메모리를 해제합니다.
        if (disposed)
        {
            buffers.Dispose();
            return;
        }

        GetBuffersBySize(buffers.Size, includeGreedyScratchBuffers).Push(buffers);
    }

    public void Dispose()
    {
        // ChunkManager가 meshBuilder를 교체하거나 파괴할 때 호출됩니다.
        // 이 시점에는 풀 안에 대기 중인 모든 Persistent 버퍼를 실제로 Dispose합니다.
        if (disposed)
        {
            return;
        }

        disposed = true;
        DisposeStacks(basicBuffers);
        DisposeStacks(greedyBuffers);
        basicBuffers.Clear();
        greedyBuffers.Clear();
    }

    private Stack<JobMeshBuildBuffers> GetBuffersBySize(int size, bool includeGreedyScratchBuffers)
    {
        // Naive와 Greedy는 필요한 스크래치 버퍼 구성이 다르므로 풀을 따로 유지합니다.
        // 같은 size라도 greedy scratch 포함 여부가 다르면 섞지 않습니다.
        Dictionary<int, Stack<JobMeshBuildBuffers>> map = includeGreedyScratchBuffers ? greedyBuffers : basicBuffers;
        if (!map.TryGetValue(size, out Stack<JobMeshBuildBuffers> buffers))
        {
            buffers = new Stack<JobMeshBuildBuffers>();
            map.Add(size, buffers);
        }

        return buffers;
    }

    private void DisposeStacks(Dictionary<int, Stack<JobMeshBuildBuffers>> buffersBySize)
    {
        // 풀 안에 보관 중인 버퍼를 끝까지 꺼내며 Native 메모리를 정리합니다.
        foreach (Stack<JobMeshBuildBuffers> buffers in buffersBySize.Values)
        {
            while (buffers.Count > 0)
            {
                JobMeshBuildBuffers buffer = buffers.Pop();
                buffer.Dispose();
            }
        }
    }
}
