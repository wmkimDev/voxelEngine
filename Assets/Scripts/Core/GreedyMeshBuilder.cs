public sealed class GreedyMeshBuilder : IMeshBuilder
{
    private enum Axis
    {
        X = 0,
        Y = 1,
        Z = 2,
    }

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
        public readonly Axis Axis;
        public readonly bool Positive;
        public readonly int FaceIndex;

        public FaceOrientation(Axis axis, bool positive, int faceIndex)
        {
            Axis = axis;
            Positive = positive;
            FaceIndex = faceIndex;
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
        // faceIndex는 아래 QuadCornerOrder와 같은 순서를 공유합니다.
        new FaceOrientation(Axis.X, positive: true, faceIndex: 0),
        new FaceOrientation(Axis.X, positive: false, faceIndex: 1),
        new FaceOrientation(Axis.Y, positive: true, faceIndex: 2),
        new FaceOrientation(Axis.Y, positive: false, faceIndex: 3),
        new FaceOrientation(Axis.Z, positive: true, faceIndex: 4),
        new FaceOrientation(Axis.Z, positive: false, faceIndex: 5),
    };

    // p0 / pU / pV / pUV 네 꼭짓점을 어떤 순서로 넣어야
    // 각 방향의 법선과 삼각형 winding이 NaiveMeshBuilder와 일치하는지 정의합니다.
    private static readonly int[,] QuadCornerOrder =
    {
        { 0, 1, 3, 2 }, // +X
        { 2, 3, 1, 0 }, // -X
        { 2, 3, 1, 0 }, // +Y
        { 0, 1, 3, 2 }, // -Y
        { 1, 3, 2, 0 }, // +Z
        { 0, 2, 3, 1 }, // -Z
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
                LocalPos voxel = ToLocalPos(face.Axis, layer, u, v);
                byte voxelType = neighborhood.GetVoxel(voxel);
                if (voxelType == VoxelType.Air)
                {
                    mask[u, v] = new FaceCell(false, VoxelType.Air);
                    continue;
                }

                // 현재 voxel 옆이 공기일 때만 해당 방향의 면이 보입니다.
                // 이웃 청크가 있으면 ChunkNeighborhood가 경계 너머 voxel까지 확인합니다.
                int neighborOffset = face.Positive ? 1 : -1;
                LocalPos neighbor = Offset(voxel, face.Axis, neighborOffset);
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
        Vec3 origin = GetFaceOrigin(face, layer, rect.StartU, rect.StartV);
        Vec3 uVector = GetUVector(face.Axis, rect.Width);
        Vec3 vVector = GetVVector(face.Axis, rect.Height);
        Vec3 normal = GetNormal(face.Axis, face.Positive);

        Vec3 p0 = origin;
        Vec3 pU = origin + uVector;
        Vec3 pV = origin + vVector;
        Vec3 pUV = origin + uVector + vVector;

        WriteFaceWithCorrectWinding(meshData, face.FaceIndex, voxelType, normal, p0, pU, pV, pUV);
    }

    private static void WriteFaceWithCorrectWinding(
        ChunkMeshData meshData,
        int faceIndex,
        byte voxelType,
        Vec3 normal,
        Vec3 p0,
        Vec3 pU,
        Vec3 pV,
        Vec3 pUV)
    {
        // faceIndex는 +X, -X, +Y, -Y, +Z, -Z 순서입니다.
        // 이 방향별 정점 순서를 맞춰야 삼각형 winding이 법선 방향과 일치합니다.
        QuadMeshWriter.Write(
            meshData,
            voxelType,
            normal,
            GetCorner(QuadCornerOrder[faceIndex, 0], p0, pU, pV, pUV),
            GetCorner(QuadCornerOrder[faceIndex, 1], p0, pU, pV, pUV),
            GetCorner(QuadCornerOrder[faceIndex, 2], p0, pU, pV, pUV),
            GetCorner(QuadCornerOrder[faceIndex, 3], p0, pU, pV, pUV));
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

    private static LocalPos ToLocalPos(Axis axis, int layer, int u, int v)
    {
        // axis별로 "layer가 고정되는 축"이 달라집니다.
        // X면을 처리할 때는 x=layer이고, u/v는 y/z가 됩니다.
        return axis switch
        {
            Axis.X => new LocalPos(layer, u, v),
            Axis.Y => new LocalPos(u, layer, v),
            _ => new LocalPos(u, v, layer)
        };
    }

    private static LocalPos Offset(LocalPos pos, Axis axis, int offset)
    {
        // 현재 voxel에서 검사 중인 축으로 한 칸 옆 좌표를 구합니다.
        // 이웃이 공기인지 확인해 "면이 노출되는가"를 판단할 때 사용합니다.
        return axis switch
        {
            Axis.X => new LocalPos(pos.X + offset, pos.Y, pos.Z),
            Axis.Y => new LocalPos(pos.X, pos.Y + offset, pos.Z),
            _ => new LocalPos(pos.X, pos.Y, pos.Z + offset)
        };
    }

    private static Vec3 GetFaceOrigin(FaceOrientation face, int layer, int u, int v)
    {
        // +방향 면은 voxel의 끝쪽 평면(layer + 1)에 있고,
        // -방향 면은 voxel의 시작쪽 평면(layer)에 있습니다.
        // 즉 "현재 직사각형 쿼드의 시작 꼭짓점이 3D 공간에서 어디냐"를 계산하는 함수입니다.
        int faceLayer = face.Positive ? layer + 1 : layer;

        return face.Axis switch
        {
            Axis.X => new Vec3(faceLayer, u, v),
            Axis.Y => new Vec3(u, faceLayer, v),
            _ => new Vec3(u, v, faceLayer)
        };
    }

    private static Vec3 GetUVector(Axis axis, int width)
    {
        // 2D mask에서 가로(width)로 합쳐진 길이를 3D 공간 벡터로 바꿉니다.
        // 어떤 축 면을 처리 중인지에 따라 실제 늘어나는 방향이 달라집니다.
        return axis switch
        {
            Axis.X => new Vec3(0, width, 0),
            Axis.Y => new Vec3(width, 0, 0),
            _ => new Vec3(width, 0, 0)
        };
    }

    private static Vec3 GetVVector(Axis axis, int height)
    {
        // 2D mask에서 세로(height)로 합쳐진 길이를 3D 공간 벡터로 바꿉니다.
        // GetUVector와 짝을 이루며, 두 벡터가 합쳐져 큰 쿼드의 크기가 됩니다.
        return axis switch
        {
            Axis.X => new Vec3(0, 0, height),
            Axis.Y => new Vec3(0, 0, height),
            _ => new Vec3(0, height, 0)
        };
    }

    private static Vec3 GetNormal(Axis axis, bool positive)
    {
        // 이 쿼드가 어느 방향을 바라보는지 나타내는 법선 벡터입니다.
        // 조명 계산과 winding 검증에서 사용됩니다.
        int sign = positive ? 1 : -1;

        return axis switch
        {
            Axis.X => new Vec3(sign, 0, 0),
            Axis.Y => new Vec3(0, sign, 0),
            _ => new Vec3(0, 0, sign)
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
