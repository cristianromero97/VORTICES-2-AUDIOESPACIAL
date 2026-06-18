using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct SessionData
{
    public string sessionName;
    public int userId;
    public string environmentName;
    public bool isOnlineSession;
    public string displayMode; // Opcional (Plane, Radial, etc.)
    public string browsingMode; // Siempre Online
    public bool volumetric; // Opcional
    public Vector3Int dimension; // X, Y, Z
    public List<string> elementPaths; // Rutas o URLs
    public List<string> audioPaths;  // URLs o rutas de audio (online usa http://)
    public List<string> categories; // Categor�as seleccionadas
    // Sala Environment
    public int minRooms; // N° de salas (determina layout; RoomGeometry es determinista)
    public int maxRooms;
    // Audio
    public int configLevel; // Nivel de configuración HRTF (se propaga a todos los participantes)
    // Perfil acústico override (aplanado — el servidor no tiene AudioManager)
    public bool  hasAcousticOverride;
    public float acousticSpatialBlend;
    public float acousticSpread;
    public float acousticDopplerLevel;
    public int   acousticRolloffMode;   // int (igual que ProfileOverrideData.rolloffMode)
    public bool  acousticSpatialize;
    // Emitter override
    public bool  hasEmitterOverride;
    public float emitterBaseVolume;
    public int   emitterMinConfigLevel;
    public float emitterMinDistance;
    public float emitterMaxDistance;
}



