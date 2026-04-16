using System.Collections.Generic;

// 절차적 생성으로 만든 ChunkData를 메모리에 잠깐 보관하는 런타임 캐시입니다.
// 다시 보는 청크를 생성기부터 다시 만들지 않게 해서, 회전/왕복 이동 시 재생성 비용과 팝인을 줄입니다.
public sealed class ChunkDataCache
{
    private readonly Dictionary<ChunkPos, LinkedListNode<CacheEntry>> entries = new();
    private readonly LinkedList<CacheEntry> lru = new();
    private int capacity;

    private readonly struct CacheEntry
    {
        public CacheEntry(ChunkPos chunkPos, ChunkData chunkData)
        {
            ChunkPos = chunkPos;
            ChunkData = chunkData;
        }

        public ChunkPos ChunkPos { get; }
        public ChunkData ChunkData { get; }
    }

    public ChunkDataCache(int capacity)
    {
        SetCapacity(capacity);
    }

    // 인스펙터 설정이 바뀌면 캐시 용량도 즉시 따라가게 합니다.
    // 새 용량보다 많이 들고 있던 청크는 오래 안 쓴 것부터 버립니다.
    public void SetCapacity(int newCapacity)
    {
        capacity = newCapacity < 0 ? 0 : newCapacity;
        TrimToCapacity();
    }

    // 다시 필요한 청크를 캐시에서 "꺼내"면서 캐시에서는 제거합니다.
    // 같은 청크를 런타임 월드와 캐시에 동시에 들고 있지 않게 하려는 의도입니다.
    public bool TryTake(ChunkPos chunkPos, out ChunkData chunkData)
    {
        if (!entries.TryGetValue(chunkPos, out LinkedListNode<CacheEntry> node))
        {
            chunkData = null;
            return false;
        }

        entries.Remove(chunkPos);
        lru.Remove(node);
        chunkData = node.Value.ChunkData;
        return true;
    }

    // 언로드된 ChunkData를 캐시에 넣습니다.
    // 이미 같은 좌표가 있으면 최신 데이터로 덮고, 가장 최근에 본 항목으로 갱신합니다.
    public void Store(ChunkPos chunkPos, ChunkData chunkData)
    {
        if (capacity == 0 || chunkData == null)
        {
            return;
        }

        if (entries.TryGetValue(chunkPos, out LinkedListNode<CacheEntry> existingNode))
        {
            lru.Remove(existingNode);
            entries.Remove(chunkPos);
        }

        var node = new LinkedListNode<CacheEntry>(new CacheEntry(chunkPos, chunkData));
        lru.AddFirst(node);
        entries[chunkPos] = node;
        TrimToCapacity();
    }

    public void Clear()
    {
        entries.Clear();
        lru.Clear();
    }

    private void TrimToCapacity()
    {
        while (entries.Count > capacity && lru.Last != null)
        {
            // 끝쪽이 가장 오래 안 쓴 청크이므로 LRU 방식으로 먼저 제거합니다.
            LinkedListNode<CacheEntry> nodeToRemove = lru.Last;
            lru.RemoveLast();
            entries.Remove(nodeToRemove.Value.ChunkPos);
        }
    }
}
