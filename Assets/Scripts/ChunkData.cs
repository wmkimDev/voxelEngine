using System;

public sealed class ChunkData
{
    public const int DefaultSize = 8;

    // 지금은 byte 값 전체를 voxel 타입 ID로 씁니다.
    // 예: 0 = Air, 1 = Dirt, 2 = Grass, 3 = Stone, 4 = Sand
    public const byte Air = 0;
    public const byte Dirt = 1;
    public const byte Grass = 2;
    public const byte Stone = 3;
    public const byte Sand = 4;

    private readonly byte[] voxels;

    public ChunkData(int size = DefaultSize)
    {
        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Chunk size must be greater than zero.");
        }

        Size = size;
        voxels = new byte[size * size * size];
    }

    public int Size { get; }

    public byte GetVoxel(int x, int y, int z)
    {
        if (!IsInsideChunk(x, y, z))
        {
            return Air;
        }

        return voxels[ToIndex(x, y, z)];
    }

    public void SetVoxel(int x, int y, int z, byte value)
    {
        if (!IsInsideChunk(x, y, z))
        {
            return;
        }

        voxels[ToIndex(x, y, z)] = value;
    }

    public bool IsSolid(int x, int y, int z)
    {
        return GetVoxel(x, y, z) != Air;
    }

    public bool IsInsideChunk(int x, int y, int z)
    {
        return x >= 0 && y >= 0 && z >= 0 && x < Size && y < Size && z < Size;
    }

    public int ToIndex(int x, int y, int z)
    {
        // 3차원 좌표를 1차원 배열 인덱스로 변환합니다.
        return x + (y * Size) + (z * Size * Size);
    }
}
