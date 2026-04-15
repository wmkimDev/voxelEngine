using System.Collections.Generic;

public interface IChunkStreamingPolicy
{
    // centerChunk를 기준으로 "월드에 반드시 존재해야 하는 청크 목록"만 계산합니다.
    // 실제 생성/삭제는 ChunkManager가 담당하므로, 이 인터페이스는 순수 계산에 가깝게 유지합니다.
    IReadOnlyCollection<ChunkPos> GetRequiredChunks(ChunkPos centerChunk);
}
