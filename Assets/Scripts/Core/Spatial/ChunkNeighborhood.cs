using System;

public readonly struct ChunkNeighborhood
{
    // Center는 지금 메시를 만들 대상 청크입니다.
    // 나머지 6개는 Center의 각 면에 맞닿은 청크이며, 없으면 null일 수 있습니다.
    public readonly IChunkDataStore Center;
    public readonly IChunkDataStore PositiveX;
    public readonly IChunkDataStore NegativeX;
    public readonly IChunkDataStore PositiveY;
    public readonly IChunkDataStore NegativeY;
    public readonly IChunkDataStore PositiveZ;
    public readonly IChunkDataStore NegativeZ;

    public ChunkNeighborhood(
        IChunkDataStore center,
        IChunkDataStore positiveX,
        IChunkDataStore negativeX,
        IChunkDataStore positiveY,
        IChunkDataStore negativeY,
        IChunkDataStore positiveZ,
        IChunkDataStore negativeZ)
    {
        Center = center ?? throw new ArgumentNullException(nameof(center));
        PositiveX = positiveX;
        NegativeX = negativeX;
        PositiveY = positiveY;
        NegativeY = negativeY;
        PositiveZ = positiveZ;
        NegativeZ = negativeZ;
    }

    public int Size => Center.Size;

    public byte GetVoxel(LocalPos pos)
    {
        // 먼저 대상 청크 내부 좌표인지 확인합니다.
        // 대부분의 voxel 검사는 청크 내부에서 끝나므로 이 경로가 가장 흔합니다.
        if (Center.IsInsideChunk(pos))
        {
            return Center.GetVoxel(pos);
        }

        // 메시 빌더는 한 면씩 검사하므로 보통 한 축만 청크 밖으로 나갑니다.
        // 이웃이 아직 없는 방향은 현재 단계에서는 공기로 취급해 외곽 면을 만듭니다.
        int size = Center.Size;
        if (pos.X < 0 && IsInRange(pos.Y, size) && IsInRange(pos.Z, size))
        {
            // x가 -1이면 왼쪽 청크의 마지막 x 칸(size - 1)을 확인합니다.
            return GetOrAir(NegativeX, new LocalPos(size - 1, pos.Y, pos.Z));
        }

        if (pos.X >= size && IsInRange(pos.Y, size) && IsInRange(pos.Z, size))
        {
            // x가 size이면 오른쪽 청크의 첫 x 칸(0)을 확인합니다.
            return GetOrAir(PositiveX, new LocalPos(0, pos.Y, pos.Z));
        }

        if (pos.Y < 0 && IsInRange(pos.X, size) && IsInRange(pos.Z, size))
        {
            // y가 -1이면 아래 청크의 맨 위 y 칸(size - 1)을 확인합니다.
            return GetOrAir(NegativeY, new LocalPos(pos.X, size - 1, pos.Z));
        }

        if (pos.Y >= size && IsInRange(pos.X, size) && IsInRange(pos.Z, size))
        {
            // y가 size이면 위 청크의 첫 y 칸(0)을 확인합니다.
            return GetOrAir(PositiveY, new LocalPos(pos.X, 0, pos.Z));
        }

        if (pos.Z < 0 && IsInRange(pos.X, size) && IsInRange(pos.Y, size))
        {
            // z가 -1이면 뒤쪽 청크의 마지막 z 칸(size - 1)을 확인합니다.
            return GetOrAir(NegativeZ, new LocalPos(pos.X, pos.Y, size - 1));
        }

        if (pos.Z >= size && IsInRange(pos.X, size) && IsInRange(pos.Y, size))
        {
            // z가 size이면 앞쪽 청크의 첫 z 칸(0)을 확인합니다.
            return GetOrAir(PositiveZ, new LocalPos(pos.X, pos.Y, 0));
        }

        // 두 축 이상이 동시에 청크 밖으로 나간 대각선 좌표는 현재 나이브 메시 빌더에서 필요하지 않습니다.
        // 필요해지기 전까지는 공기로 취급해 계약을 단순하게 유지합니다.
        return VoxelType.Air;
    }

    private static byte GetOrAir(IChunkDataStore chunkData, LocalPos pos)
    {
        // null 이웃은 "아직 로드되지 않은 청크" 또는 "월드 바깥"입니다.
        // 이번 단계에서는 빌드를 보류하지 않고 공기로 보아 외곽 면을 생성합니다.
        return chunkData == null ? VoxelType.Air : chunkData.GetVoxel(pos);
    }

    private static bool IsInRange(int value, int size)
    {
        return value >= 0 && value < size;
    }
}
