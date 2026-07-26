using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;


public struct SessionCreatedMessage : NetworkMessage
{
    public bool success;
    public string sessionName;
    public int userId;
    public string environmentName;
    public bool isOnlineSession;
    public string displayMode; // Opcional
    public string browsingMode;
    public bool volumetric;
    public Vector3Int dimension;
    public List<string> elementPaths;
    public List<string> audioPaths;  // URLs o rutas de audio (online usa http://)
    public List<string> categories; // Categorías seleccionadas
    // Sala Environment
    public int minRooms;
    public int maxRooms;
    public int roomSeed; // Semilla de shuffle — garantiza misma asignación de audio en todos los clientes
    public List<string> selectedObjectTypes; // Tipos de objeto que emitirán audio (vacío = todos)
    // Audio
    public int configLevel;
    // Perfil acústico override
    public bool  hasAcousticOverride;
    public float acousticSpatialBlend;
    public float acousticSpread;
    public float acousticDopplerLevel;
    public int   acousticRolloffMode;
    public bool  acousticSpatialize;
    // Emitter override
    public bool  hasEmitterOverride;
    public float emitterBaseVolume;
    public int   emitterMinConfigLevel;
    public float emitterMinDistance;
    public float emitterMaxDistance;
}
