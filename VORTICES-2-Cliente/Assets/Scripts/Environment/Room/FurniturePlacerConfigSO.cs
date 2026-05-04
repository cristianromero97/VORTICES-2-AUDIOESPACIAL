using System.Collections.Generic;
using UnityEngine;

namespace Vortices
{
    /// <summary>
    /// ScriptableObject que actúa como fuente única de verdad para la configuración
    /// de muebles de la Sala Environment. Al ser un asset independiente, puede ser
    /// referenciado tanto por RoomFurniturePlacer (escena Sala) como por SalaPanel
    /// (menú principal), resolviendo el problema de acceso entre escenas.
    /// </summary>
    [CreateAssetMenu(fileName = "FurniturePlacerConfig", menuName = "Vortices/Furniture Placer Config")]
    public class FurniturePlacerConfigSO : ScriptableObject
    {
        [Header("Muebles")]
        public List<RoomFurniturePlacer.FurniturePlacement> furniturePlacements
            = new List<RoomFurniturePlacer.FurniturePlacement>();

        /// <summary>
        /// Calcula cuántos objetos de cada tipo se instanciarían para <paramref name="roomCount"/> salas.
        /// Devuelve un diccionario: nombre del prefab → cantidad total.
        /// </summary>
        public Dictionary<string, int> CalculateObjectCounts(int roomCount)
        {
            var counts = new Dictionary<string, int>();
            if (furniturePlacements == null) return counts;

            for (int roomIndex = 1; roomIndex <= roomCount; roomIndex++)
            {
                int side = roomIndex % 2 == 0 ? -1 : 1;
                foreach (RoomFurniturePlacer.FurniturePlacement p in furniturePlacements)
                {
                    if (p == null || p.prefab == null) continue;
                    if (!ShouldPlaceInRoom(p, roomIndex, side)) continue;

                    string prefabName = p.prefab.name;
                    if (!counts.ContainsKey(prefabName)) counts[prefabName] = 0;
                    counts[prefabName]++;
                }
            }
            return counts;
        }

        /// <summary>
        /// Calcula el total de objetos para <paramref name="roomCount"/> salas.
        /// </summary>
        public int CalculateTotalObjects(int roomCount)
        {
            int total = 0;
            foreach (var pair in CalculateObjectCounts(roomCount))
                total += pair.Value;
            return total;
        }

        private bool ShouldPlaceInRoom(RoomFurniturePlacer.FurniturePlacement p, int roomIndex, int side)
        {
            if (p.useSpecificRooms)
            {
                if (p.specificRoomIndices == null || p.specificRoomIndices.Count == 0) return false;
                for (int i = 0; i < p.specificRoomIndices.Count; i++)
                    if (p.specificRoomIndices[i] == roomIndex) return true;
                return false;
            }

            int everyN = Mathf.Max(1, p.placeEveryNRooms);
            if (((roomIndex - 1) % everyN) != 0) return false;
            if (side < 0 && !p.placeOnLeftRooms) return false;
            if (side > 0 && !p.placeOnRightRooms) return false;
            return true;
        }
    }
}
