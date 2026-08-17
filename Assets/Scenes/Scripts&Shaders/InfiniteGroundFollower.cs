using UnityEngine;

/// <summary>
/// Hace que el piso parezca infinito: mantiene el plano centrado en X/Z justo debajo
/// del jugador (nunca se aleja lo suficiente como para que se note el borde -- sobre
/// todo si lo combinas con niebla de distancia para tapar el resto), y compensa
/// moviendo el offset de la textura en sentido contrario a la misma velocidad, para
/// que la textura quede "fija" en el mundo aunque el plano se este moviendo.
///
/// Se calcula con la posicion ABSOLUTA del jugador (no acumulando delta cuadro a
/// cuadro), asi que no hay deriva por errores de redondeo aunque juegues horas.
/// </summary>
public class InfiniteGroundFollower : MonoBehaviour
{
    [Tooltip("Si se deja vacio, busca el objeto con tag 'Player'.")]
    public Transform target;

    [Tooltip("Renderer del piso. Si se deja vacio, se usa el de este mismo GameObject.")]
    public Renderer groundRenderer;

    [Tooltip("Cuantas unidades de mundo ocupa UN tile completo de la textura, en X y en Z. Depende del tamano del mesh y del tiling del material -- probalo y ajustalo hasta que la textura no 'viaje'.")]
    public Vector2 textureWorldSize = new Vector2(10f, 10f);

    private Material groundMaterial;
    private float fixedY;

    private void Start()
    {
        if (groundRenderer == null) groundRenderer = GetComponent<Renderer>();
        groundMaterial = groundRenderer.material; // instancia unica, no afecta a otros objetos que usen el mismo material
        fixedY = transform.position.y;

        if (target == null)
        {
            GameObject playerGO = GameObject.FindGameObjectWithTag("Player");
            if (playerGO != null) target = playerGO.transform;
        }
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            GameObject playerGO = GameObject.FindGameObjectWithTag("Player");
            if (playerGO == null) return;
            target = playerGO.transform;
        }

        // El plano sigue al jugador en X/Z, manteniendo su altura original.
        transform.position = new Vector3(target.position.x, fixedY, target.position.z);

        // La textura se desplaza en sentido contrario a la posicion del jugador,
        // asi que aunque el plano se mueva con el, la textura parece quedarse quieta.
        groundMaterial.mainTextureOffset = new Vector2(
            -target.position.x / textureWorldSize.x,
            -target.position.z / textureWorldSize.y
        );
    }
}
