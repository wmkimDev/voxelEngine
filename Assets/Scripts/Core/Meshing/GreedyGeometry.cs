// Greedy 계열 메셔가 공유하는 3D 좌표/축 변환 규칙입니다.
// mask 병합 자체는 각 구현체가 따로 가지되, face 방향을 실제 공간 좌표로 바꾸는 규칙만 공통으로 둡니다.
public static class GreedyGeometry
{
    public enum Axis
    {
        X = 0,
        Y = 1,
        Z = 2,
    }

    public static Axis GetAxis(FaceDirection direction)
    {
        // 현재 처리 중인 face가 X/Y/Z 중 어느 축에 수직한 면인지 돌려줍니다.
        // 예를 들어 +X, -X 면은 모두 X축 면으로 취급합니다.
        return direction switch
        {
            FaceDirection.PositiveX => Axis.X,
            FaceDirection.NegativeX => Axis.X,
            FaceDirection.PositiveY => Axis.Y,
            FaceDirection.NegativeY => Axis.Y,
            _ => Axis.Z
        };
    }

    public static bool IsPositive(FaceDirection direction)
    {
        // 해당 face가 축의 +방향 끝면인지, -방향 시작면인지 알려줍니다.
        // Greedy는 이 값으로 면 평면이 layer인지 layer + 1인지 결정합니다.
        return direction switch
        {
            FaceDirection.PositiveX => true,
            FaceDirection.PositiveY => true,
            FaceDirection.PositiveZ => true,
            _ => false
        };
    }

    public static LocalPos ToLocalPos(Axis axis, int layer, int u, int v)
    {
        // Greedy는 한 축을 고정한 2D mask(layer, u, v)를 돌기 때문에,
        // 이 좌표를 실제 청크 내부 x/y/z 로컬 좌표로 바꿔야 voxel을 읽을 수 있습니다.
        return axis switch
        {
            Axis.X => new LocalPos(layer, u, v),
            Axis.Y => new LocalPos(u, layer, v),
            _ => new LocalPos(u, v, layer)
        };
    }

    public static LocalPos Offset(LocalPos pos, Axis axis, int offset)
    {
        // 현재 face가 보는 축으로 한 칸 옆 좌표를 구합니다.
        // 이웃 voxel이 공기인지 확인해서 "이 면이 보이는가"를 판단할 때 사용합니다.
        return axis switch
        {
            Axis.X => new LocalPos(pos.X + offset, pos.Y, pos.Z),
            Axis.Y => new LocalPos(pos.X, pos.Y + offset, pos.Z),
            _ => new LocalPos(pos.X, pos.Y, pos.Z + offset)
        };
    }

    public static Vec3 GetFaceOrigin(Axis axis, bool positive, int layer, int u, int v)
    {
        // 병합된 큰 쿼드의 시작 꼭짓점을 3D 공간에서 계산합니다.
        // -방향 면은 layer 평면에, +방향 면은 voxel의 바깥쪽인 layer + 1 평면에 놓입니다.
        int faceLayer = positive ? layer + 1 : layer;

        return axis switch
        {
            Axis.X => new Vec3(faceLayer, u, v),
            Axis.Y => new Vec3(u, faceLayer, v),
            _ => new Vec3(u, v, faceLayer)
        };
    }

    public static Vec3 GetUVector(Axis axis, int width)
    {
        // 2D mask에서 가로(width)로 병합된 길이를 3D 벡터로 바꿉니다.
        // 어떤 축 면을 처리 중인지에 따라 실제 늘어나는 축이 달라집니다.
        return axis switch
        {
            Axis.X => new Vec3(0, width, 0),
            Axis.Y => new Vec3(width, 0, 0),
            _ => new Vec3(width, 0, 0)
        };
    }

    public static Vec3 GetVVector(Axis axis, int height)
    {
        // 2D mask에서 세로(height)로 병합된 길이를 3D 벡터로 바꿉니다.
        // GetUVector와 짝을 이뤄서 최종 merged quad의 크기를 만듭니다.
        return axis switch
        {
            Axis.X => new Vec3(0, 0, height),
            Axis.Y => new Vec3(0, 0, height),
            _ => new Vec3(0, height, 0)
        };
    }
}
