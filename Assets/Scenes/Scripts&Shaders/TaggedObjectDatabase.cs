using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Base de datos central: la lista completa de objetos disponibles para la red.
/// Se arma una sola vez en el editor (arrastrando los TaggedObjectData) y el
/// NetworkGenerator la consulta en tiempo de ejecucion.
/// </summary>
[CreateAssetMenu(fileName = "Tagged Object Database", menuName = "Network/Tagged Object Database")]
public class TaggedObjectDatabase : ScriptableObject
{
    public List<TaggedObjectData> allObjects = new List<TaggedObjectData>();

    public List<TaggedObjectData> GetByTag(NetworkTag tag)
    {
        return allObjects.Where(o => o.HasTag(tag)).ToList();
    }

    public List<NetworkTag> GetAllTags()
    {
        return allObjects.SelectMany(o => o.tags).Distinct().ToList();
    }

    /// <summary>
    /// Devuelve un objeto random que tenga el tag pedido, excluyendo opcionalmente algunos.
    /// </summary>
    public TaggedObjectData GetRandomByTag(NetworkTag tag, List<TaggedObjectData> exclude = null)
    {
        var candidates = GetByTag(tag);
        if (exclude != null && exclude.Count > 0)
            candidates = candidates.Except(exclude).ToList();

        if (candidates.Count == 0) return null;
        return candidates[Random.Range(0, candidates.Count)];
    }
}
