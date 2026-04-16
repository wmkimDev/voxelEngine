using Unity.Collections;

// Job 안에서 중심 청크와 6방향 이웃 청크를 "하나의 voxel 읽기 창구"처럼 다루는 도우미입니다.
// 메셔는 "이 좌표의 voxel이 무엇인지"만 물어보고, 실제로 중심/이웃/청크 밖 Air 판단은 이 struct가 맡습니다.
public struct NativeChunkNeighborhood
{
    public int Size;

    [ReadOnly] public NativeArray<byte> Center;
    [ReadOnly] public NativeArray<byte> PositiveX;
    [ReadOnly] public NativeArray<byte> NegativeX;
    [ReadOnly] public NativeArray<byte> PositiveY;
    [ReadOnly] public NativeArray<byte> NegativeY;
    [ReadOnly] public NativeArray<byte> PositiveZ;
    [ReadOnly] public NativeArray<byte> NegativeZ;

    // Job 쪽에서는 ChunkNeighborhood 대신 이 struct가 "이 좌표의 voxel이 무엇인지"를 판단합니다.
    // 중심 청크/이웃 청크/청크 밖 공기 처리 규칙을 한곳에 모아둔 Job 전용 읽기 도우미입니다.
    public byte GetVoxel(LocalPos pos)
    {
        if (IsInside(pos))
        {
            return GetVoxel(Center, pos);
        }

        if (pos.X < 0 && IsInRange(pos.Y) && IsInRange(pos.Z))
        {
            return GetVoxel(NegativeX, new LocalPos(Size - 1, pos.Y, pos.Z));
        }

        if (pos.X >= Size && IsInRange(pos.Y) && IsInRange(pos.Z))
        {
            return GetVoxel(PositiveX, new LocalPos(0, pos.Y, pos.Z));
        }

        if (pos.Y < 0 && IsInRange(pos.X) && IsInRange(pos.Z))
        {
            return GetVoxel(NegativeY, new LocalPos(pos.X, Size - 1, pos.Z));
        }

        if (pos.Y >= Size && IsInRange(pos.X) && IsInRange(pos.Z))
        {
            return GetVoxel(PositiveY, new LocalPos(pos.X, 0, pos.Z));
        }

        if (pos.Z < 0 && IsInRange(pos.X) && IsInRange(pos.Y))
        {
            return GetVoxel(NegativeZ, new LocalPos(pos.X, pos.Y, Size - 1));
        }

        if (pos.Z >= Size && IsInRange(pos.X) && IsInRange(pos.Y))
        {
            return GetVoxel(PositiveZ, new LocalPos(pos.X, pos.Y, 0));
        }

        return VoxelType.Air;
    }

    private byte GetVoxel(NativeArray<byte> voxels, LocalPos pos)
    {
        int index = pos.X + (pos.Y * Size) + (pos.Z * Size * Size);
        return voxels[index];
    }

    private bool IsInside(LocalPos pos)
    {
        return IsInRange(pos.X) && IsInRange(pos.Y) && IsInRange(pos.Z);
    }

    private bool IsInRange(int value)
    {
        return value >= 0 && value < Size;
    }
}
