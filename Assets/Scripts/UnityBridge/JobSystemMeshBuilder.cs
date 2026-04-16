using Unity.Collections;
using Unity.Jobs;

public sealed class JobSystemMeshBuilder : IMeshBuilder
{
    public IMeshBuildHandle Schedule(ChunkNeighborhood neighborhood)
    {
        int size = neighborhood.Size;
        int voxelCount = size * size * size;

        // Job은 IChunkDataStore 같은 일반 C# 객체를 직접 다루기 어렵습니다.
        // 그래서 중심 청크와 6방향 이웃 청크를 byte 버퍼로 평탄화해서 넘깁니다.
        var center = CreateChunkBuffer(neighborhood.Center, voxelCount);
        var positiveX = CreateChunkBuffer(neighborhood.PositiveX, voxelCount);
        var negativeX = CreateChunkBuffer(neighborhood.NegativeX, voxelCount);
        var positiveY = CreateChunkBuffer(neighborhood.PositiveY, voxelCount);
        var negativeY = CreateChunkBuffer(neighborhood.NegativeY, voxelCount);
        var positiveZ = CreateChunkBuffer(neighborhood.PositiveZ, voxelCount);
        var negativeZ = CreateChunkBuffer(neighborhood.NegativeZ, voxelCount);

        var vertices = new NativeList<Vec3>(Allocator.TempJob);
        var triangles = new NativeList<int>(Allocator.TempJob);
        var normals = new NativeList<Vec3>(Allocator.TempJob);
        var uvs = new NativeList<Vec2>(Allocator.TempJob);

        var job = new BuildNaiveMeshJob
        {
            Neighborhood = new NativeChunkNeighborhood
            {
                Size = size,
                Center = center,
                PositiveX = positiveX,
                NegativeX = negativeX,
                PositiveY = positiveY,
                NegativeY = negativeY,
                PositiveZ = positiveZ,
                NegativeZ = negativeZ,
            },
            Writer = new NativeQuadWriter
            {
                Vertices = vertices,
                Triangles = triangles,
                Normals = normals,
                Uvs = uvs,
            },
        };

        // 여기서는 "계산을 워커 스레드로 예약"만 합니다.
        // 실제 결과 수집은 handle.Complete()를 호출하는 쪽에서 일어납니다.
        JobHandle handle = job.Schedule();
        return new JobSystemMeshBuildHandle(
            handle,
            center,
            positiveX,
            negativeX,
            positiveY,
            negativeY,
            positiveZ,
            negativeZ,
            vertices,
            triangles,
            normals,
            uvs);
    }

    // Job은 IChunkDataStore를 직접 읽지 않고 NativeArray<byte>만 읽습니다.
    // 그래서 청크 하나를 "Job이 읽을 voxel byte 버퍼"로 복사해 넘깁니다.
    private static NativeArray<byte> CreateChunkBuffer(IChunkDataStore chunkData, int voxelCount)
    {
        var buffer = new NativeArray<byte>(voxelCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        if (chunkData == null)
        {
            // 이 방향 청크가 없으면 "전부 Air인 청크"처럼 버퍼를 채웁니다.
            // 그러면 Job은 이 방향을 빈 공간으로 보고 외곽 면을 만들 수 있습니다.
            for (int i = 0; i < voxelCount; i++)
            {
                buffer[i] = VoxelType.Air;
            }

            return buffer;
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

        return buffer;
    }

    private struct BuildNaiveMeshJob : IJob
    {
        public NativeChunkNeighborhood Neighborhood;
        public NativeQuadWriter Writer;

        public void Execute()
        {
            // 알고리즘 자체는 NaiveMeshBuilder와 같습니다.
            // 차이는 메인 스레드가 아니라 Job 워커 스레드에서 돈다는 점입니다.
            for (int z = 0; z < Neighborhood.Size; z++)
            {
                for (int y = 0; y < Neighborhood.Size; y++)
                {
                    for (int x = 0; x < Neighborhood.Size; x++)
                    {
                        var localPos = new LocalPos(x, y, z);
                        byte voxelType = Neighborhood.GetVoxel(localPos);
                        if (voxelType == VoxelType.Air)
                        {
                            continue;
                        }

                        var voxelLocalPosition = new Vec3(x, y, z);

                        for (int face = 0; face < 6; face++)
                        {
                            FaceDirection direction = (FaceDirection)face;
                            Vec3 normal = FaceTopology.GetNormal(direction);
                            var neighborPos = new LocalPos(
                                x + (int)normal.X,
                                y + (int)normal.Y,
                                z + (int)normal.Z);

                            if (Neighborhood.GetVoxel(neighborPos) != VoxelType.Air)
                            {
                                continue;
                            }

                            Writer.Write(direction, voxelType, voxelLocalPosition);
                        }
                    }
                }
            }
        }
    }
}
