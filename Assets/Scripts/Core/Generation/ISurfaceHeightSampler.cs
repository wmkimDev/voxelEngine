/// <summary>
/// 월드 좌표 기준으로 표면 높이를 샘플링하는 계약입니다.
/// 주로 스폰 위치 정렬이나 지표 기준 판단에 사용합니다.
/// </summary>
public interface ISurfaceHeightSampler
{
    /// <summary>
    /// 주어진 world XZ 좌표의 대표 표면 높이를 반환합니다.
    /// </summary>
    int GetSurfaceHeight(int worldX, int worldZ);
}
