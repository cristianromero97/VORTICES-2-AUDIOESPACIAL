using UnityEngine;
using System.Collections.Generic;
 
namespace Vortices
{
    /// <summary>
    /// RoomFurniturePlacer: Única responsabilidad — instanciar y posicionar prefabs de muebles
    /// dentro de una sala generada, respetando reglas de colocación (sala específica, cada N salas,
    /// izquierda/derecha, apilar sobre otro mueble).
    ///
    /// Es llamado por <see cref="CorridorRoomsGenerator"/> una vez construida cada sala.
    /// Delega la inyección de audio a <see cref="FurnitureAudioInjector"/>.
    /// No sabe nada de geometría de pasillos ni de audio.
    /// </summary>
    [DisallowMultipleComponent]
    public class RoomFurniturePlacer : MonoBehaviour
    {
        // ─────────────────────────────────────────────
        //  Data class
        // ─────────────────────────────────────────────
    
        [System.Serializable]
        public class FurniturePlacement
        {
            [Tooltip("Identificador único de este mueble dentro de la sala. " +
                     "Usado por placeOnTopOfId para apilar objetos.")]
            public string id = "Furniture";
    
            [Tooltip("Prefab a instanciar.")]
            public GameObject prefab;
    
            [Tooltip("Posición local relativa al centro de la sala.")]
            public Vector3 localPosition = Vector3.zero;
    
            [Tooltip("Rotación local en ángulos de Euler.")]
            public Vector3 localEulerAngles = Vector3.zero;
    
            [Tooltip("Escala local. Si algún eje es 0, se usa la escala del prefab.")]
            public Vector3 localScale = Vector3.one;
    
            [Tooltip("ID de otro mueble sobre cuya superficie apoyar este. " +
                     "Dejar vacío para posicionar directamente.")]
            public string placeOnTopOfId = string.Empty;
    
            [Tooltip("Separación extra sobre la superficie del mueble base (metros).")]
            [Min(0f)] public float topOffset = 0.02f;
    
            [Tooltip("Si está activo, este mueble solo aparece en las salas indicadas en specificRoomIndices.")]
            public bool useSpecificRooms = false;
    
            [Tooltip("Índices de sala (1-based) en los que colocar este mueble (solo si useSpecificRooms = true).")]
            public List<int> specificRoomIndices = new List<int>();
    
            [Tooltip("Colocar cada cuántas salas (ignorado si useSpecificRooms = true).")]
            [Min(1)] public int placeEveryNRooms = 1;
    
            [Tooltip("¿Colocar en salas del lado izquierdo?")]
            public bool placeOnLeftRooms = true;
    
            [Tooltip("¿Colocar en salas del lado derecho?")]
            public bool placeOnRightRooms = true;
    
            [Tooltip("Configuración de SoundEmitter por sala. Dejar sin overrides para que el mueble no emita sonido.")]
            public FurnitureAudioInjector.FurnitureAudioConfig audio = new FurnitureAudioInjector.FurnitureAudioConfig();
        }
    
        // ─────────────────────────────────────────────
        //  Inspector
        // ─────────────────────────────────────────────
    
        [Header("Muebles")]
        [SerializeField] private List<FurniturePlacement> furniturePlacements = new List<FurniturePlacement>();
    
        [Header("Dependencias")]
        [Tooltip("Referencia al inyector de audio. Puede estar en el mismo GameObject o en otro.")]
        [SerializeField] private FurnitureAudioInjector audioInjector;
    
        // ─────────────────────────────────────────────
        //  API pública — cálculo de objetos
        // ─────────────────────────────────────────────
 
        /// <summary>
        /// Calcula cuántos objetos de cada tipo se instanciarían para un número de salas dado.
        /// Devuelve un diccionario: nombre del prefab → cantidad total.
        /// </summary>
        public Dictionary<string, int> CalculateObjectCounts(int roomCount)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            if (furniturePlacements == null) return counts;
 
            for (int roomIndex = 1; roomIndex <= roomCount; roomIndex++)
            {
                int side = roomIndex % 2 == 0 ? -1 : 1;
                foreach (FurniturePlacement placement in furniturePlacements)
                {
                    if (placement == null || placement.prefab == null) continue;
                    if (!string.IsNullOrWhiteSpace(placement.placeOnTopOfId)) continue;
                    if (!ShouldPlaceInRoom(placement, roomIndex, side)) continue;
 
                    string prefabName = placement.prefab.name;
                    if (!counts.ContainsKey(prefabName))
                        counts[prefabName] = 0;
                    counts[prefabName]++;
                }
            }
            return counts;
        }
 
        /// <summary>
        /// Calcula el total de objetos para un número de salas dado.
        /// </summary>
        public int CalculateTotalObjects(int roomCount)
        {
            int total = 0;
            foreach (var pair in CalculateObjectCounts(roomCount))
                total += pair.Value;
            return total;
        }
 
        // ─────────────────────────────────────────────
        //  API pública (llamada por CorridorRoomsGenerator)
        // ─────────────────────────────────────────────
    
        /// <summary>
        /// Instancia y posiciona los muebles correspondientes a la sala indicada.
        /// </summary>
        /// <param name="roomRoot">Transform raíz de la sala recién construida.</param>
        /// <param name="roomIndex">Número de sala (1-based).</param>
        /// <param name="side">Lado del corredor: 1 = derecha, -1 = izquierda.</param>
        /// <param name="roomCenterX">Centro X de la sala en coordenadas del mundo.</param>
        /// <param name="roomCenterZ">Centro Z de la sala en coordenadas del mundo.</param>
        public void PlaceInRoom(Transform roomRoot, int roomIndex, int side, float roomCenterX, float roomCenterZ)
        {
            if (furniturePlacements == null || furniturePlacements.Count == 0)
            {
                return;
            }
    
            Transform furnitureRoot = new GameObject("Furniture").transform;
            furnitureRoot.SetParent(roomRoot, false);
            furnitureRoot.localPosition = new Vector3(roomCenterX, 0f, roomCenterZ);
    
            Dictionary<string, GameObject> placedById = new Dictionary<string, GameObject>();
            List<FurniturePlacement> placeOnTopQueue  = new List<FurniturePlacement>();
    
            // Primera pasada: muebles que se posicionan directamente
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
    
            // Segunda pasada: muebles que se apilan sobre otros
            foreach (FurniturePlacement placement in placeOnTopQueue)
            {
                GameObject instance = PlaceFurnitureInstance(placement, furnitureRoot, roomIndex, placedById);
                if (instance == null)
                {
                    continue;
                }
    
                if (!placedById.TryGetValue(placement.placeOnTopOfId, out GameObject target))
                {
                    Debug.LogWarning($"[RoomFurniturePlacer] No se encontró el mueble base '{placement.placeOnTopOfId}' " +
                                     $"para apilar '{instance.name}' en Room {roomIndex}.");
                    continue;
                }
    
                SnapInstanceOnTop(instance.transform, target.transform, placement.topOffset);
            }
        }
    
        // ─────────────────────────────────────────────
        //  Lógica interna
        // ─────────────────────────────────────────────
    
        private GameObject PlaceFurnitureInstance(
            FurniturePlacement placement,
            Transform furnitureRoot,
            int roomIndex,
            Dictionary<string, GameObject> placedById)
        {
            GameObject instance = Instantiate(placement.prefab, furnitureRoot);
    
            string id = string.IsNullOrWhiteSpace(placement.id) ? placement.prefab.name : placement.id;
            instance.name = id + "_Room" + roomIndex;
    
            instance.transform.localPosition = placement.localPosition;
            instance.transform.localRotation = Quaternion.Euler(placement.localEulerAngles);
            instance.transform.localScale    = ResolveScale(placement);
    
            placedById[id] = instance;
    
            // Delegar audio al inyector
            if (audioInjector != null)
            {
                audioInjector.InjectAudio(instance, placement.audio, roomIndex);
            }
    
            return instance;
        }
    
        private bool ShouldPlaceInRoom(FurniturePlacement placement, int roomIndex, int side)
        {
            if (placement.useSpecificRooms)
            {
                if (placement.specificRoomIndices == null || placement.specificRoomIndices.Count == 0)
                {
                    return false;
                }
    
                for (int i = 0; i < placement.specificRoomIndices.Count; i++)
                {
                    if (placement.specificRoomIndices[i] == roomIndex)
                    {
                        return true;
                    }
                }
    
                return false;
            }
    
            int everyN = Mathf.Max(1, placement.placeEveryNRooms);
            if (((roomIndex - 1) % everyN) != 0)
            {
                return false;
            }
    
            if (side < 0 && !placement.placeOnLeftRooms)
            {
                return false;
            }
    
            if (side > 0 && !placement.placeOnRightRooms)
            {
                return false;
            }
    
            return true;
        }
    
        private Vector3 ResolveScale(FurniturePlacement placement)
        {
            Vector3 scale = placement.localScale;
    
            bool hasZero = Mathf.Approximately(scale.x, 0f)
                        || Mathf.Approximately(scale.y, 0f)
                        || Mathf.Approximately(scale.z, 0f);
    
            if (!hasZero)
            {
                return scale;
            }
    
            scale = placement.prefab.transform.localScale;
    
            bool prefabAlsoZero = Mathf.Approximately(scale.x, 0f)
                               || Mathf.Approximately(scale.y, 0f)
                               || Mathf.Approximately(scale.z, 0f);
    
            return prefabAlsoZero ? Vector3.one : scale;
        }
    
        private void SnapInstanceOnTop(Transform moving, Transform target, float extraOffset)
        {
            if (!TryGetCombinedBounds(target, out Bounds targetBounds)
             || !TryGetCombinedBounds(moving, out Bounds movingBounds))
            {
                Debug.LogWarning($"[RoomFurniturePlacer] No se pudieron calcular bounds para apilar " +
                                 $"'{moving.name}' sobre '{target.name}'.");
                return;
            }
    
            float yDelta = (targetBounds.max.y - movingBounds.min.y) + Mathf.Max(0f, extraOffset);
            moving.position += new Vector3(0f, yDelta, 0f);
        }
    
        private bool TryGetCombinedBounds(Transform root, out Bounds combined)
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
    }
}