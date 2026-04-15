using System;

public readonly struct WorldPos : IEquatable<WorldPos>
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

    public static WorldPos FromFloatsFloor(float x, float y, float z)
    {
        return new WorldPos(
            (int)Math.Floor(x),
            (int)Math.Floor(y),
            (int)Math.Floor(z));
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

    public bool Equals(WorldPos other)
    {
        return X == other.X && Y == other.Y && Z == other.Z;
    }

    public override bool Equals(object obj)
    {
        return obj is WorldPos other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y, Z);
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

public readonly struct ChunkPos : IEquatable<ChunkPos>
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

    public WorldPos ToWorldOrigin(int chunkSize)
    {
        return new WorldPos(X * chunkSize, Y * chunkSize, Z * chunkSize);
    }

    public bool Equals(ChunkPos other)
    {
        return X == other.X && Y == other.Y && Z == other.Z;
    }

    public override bool Equals(object obj)
    {
        return obj is ChunkPos other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y, Z);
    }
}

public readonly struct LocalPos : IEquatable<LocalPos>
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

    public static LocalPos FromFloatsFloor(float x, float y, float z)
    {
        return new LocalPos(
            (int)Math.Floor(x),
            (int)Math.Floor(y),
            (int)Math.Floor(z));
    }

    public bool Equals(LocalPos other)
    {
        return X == other.X && Y == other.Y && Z == other.Z;
    }

    public override bool Equals(object obj)
    {
        return obj is LocalPos other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y, Z);
    }
}
