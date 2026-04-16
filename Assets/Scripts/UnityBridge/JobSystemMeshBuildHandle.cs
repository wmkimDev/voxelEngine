using Unity.Collections;
using Unity.Jobs;

public sealed class JobSystemMeshBuildHandle : IMeshBuildHandle
{
    private JobHandle handle;
    private NativeArray<byte> center;
    private NativeArray<byte> positiveX;
    private NativeArray<byte> negativeX;
    private NativeArray<byte> positiveY;
    private NativeArray<byte> negativeY;
    private NativeArray<byte> positiveZ;
    private NativeArray<byte> negativeZ;
    private NativeList<Vec3> vertices;
    private NativeList<int> triangles;
    private NativeList<Vec3> normals;
    private NativeList<Vec2> uvs;
    private ChunkMeshData completedMeshData;
    private bool hasCompleted;

    public JobSystemMeshBuildHandle(
        JobHandle handle,
        NativeArray<byte> center,
        NativeArray<byte> positiveX,
        NativeArray<byte> negativeX,
        NativeArray<byte> positiveY,
        NativeArray<byte> negativeY,
        NativeArray<byte> positiveZ,
        NativeArray<byte> negativeZ,
        NativeList<Vec3> vertices,
        NativeList<int> triangles,
        NativeList<Vec3> normals,
        NativeList<Vec2> uvs)
    {
        this.handle = handle;
        this.center = center;
        this.positiveX = positiveX;
        this.negativeX = negativeX;
        this.positiveY = positiveY;
        this.negativeY = negativeY;
        this.positiveZ = positiveZ;
        this.negativeZ = negativeZ;
        this.vertices = vertices;
        this.triangles = triangles;
        this.normals = normals;
        this.uvs = uvs;
    }

    public bool IsCompleted => hasCompleted || handle.IsCompleted;

    public ChunkMeshData Complete()
    {
        if (hasCompleted)
        {
            return completedMeshData;
        }

        handle.Complete();

        // Job이 만든 NativeList 결과를 기존 렌더 경로가 그대로 쓸 수 있도록
        // Core의 ChunkMeshData로 한 번 옮겨 담습니다.
        var meshData = new ChunkMeshData();
        for (int i = 0; i < vertices.Length; i++)
        {
            meshData.Vertices.Add(vertices[i]);
        }

        for (int i = 0; i < triangles.Length; i++)
        {
            meshData.Triangles.Add(triangles[i]);
        }

        for (int i = 0; i < normals.Length; i++)
        {
            meshData.Normals.Add(normals[i]);
        }

        for (int i = 0; i < uvs.Length; i++)
        {
            meshData.Uvs.Add(uvs[i]);
        }

        DisposeNativeData();
        completedMeshData = meshData;
        hasCompleted = true;
        return completedMeshData;
    }

    private void DisposeNativeData()
    {
        // Job용 메모리는 직접 해제해야 누수가 생기지 않습니다.
        if (center.IsCreated) center.Dispose();
        if (positiveX.IsCreated) positiveX.Dispose();
        if (negativeX.IsCreated) negativeX.Dispose();
        if (positiveY.IsCreated) positiveY.Dispose();
        if (negativeY.IsCreated) negativeY.Dispose();
        if (positiveZ.IsCreated) positiveZ.Dispose();
        if (negativeZ.IsCreated) negativeZ.Dispose();
        if (vertices.IsCreated) vertices.Dispose();
        if (triangles.IsCreated) triangles.Dispose();
        if (normals.IsCreated) normals.Dispose();
        if (uvs.IsCreated) uvs.Dispose();
    }
}
