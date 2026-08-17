using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Efecto para nodos con imagen: agregar este componente al mismo GameObject que tiene
/// el SpriteRenderer (por ejemplo, en el nodePrefab, o dejar que SpawnedNode lo agregue).
///
/// Hace tres cosas:
///  1. Siempre mira hacia el jugador (billboard).
///  2. Flota suavemente arriba/abajo, con vaiven lateral y de rotacion opcionales, y genera
///     copias translucidas cerca de si -- cada una con su propia fase/velocidad/amplitud de
///     flote, para que se vean como copias oniricas independientes y no como un trail de
///     movimiento repitiendo el mismo camino.
///  3. Si se le asigna un material con el shader "Network/SoftEdgeSprite", difumina los
///     bordes de la imagen (y lo mismo aplica automaticamente a las copias fantasma).
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class FloatingImageEffect : MonoBehaviour
{
    [Header("Mirar al jugador")]
    public bool faceTarget = true;
    [Tooltip("Si se deja vacio, busca el objeto con tag 'Player'. Si no encuentra ninguno, usa la camara principal.")]
    public Transform target;
    [Tooltip("Activar si la imagen queda mirando para el lado contrario al esperado")]
    public bool flip180 = false;

    [Header("Flote vertical")]
    [Tooltip("Cuanto se suma a la altura de spawn como punto central del flote (no es la amplitud, es la altura base)")]
    public float baseHeightOffset = 0f;
    public float floatAmplitude = 0.15f;
    public float floatSpeed = 1f;

    [Header("Vaiven lateral (0 = desactivado)")]
    public float swayAmplitude = 0f;
    public float swaySpeed = 0.7f;

    [Header("Vaiven de rotacion / balanceo (0 = desactivado)")]
    [Tooltip("Grados de inclinacion maxima, pensado para valores chicos (2-6 grados)")]
    public float rotationWobbleDegrees = 3f;
    public float rotationWobbleSpeed = 0.8f;

    [Header("Copias translucidas (efecto onirico)")]
    [Tooltip("Cuantas copias fantasma genera cerca del sprite. 0 = desactivado")]
    public int ghostCount = 2;
    [Range(0f, 1f)] public float ghostAlpha = 0.25f;
    [Tooltip("Cuanto se reduce el alpha de cada copia siguiente (multiplicador)")]
    [Range(0f, 1f)] public float ghostAlphaFalloff = 0.6f;
    [Tooltip("Que tan lejos del original puede aparecer cada copia (desplazamiento fijo aleatorio)")]
    public float ghostSpreadRadius = 0.4f;
    [Tooltip("Variacion aleatoria de velocidad de flote de cada copia respecto al original (0.5 = entre 50% y 150% de la velocidad)")]
    [Range(0f, 1f)] public float ghostSpeedVariation = 0.5f;
    [Tooltip("Variacion aleatoria de amplitud de flote de cada copia respecto al original")]
    [Range(0f, 1f)] public float ghostAmplitudeVariation = 0.5f;

    [Header("Bordes suaves (opcional)")]
    [Tooltip("Material con el shader Network/SoftEdgeSprite. Si se deja vacio, no se toca el material del SpriteRenderer.")]
    public Material softEdgeMaterialOverride;
    [Range(0f, 0.5f)] public float edgeFeather = 0.15f;
    [Range(0f, 1f)] public float edgeShape = 1f;

    private struct GhostData
    {
        public SpriteRenderer renderer;
        public Vector3 baseOffset;      // desplazamiento fijo respecto a basePosition
        public float phase;             // fase propia, independiente del original
        public float speedMultiplier;   // velocidad de flote propia
        public float amplitudeMultiplier; // amplitud de flote propia
    }

    private SpriteRenderer spriteRenderer;
    private Vector3 basePosition;
    private float phaseOffset;

    private readonly List<GhostData> ghosts = new List<GhostData>();
    private bool ghostsBuilt = false;

    private void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        basePosition = transform.position + Vector3.up * baseHeightOffset;
        phaseOffset = Random.Range(0f, Mathf.PI * 2f); // para que no floten todos sincronizados

        if (softEdgeMaterialOverride != null)
        {
            Material matInstance = new Material(softEdgeMaterialOverride);
            matInstance.SetFloat("_FeatherAmount", edgeFeather);
            matInstance.SetFloat("_FeatherShape", edgeShape);
            spriteRenderer.material = matInstance;
        }
    }

    private void LateUpdate()
    {
        if (faceTarget) UpdateFacing();
        UpdateFloatMotion();
        UpdateGhosts();
    }

    // ---------- Mirar al jugador ----------

    private void UpdateFacing()
    {
        if (target == null)
        {
            // La camara es la referencia correcta para un billboard (es literalmente el
            // punto de vista al que tiene que mirar). Usar el Player como respaldo si no
            // hay camara -- pero OJO: si el pivote del Player esta en los pies (comun con
            // CharacterController), el sprite terminaria inclinandose hacia abajo.
            if (Camera.main != null)
            {
                target = Camera.main.transform;
            }
            else
            {
                GameObject playerGO = GameObject.FindGameObjectWithTag("Player");
                if (playerGO != null) target = playerGO.transform;
                else return;
            }
        }

        transform.rotation = ComputeFacingRotation(transform.position, rotationWobbleSpeed, phaseOffset, rotationWobbleDegrees);
    }

    private Quaternion ComputeFacingRotation(Vector3 fromPosition, float wobbleSpeed, float wobblePhase, float wobbleDegrees)
    {
        Vector3 lookDir = fromPosition - target.position;
        if (lookDir.sqrMagnitude < 0.0001f) return transform.rotation;

        Quaternion faceRotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
        if (flip180) faceRotation *= Quaternion.Euler(0f, 180f, 0f);

        float wobble = wobbleDegrees > 0f
            ? Mathf.Sin(Time.time * wobbleSpeed + wobblePhase) * wobbleDegrees
            : 0f;
        Quaternion wobbleRotation = Quaternion.AngleAxis(wobble, Vector3.forward);

        return faceRotation * wobbleRotation;
    }

    // ---------- Flote ----------

    private void UpdateFloatMotion()
    {
        transform.position = basePosition + ComputeFloatOffset(floatSpeed, phaseOffset, floatAmplitude, swaySpeed, swayAmplitude);
    }

    private Vector3 ComputeFloatOffset(float fSpeed, float fPhase, float fAmplitude, float sSpeed, float sAmplitude)
    {
        float bob = Mathf.Sin(Time.time * fSpeed + fPhase) * fAmplitude;

        Vector3 sway = Vector3.zero;
        if (sAmplitude > 0f)
        {
            Vector3 right = transform.right; // relativo a la orientacion actual (ya mirando al jugador)
            sway = right * Mathf.Sin(Time.time * sSpeed + fPhase * 1.3f) * sAmplitude;
        }

        return Vector3.up * bob + sway;
    }

    // ---------- Copias fantasma ----------

    private void BuildGhosts()
    {
        for (int i = 0; i < ghostCount; i++)
        {
            GameObject go = new GameObject($"Ghost_{i}");
            go.transform.SetParent(transform.parent, worldPositionStays: true);
            go.transform.localScale = transform.localScale;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = spriteRenderer.sprite;
            sr.sharedMaterial = spriteRenderer.sharedMaterial; // hereda el shader de bordes suaves si se asigno
            sr.sortingOrder = spriteRenderer.sortingOrder - (i + 1);

            Color c = spriteRenderer.color;
            c.a = ghostAlpha * Mathf.Pow(ghostAlphaFalloff, i);
            sr.color = c;

            ghosts.Add(new GhostData
            {
                renderer = sr,
                baseOffset = Random.insideUnitSphere * ghostSpreadRadius,
                phase = Random.Range(0f, Mathf.PI * 2f),
                speedMultiplier = 1f + Random.Range(-ghostSpeedVariation, ghostSpeedVariation),
                amplitudeMultiplier = 1f + Random.Range(-ghostAmplitudeVariation, ghostAmplitudeVariation)
            });
        }
    }

    private void UpdateGhosts()
    {
        if (ghostCount <= 0) return;

        if (!ghostsBuilt)
        {
            if (spriteRenderer.sprite == null) return; // todavia no se asigno la imagen, esperar
            BuildGhosts();
            ghostsBuilt = true;
        }

        foreach (var ghost in ghosts)
        {
            Vector3 ghostBase = basePosition + ghost.baseOffset;
            Vector3 floatOffset = ComputeFloatOffset(
                floatSpeed * ghost.speedMultiplier, ghost.phase, floatAmplitude * ghost.amplitudeMultiplier,
                swaySpeed * ghost.speedMultiplier, swayAmplitude * ghost.amplitudeMultiplier);

            Vector3 ghostPosition = ghostBase + floatOffset;
            ghost.renderer.transform.position = ghostPosition;

            if (faceTarget && target != null)
            {
                ghost.renderer.transform.rotation = ComputeFacingRotation(
                    ghostPosition, rotationWobbleSpeed * ghost.speedMultiplier, ghost.phase,
                    rotationWobbleDegrees * ghost.amplitudeMultiplier);
            }
            else
            {
                ghost.renderer.transform.rotation = transform.rotation;
            }
        }
    }
}
