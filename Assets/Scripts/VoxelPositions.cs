using System;
using UnityEngine;

public readonly struct WorldPos
{
    public readonly int X;
    public readonly int Y;
    public readonly int Z;

    public WorldPos(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static WorldPos FromVector3Floor(Vector3 value)
    {
        return new WorldPos(
            Mathf.FloorToInt(value.x),
            Mathf.FloorToInt(value.y),
            Mathf.FloorToInt(value.z));
    }

    public ChunkPos ToChunkPos(int chunkSize)
    {
        ThrowIfInvalidChunkSize(chunkSize);

        return new ChunkPos(
            FloorDiv(X, chunkSize),
            FloorDiv(Y, chunkSize),
            FloorDiv(Z, chunkSize));
    }

    public LocalPos ToLocalPos(int chunkSize)
    {
        ThrowIfInvalidChunkSize(chunkSize);

        return new LocalPos(
            FloorMod(X, chunkSize),
            FloorMod(Y, chunkSize),
            FloorMod(Z, chunkSize));
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        return remainder != 0 && ((remainder < 0) != (divisor < 0))
            ? quotient - 1
            : quotient;
    }

    private static int FloorMod(int value, int divisor)
    {
        int result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    private static void ThrowIfInvalidChunkSize(int chunkSize)
    {
        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be greater than zero.");
        }
    }
}

public readonly struct ChunkPos
{
    public readonly int X;
    public readonly int Y;
    public readonly int Z;

    public ChunkPos(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}

public readonly struct LocalPos
{
    public readonly int X;
    public readonly int Y;
    public readonly int Z;

    public LocalPos(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static LocalPos FromVector3Floor(Vector3 value)
    {
        return new LocalPos(
            Mathf.FloorToInt(value.x),
            Mathf.FloorToInt(value.y),
            Mathf.FloorToInt(value.z));
    }
}
