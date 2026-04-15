public interface IChunkDataStore
{
    int Size { get; }

    // 청크 밖 좌표는 Air로 취급합니다.
    // 메시 빌더가 경계 면을 만들 수 있게 하는 중요한 계약입니다.
    byte GetVoxel(int x, int y, int z);

    // 청크 밖 좌표에 대한 쓰기는 무시합니다.
    void SetVoxel(int x, int y, int z, byte value);

    bool IsInsideChunk(int x, int y, int z);
}
