public sealed class NaiveMeshBuilder : IMeshBuilder
{
    // 각 면이 바라보는 방향입니다.
    // 이 방향으로 voxel 하나만큼 이동해서 이웃 voxel이 공기인지 검사합니다.
    private static readonly Vec3[] FaceNormals =
    {
        new Vec3(1, 0, 0),
        new Vec3(-1, 0, 0),
        new Vec3(0, 1, 0),
        new Vec3(0, -1, 0),
        new Vec3(0, 0, 1),
        new Vec3(0, 0, -1),
    };

    // voxel 하나에서 각 면을 이루는 네 꼭짓점의 상대 좌표입니다.
    // 예를 들어 +X 면은 x가 1인 평면 위의 네 점으로 구성됩니다.
    private static readonly Vec3[,] FaceCorners =
    {
        { new Vec3(1, 0, 0), new Vec3(1, 1, 0), new Vec3(1, 1, 1), new Vec3(1, 0, 1) }, // +X
        { new Vec3(0, 0, 1), new Vec3(0, 1, 1), new Vec3(0, 1, 0), new Vec3(0, 0, 0) }, // -X
        { new Vec3(0, 1, 1), new Vec3(1, 1, 1), new Vec3(1, 1, 0), new Vec3(0, 1, 0) }, // +Y
        { new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 0, 1), new Vec3(0, 0, 1) }, // -Y
        { new Vec3(1, 0, 1), new Vec3(1, 1, 1), new Vec3(0, 1, 1), new Vec3(0, 0, 1) }, // +Z
        { new Vec3(0, 0, 0), new Vec3(0, 1, 0), new Vec3(1, 1, 0), new Vec3(1, 0, 0) }, // -Z
    };

    public IMeshBuildHandle Schedule(ChunkNeighborhood neighborhood)
    {
        // Naive 구현은 아직 별도 스레드를 쓰지 않습니다.
        // 대신 Job 기반 구현과 같은 계약을 맞추기 위해 즉시 계산한 결과를 완료된 handle로 감쌉니다.
        return new CompletedMeshBuildHandle(BuildNow(neighborhood));
    }

    private static ChunkMeshData BuildNow(ChunkNeighborhood neighborhood)
    {
        var meshData = new ChunkMeshData();
        IChunkDataStore chunkData = neighborhood.Center;

        for (int z = 0; z < chunkData.Size; z++)
        {
            for (int y = 0; y < chunkData.Size; y++)
            {
                for (int x = 0; x < chunkData.Size; x++)
                {
                    var localPos = new LocalPos(x, y, z);
                    byte voxelType = chunkData.GetVoxel(localPos);
                    if (voxelType == VoxelType.Air)
                    {
                        continue;
                    }

                    var voxelLocalPosition = new Vec3(x, y, z);

                    for (int face = 0; face < FaceNormals.Length; face++)
                    {
                        // 현재 면 방향으로 이웃 voxel을 검사합니다.
                        // 이 좌표가 청크 밖이면 ChunkNeighborhood가 자동으로 이웃 청크 좌표로 바꿔줍니다.
                        Vec3 normal = FaceNormals[face];
                        var neighborPos = new LocalPos(
                            x + (int)normal.X,
                            y + (int)normal.Y,
                            z + (int)normal.Z);

                        if (neighborhood.GetVoxel(neighborPos) != VoxelType.Air)
                        {
                            continue;
                        }

                        // 이웃 청크까지 확인한 뒤에도 공기라면 이 면은 외부에 노출됩니다.
                        // 즉, 청크 경계에 붙어 있는 두 Solid voxel 사이에는 더 이상 불필요한 면을 만들지 않습니다.
                        AddFace(face, voxelType, voxelLocalPosition, meshData);
                    }
                }
            }
        }

        return meshData;
    }

    private static void AddFace(
        int faceIndex,
        byte voxelType,
        Vec3 voxelLocalPosition,
        ChunkMeshData meshData)
    {
        QuadMeshWriter.Write(
            meshData,
            voxelType,
            FaceNormals[faceIndex],
            voxelLocalPosition + FaceCorners[faceIndex, 0],
            voxelLocalPosition + FaceCorners[faceIndex, 1],
            voxelLocalPosition + FaceCorners[faceIndex, 2],
            voxelLocalPosition + FaceCorners[faceIndex, 3]);
    }

}
