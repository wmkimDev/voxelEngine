using Unity.Collections;

// Job 메셔가 공통으로 쓰는 입력/출력 Native 버퍼 묶음입니다.
// 중심/이웃 청크 voxel 버퍼와 메시 결과 버퍼를 한 곳에 모아서
// JobNaive와 JobGreedy가 같은 준비/정리 흐름을 재사용하게 합니다.
public struct JobMeshBuildBuffers
{
    public int Size;
    public NativeChunkNeighborhood Neighborhood;
    public NativeList<Vec3> Vertices;
    public NativeList<int> Triangles;
    public NativeList<Vec3> Normals;
    public NativeList<Vec2> Uvs;
    public NativeArray<byte> MaskVisible;
    public NativeArray<byte> MaskVoxelTypes;
    public NativeArray<byte> Consumed;

    public static JobMeshBuildBuffers Create(
        int size,
        bool includeGreedyScratchBuffers,
        Allocator allocator)
    {
        int voxelCount = size * size * size;

        var buffers = new JobMeshBuildBuffers
        {
            Size = size,
            Neighborhood = new NativeChunkNeighborhood
            {
                Size = size,
                Center = new NativeArray<byte>(voxelCount, allocator, NativeArrayOptions.UninitializedMemory),
                PositiveX = new NativeArray<byte>(voxelCount, allocator, NativeArrayOptions.UninitializedMemory),
                NegativeX = new NativeArray<byte>(voxelCount, allocator, NativeArrayOptions.UninitializedMemory),
                PositiveY = new NativeArray<byte>(voxelCount, allocator, NativeArrayOptions.UninitializedMemory),
                NegativeY = new NativeArray<byte>(voxelCount, allocator, NativeArrayOptions.UninitializedMemory),
                PositiveZ = new NativeArray<byte>(voxelCount, allocator, NativeArrayOptions.UninitializedMemory),
                NegativeZ = new NativeArray<byte>(voxelCount, allocator, NativeArrayOptions.UninitializedMemory),
            },
            Vertices = new NativeList<Vec3>(allocator),
            Triangles = new NativeList<int>(allocator),
            Normals = new NativeList<Vec3>(allocator),
            Uvs = new NativeList<Vec2>(allocator),
        };

        if (includeGreedyScratchBuffers)
        {
            int maskCellCount = size * size;
            buffers.MaskVisible = new NativeArray<byte>(
                maskCellCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);
            buffers.MaskVoxelTypes = new NativeArray<byte>(
                maskCellCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);
            buffers.Consumed = new NativeArray<byte>(
                maskCellCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);
        }

        return buffers;
    }

    public void Populate(ChunkNeighborhood neighborhood)
    {
        Neighborhood.Size = neighborhood.Size;
        FillChunkBuffer(neighborhood.Center, Neighborhood.Center);
        FillChunkBuffer(neighborhood.PositiveX, Neighborhood.PositiveX);
        FillChunkBuffer(neighborhood.NegativeX, Neighborhood.NegativeX);
        FillChunkBuffer(neighborhood.PositiveY, Neighborhood.PositiveY);
        FillChunkBuffer(neighborhood.NegativeY, Neighborhood.NegativeY);
        FillChunkBuffer(neighborhood.PositiveZ, Neighborhood.PositiveZ);
        FillChunkBuffer(neighborhood.NegativeZ, Neighborhood.NegativeZ);

        Vertices.Clear();
        Triangles.Clear();
        Normals.Clear();
        Uvs.Clear();
    }

    public NativeQuadWriter CreateWriter()
    {
        return new NativeQuadWriter
        {
            Vertices = Vertices,
            Triangles = Triangles,
            Normals = Normals,
            Uvs = Uvs,
        };
    }

    public void Dispose()
    {
        if (Neighborhood.Center.IsCreated) Neighborhood.Center.Dispose();
        if (Neighborhood.PositiveX.IsCreated) Neighborhood.PositiveX.Dispose();
        if (Neighborhood.NegativeX.IsCreated) Neighborhood.NegativeX.Dispose();
        if (Neighborhood.PositiveY.IsCreated) Neighborhood.PositiveY.Dispose();
        if (Neighborhood.NegativeY.IsCreated) Neighborhood.NegativeY.Dispose();
        if (Neighborhood.PositiveZ.IsCreated) Neighborhood.PositiveZ.Dispose();
        if (Neighborhood.NegativeZ.IsCreated) Neighborhood.NegativeZ.Dispose();
        if (Vertices.IsCreated) Vertices.Dispose();
        if (Triangles.IsCreated) Triangles.Dispose();
        if (Normals.IsCreated) Normals.Dispose();
        if (Uvs.IsCreated) Uvs.Dispose();
        if (MaskVisible.IsCreated) MaskVisible.Dispose();
        if (MaskVoxelTypes.IsCreated) MaskVoxelTypes.Dispose();
        if (Consumed.IsCreated) Consumed.Dispose();
    }

    // Job은 IChunkDataStore 같은 일반 C# 객체를 직접 읽지 않고 NativeArray<byte>만 읽습니다.
    // 그래서 청크 하나를 "Job이 읽을 voxel byte 버퍼"로 복사해 넘깁니다.
    private static void FillChunkBuffer(IChunkDataStore chunkData, NativeArray<byte> buffer)
    {
        if (chunkData == null)
        {
            // 이 방향 청크가 없으면 "전부 Air인 청크"처럼 버퍼를 채웁니다.
            // 그러면 Job은 이 방향을 빈 공간으로 보고 외곽 면을 만들 수 있습니다.
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = VoxelType.Air;
            }

            return;
        }

        if (chunkData is IRawVoxelBufferSource rawVoxelBufferSource)
        {
            byte[] rawVoxels = rawVoxelBufferSource.GetRawVoxelBuffer();
            NativeArray<byte>.Copy(rawVoxels, buffer);

            return;
        }

        int index = 0;
        for (int z = 0; z < chunkData.Size; z++)
        {
            for (int y = 0; y < chunkData.Size; y++)
            {
                for (int x = 0; x < chunkData.Size; x++)
                {
                    buffer[index++] = chunkData.GetVoxel(new LocalPos(x, y, z));
                }
            }
        }
    }
}
