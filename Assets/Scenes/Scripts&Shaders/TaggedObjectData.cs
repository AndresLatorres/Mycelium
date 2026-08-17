using UnityEngine;
using System.Collections.Generic;

public enum ContentType { Image, Audio }

/// <summary>
/// Representa un objeto de la red: puede ser una imagen o un audio,
/// y tiene una lista de tags/caracteristicas que lo conectan con otros objetos.
/// </summary>
[CreateAssetMenu(fileName = "New Tagged Object", menuName = "Network/Tagged Object")]
public class TaggedObjectData : ScriptableObject
{
    [Header("Identidad")]
    public string id;
    public ContentType contentType;

    [Header("Contenido")]
    public Sprite image;   // usado si contentType == Image
    public AudioClip audio; // usado si contentType == Audio

    [Header("Tags")]
    [Tooltip("Todas las caracteristicas/tags que tiene este objeto (arrastra assets NetworkTag)")]
    public List<NetworkTag> tags = new List<NetworkTag>();

    [Header("Informacion (opcional)")]
    [Tooltip("Texto que aparece flotando al lado del nodo si el jugador lo mira fijo un rato (requiere el componente GazeInfoDisplay)")]
    [TextArea(2, 5)]
    public string infoText;

    [Header("Opcional")]
    [Tooltip("Si queres que este objeto use un prefab distinto al generico, asignalo aca")]
    public GameObject prefabOverride;

    public bool HasTag(NetworkTag tag)
    {
        return tags.Contains(tag);
    }
}
