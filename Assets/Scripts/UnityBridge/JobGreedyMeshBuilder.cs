using System;
using Unity.Collections;
using Unity.Jobs;

public sealed class JobGreedyMeshBuilder : IMeshBuilder, IDisposable
{
    private readonly JobMeshBuildBufferPool bufferPool = new();

    public IMeshBuildHandle Schedule(ChunkNeighborhood neighborhood)
    {
        JobMeshBuildBuffers buffers = bufferPool.Rent(neighborhood, includeGreedyScratchBuffers: true);

        var job = new BuildGreedyMeshJob
        {
            Neighborhood = buffers.Neighborhood,
            Writer = buffers.CreateWriter(),
            MaskVisible = buffers.MaskVisible,
            MaskVoxelTypes = buffers.MaskVoxelTypes,
            Consumed = buffers.Consumed,
        };

        JobHandle handle = job.Schedule();
        return new JobSystemMeshBuildHandle(handle, buffers, bufferPool, hasGreedyScratchBuffers: true);
    }

    public void Dispose()
    {
        bufferPool.Dispose();
    }

    private struct BuildGreedyMeshJob : IJob
    {
        private readonly struct MergeRect
        {
            // 현재 layer의 2D mask에서 하나의 큰 쿼드로 합쳐진 직사각형 영역입니다.
            public readonly int StartU;
            public readonly int StartV;
            public readonly int Width;
            public readonly int Height;

            public MergeRect(int startU, int startV, int width, int height)
            {
                StartU = startU;
                StartV = startV;
                Width = width;
                Height = height;
            }
        }

        public NativeChunkNeighborhood Neighborhood;
        public NativeQuadWriter Writer;
        public NativeArray<byte> MaskVisible;
        public NativeArray<byte> MaskVoxelTypes;
        public NativeArray<byte> Consumed;

        public void Execute()
        {
            int size = Neighborhood.Size;

            // 6방향 face를 각각 따로 처리합니다.
            // 한 방향의 한 layer를 2D mask로 만들고, 그 mask 위에서 큰 직사각형으로 병합합니다.
            for (int faceIndex = 0; faceIndex < 6; faceIndex++)
            {
                FaceDirection direction = (FaceDirection)faceIndex;
                GreedyGeometry.Axis axis = GreedyGeometry.GetAxis(direction);
                bool positive = GreedyGeometry.IsPositive(direction);

                for (int layer = 0; layer < size; layer++)
                {
                    FillMask(axis, positive, layer);
                    ClearConsumed();

                    for (int v = 0; v < size; v++)
                    {
                        for (int u = 0; u < size; u++)
                        {
                            int maskIndex = GetMaskIndex(u, v);
                            if (Consumed[maskIndex] != 0 || MaskVisible[maskIndex] == 0)
                            {
                                continue;
                            }

                            byte voxelType = MaskVoxelTypes[maskIndex];
                            MergeRect rect = FindMergeRect(u, v, voxelType);
                            MarkConsumed(rect);
                            AddMergedFace(direction, axis, positive, layer, rect, voxelType);
                        }
                    }
                }
            }
        }

        private void FillMask(GreedyGeometry.Axis axis, bool positive, int layer)
        {
            int size = Neighborhood.Size;

            // 현재 layer를 2D 판으로 보고 "보이는 면이 있는가"와 "voxel 타입이 무엇인가"를 기록합니다.
            for (int v = 0; v < size; v++)
            {
                for (int u = 0; u < size; u++)
                {
                    LocalPos voxel = GreedyGeometry.ToLocalPos(axis, layer, u, v);
                    byte voxelType = Neighborhood.GetVoxel(voxel);
                    int maskIndex = GetMaskIndex(u, v);

                    if (voxelType == VoxelType.Air)
                    {
                        MaskVisible[maskIndex] = 0;
                        MaskVoxelTypes[maskIndex] = VoxelType.Air;
                        continue;
                    }

                    int neighborOffset = positive ? 1 : -1;
                    LocalPos neighbor = GreedyGeometry.Offset(voxel, axis, neighborOffset);
                    bool isVisible = Neighborhood.GetVoxel(neighbor) == VoxelType.Air;

                    MaskVisible[maskIndex] = isVisible ? (byte)1 : (byte)0;
                    MaskVoxelTypes[maskIndex] = voxelType;
                }
            }
        }

        private MergeRect FindMergeRect(int startU, int startV, byte voxelType)
        {
            // 시작 칸에서 먼저 가로 폭을 찾고, 그 폭이 유지되는 행만 아래로 확장합니다.
            int width = GetRunWidth(startU, startV, voxelType);
            int height = GetRunHeight(startU, startV, width, voxelType);
            return new MergeRect(startU, startV, width, height);
        }

        private int GetRunWidth(int startU, int v, byte voxelType)
        {
            int size = Neighborhood.Size;
            int width = 0;

            // 같은 행에서 같은 타입의 보이는 면이 몇 칸 연속되는지 찾습니다.
            for (int u = startU; u < size; u++)
            {
                int maskIndex = GetMaskIndex(u, v);
                if (Consumed[maskIndex] != 0 ||
                    MaskVisible[maskIndex] == 0 ||
                    MaskVoxelTypes[maskIndex] != voxelType)
                {
                    break;
                }

                width++;
            }

            return width;
        }

        private int GetRunHeight(int startU, int startV, int width, byte voxelType)
        {
            int size = Neighborhood.Size;
            int height = 0;

            // 위에서 찾은 width 전체가 유지되는 행만 height에 포함합니다.
            // 한 칸이라도 타입이 다르거나 이미 합쳐진 칸이면 직사각형 확장을 멈춥니다.
            for (int v = startV; v < size; v++)
            {
                for (int u = startU; u < startU + width; u++)
                {
                    int maskIndex = GetMaskIndex(u, v);
                    if (Consumed[maskIndex] != 0 ||
                        MaskVisible[maskIndex] == 0 ||
                        MaskVoxelTypes[maskIndex] != voxelType)
                    {
                        return height;
                    }
                }

                height++;
            }

            return height;
        }

        private void AddMergedFace(
            FaceDirection direction,
            GreedyGeometry.Axis axis,
            bool positive,
            int layer,
            MergeRect rect,
            byte voxelType)
        {
            // 병합된 2D 직사각형을 실제 3D 쿼드의 네 꼭짓점으로 바꿔 writer에 넘깁니다.
            Vec3 origin = GreedyGeometry.GetFaceOrigin(axis, positive, layer, rect.StartU, rect.StartV);
            Vec3 uVector = GreedyGeometry.GetUVector(axis, rect.Width);
            Vec3 vVector = GreedyGeometry.GetVVector(axis, rect.Height);

            Writer.WriteMerged(
                direction,
                voxelType,
                origin,
                origin + uVector,
                origin + vVector,
                origin + uVector + vVector);
        }

        private void ClearConsumed()
        {
            // 새 layer를 처리하기 전에 "이미 큰 쿼드로 합친 칸" 표시를 모두 비웁니다.
            for (int i = 0; i < Consumed.Length; i++)
            {
                Consumed[i] = 0;
            }
        }

        private void MarkConsumed(MergeRect rect)
        {
            // 이번에 하나의 큰 쿼드로 처리한 직사각형 영역을 다시 시작점으로 쓰지 않도록 표시합니다.
            for (int v = rect.StartV; v < rect.StartV + rect.Height; v++)
            {
                for (int u = rect.StartU; u < rect.StartU + rect.Width; u++)
                {
                    Consumed[GetMaskIndex(u, v)] = 1;
                }
            }
        }

        private int GetMaskIndex(int u, int v)
        {
            // 2D mask 좌표를 1차원 NativeArray 인덱스로 바꿉니다.
            return u + (v * Neighborhood.Size);
        }

    }
}
