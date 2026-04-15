using System.Collections.Generic;
using UnityEngine;

public sealed class ChunkMeshData
{
    public readonly List<Vector3> Vertices = new();
    public readonly List<int> Triangles = new();
    public readonly List<Vector3> Normals = new();
    public readonly List<Vector2> Uvs = new();
}

public sealed class NaiveMeshBuilder : IMeshBuilder
{
    // 각 면이 바라보는 방향입니다.
    // 이 방향으로 voxel 하나만큼 이동해서 이웃 voxel이 공기인지 검사합니다.
    private static readonly Vector3[] FaceNormals =
    {
        Vector3.right,
        Vector3.left,
        Vector3.up,
        Vector3.down,
        Vector3.forward,
        Vector3.back,
    };

    // voxel 하나에서 각 면을 이루는 네 꼭짓점의 상대 좌표입니다.
    // 예를 들어 +X 면은 x가 1인 평면 위의 네 점으로 구성됩니다.
    private static readonly Vector3[,] FaceCorners =
    {
        { new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1) }, // +X
        { new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0) }, // -X
        { new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0), new Vector3(0, 1, 0) }, // +Y
        { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 0, 1) }, // -Y
        { new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1), new Vector3(0, 0, 1) }, // +Z
        { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) }, // -Z
    };

    private static readonly Vector2[] FaceUvs =
    {
        new Vector2(0, 0),
        new Vector2(0, 1),
        new Vector2(1, 1),
        new Vector2(1, 0),
    };

    public ChunkMeshData Build(IChunkDataStore chunkData)
    {
        var meshData = new ChunkMeshData();

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

                    var voxelLocalPosition = new Vector3(x, y, z);

                    for (int face = 0; face < FaceNormals.Length; face++)
                    {
                        // 현재 면 방향으로 이웃 voxel을 검사합니다.
                        // 이웃도 Air가 아닌 voxel이면 그 사이의 면은 보이지 않으므로 만들지 않습니다.
                        Vector3 normal = FaceNormals[face];
                        var neighborPos = new LocalPos(
                            x + (int)normal.x,
                            y + (int)normal.y,
                            z + (int)normal.z);

                        if (chunkData.GetVoxel(neighborPos) != VoxelType.Air)
                        {
                            continue;
                        }

                        // 이웃이 공기이거나 청크 밖이면 이 면은 외부에 노출됩니다.
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
        Vector3 voxelLocalPosition,
        ChunkMeshData meshData)
    {
        int startIndex = meshData.Vertices.Count;

        // 쿼드 하나는 정점 4개로 표현합니다.
        for (int i = 0; i < 4; i++)
        {
            meshData.Vertices.Add(voxelLocalPosition + FaceCorners[faceIndex, i]);
            meshData.Normals.Add(FaceNormals[faceIndex]);
            meshData.Uvs.Add(GetAtlasUv(voxelType, FaceUvs[i]));
        }

        // Unity의 Mesh 삼각형은 정점 인덱스 3개 단위입니다.
        // 쿼드 하나를 삼각형 두 개로 나눠서 넣습니다.
        meshData.Triangles.Add(startIndex + 0);
        meshData.Triangles.Add(startIndex + 1);
        meshData.Triangles.Add(startIndex + 2);
        meshData.Triangles.Add(startIndex + 0);
        meshData.Triangles.Add(startIndex + 2);
        meshData.Triangles.Add(startIndex + 3);
    }

    private static Vector2 GetAtlasUv(byte voxelType, Vector2 faceUv)
    {
        // 4칸짜리 가로 아틀라스를 사용합니다.
        // Dirt, Grass, Stone, Sand가 각각 다른 x 영역을 씁니다.
        int tileIndex = voxelType switch
        {
            VoxelType.Dirt => 0,
            VoxelType.Grass => 1,
            VoxelType.Stone => 2,
            VoxelType.Sand => 3,
            _ => 0
        };

        const float tileCount = 4f;
        float tileWidth = 1f / tileCount;
        float padding = 0.01f;
        float u = (tileIndex * tileWidth) + Mathf.Lerp(padding, tileWidth - padding, faceUv.x);
        float v = Mathf.Lerp(padding, 1f - padding, faceUv.y);

        return new Vector2(u, v);
    }
}
