using System;
using Unity.Collections;
using Unity.Jobs;

public sealed class JobSystemMeshBuilder : IMeshBuilder, IDisposable
{
    private readonly JobMeshBuildBufferPool bufferPool = new();

    public IMeshBuildHandle Schedule(ChunkNeighborhood neighborhood)
    {
        JobMeshBuildBuffers buffers = bufferPool.Rent(neighborhood, includeGreedyScratchBuffers: false);

        var job = new BuildNaiveMeshJob
        {
            Neighborhood = buffers.Neighborhood,
            Writer = buffers.CreateWriter(),
        };

        // 여기서는 "계산을 워커 스레드로 예약"만 합니다.
        // 실제 결과 수집은 handle.Complete()를 호출하는 쪽에서 일어납니다.
        JobHandle handle = job.Schedule();
        return new JobSystemMeshBuildHandle(handle, buffers, bufferPool, hasGreedyScratchBuffers: false);
    }

    public void Dispose()
    {
        bufferPool.Dispose();
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
