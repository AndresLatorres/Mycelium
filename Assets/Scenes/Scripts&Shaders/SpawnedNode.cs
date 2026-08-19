using UnityEngine;
using System;
using System.Collections;

/// <summary>
/// Componente que se agrega automaticamente a cada objeto instanciado por el NetworkGenerator.
/// Se encarga de:
///  - Mostrar el contenido (sprite o audio) del TaggedObjectData asignado.
///  - Detectar por distancia cuando el jugador esta cerca y se queda el tiempo suficiente
///    (no usa triggers de fisica, asi que funciona sin importar como muevas al jugador:
///    drag manual, Rigidbody, CharacterController, NavMesh, etc).
///  - Avisar (evento) cuando eso pasa, para que el NetworkGenerator decida que generar despues.
/// </summary>
public class SpawnedNode : MonoBehaviour
{
    public TaggedObjectData Data { get; private set; }

    /// <summary>
    /// El tag bajo el cual fue generado este nodo (el tag comun del grupo al que pertenece,
    /// o el tag "distinto" que representa si es un nodo conector).
    /// </summary>
    public NetworkTag ClusterTag { get; private set; }

    /// <summary>
    /// Todos los nodos ubicados dentro del MISMO llamado a GenerateCluster comparten
    /// este numero (un "nivel"/tanda de generacion). Se usa para no conectar hermanos
    /// del mismo anillo entre si (ya estan conectados via el centro comun) sin afectar
    /// las conexiones con nodos de una tanda anterior o posterior.
    /// </summary>
    public int GenerationId { get; private set; }

    /// <summary>
    /// Color de la "colonia" a la que pertenece este nodo -- se hereda del nodo/conector
    /// que originó el cluster (o se genera nuevo, con una variacion aleatoria de tono,
    /// si es un origen nuevo sin centro). Se usa para teñir el micelio de este nodo,
    /// asi cada colonia del plato de petri se ve visualmente distinta.
    /// </summary>
    public Color ColonyColor { get; private set; }

    /// <summary>
    /// Centro del grupo/circulo al que pertenece este nodo (o su propia posicion si es un
    /// conector suelto). Se usa para calcular "hacia afuera" correctamente al generar el
    /// siguiente conector, en vez de usar el origen general del NetworkGenerator.
    /// </summary>
    public Vector3 ClusterCenter { get; private set; }

    /// <summary>
    /// True si este nodo fue instanciado individualmente como "puente" entre dos grupos
    /// (en vez de ser parte de un anillo circular).
    /// </summary>
    public bool IsConnector { get; private set; }

    [Header("Deteccion de proximidad (por distancia, sin fisica)")]
    public float dwellTimeRequired = 2f;
    public float triggerRadius = 2f;

    [Header("Aparicion suave")]
    [Tooltip("Cuanto tarda el nodo en aparecer (escala + fundido) apenas se crea")]
    public float appearDuration = 0.6f;

    public event Action<SpawnedNode> OnDwellComplete;

    // Se busca una sola vez y se comparte entre todos los nodos.
    private static Transform playerTransform;

    private bool playerInside = false;
    private float dwellTimer = 0f;
    private bool alreadyTriggered = false;

    private SpriteRenderer spriteRenderer;
    private AudioSource audioSource;

    public void Initialize(TaggedObjectData data, NetworkTag clusterTag, Vector3 clusterCenter, int generationId, Color colonyColor)
    {
        Data = data;
        ClusterTag = clusterTag;
        ClusterCenter = clusterCenter;
        GenerationId = generationId;
        ColonyColor = colonyColor;
        ApplyVisuals();
        StartCoroutine(AppearRoutine());
    }

    /// <summary>
    /// Permite que el NetworkGenerator configure estos valores desde un solo lugar
    /// en vez de tener que editarlos en cada prefab.
    /// </summary>
    public void ConfigureApproach(float radius, float dwellTime)
    {
        triggerRadius = radius;
        dwellTimeRequired = dwellTime;
    }

    /// <summary>
    /// Igual que ConfigureApproach: permite que el NetworkGenerator fije este valor desde
    /// un solo lugar, incluso en prefabs que no tenian SpawnedNode y se lo agregan recien
    /// en tiempo de ejecucion (esos siempre nacerian con el default si no se llama esto).
    /// </summary>
    public void ConfigureAppearance(float duration)
    {
        appearDuration = duration;
    }

    public void MarkAsConnector()
    {
        IsConnector = true;
    }

    /// <summary>
    /// Anima el nodo desde escala 0 / alpha 0 hasta su tamano y color reales, para que
    /// aparezca suavemente en vez de aparecer de golpe. Funciona igual para nodos con
    /// imagen (fundido + escala) y con audio (solo escala, ya que no tienen renderer).
    /// </summary>
    private IEnumerator AppearRoutine()
    {
        Vector3 targetScale = transform.localScale;
        transform.localScale = Vector3.zero;

        bool hasSprite = spriteRenderer != null;
        Color targetColor = hasSprite ? spriteRenderer.color : Color.white;
        if (hasSprite)
        {
            Color startColor = targetColor;
            startColor.a = 0f;
            spriteRenderer.color = startColor;
        }

        float elapsed = 0f;
        while (elapsed < appearDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / appearDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f); // ease-out cubico, se siente mas organico que lineal

            transform.localScale = targetScale * eased;

            if (hasSprite)
            {
                Color c = targetColor;
                c.a = targetColor.a * eased;
                spriteRenderer.color = c;
            }

            yield return null;
        }

        transform.localScale = targetScale;
        if (hasSprite) spriteRenderer.color = targetColor;
    }

    private void ApplyVisuals()
    {
        if (Data.contentType == ContentType.Image && Data.image != null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null) spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = Data.image;
        }
        else if (Data.contentType == ContentType.Audio && Data.audio != null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.clip = Data.audio;
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f; // audio 3D, opcional segun tu proyecto
        }
    }

    private void Update()
    {
        if (alreadyTriggered) return;

        if (playerTransform == null)
        {
            GameObject playerGO = GameObject.FindGameObjectWithTag("Player");
            if (playerGO == null) return; // todavia no hay Player en la escena
            playerTransform = playerGO.transform;
        }

        float distance = Vector3.Distance(transform.position, playerTransform.position);
        bool isInsideNow = distance <= triggerRadius;

        if (isInsideNow && !playerInside && audioSource != null)
        {
            audioSource.Play(); // el jugador acaba de entrar en el radio
        }

        playerInside = isInsideNow;

        if (playerInside)
        {
            dwellTimer += Time.deltaTime;
            if (dwellTimer >= dwellTimeRequired)
            {
                alreadyTriggered = true;
                OnDwellComplete?.Invoke(this);
            }
        }
        else if (dwellTimer > 0f)
        {
            dwellTimer = 0f;
        }
    }
}
