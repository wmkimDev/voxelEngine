using Unity.Collections;
using System.Collections.Generic;

// Job 메셔가 공통으로 쓰는 입력/출력 Native 버퍼 묶음입니다.
// 중심/이웃 청크 voxel 버퍼와 메시 결과 버퍼를 한 곳에 모아서
// JobNaive와 JobGreedy가 같은 준비/정리 흐름을 재사용하게 합니다.
public struct JobMeshBuildBuffers
{
    // 이웃 청크가 없을 때 재사용하는 "전부 Air" voxel 버퍼 캐시입니다.
    private static readonly Dictionary<int, byte[]> EmptyVoxelBuffersByLength = new();

    // 현재 버퍼가 대응하는 청크 한 변 길이입니다.
    public int Size;

    // 중심 청크와 6방향 이웃 청크의 voxel 데이터를 Job이 읽기 좋은 Native 형태로 담습니다.
    public NativeChunkNeighborhood Neighborhood;

    // 메셔가 최종적으로 채우는 메시 출력 버퍼입니다.
    public NativeList<Vec3> Vertices;
    public NativeList<int> Triangles;
    public NativeList<Vec3> Normals;
    public NativeList<Vec2> Uvs;

    // greedy meshing에서 한 slice를 스캔할 때만 쓰는 임시 마스크 버퍼입니다.
    public NativeArray<byte> MaskVisible;
    public NativeArray<byte> MaskVoxelTypes;
    public NativeArray<byte> Consumed;

    // 청크 크기에 맞는 입력/출력 Native 버퍼 묶음을 새로 만듭니다.
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

    // 이번 빌드에 사용할 중심/이웃 voxel 데이터를 복사하고, 이전 메시 결과 버퍼를 비웁니다.
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

    // NativeList 기반 출력 버퍼를 그대로 쓰는 쿼드 작성기 뷰를 반환합니다.
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

    // 이 묶음이 소유한 모든 Native 메모리를 정리합니다.
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
            // 길이별 0-filled 버퍼를 재사용해 요소 단위 루프 없이 bulk copy 합니다.
            NativeArray<byte>.Copy(GetEmptyVoxelBuffer(buffer.Length), buffer);
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

    // 길이별 빈 voxel 버퍼를 재사용해 "전부 Air" 복사를 빠르게 처리합니다.
    private static byte[] GetEmptyVoxelBuffer(int length)
    {
        if (!EmptyVoxelBuffersByLength.TryGetValue(length, out byte[] emptyBuffer))
        {
            emptyBuffer = new byte[length];
            EmptyVoxelBuffersByLength.Add(length, emptyBuffer);
        }

        return emptyBuffer;
    }
}
