using UnityEngine;
using System.Collections.Generic;

public class CorridorRoomsGenerator : MonoBehaviour
{
    // ─────────────────────────────────────────────
    //  Configuración de audio opcional por mueble
    // ─────────────────────────────────────────────
    [System.Serializable]
    private class RoomAudioOverride
    {
        [Tooltip("ID de sala al que aplica esta configuración (Room_1 => 1, Room_2 => 2, etc.).")]
        [Min(1)] public int roomId = 1;

        [Tooltip("Clip a reproducir para esta sala. Si está vacío, se usa el clip base.")]
        public AudioClip audioClip;

        [Tooltip("Volumen base del emisor para esta sala (0–1).")]
        [Range(0f, 1f)] public float baseVolume = 1f;

        [Tooltip("Nivel mínimo de inmersión para activar el sonido en esta sala (1–6).")]
        [Range(1, 6)] public int minImmersionLevel = 2;

        [Tooltip("Distancia mínima de audio 3D para esta sala.")]
        [Min(0f)] public float minDistance = 1f;

        [Tooltip("Distancia máxima de audio 3D para esta sala.")]
        [Min(0f)] public float maxDistance = 10f;

        [Tooltip("Si está activo, reemplaza también el tipo de emisor en esta sala.")]
        public bool overrideEmitterType = false;

        [Tooltip("Tipo de emisor alternativo para esta sala (solo si overrideEmitterType = true).")]
        public SoundEmitter.SoundEmitterType emitterType = SoundEmitter.SoundEmitterType.Custom;
    }

    [System.Serializable]
    private class FurnitureAudioConfig
    {
        [Header("Audio por Sala")]
        [Tooltip("Lista de audio por sala. Permite múltiples audios para el mismo objeto, según roomId.")]
        public List<RoomAudioOverride> roomAudioOverrides = new List<RoomAudioOverride>();
    }

    // ─────────────────────────────────────────────
    //  Placement de mueble
    // ─────────────────────────────────────────────
    [System.Serializable]
    private class FurniturePlacement
    {
        public string id = "Furniture";
        public GameObject prefab;
        public Vector3 localPosition = Vector3.zero;
        public Vector3 localEulerAngles = Vector3.zero;
        public Vector3 localScale = Vector3.one;
        public string placeOnTopOfId = string.Empty;
        [Min(0f)] public float topOffset = 0.02f;
        public bool useSpecificRooms = false;
        public List<int> specificRoomIndices = new List<int>();
        [Min(1)] public int placeEveryNRooms = 1;
        public bool placeOnLeftRooms = true;
        public bool placeOnRightRooms = true;

        [Header("Audio (opcional)")]
        [Tooltip("Configuración de SoundEmitter para este mueble. Deja addSoundEmitter en false para ignorarlo.")]
        public FurnitureAudioConfig audio = new FurnitureAudioConfig();
    }

    [Header("Generation")]
    [SerializeField] private bool generateOnStart = false;
    [SerializeField] private bool clearBeforeGenerate = true;
    [SerializeField] [Min(1)] private int roomCount = 6;
    [SerializeField] private bool movePlayerToStartOnGenerate = true;

    [Header("Player Start")]
    [SerializeField] private Vector3 playerStartLocalPosition = new Vector3(0f, 0f, 1.5f);
    [SerializeField] private Vector3 playerStartLocalEulerAngles = Vector3.zero;

    [Header("Corridor")]
    [SerializeField] [Min(2f)] private float corridorWidth = 4f;
    [SerializeField] [Min(2f)] private float corridorHeight = 3f;
    [SerializeField] [Min(4f)] private float corridorSegmentLength = 8f;
    [SerializeField] [Min(0.05f)] private float wallThickness = 0.2f;

    [Header("Room")]
    [SerializeField] [Min(3f)] private float roomWidth = 6f;
    [SerializeField] [Min(3f)] private float roomDepth = 6f;

    [Header("Doorway")]
    [SerializeField] [Min(0.8f)] private float doorwayWidth = 1.8f;
    [SerializeField] [Min(1.8f)] private float doorwayHeight = 2.2f;

    [Header("Materials (assign LB3D materials here)")]
    [SerializeField] private Material corridorFloorMaterial;
    [SerializeField] private Material corridorWallMaterial;
    [SerializeField] private Material corridorCeilingMaterial;
    [SerializeField] private Material roomFloorMaterial;
    [SerializeField] private Material roomWallMaterial;
    [SerializeField] private Material roomCeilingMaterial;

    [Header("Furniture")]
    [SerializeField] private bool generateFurniture = true;
    [SerializeField] private List<FurniturePlacement> furniturePlacements = new List<FurniturePlacement>();

    private const string GeneratedRootName = "GeneratedScenario";

    private void Start()
    {
        if (generateOnStart)
        {
            GenerateScenario();
        }
    }

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

        if (Application.isPlaying)
        {
            RefreshSpatializerManagers();
        }
    }

    private void RefreshSpatializerManagers()
    {
        SpatialiazerManager[] spatializerManagers = FindObjectsOfType<SpatialiazerManager>(includeInactive: true);
        for (int i = 0; i < spatializerManagers.Length; i++)
        {
            if (spatializerManagers[i] == null)
            {
                continue;
            }

            spatializerManagers[i].ApplyToTargets();
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

    private void BuildCorridor(Transform parent, float totalLength)
    {
        Vector3 floorScale = new Vector3(corridorWidth, wallThickness, totalLength);
        Vector3 floorPosition = new Vector3(0f, -wallThickness * 0.5f, totalLength * 0.5f);
        CreateBlock("CorridorFloor", floorPosition, floorScale, corridorFloorMaterial, parent);

        Vector3 ceilingScale = new Vector3(corridorWidth, wallThickness, totalLength);
        Vector3 ceilingPosition = new Vector3(0f, corridorHeight + wallThickness * 0.5f, totalLength * 0.5f);
        CreateBlock("CorridorCeiling", ceilingPosition, ceilingScale, corridorCeilingMaterial, parent);

        Vector3 sideWallScale = new Vector3(wallThickness, corridorHeight, totalLength);
        float halfCorridor = corridorWidth * 0.5f;
        BuildCorridorSideWall(parent, -halfCorridor, totalLength, -1);
        BuildCorridorSideWall(parent, halfCorridor, totalLength, 1);

        Vector3 endWallScale = new Vector3(corridorWidth, corridorHeight, wallThickness);
        CreateBlock("CorridorStartCap", new Vector3(0f, corridorHeight * 0.5f, -wallThickness * 0.5f), endWallScale, corridorWallMaterial, parent);
        CreateBlock("CorridorEndCap", new Vector3(0f, corridorHeight * 0.5f, totalLength + wallThickness * 0.5f), endWallScale, corridorWallMaterial, parent);
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

            float zCenter = (i + 0.5f) * corridorSegmentLength;
            float openingStartZ = zCenter - doorwayWidth * 0.5f;
            float openingEndZ = zCenter + doorwayWidth * 0.5f;

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

    private void BuildRoom(Transform parent, float zCenter, int side, int index)
    {
        string roomName = "Room_" + index;
        GameObject roomRoot = new GameObject(roomName);
        roomRoot.transform.SetParent(parent, false);

        float corridorHalf = corridorWidth * 0.5f;
        float xCenter = side * (corridorHalf + roomWidth * 0.5f + wallThickness * 0.5f);

        CreateBlock(
            roomName + "_Floor",
            new Vector3(xCenter, -wallThickness * 0.5f, zCenter),
            new Vector3(roomWidth, wallThickness, roomDepth),
            roomFloorMaterial,
            roomRoot.transform);

        CreateBlock(
            roomName + "_Ceiling",
            new Vector3(xCenter, corridorHeight + wallThickness * 0.5f, zCenter),
            new Vector3(roomWidth, wallThickness, roomDepth),
            roomCeilingMaterial,
            roomRoot.transform);

        float outerWallX = xCenter + side * (roomWidth * 0.5f - wallThickness * 0.5f);
        CreateBlock(
            roomName + "_OuterWall",
            new Vector3(outerWallX, corridorHeight * 0.5f, zCenter),
            new Vector3(wallThickness, corridorHeight, roomDepth),
            roomWallMaterial,
            roomRoot.transform);

        float frontZ = zCenter - roomDepth * 0.5f + wallThickness * 0.5f;
        float backZ = zCenter + roomDepth * 0.5f - wallThickness * 0.5f;

        CreateBlock(
            roomName + "_FrontWall",
            new Vector3(xCenter, corridorHeight * 0.5f, frontZ),
            new Vector3(roomWidth, corridorHeight, wallThickness),
            roomWallMaterial,
            roomRoot.transform);

        CreateBlock(
            roomName + "_BackWall",
            new Vector3(xCenter, corridorHeight * 0.5f, backZ),
            new Vector3(roomWidth, corridorHeight, wallThickness),
            roomWallMaterial,
            roomRoot.transform);

        float doorwayWallX = xCenter - side * (roomWidth * 0.5f - wallThickness * 0.5f);
        BuildDoorwayWall(
            roomName,
            roomRoot.transform,
            doorwayWallX,
            zCenter,
            roomDepth,
            doorwayWidth,
            doorwayHeight,
            roomWallMaterial);

        if (generateFurniture)
        {
            PlaceFurnitureInRoom(roomRoot.transform, index, side, xCenter, zCenter);
        }
    }

    private void PlaceFurnitureInRoom(Transform roomRoot, int roomIndex, int side, float roomCenterX, float roomCenterZ)
    {
        if (furniturePlacements == null || furniturePlacements.Count == 0)
        {
            return;
        }

        Transform furnitureRoot = new GameObject("Furniture").transform;
        furnitureRoot.SetParent(roomRoot, false);
        furnitureRoot.localPosition = new Vector3(roomCenterX, 0f, roomCenterZ);

        Dictionary<string, GameObject> placedById = new Dictionary<string, GameObject>();
        List<FurniturePlacement> placeOnTopQueue = new List<FurniturePlacement>();

        foreach (FurniturePlacement placement in furniturePlacements)
        {
            if (placement == null || placement.prefab == null)
            {
                continue;
            }

            if (!ShouldPlaceInRoom(placement, roomIndex, side))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(placement.placeOnTopOfId))
            {
                placeOnTopQueue.Add(placement);
                continue;
            }

            PlaceFurnitureInstance(placement, furnitureRoot, roomIndex, placedById);
        }

        foreach (FurniturePlacement placement in placeOnTopQueue)
        {
            GameObject instance = PlaceFurnitureInstance(placement, furnitureRoot, roomIndex, placedById);
            if (instance == null)
            {
                continue;
            }

            if (!placedById.TryGetValue(placement.placeOnTopOfId, out GameObject target))
            {
                Debug.LogWarning("[CorridorRoomsGenerator] No se encontró el mueble objetivo '" + placement.placeOnTopOfId + "' para colocar '" + instance.name + "' en Room " + roomIndex + ".");
                continue;
            }

            SnapInstanceOnTop(instance.transform, target.transform, placement.topOffset);
        }
    }

    private GameObject PlaceFurnitureInstance(FurniturePlacement placement, Transform furnitureRoot, int roomIndex, Dictionary<string, GameObject> placedById)
    {
        GameObject instance = Instantiate(placement.prefab, furnitureRoot);
        string placementId = string.IsNullOrWhiteSpace(placement.id) ? placement.prefab.name : placement.id;
        instance.name = placementId + "_Room" + roomIndex;
        instance.transform.localPosition = placement.localPosition;
        instance.transform.localRotation = Quaternion.Euler(placement.localEulerAngles);

        Vector3 desiredScale = placement.localScale;
        bool hasZeroScale = Mathf.Approximately(desiredScale.x, 0f)
                            || Mathf.Approximately(desiredScale.y, 0f)
                            || Mathf.Approximately(desiredScale.z, 0f);

        if (hasZeroScale)
        {
            desiredScale = placement.prefab.transform.localScale;
            if (Mathf.Approximately(desiredScale.x, 0f)
                || Mathf.Approximately(desiredScale.y, 0f)
                || Mathf.Approximately(desiredScale.z, 0f))
            {
                desiredScale = Vector3.one;
            }
        }

        instance.transform.localScale = desiredScale;
        placedById[placementId] = instance;

        // Inyectar SoundEmitter si esta configurado para este mueble
        InjectAudioIfNeeded(instance, placement.audio, roomIndex);

        return instance;
    }

    /// <summary>
    /// Agrega o configura un SoundEmitter en el GameObject instanciado segun la
    /// FurnitureAudioConfig del placement. Si el prefab ya tiene un SoundEmitter,
    /// se reutiliza; de lo contrario se agrega uno nuevo.
    /// </summary>
    private void InjectAudioIfNeeded(GameObject instance, FurnitureAudioConfig audioConfig, int roomIndex)
    {
        if (audioConfig == null)
        {
            return;
        }

        bool hasRoomOverride = TryGetRoomAudioOverride(audioConfig, roomIndex, out RoomAudioOverride roomOverride);
        if (!hasRoomOverride)
        {
            return;
        }

        SoundEmitter.SoundEmitterType emitterType = roomOverride.emitterType;
        AudioClip clip = roomOverride.audioClip;
        float baseVolume = roomOverride.baseVolume;
        int minImmersionLevel = roomOverride.minImmersionLevel;
        float minDistance = roomOverride.minDistance;
        float maxDistance = roomOverride.maxDistance;

        if (!roomOverride.overrideEmitterType)
        {
            emitterType = SoundEmitter.SoundEmitterType.Custom;
        }

        SoundEmitter emitter = instance.GetComponentInChildren<SoundEmitter>(includeInactive: true);
        if (emitter == null)
        {
            emitter = instance.AddComponent<SoundEmitter>();
        }

        emitter.Configure(
            emitterType,
            clip,
            baseVolume,
            minImmersionLevel,
            minDistance,
            maxDistance,
            roomIndex);

        if (Application.isPlaying && AudioManager.Instance != null)
        {
            AudioManager.Instance.RegisterEmitter(emitter);
        }
    }

    private bool TryGetRoomAudioOverride(FurnitureAudioConfig audioConfig, int roomIndex, out RoomAudioOverride roomOverride)
    {
        roomOverride = null;

        if (audioConfig == null || audioConfig.roomAudioOverrides == null)
        {
            return false;
        }

        for (int i = 0; i < audioConfig.roomAudioOverrides.Count; i++)
        {
            RoomAudioOverride candidate = audioConfig.roomAudioOverrides[i];
            if (candidate == null)
            {
                continue;
            }

            if (candidate.roomId == roomIndex)
            {
                roomOverride = candidate;
                return true;
            }
        }

        return false;
    }

    private void SnapInstanceOnTop(Transform moving, Transform target, float extraOffset)
    {
        if (!TryGetCombinedRendererBounds(target, out Bounds targetBounds)
            || !TryGetCombinedRendererBounds(moving, out Bounds movingBounds))
        {
            Debug.LogWarning("[CorridorRoomsGenerator] No se pudieron calcular bounds para apilar muebles ('" + moving.name + "' sobre '" + target.name + "').");
            return;
        }

        float yDelta = (targetBounds.max.y - movingBounds.min.y) + Mathf.Max(0f, extraOffset);
        moving.position += new Vector3(0f, yDelta, 0f);
    }

    private bool TryGetCombinedRendererBounds(Transform root, out Bounds combined)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
        {
            combined = default;
            return false;
        }

        combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            combined.Encapsulate(renderers[i].bounds);
        }

        return true;
    }

    private bool ShouldPlaceInRoom(FurniturePlacement placement, int roomIndex, int side)
    {
        if (placement.useSpecificRooms)
        {
            if (placement.specificRoomIndices == null || placement.specificRoomIndices.Count == 0)
            {
                return false;
            }

            bool roomIsIncluded = false;
            for (int i = 0; i < placement.specificRoomIndices.Count; i++)
            {
                if (placement.specificRoomIndices[i] == roomIndex)
                {
                    roomIsIncluded = true;
                    break;
                }
            }

            return roomIsIncluded;
        }

        int everyN = Mathf.Max(1, placement.placeEveryNRooms);
        if (((roomIndex - 1) % everyN) != 0)
        {
            return false;
        }

        bool isLeftRoom = side < 0;
        bool isRightRoom = side > 0;
        if ((isLeftRoom && !placement.placeOnLeftRooms) || (isRightRoom && !placement.placeOnRightRooms))
        {
            return false;
        }

        return true;
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
        float sideSegmentLength = Mathf.Max(0f, (wallLength - openingWidth) * 0.5f);

        if (sideSegmentLength > 0.001f)
        {
            float segmentOffset = openingWidth * 0.5f + sideSegmentLength * 0.5f;

            CreateBlock(
                roomName + "_DoorWall_SideA",
                new Vector3(wallX, corridorHeight * 0.5f, wallCenterZ - segmentOffset),
                new Vector3(wallThickness, corridorHeight, sideSegmentLength),
                material,
                parent);

            CreateBlock(
                roomName + "_DoorWall_SideB",
                new Vector3(wallX, corridorHeight * 0.5f, wallCenterZ + segmentOffset),
                new Vector3(wallThickness, corridorHeight, sideSegmentLength),
                material,
                parent);
        }

        float topHeight = Mathf.Max(0f, corridorHeight - openingHeight);
        if (topHeight > 0.001f)
        {
            float topCenterY = openingHeight + topHeight * 0.5f;
            CreateBlock(
                roomName + "_DoorWall_Top",
                new Vector3(wallX, topCenterY, wallCenterZ),
                new Vector3(wallThickness, topHeight, openingWidth),
                material,
                parent);
        }
    }

    private GameObject CreateBlock(string blockName, Vector3 localPosition, Vector3 localScale, Material material, Transform parent)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = blockName;
        block.transform.SetParent(parent, false);
        block.transform.localPosition = localPosition;
        block.transform.localScale = localScale;

        if (material != null)
        {
            Renderer rendererComponent = block.GetComponent<Renderer>();
            if (rendererComponent != null)
            {
                rendererComponent.sharedMaterial = material;
            }
        }

        return block;
    }

    private Transform GetOrCreateGeneratedRoot()
    {
        Transform generatedRoot = transform.Find(GeneratedRootName);
        if (generatedRoot != null)
        {
            return generatedRoot;
        }

        GameObject root = new GameObject(GeneratedRootName);
        root.transform.SetParent(transform, false);
        return root.transform;
    }

    private void ValidateDoorway()
    {
        doorwayWidth = Mathf.Clamp(doorwayWidth, 0.8f, roomDepth - 0.2f);
        doorwayHeight = Mathf.Clamp(doorwayHeight, 1.8f, corridorHeight - 0.2f);
    }

    private void MovePlayerToStart()
    {
        GameObject xrOrigin = GameObject.Find("XR Origin");
        if (xrOrigin == null)
        {
            Debug.LogWarning("[CorridorRoomsGenerator] No se encontró 'XR Origin' para moverlo al inicio del corredor.");
            return;
        }

        Vector3 worldPosition = transform.TransformPoint(playerStartLocalPosition);
        Quaternion worldRotation = transform.rotation * Quaternion.Euler(playerStartLocalEulerAngles);
        xrOrigin.transform.SetPositionAndRotation(worldPosition, worldRotation);
    }
}