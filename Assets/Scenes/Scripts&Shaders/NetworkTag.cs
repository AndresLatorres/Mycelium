using UnityEngine;

/// <summary>
/// Un tag de la red, como asset independiente. Se asocia a los TaggedObjectData
/// arrastrando el mismo asset (no escribiendo texto), asi que no hay forma de tipear
/// mal un tag, y renombrar/editar este asset actualiza automaticamente a todos los
/// objetos que lo tienen asociado (todos apuntan a la misma referencia).
/// </summary>
[CreateAssetMenu(fileName = "New Tag", menuName = "Network/Tag")]
public class NetworkTag : ScriptableObject
{
    public string tagName;

    [Tooltip("Opcional: si se asigna, este objeto se instancia justo en el CENTRO de cualquier cluster nuevo generado con este tag (el inicial incluido), en vez de dejar el centro vacio. Si se deja vacio, el cluster funciona como siempre (sin nodo en el centro).")]
    public TaggedObjectData centerNodeOverride;

    public override string ToString() => string.IsNullOrEmpty(tagName) ? name : tagName;
}
