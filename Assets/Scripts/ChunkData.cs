using System;

public sealed class ChunkData : IChunkDataStore
{
    public const int DefaultSize = 8;

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
            return VoxelType.Air;
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
        return GetVoxel(x, y, z) != VoxelType.Air;
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
