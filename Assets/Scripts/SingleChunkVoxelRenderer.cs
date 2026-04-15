using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class SingleChunkVoxelRenderer : MonoBehaviour
{
    // 청크 크기를 8x8x8로 고정합니다.
    private const int ChunkSize = 8;

    // 지금은 byte 값 전체를 voxel 타입 ID로 씁니다.
    // 예: 0 = Air, 1 = Dirt, 2 = Grass, 3 = Stone, 4 = Sand
    private const byte Air = 0;
    private const byte Dirt = 1;
    private const byte Grass = 2;
    private const byte Stone = 3;
    private const byte Sand = 4;

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

    // 지금은 텍스처가 없어도 Mesh API 학습을 위해 UV를 넣어둡니다.
    // 나중에 voxel 텍스처 아틀라스를 쓰면 이 값이 중요해집니다.
    private static readonly Vector2[] FaceUvs =
    {
        new Vector2(0, 0),
        new Vector2(0, 1),
        new Vector2(1, 1),
        new Vector2(1, 0),
    };

    [SerializeField] private Material material;
    [SerializeField] private Texture2D voxelAtlas;

    // 3차원 복셀 좌표를 1차원 배열에 저장합니다.
    // index = x + y * size + z * size * size
    private readonly byte[] voxels = new byte[ChunkSize * ChunkSize * ChunkSize];
    private Mesh generatedMesh;

    [ContextMenu("Rebuild Chunk")]
    private void RebuildChunk()
    {
        FillHardcodedChunk();
        BuildMesh();
        EnsureMaterial();
    }

    private void Awake()
    {
        RebuildChunk();
    }

    private void OnValidate()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        RebuildChunk();
    }

    private void OnDestroy()
    {
        if (generatedMesh != null)
        {
            if (Application.isPlaying)
            {
                Destroy(generatedMesh);
            }
            else
            {
                DestroyImmediate(generatedMesh);
            }
        }
    }

    private void FillHardcodedChunk()
    {
        // 먼저 y 높이에 따라 다른 voxel 타입으로 채웁니다.
        // 같은 byte 배열 안에서도 1은 Dirt, 2는 Grass, 3은 Stone처럼 의미를 나눌 수 있습니다.
        for (int z = 0; z < ChunkSize; z++)
        {
            for (int y = 0; y < ChunkSize; y++)
            {
                for (int x = 0; x < ChunkSize; x++)
                {
                    SetVoxel(x, y, z, GetVoxelTypeForHeight(y));
                }
            }
        }

        // 가운데를 파내서 빈 공간을 만듭니다.
        // 이 내부 공간과 맞닿는 voxel의 면은 화면에 보여야 합니다.
        for (int z = 2; z <= 5; z++)
        {
            for (int y = 2; y <= 5; y++)
            {
                for (int x = 2; x <= 5; x++)
                {
                    SetVoxel(x, y, z, Air);
                }
            }
        }

        // 앞쪽으로 작은 터널을 뚫습니다.
        // Scene 뷰에서 내부 면과 외부 면을 함께 확인하기 쉽게 하기 위함입니다.
        for (int z = 0; z <= 2; z++)
        {
            for (int y = 2; y <= 4; y++)
            {
                for (int x = 3; x <= 4; x++)
                {
                    SetVoxel(x, y, z, Air);
                }
            }
        }

        // 한쪽 모서리에 Sand voxel을 넣어 타입이 추가되어도 같은 메시 생성 로직을 쓰는지 확인합니다.
        for (int z = 5; z <= 7; z++)
        {
            for (int y = 1; y <= 3; y++)
            {
                for (int x = 0; x <= 2; x++)
                {
                    SetVoxel(x, y, z, Sand);
                }
            }
        }
    }

    private void BuildMesh()
    {
        // Unity Mesh는 정점, 삼각형 인덱스, 노멀, UV 배열로 구성됩니다.
        // 여기서는 공기와 맞닿은 면마다 정점 4개와 삼각형 2개를 추가합니다.
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();

        for (int z = 0; z < ChunkSize; z++)
        {
            for (int y = 0; y < ChunkSize; y++)
            {
                for (int x = 0; x < ChunkSize; x++)
                {
                    if (!IsSolid(x, y, z))
                    {
                        continue;
                    }

                    byte voxelType = GetVoxel(x, y, z);
                    var voxelLocalPosition = new Vector3(x, y, z);

                    for (int face = 0; face < FaceNormals.Length; face++)
                    {
                        // 현재 면 방향으로 이웃 voxel을 검사합니다.
                        // 이웃도 Air가 아닌 voxel이면 그 사이의 면은 보이지 않으므로 만들지 않습니다.
                        Vector3 normal = FaceNormals[face];
                        int neighborX = x + (int)normal.x;
                        int neighborY = y + (int)normal.y;
                        int neighborZ = z + (int)normal.z;

                        if (IsSolid(neighborX, neighborY, neighborZ))
                        {
                            continue;
                        }

                        // 이웃이 공기이거나 청크 밖이면 이 면은 외부에 노출됩니다.
                        AddFace(face, voxelType, voxelLocalPosition, vertices, triangles, normals, uvs);
                    }
                }
            }
        }

        if (generatedMesh == null)
        {
            generatedMesh = new Mesh
            {
                name = "Single Chunk Voxel Mesh"
            };
        }
        else
        {
            generatedMesh.Clear();
        }

        generatedMesh.indexFormat = vertices.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        generatedMesh.SetVertices(vertices);
        generatedMesh.SetTriangles(triangles, 0);
        generatedMesh.SetNormals(normals);
        generatedMesh.SetUVs(0, uvs);
        generatedMesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = generatedMesh;
    }

    private void AddFace(
        int faceIndex,
        byte voxelType,
        Vector3 voxelLocalPosition,
        List<Vector3> vertices,
        List<int> triangles,
        List<Vector3> normals,
        List<Vector2> uvs)
    {
        int startIndex = vertices.Count;

        // 쿼드 하나는 정점 4개로 표현합니다.
        for (int i = 0; i < 4; i++)
        {
            vertices.Add(voxelLocalPosition + FaceCorners[faceIndex, i]);
            normals.Add(FaceNormals[faceIndex]);
            uvs.Add(GetAtlasUv(voxelType, FaceUvs[i]));
        }

        // Unity의 Mesh 삼각형은 정점 인덱스 3개 단위입니다.
        // 쿼드 하나를 삼각형 두 개로 나눠서 넣습니다.
        triangles.Add(startIndex + 0);
        triangles.Add(startIndex + 1);
        triangles.Add(startIndex + 2);
        triangles.Add(startIndex + 0);
        triangles.Add(startIndex + 2);
        triangles.Add(startIndex + 3);
    }

    private void EnsureMaterial()
    {
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();

        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader != null)
            {
                material = new Material(shader)
                {
                    color = Color.white
                };
            }
        }

        if (material != null)
        {
            if (voxelAtlas == null)
            {
                Debug.LogError(
                    $"{nameof(voxelAtlas)} is required. Assign voxel_atlas.png in the Inspector.",
                    this);
                return;
            }

            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", voxelAtlas);
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", voxelAtlas);
            }

            meshRenderer.sharedMaterial = material;
        }
    }

    private bool IsSolid(int x, int y, int z)
    {
        // 청크 밖은 공기처럼 취급합니다.
        // 그래서 바깥쪽 경계 면이 생성됩니다.
        if (x < 0 || y < 0 || z < 0 || x >= ChunkSize || y >= ChunkSize || z >= ChunkSize)
        {
            return false;
        }

        return voxels[ToIndex(x, y, z)] != Air;
    }

    private byte GetVoxel(int x, int y, int z)
    {
        return voxels[ToIndex(x, y, z)];
    }

    private void SetVoxel(int x, int y, int z, byte value)
    {
        voxels[ToIndex(x, y, z)] = value;
    }

    private static byte GetVoxelTypeForHeight(int y)
    {
        if (y == ChunkSize - 1)
        {
            return Grass;
        }

        if (y <= 1)
        {
            return Stone;
        }

        return Dirt;
    }

    private static Vector2 GetAtlasUv(byte voxelType, Vector2 faceUv)
    {
        // 4칸짜리 가로 아틀라스를 사용합니다.
        // Dirt, Grass, Stone, Sand가 각각 다른 x 영역을 씁니다.
        int tileIndex = voxelType switch
        {
            Dirt => 0,
            Grass => 1,
            Stone => 2,
            Sand => 3,
            _ => 0
        };

        const float tileCount = 4f;
        float tileWidth = 1f / tileCount;
        float padding = 0.01f;
        float u = (tileIndex * tileWidth) + Mathf.Lerp(padding, tileWidth - padding, faceUv.x);
        float v = Mathf.Lerp(padding, 1f - padding, faceUv.y);

        return new Vector2(u, v);
    }

    private static int ToIndex(int x, int y, int z)
    {
        // 3차원 좌표를 1차원 배열 인덱스로 변환합니다.
        return x + (y * ChunkSize) + (z * ChunkSize * ChunkSize);
    }
}
