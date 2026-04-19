using UnityEngine;

/// <summary>
/// CorridorRoomsGenerator: Única responsabilidad — construir la geometría del corredor
/// y sus salas laterales (suelos, techos, paredes, vanos de puerta).
///
/// Cuando termina de construir cada sala, avisa a <see cref="RoomFurniturePlacer"/>
/// para que coloque los muebles. No sabe nada de audio ni de muebles concretos.
/// </summary>
[DisallowMultipleComponent]
public class RoomGeometry : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────

    [Header("Generación")]
    [SerializeField] private bool generateOnStart = false;
    [SerializeField] private bool clearBeforeGenerate = true;
    [SerializeField, Min(1)] private int roomCount = 6;
    [SerializeField] private bool movePlayerToStartOnGenerate = true;

    [Header("Posición inicial del jugador")]
    [SerializeField] private Vector3 playerStartLocalPosition = new Vector3(0f, 0f, 1.5f);
    [SerializeField] private Vector3 playerStartLocalEulerAngles = Vector3.zero;

    [Header("Corredor")]
    [SerializeField, Min(2f)] private float corridorWidth = 4f;
    [SerializeField, Min(2f)] private float corridorHeight = 3f;
    [SerializeField, Min(4f)] private float corridorSegmentLength = 8f;
    [SerializeField, Min(0.05f)] private float wallThickness = 0.2f;

    [Header("Salas")]
    [SerializeField, Min(3f)] private float roomWidth = 6f;
    [SerializeField, Min(3f)] private float roomDepth = 6f;

    [Header("Vano de puerta")]
    [SerializeField, Min(0.8f)] private float doorwayWidth = 1.8f;
    [SerializeField, Min(1.8f)] private float doorwayHeight = 2.2f;

    [Header("Materiales")]
    [SerializeField] private Material corridorFloorMaterial;
    [SerializeField] private Material corridorWallMaterial;
    [SerializeField] private Material corridorCeilingMaterial;
    [SerializeField] private Material roomFloorMaterial;
    [SerializeField] private Material roomWallMaterial;
    [SerializeField] private Material roomCeilingMaterial;

    [Header("Dependencias")]
    [Tooltip("Encargado de instanciar muebles en cada sala. Puede ser null si no se usan muebles.")]
    [SerializeField] private RoomFurniturePlacer furniturePlacer;

    // ─────────────────────────────────────────────
    //  Constantes
    // ─────────────────────────────────────────────

    private const string GeneratedRootName = "GeneratedScenario";

    // ─────────────────────────────────────────────
    //  Unity
    // ─────────────────────────────────────────────

    private void Start()
    {
        if (generateOnStart)
        {
            GenerateScenario();
        }
    }

    // ─────────────────────────────────────────────
    //  API pública
    // ─────────────────────────────────────────────

    [ContextMenu("Generate Scenario")]
    public void GenerateScenario()
    {
        ValidateDoorway();

        if (clearBeforeGenerate)
        {
            ClearGeneratedScenario();
        }

        Transform generatedRoot = GetOrCreateGeneratedRoot();
        float totalLength = roomCount * corridorSegmentLength;

        BuildCorridor(generatedRoot, totalLength);

        for (int i = 0; i < roomCount; i++)
        {
            float zCenter = (i + 0.5f) * corridorSegmentLength;
            int side = i % 2 == 0 ? 1 : -1;
            BuildRoom(generatedRoot, zCenter, side, i + 1);
        }

        if (Application.isPlaying && movePlayerToStartOnGenerate)
        {
            MovePlayerToStart();
        }
    }

    [ContextMenu("Clear Generated Scenario")]
    public void ClearGeneratedScenario()
    {
        Transform generatedRoot = transform.Find(GeneratedRootName);

        if (generatedRoot == null)
        {
            return;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            DestroyImmediate(generatedRoot.gameObject);
            return;
        }
#endif
        Destroy(generatedRoot.gameObject);
    }

    // ─────────────────────────────────────────────
    //  Construcción del corredor
    // ─────────────────────────────────────────────

    private void BuildCorridor(Transform parent, float totalLength)
    {
        // Suelo y techo
        CreateBlock("CorridorFloor",
            new Vector3(0f, -wallThickness * 0.5f, totalLength * 0.5f),
            new Vector3(corridorWidth, wallThickness, totalLength),
            corridorFloorMaterial, parent);

        CreateBlock("CorridorCeiling",
            new Vector3(0f, corridorHeight + wallThickness * 0.5f, totalLength * 0.5f),
            new Vector3(corridorWidth, wallThickness, totalLength),
            corridorCeilingMaterial, parent);

        // Paredes laterales (con huecos para las puertas)
        float halfCorridor = corridorWidth * 0.5f;
        BuildCorridorSideWall(parent, -halfCorridor, totalLength, side: -1);
        BuildCorridorSideWall(parent,  halfCorridor, totalLength, side:  1);

        // Tapas de inicio y fin
        Vector3 endWallScale = new Vector3(corridorWidth, corridorHeight, wallThickness);
        CreateBlock("CorridorStartCap",
            new Vector3(0f, corridorHeight * 0.5f, -wallThickness * 0.5f),
            endWallScale, corridorWallMaterial, parent);

        CreateBlock("CorridorEndCap",
            new Vector3(0f, corridorHeight * 0.5f, totalLength + wallThickness * 0.5f),
            endWallScale, corridorWallMaterial, parent);
    }

    private void BuildCorridorSideWall(Transform parent, float wallX, float totalLength, int side)
    {
        float wallStartZ = 0f;

        for (int i = 0; i < roomCount; i++)
        {
            int roomSide = i % 2 == 0 ? 1 : -1;
            if (roomSide != side)
            {
                continue;
            }

            float zCenter      = (i + 0.5f) * corridorSegmentLength;
            float openingStartZ = zCenter - doorwayWidth * 0.5f;
            float openingEndZ   = zCenter + doorwayWidth * 0.5f;

            if (openingStartZ > wallStartZ)
            {
                CreateCorridorWallSegment(parent, wallX, wallStartZ, openingStartZ);
            }

            wallStartZ = Mathf.Max(wallStartZ, openingEndZ);
        }

        if (wallStartZ < totalLength)
        {
            CreateCorridorWallSegment(parent, wallX, wallStartZ, totalLength);
        }
    }

    private void CreateCorridorWallSegment(Transform parent, float wallX, float startZ, float endZ)
    {
        float segmentLength = endZ - startZ;

        if (segmentLength <= 0.001f)
        {
            return;
        }

        CreateBlock(
            "CorridorWallSegment",
            new Vector3(wallX, corridorHeight * 0.5f, startZ + segmentLength * 0.5f),
            new Vector3(wallThickness, corridorHeight, segmentLength),
            corridorWallMaterial,
            parent);
    }

    // ─────────────────────────────────────────────
    //  Construcción de salas
    // ─────────────────────────────────────────────

    private void BuildRoom(Transform parent, float zCenter, int side, int index)
    {
        string roomName = "Room_" + index;
        GameObject roomRoot = new GameObject(roomName);
        roomRoot.transform.SetParent(parent, false);

        float corridorHalf = corridorWidth * 0.5f;
        float xCenter = side * (corridorHalf + roomWidth * 0.5f + wallThickness * 0.5f);

        // Suelo y techo de sala
        CreateBlock(roomName + "_Floor",
            new Vector3(xCenter, -wallThickness * 0.5f, zCenter),
            new Vector3(roomWidth, wallThickness, roomDepth),
            roomFloorMaterial, roomRoot.transform);

        CreateBlock(roomName + "_Ceiling",
            new Vector3(xCenter, corridorHeight + wallThickness * 0.5f, zCenter),
            new Vector3(roomWidth, wallThickness, roomDepth),
            roomCeilingMaterial, roomRoot.transform);

        // Pared exterior (opuesta al pasillo)
        float outerWallX = xCenter + side * (roomWidth * 0.5f - wallThickness * 0.5f);
        CreateBlock(roomName + "_OuterWall",
            new Vector3(outerWallX, corridorHeight * 0.5f, zCenter),
            new Vector3(wallThickness, corridorHeight, roomDepth),
            roomWallMaterial, roomRoot.transform);

        // Paredes frontales y traseras
        float frontZ = zCenter - roomDepth * 0.5f + wallThickness * 0.5f;
        float backZ  = zCenter + roomDepth * 0.5f - wallThickness * 0.5f;

        CreateBlock(roomName + "_FrontWall",
            new Vector3(xCenter, corridorHeight * 0.5f, frontZ),
            new Vector3(roomWidth, corridorHeight, wallThickness),
            roomWallMaterial, roomRoot.transform);

        CreateBlock(roomName + "_BackWall",
            new Vector3(xCenter, corridorHeight * 0.5f, backZ),
            new Vector3(roomWidth, corridorHeight, wallThickness),
            roomWallMaterial, roomRoot.transform);

        // Pared con vano de puerta (cara al pasillo)
        float doorwayWallX = xCenter - side * (roomWidth * 0.5f - wallThickness * 0.5f);
        BuildDoorwayWall(roomName, roomRoot.transform, doorwayWallX, zCenter, roomDepth, doorwayWidth, doorwayHeight, roomWallMaterial);

        // Delegar colocación de muebles
        if (furniturePlacer != null)
        {
            furniturePlacer.PlaceInRoom(roomRoot.transform, index, side, xCenter, zCenter);
        }
    }

    private void BuildDoorwayWall(
        string roomName,
        Transform parent,
        float wallX,
        float wallCenterZ,
        float wallLength,
        float openingWidth,
        float openingHeight,
        Material material)
    {
        // Segmentos laterales al hueco
        float sideLength = Mathf.Max(0f, (wallLength - openingWidth) * 0.5f);
        if (sideLength > 0.001f)
        {
            float offset = openingWidth * 0.5f + sideLength * 0.5f;

            CreateBlock(roomName + "_DoorWall_SideA",
                new Vector3(wallX, corridorHeight * 0.5f, wallCenterZ - offset),
                new Vector3(wallThickness, corridorHeight, sideLength),
                material, parent);

            CreateBlock(roomName + "_DoorWall_SideB",
                new Vector3(wallX, corridorHeight * 0.5f, wallCenterZ + offset),
                new Vector3(wallThickness, corridorHeight, sideLength),
                material, parent);
        }

        // Dintel sobre el hueco
        float topHeight = Mathf.Max(0f, corridorHeight - openingHeight);
        if (topHeight > 0.001f)
        {
            CreateBlock(roomName + "_DoorWall_Top",
                new Vector3(wallX, openingHeight + topHeight * 0.5f, wallCenterZ),
                new Vector3(wallThickness, topHeight, openingWidth),
                material, parent);
        }
    }

    // ─────────────────────────────────────────────
    //  Utilidades
    // ─────────────────────────────────────────────

    private GameObject CreateBlock(string blockName, Vector3 localPosition, Vector3 localScale, Material material, Transform parent)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = blockName;
        block.transform.SetParent(parent, false);
        block.transform.localPosition = localPosition;
        block.transform.localScale = localScale;

        if (material != null)
        {
            Renderer r = block.GetComponent<Renderer>();
            if (r != null)
            {
                r.sharedMaterial = material;
            }
        }

        return block;
    }

    private Transform GetOrCreateGeneratedRoot()
    {
        Transform found = transform.Find(GeneratedRootName);
        if (found != null)
        {
            return found;
        }

        GameObject root = new GameObject(GeneratedRootName);
        root.transform.SetParent(transform, false);
        return root.transform;
    }

    private void ValidateDoorway()
    {
        doorwayWidth  = Mathf.Clamp(doorwayWidth,  0.8f, roomDepth - 0.2f);
        doorwayHeight = Mathf.Clamp(doorwayHeight, 1.8f, corridorHeight - 0.2f);
    }

    private void MovePlayerToStart()
    {
        GameObject xrOrigin = GameObject.Find("XR Origin");
        if (xrOrigin == null)
        {
            Debug.LogWarning("[CorridorRoomsGenerator] No se encontró 'XR Origin' para moverlo al inicio.");
            return;
        }

        Vector3 worldPos      = transform.TransformPoint(playerStartLocalPosition);
        Quaternion worldRot   = transform.rotation * Quaternion.Euler(playerStartLocalEulerAngles);
        xrOrigin.transform.SetPositionAndRotation(worldPos, worldRot);
    }
}
