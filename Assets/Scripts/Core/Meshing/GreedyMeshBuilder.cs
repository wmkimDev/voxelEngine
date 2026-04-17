public sealed class GreedyMeshBuilder : IMeshBuilder
{
    private readonly struct FaceCell
    {
        // mask 한 칸이 "이 위치에 그릴 면이 있는가"와 "어떤 voxel 타입의 면인가"를 기억합니다.
        // 같은 타입의 보이는 면만 하나의 큰 쿼드로 합칠 수 있습니다.
        public readonly bool IsVisible;
        public readonly byte VoxelType;

        public FaceCell(bool isVisible, byte voxelType)
        {
            IsVisible = isVisible;
            VoxelType = voxelType;
        }
    }

    private readonly struct FaceOrientation
    {
        // Greedy는 "어느 축의 어느 방향 면을 처리 중인가"를 반복해서 넘겨야 합니다.
        // axis와 positive를 따로 들고 다니면 호출부가 길어지므로 하나로 묶어둡니다.
        public readonly GreedyGeometry.Axis Axis;
        public readonly bool Positive;
        public readonly FaceDirection Direction;

        public FaceOrientation(GreedyGeometry.Axis axis, bool positive, FaceDirection direction)
        {
            Axis = axis;
            Positive = positive;
            Direction = direction;
        }
    }

    private readonly struct MergeRect
    {
        // mask 위에서 하나의 큰 쿼드로 합쳐진 직사각형 영역입니다.
        // StartU/StartV는 시작 칸, Width/Height는 병합된 크기입니다.
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

    private static readonly FaceOrientation[] FaceOrientations =
    {
        // +X, -X, +Y, -Y, +Z, -Z 순서로 한 번씩 처리합니다.
        // Direction은 FaceTopology의 공용 winding/normal 규칙과 직접 연결됩니다.
        new FaceOrientation(GreedyGeometry.Axis.X, positive: true, FaceDirection.PositiveX),
        new FaceOrientation(GreedyGeometry.Axis.X, positive: false, FaceDirection.NegativeX),
        new FaceOrientation(GreedyGeometry.Axis.Y, positive: true, FaceDirection.PositiveY),
        new FaceOrientation(GreedyGeometry.Axis.Y, positive: false, FaceDirection.NegativeY),
        new FaceOrientation(GreedyGeometry.Axis.Z, positive: true, FaceDirection.PositiveZ),
        new FaceOrientation(GreedyGeometry.Axis.Z, positive: false, FaceDirection.NegativeZ),
    };

    public IMeshBuildHandle Schedule(ChunkNeighborhood neighborhood)
    {
        return new CompletedMeshBuildHandle(BuildNow(neighborhood));
    }

    private static ChunkMeshData BuildNow(ChunkNeighborhood neighborhood)
    {
        var meshData = new ChunkMeshData();

        // Greedy meshing은 6방향 면을 각각 따로 처리합니다.
        foreach (FaceOrientation face in FaceOrientations)
        {
            BuildFaceOrientation(neighborhood, meshData, face);
        }

        return meshData;
    }

    private static void BuildFaceOrientation(
        ChunkNeighborhood neighborhood,
        ChunkMeshData meshData,
        FaceOrientation face)
    {
        int size = neighborhood.Size;

        // mask는 현재 layer에서 "그려야 하는 면"만 표시하는 2D 판입니다.
        // consumed는 이미 큰 쿼드에 포함된 mask 칸을 다시 쓰지 않기 위한 표시입니다.
        var mask = new FaceCell[size, size];
        var consumed = new bool[size, size];

        for (int layer = 0; layer < size; layer++)
        {
            // 한 방향의 한 슬라이스를 2D 판으로 보고, 같은 voxel 타입의 노출된 면을 직사각형으로 합칩니다.
            FillMask(neighborhood, face, layer, mask);
            ClearConsumed(consumed);

            for (int v = 0; v < size; v++)
            {
                for (int u = 0; u < size; u++)
                {
                    if (!TryGetVisibleCell(mask, consumed, u, v, out FaceCell cell))
                    {
                        continue;
                    }

                    MergeRect rect = FindMergeRect(mask, consumed, u, v, cell.VoxelType);
                    MarkConsumed(consumed, rect);
                    AddMergedFace(meshData, face, layer, rect, cell.VoxelType);
                }
            }
        }
    }

    private static bool TryGetVisibleCell(
        FaceCell[,] mask,
        bool[,] consumed,
        int u,
        int v,
        out FaceCell cell)
    {
        cell = mask[u, v];
        // 이미 더 큰 쿼드에 포함된 칸이거나, 애초에 그릴 면이 없으면
        // 이 칸은 새로운 직사각형의 시작점이 될 수 없습니다.
        return !consumed[u, v] && cell.IsVisible;
    }

    private static void FillMask(
        ChunkNeighborhood neighborhood,
        FaceOrientation face,
        int layer,
        FaceCell[,] mask)
    {
        int size = neighborhood.Size;

        for (int v = 0; v < size; v++)
        {
            for (int u = 0; u < size; u++)
            {
                // axis/layer/u/v는 "현재 처리 중인 2D 판"의 좌표입니다.
                // ToLocalPos가 이것을 실제 청크 내부 x/y/z 좌표로 바꿉니다.
                LocalPos voxel = GreedyGeometry.ToLocalPos(face.Axis, layer, u, v);
                byte voxelType = neighborhood.GetVoxel(voxel);
                if (voxelType == VoxelType.Air)
                {
                    mask[u, v] = new FaceCell(false, VoxelType.Air);
                    continue;
                }

                // 현재 voxel 옆이 공기일 때만 해당 방향의 면이 보입니다.
                // 이웃 청크가 있으면 ChunkNeighborhood가 경계 너머 voxel까지 확인합니다.
                int neighborOffset = face.Positive ? 1 : -1;
                LocalPos neighbor = GreedyGeometry.Offset(voxel, face.Axis, neighborOffset);
                bool visible = neighborhood.GetVoxel(neighbor) == VoxelType.Air;
                mask[u, v] = new FaceCell(visible, voxelType);
            }
        }
    }

    private static MergeRect FindMergeRect(
        FaceCell[,] mask,
        bool[,] consumed,
        int startU,
        int startV,
        byte voxelType)
    {
        // 시작 칸에서 먼저 가로 길이를 찾고,
        // 그 가로 폭이 유지되는 행만 아래로 늘려서 최종 직사각형을 만듭니다.
        int width = GetRunWidth(mask, consumed, startU, startV, voxelType);
        int height = GetRunHeight(mask, consumed, startU, startV, width, voxelType);
        return new MergeRect(startU, startV, width, height);
    }

    private static int GetRunWidth(
        FaceCell[,] mask,
        bool[,] consumed,
        int startU,
        int v,
        byte voxelType)
    {
        int size = mask.GetLength(0);
        int width = 0;

        // 같은 행에서 같은 타입의 보이는 면이 연속되는 길이를 찾습니다.
        for (int u = startU; u < size; u++)
        {
            FaceCell cell = mask[u, v];
            if (consumed[u, v] || !cell.IsVisible || cell.VoxelType != voxelType)
            {
                break;
            }

            width++;
        }

        return width;
    }

    private static int GetRunHeight(
        FaceCell[,] mask,
        bool[,] consumed,
        int startU,
        int startV,
        int width,
        byte voxelType)
    {
        int size = mask.GetLength(1);
        int height = 0;

        // 위에서 찾은 width 전체가 같은 타입으로 유지되는 행만 높이에 포함합니다.
        // 한 칸이라도 타입이 다르거나 이미 소비된 칸이면 직사각형 확장을 멈춥니다.
        for (int v = startV; v < size; v++)
        {
            for (int u = startU; u < startU + width; u++)
            {
                FaceCell cell = mask[u, v];
                if (consumed[u, v] || !cell.IsVisible || cell.VoxelType != voxelType)
                {
                    return height;
                }
            }

            height++;
        }

        return height;
    }

    private static void AddMergedFace(
        ChunkMeshData meshData,
        FaceOrientation face,
        int layer,
        MergeRect rect,
        byte voxelType)
    {
        Vec3 origin = GreedyGeometry.GetFaceOrigin(face.Axis, face.Positive, layer, rect.StartU, rect.StartV);
        Vec3 uVector = GreedyGeometry.GetUVector(face.Axis, rect.Width);
        Vec3 vVector = GreedyGeometry.GetVVector(face.Axis, rect.Height);
        Vec3 normal = FaceTopology.GetNormal(face.Direction);

        Vec3 p0 = origin;
        Vec3 pU = origin + uVector;
        Vec3 pV = origin + vVector;
        Vec3 pUV = origin + uVector + vVector;

        WriteFaceWithCorrectWinding(meshData, face.Direction, voxelType, normal, p0, pU, pV, pUV);
    }

    private static void WriteFaceWithCorrectWinding(
        ChunkMeshData meshData,
        FaceDirection direction,
        byte voxelType,
        Vec3 normal,
        Vec3 p0,
        Vec3 pU,
        Vec3 pV,
        Vec3 pUV)
    {
        // winding 규칙은 FaceTopology의 공용 정의를 사용합니다.
        // 즉 Greedy 전용 규칙이 아니라 엔진 전체가 공유하는 방향 규칙입니다.
        QuadMeshWriter.Write(
            meshData,
            voxelType,
            normal,
            GetCorner(FaceTopology.GetWindingCornerIndex(direction, 0), p0, pU, pV, pUV),
            GetCorner(FaceTopology.GetWindingCornerIndex(direction, 1), p0, pU, pV, pUV),
            GetCorner(FaceTopology.GetWindingCornerIndex(direction, 2), p0, pU, pV, pUV),
            GetCorner(FaceTopology.GetWindingCornerIndex(direction, 3), p0, pU, pV, pUV));
    }

    private static Vec3 GetCorner(int index, Vec3 p0, Vec3 pU, Vec3 pV, Vec3 pUV)
    {
        // QuadCornerOrder는 숫자 인덱스만 들고 있으므로,
        // 실제 정점 좌표로 바꿔주는 변환 단계가 하나 필요합니다.
        return index switch
        {
            0 => p0,
            1 => pU,
            2 => pV,
            _ => pUV
        };
    }

    private static void ClearConsumed(bool[,] consumed)
    {
        // 새 layer를 처리하기 전에 "이미 큰 쿼드로 합쳐진 칸" 표시를 전부 초기화합니다.
        int width = consumed.GetLength(0);
        int height = consumed.GetLength(1);

        for (int v = 0; v < height; v++)
        {
            for (int u = 0; u < width; u++)
            {
                consumed[u, v] = false;
            }
        }
    }

    private static void MarkConsumed(bool[,] consumed, MergeRect rect)
    {
        // startU/startV에서 찾은 width x height 직사각형 영역을
        // 이번 layer에서는 이미 처리한 칸으로 표시합니다.
        // 이렇게 해야 같은 면을 다시 시작점으로 잡지 않습니다.
        for (int v = rect.StartV; v < rect.StartV + rect.Height; v++)
        {
            for (int u = rect.StartU; u < rect.StartU + rect.Width; u++)
            {
                consumed[u, v] = true;
            }
        }
    }
}
