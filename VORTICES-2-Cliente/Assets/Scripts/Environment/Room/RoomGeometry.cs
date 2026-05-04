using UnityEngine;
 
namespace Vortices
{
    /// <summary>
    /// RoomGeometry: Construye la geometría de un corredor lineal con salas laterales.
    ///
    /// LÍMITE DE SALAS:
    ///   roomCount se clampea automáticamente a maxRooms para evitar que valores
    ///   altos generen demasiados GameObjects y congelen la escena. Se emite un
    ///   warning en consola si el valor configurado supera el límite.
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
        [Tooltip("Número de salas a generar. Se clampea a Max Rooms automáticamente.")]
        [SerializeField, Min(1)] private int roomCount = 6;
        [Tooltip("Techo duro de salas permitidas. Protege contra valores accidentalmente altos.")]
        [SerializeField, Min(1)] private int maxRooms = 50;
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
                GenerateScenario();
        }
 
#if UNITY_EDITOR
        private void OnEnable()
        {
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }
 
        private void OnDisable()
        {
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }
 
        private void OnPlayModeChanged(UnityEditor.PlayModeStateChange state)
        {
            // Al salir de Play, regenera automáticamente el escenario en el Editor
            if (state == UnityEditor.PlayModeStateChange.EnteredEditMode)
                RegenerateScenario();
        }
 
        private void OnValidate()
        {
            if (roomCount > maxRooms)
                Debug.LogWarning($"[RoomGeometry] Room Count ({roomCount}) supera Max Rooms ({maxRooms}). " +
                                 "Se usará el límite al generar.", this);
        }
#endif
 
        // ─────────────────────────────────────────────
        //  API pública
        // ─────────────────────────────────────────────
 
        /// <summary>
        /// Calcula el total de objetos que se generarían con el roomCount actual.
        /// Llamado por SalaPanel en el Step 1.
        /// </summary>
        public int GetTotalObjectCount()
        {
            if (furniturePlacer == null) return 0;
            int safeCount = Mathf.Clamp(roomCount, 1, maxRooms);
            return furniturePlacer.CalculateTotalObjects(safeCount);
        }
 
        /// <summary>
        /// Devuelve el conteo de objetos por tipo de prefab para el roomCount actual.
        /// Llamado por SalaPanel en el Step 3.
        /// </summary>
        public System.Collections.Generic.Dictionary<string, int> GetObjectCountsByType()
        {
            if (furniturePlacer == null)
                return new System.Collections.Generic.Dictionary<string, int>();
            int safeCount = Mathf.Clamp(roomCount, 1, maxRooms);
            return furniturePlacer.CalculateObjectCounts(safeCount);
        }
 
        /// <summary>
        /// Aplica roomCount y maxRooms desde el SalaPanel antes de generar.
        /// </summary>
        public void SetRoomConfig(int minRooms, int maxRoomsValue)
        {
            roomCount = Mathf.Max(1, minRooms);
            maxRooms  = Mathf.Max(roomCount, maxRoomsValue);
        }
 
        [ContextMenu("Regenerate Scenario")]
        public void RegenerateScenario()
        {
            ClearGeneratedScenario();
            GenerateScenario();
        }
 
        [ContextMenu("Generate Scenario")]
        public void GenerateScenario()
        {
            ValidateDoorway();
 
            if (clearBeforeGenerate)
                ClearGeneratedScenario();
 
            int safeCount = Mathf.Clamp(roomCount, 1, maxRooms);
            if (safeCount < roomCount)
                Debug.LogWarning($"[RoomGeometry] Room Count recortado de {roomCount} a {safeCount} (Max Rooms).");
 
            Transform generatedRoot = GetOrCreateGeneratedRoot();
            float totalLength = safeCount * corridorSegmentLength;
 
            BuildCorridor(generatedRoot, totalLength, safeCount);
 
            for (int i = 0; i < safeCount; i++)
            {
                float zCenter = (i + 0.5f) * corridorSegmentLength;
                int side = i % 2 == 0 ? 1 : -1;
                BuildRoom(generatedRoot, zCenter, side, i + 1);
            }
 
            if (Application.isPlaying && movePlayerToStartOnGenerate)
                MovePlayerToStart();
        }
 
        [ContextMenu("Clear Generated Scenario")]
        public void ClearGeneratedScenario()
        {
            Transform root = transform.Find(GeneratedRootName);
            if (root == null) return;
 
#if UNITY_EDITOR
            if (!Application.isPlaying) { DestroyImmediate(root.gameObject); return; }
#endif
            Destroy(root.gameObject);
        }
 
        // ─────────────────────────────────────────────
        //  Construcción del corredor
        // ─────────────────────────────────────────────
 
        private void BuildCorridor(Transform parent, float totalLength, int safeRoomCount)
        {
            CreateBlock("CorridorFloor",
                new Vector3(0f, -wallThickness * 0.5f, totalLength * 0.5f),
                new Vector3(corridorWidth, wallThickness, totalLength),
                corridorFloorMaterial, parent);
 
            CreateBlock("CorridorCeiling",
                new Vector3(0f, corridorHeight + wallThickness * 0.5f, totalLength * 0.5f),
                new Vector3(corridorWidth, wallThickness, totalLength),
                corridorCeilingMaterial, parent);
 
            float half = corridorWidth * 0.5f;
            BuildCorridorSideWall(parent, -half, totalLength, -1, safeRoomCount);
            BuildCorridorSideWall(parent,  half, totalLength,  1, safeRoomCount);
 
            Vector3 capScale = new Vector3(corridorWidth, corridorHeight, wallThickness);
            CreateBlock("CorridorStartCap",
                new Vector3(0f, corridorHeight * 0.5f, -wallThickness * 0.5f),
                capScale, corridorWallMaterial, parent);
            CreateBlock("CorridorEndCap",
                new Vector3(0f, corridorHeight * 0.5f, totalLength + wallThickness * 0.5f),
                capScale, corridorWallMaterial, parent);
        }
 
        private void BuildCorridorSideWall(
            Transform parent, float wallX, float totalLength, int side, int safeRoomCount)
        {
            float wallStartZ = 0f;
 
            for (int i = 0; i < safeRoomCount; i++)
            {
                int roomSide = i % 2 == 0 ? 1 : -1;
                if (roomSide != side) continue;
 
                float zCenter       = (i + 0.5f) * corridorSegmentLength;
                float openingStartZ = zCenter - doorwayWidth * 0.5f;
                float openingEndZ   = zCenter + doorwayWidth * 0.5f;
 
                if (openingStartZ > wallStartZ)
                    CreateCorridorWallSegment(parent, wallX, wallStartZ, openingStartZ);
 
                wallStartZ = Mathf.Max(wallStartZ, openingEndZ);
            }
 
            if (wallStartZ < totalLength)
                CreateCorridorWallSegment(parent, wallX, wallStartZ, totalLength);
        }
 
        private void CreateCorridorWallSegment(
            Transform parent, float wallX, float startZ, float endZ)
        {
            float len = endZ - startZ;
            if (len <= 0.001f) return;
 
            CreateBlock("CorridorWallSegment",
                new Vector3(wallX, corridorHeight * 0.5f, startZ + len * 0.5f),
                new Vector3(wallThickness, corridorHeight, len),
                corridorWallMaterial, parent);
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
 
            CreateBlock(roomName + "_Floor",
                new Vector3(xCenter, -wallThickness * 0.5f, zCenter),
                new Vector3(roomWidth, wallThickness, roomDepth),
                roomFloorMaterial, roomRoot.transform);
 
            CreateBlock(roomName + "_Ceiling",
                new Vector3(xCenter, corridorHeight + wallThickness * 0.5f, zCenter),
                new Vector3(roomWidth, wallThickness, roomDepth),
                roomCeilingMaterial, roomRoot.transform);
 
            float outerWallX = xCenter + side * (roomWidth * 0.5f - wallThickness * 0.5f);
            CreateBlock(roomName + "_OuterWall",
                new Vector3(outerWallX, corridorHeight * 0.5f, zCenter),
                new Vector3(wallThickness, corridorHeight, roomDepth),
                roomWallMaterial, roomRoot.transform);
 
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
 
            float doorwayWallX = xCenter - side * (roomWidth * 0.5f - wallThickness * 0.5f);
            BuildDoorwayWall(roomName, roomRoot.transform, doorwayWallX, zCenter,
                             roomDepth, doorwayWidth, doorwayHeight, roomWallMaterial);
 
            if (furniturePlacer != null)
                furniturePlacer.PlaceInRoom(roomRoot.transform, index, side, xCenter, zCenter);
        }
 
        private void BuildDoorwayWall(
            string roomName, Transform parent,
            float wallX, float wallCenterZ,
            float wallLength, float openingWidth, float openingHeight,
            Material material)
        {
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
 
            float topHeight = Mathf.Max(0f, corridorHeight - openingHeight);
            if (topHeight > 0.001f)
                CreateBlock(roomName + "_DoorWall_Top",
                    new Vector3(wallX, openingHeight + topHeight * 0.5f, wallCenterZ),
                    new Vector3(wallThickness, topHeight, openingWidth),
                    material, parent);
        }
 
        // ─────────────────────────────────────────────
        //  Utilidades
        // ─────────────────────────────────────────────
 
        private GameObject CreateBlock(
            string blockName, Vector3 localPos, Vector3 localScale,
            Material mat, Transform parent)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = blockName;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = localPos;
            block.transform.localScale    = localScale;
 
            if (mat != null)
            {
                Renderer r = block.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = mat;
            }
            return block;
        }
 
        private Transform GetOrCreateGeneratedRoot()
        {
            Transform found = transform.Find(GeneratedRootName);
            if (found != null) return found;
 
            GameObject root = new GameObject(GeneratedRootName);
            root.transform.SetParent(transform, false);
 
            // En el Editor, marcamos el objeto con DontSave para que Unity
            // lo destruya automáticamente al entrar a Play mode.
            if (!Application.isPlaying)
                root.hideFlags = HideFlags.DontSave;
 
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
                Debug.LogWarning("[RoomGeometry] No se encontró 'XR Origin'.");
                return;
            }
 
            Vector3 wp;
            Quaternion wr;
 
            if (!TryGetSafeSpawnPose(out wp, out wr))
            {
                wp = transform.TransformPoint(playerStartLocalPosition);
                wr = transform.rotation * Quaternion.Euler(playerStartLocalEulerAngles);
            }
 
            xrOrigin.transform.SetPositionAndRotation(wp, wr);
        }
 
        private bool TryGetSafeSpawnPose(out Vector3 worldPos, out Quaternion worldRot)
        {
            worldPos = Vector3.zero;
            worldRot = Quaternion.identity;
 
            Transform generatedRoot = transform.Find(GeneratedRootName);
            if (generatedRoot == null) return false;
 
            // El corredor lineal no tiene Corridor_0 — buscamos el root directamente
            float sideMargin      = Mathf.Max(0.15f, wallThickness);
            float halfUsableWidth = Mathf.Max(0.1f, corridorWidth * 0.5f - sideMargin);
            float minLocalZ       = sideMargin;
            float maxLocalZ       = Mathf.Max(minLocalZ + 0.1f, corridorSegmentLength - sideMargin);
 
            Vector3 local = playerStartLocalPosition;
            local.x = Mathf.Clamp(local.x, -halfUsableWidth, halfUsableWidth);
            local.z = Mathf.Clamp(local.z, minLocalZ, maxLocalZ);
 
            worldPos = generatedRoot.TransformPoint(local);
            worldRot = generatedRoot.rotation * Quaternion.Euler(playerStartLocalEulerAngles);
            return true;
        }
    }
}