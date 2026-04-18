/// <summary>
/// 청크 이웃 정보를 바탕으로 메시 빌드를 예약하는 메셔 계약입니다.
/// 구현체는 즉시 계산할 수도 있고, Job처럼 비동기로 처리할 수도 있습니다.
/// </summary>
public interface IMeshBuilder
{
    /// <summary>
    /// 주어진 청크 주변 정보로 메시 빌드를 시작하고, 완료 여부를 추적할 핸들을 반환합니다.
    /// </summary>
    IMeshBuildHandle Schedule(ChunkNeighborhood neighborhood);
}

/// <summary>
/// 예약된 메시 빌드 작업의 상태를 추적하고, 완료 후 결과 메시를 회수하는 핸들입니다.
/// </summary>
public interface IMeshBuildHandle
{
    /// <summary>
    /// 메시 빌드 결과를 안전하게 회수할 수 있을 만큼 작업이 끝났는지 나타냅니다.
    /// </summary>
    bool IsCompleted { get; }

    /// <summary>
    /// 완료된 빌드 결과를 회수합니다. 호출 시점의 계약은 구현체가 정합니다.
    /// 일반적으로는 <see cref="IsCompleted"/>가 true일 때 호출해야 합니다.
    /// </summary>
    ChunkMeshData Complete();
}
