using System.Collections.Generic;
using UnityEngine;

public sealed class ChunkManager : MonoBehaviour
{
    [SerializeField] private int radiusX = 1;
    [SerializeField] private int radiusZ = 1;
    [SerializeField] private int layersY = 1;
    [SerializeField] private Material material;
    [SerializeField] private Texture2D voxelAtlas;
    [SerializeField] private Camera editCamera;
    [SerializeField] private float editDistance = 30f;
    [SerializeField] private byte placeVoxelType = VoxelType.Grass;

    private readonly Dictionary<ChunkPos, ChunkData> chunks = new();
    private readonly Dictionary<ChunkPos, ChunkRenderer> renderers = new();
    private readonly IMeshBuilder meshBuilder = new NaiveMeshBuilder();

    private void Start()
    {
        BuildFixedGrid();
    }

    private void OnValidate()
    {
        radiusX = Mathf.Max(0, radiusX);
        radiusZ = Mathf.Max(0, radiusZ);
        layersY = Mathf.Max(1, layersY);
        editDistance = Mathf.Max(0.1f, editDistance);
        placeVoxelType = (byte)Mathf.Clamp(placeVoxelType, VoxelType.Dirt, VoxelType.Sand);
    }

    [ContextMenu("Build Fixed Grid")]
    private void BuildFixedGrid()
    {
        ClearRuntimeChunks();
        chunks.Clear();
        renderers.Clear();

        // 1단계: 데이터만 먼저 전부 만듭니다.
        // 렌더러를 바로 만들면 아직 생성되지 않은 청크를 이웃으로 찾을 수 없습니다.
        for (int y = 0; y < layersY; y++)
        {
            for (int z = -radiusZ; z <= radiusZ; z++)
            {
                for (int x = -radiusX; x <= radiusX; x++)
                {
                    var chunkPos = new ChunkPos(x, y, z);
                    ChunkData chunkData = CreateDemoChunkData(chunkPos);
                    chunks.Add(chunkPos, chunkData);
                }
            }
        }

        // 2단계: 완성된 청크 딕셔너리를 기준으로 각 렌더러에 6방향 이웃을 연결합니다.
        for (int y = 0; y < layersY; y++)
        {
            for (int z = -radiusZ; z <= radiusZ; z++)
            {
                for (int x = -radiusX; x <= radiusX; x++)
                {
                    var chunkPos = new ChunkPos(x, y, z);
                    CreateChunkRenderer(chunkPos, CreateNeighborhood(chunkPos));
                }
            }
        }
    }

    private void CreateChunkRenderer(ChunkPos chunkPos, ChunkNeighborhood neighborhood)
    {
        var chunkObject = new GameObject($"Chunk ({chunkPos.X}, {chunkPos.Y}, {chunkPos.Z})");
        chunkObject.transform.SetParent(transform, worldPositionStays: false);
        chunkObject.transform.localPosition = chunkPos.ToWorldOrigin(neighborhood.Size);

        ChunkRenderer renderer = chunkObject.AddComponent<ChunkRenderer>();
        renderer.Initialize(
            neighborhood,
            meshBuilder,
            editCamera,
            material,
            voxelAtlas,
            placeVoxelType,
            editDistance,
            editedLocalPos => RebuildNeighborsTouchedByEdit(chunkPos, editedLocalPos));

        renderers.Add(chunkPos, renderer);
    }

    private ChunkData CreateDemoChunkData(ChunkPos chunkPos)
    {
        var data = new ChunkData();

        // 아직 월드 생성기는 없습니다. 청크별로 같은 규칙을 반복해 3x3x1 격자를 눈으로 확인합니다.
        for (int z = 0; z < data.Size; z++)
        {
            for (int y = 0; y < data.Size; y++)
            {
                for (int x = 0; x < data.Size; x++)
                {
                    data.SetVoxel(new LocalPos(x, y, z), GetVoxelTypeForHeight(y, data.Size));
                }
            }
        }

        // 각 청크마다 같은 구멍을 파서 청크가 독립적으로 생성되고 있음을 확인합니다.
        for (int z = 2; z <= 5; z++)
        {
            for (int y = 2; y <= 5; y++)
            {
                for (int x = 2; x <= 5; x++)
                {
                    data.SetVoxel(new LocalPos(x, y, z), VoxelType.Air);
                }
            }
        }

        for (int z = 0; z <= 2; z++)
        {
            for (int y = 2; y <= 4; y++)
            {
                for (int x = 3; x <= 4; x++)
                {
                    data.SetVoxel(new LocalPos(x, y, z), VoxelType.Air);
                }
            }
        }

        // 한쪽 모서리 색을 청크 좌표에 따라 조금 다르게 배치해 청크 경계를 구분하기 쉽게 합니다.
        int sandStartX = chunkPos.X % 2 == 0 ? 0 : 5;
        for (int z = 5; z <= 7; z++)
        {
            for (int y = 1; y <= 3; y++)
            {
                for (int x = sandStartX; x <= sandStartX + 2; x++)
                {
                    data.SetVoxel(new LocalPos(x, y, z), VoxelType.Sand);
                }
            }
        }

        return data;
    }

    private void ClearRuntimeChunks()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);

            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }
    }

    private ChunkNeighborhood CreateNeighborhood(ChunkPos chunkPos)
    {
        // 청크 좌표 기준으로 바로 옆 6개만 연결합니다.
        // 대각선 청크는 현재 면 생성에는 필요하지 않으므로 포함하지 않습니다.
        return new ChunkNeighborhood(
            GetChunk(chunkPos),
            GetChunk(new ChunkPos(chunkPos.X + 1, chunkPos.Y, chunkPos.Z)),
            GetChunk(new ChunkPos(chunkPos.X - 1, chunkPos.Y, chunkPos.Z)),
            GetChunk(new ChunkPos(chunkPos.X, chunkPos.Y + 1, chunkPos.Z)),
            GetChunk(new ChunkPos(chunkPos.X, chunkPos.Y - 1, chunkPos.Z)),
            GetChunk(new ChunkPos(chunkPos.X, chunkPos.Y, chunkPos.Z + 1)),
            GetChunk(new ChunkPos(chunkPos.X, chunkPos.Y, chunkPos.Z - 1)));
    }

    private ChunkData GetChunk(ChunkPos chunkPos)
    {
        return chunks.TryGetValue(chunkPos, out ChunkData chunkData) ? chunkData : null;
    }

    private void RebuildNeighborsTouchedByEdit(ChunkPos chunkPos, LocalPos editedLocalPos)
    {
        ChunkData chunkData = GetChunk(chunkPos);
        if (chunkData == null)
        {
            return;
        }

        int edge = chunkData.Size - 1;

        // 경계 voxel이 바뀌면 이웃 청크의 경계 면 노출 여부도 달라집니다.
        // 그래서 현재 청크는 ChunkRenderer가 직접 재빌드하고, 여기서는 맞닿은 이웃만 추가로 재빌드합니다.
        if (editedLocalPos.X == 0)
        {
            RebuildChunk(new ChunkPos(chunkPos.X - 1, chunkPos.Y, chunkPos.Z));
        }

        if (editedLocalPos.X == edge)
        {
            RebuildChunk(new ChunkPos(chunkPos.X + 1, chunkPos.Y, chunkPos.Z));
        }

        if (editedLocalPos.Y == 0)
        {
            RebuildChunk(new ChunkPos(chunkPos.X, chunkPos.Y - 1, chunkPos.Z));
        }

        if (editedLocalPos.Y == edge)
        {
            RebuildChunk(new ChunkPos(chunkPos.X, chunkPos.Y + 1, chunkPos.Z));
        }

        if (editedLocalPos.Z == 0)
        {
            RebuildChunk(new ChunkPos(chunkPos.X, chunkPos.Y, chunkPos.Z - 1));
        }

        if (editedLocalPos.Z == edge)
        {
            RebuildChunk(new ChunkPos(chunkPos.X, chunkPos.Y, chunkPos.Z + 1));
        }
    }

    private void RebuildChunk(ChunkPos chunkPos)
    {
        if (renderers.TryGetValue(chunkPos, out ChunkRenderer renderer))
        {
            renderer.RebuildMesh();
        }
    }

    private static byte GetVoxelTypeForHeight(int y, int chunkSize)
    {
        if (y == chunkSize - 1)
        {
            return VoxelType.Grass;
        }

        if (y <= 1)
        {
            return VoxelType.Stone;
        }

        return VoxelType.Dirt;
    }
}
