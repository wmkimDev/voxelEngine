using System;

public sealed class ChunkData : IChunkDataStore, IRawVoxelBufferSource
{
    public const int DefaultSize = 16;

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

    public byte GetVoxel(LocalPos pos)
    {
        if (!IsInsideChunk(pos))
        {
            return VoxelType.Air;
        }

        return voxels[ToIndex(pos)];
    }

    public void SetVoxel(LocalPos pos, byte value)
    {
        if (!IsInsideChunk(pos))
        {
            return;
        }

        voxels[ToIndex(pos)] = value;
    }

    public bool IsSolid(LocalPos pos)
    {
        return GetVoxel(pos) != VoxelType.Air;
    }

    public bool IsInsideChunk(LocalPos pos)
    {
        return pos.X >= 0 && pos.Y >= 0 && pos.Z >= 0 && pos.X < Size && pos.Y < Size && pos.Z < Size;
    }

    public int ToIndex(LocalPos pos)
    {
        // 3차원 좌표를 1차원 배열 인덱스로 변환합니다.
        return pos.X + (pos.Y * Size) + (pos.Z * Size * Size);
    }

    public byte[] GetRawVoxelBuffer()
    {
        return voxels;
    }
}
