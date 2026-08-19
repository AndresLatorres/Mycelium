using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Genera la red de objetos:
///  1. Crea un grupo circular de objetos que comparten un tag comun.
///  2. Cuando el jugador se queda cerca de uno de ellos, instancia UN objeto "conector"
///     fuera del grupo, que comparte con el nodo otro de sus tags (no el del grupo).
///  3. Cuando el jugador se acerca a ese conector, se genera un nuevo grupo circular
///     centrado ahi, usando ese tag distinto como el nuevo tag comun.
///  4. Se repite indefinidamente, formando una red interconectada de clusters.
///
/// Ademas mantiene un registro global de todos los nodos para:
///  - Evitar que nodos nuevos se superpongan con nodos existentes: si comparten un tag,
///    reutiliza el nodo existente en vez de duplicar (asi la red se "reconecta" en el
///    punto de choque en vez de solaparse); si no comparten tag, reubica el nuevo nodo.
///  - Orientar la seleccion de objetos de un cluster nuevo hacia hubs vecinos cercanos,
///    para que el objeto que "mira" hacia otro hub comparta tag con el de ese hub.
///  - Dibujar hilos de micelio entre CUALQUIER par de nodos cercanos que compartan tag,
///    sin importar si fueron generados juntos o en momentos distintos.
/// </summary>
public class NetworkGenerator : MonoBehaviour
{
    [Header("Datos")]
    public TaggedObjectDatabase database;

    [Header("Prefab generico (debe tener SpawnedNode, o se le agrega solo)")]
    public GameObject nodePrefab;

    [Header("Configuracion del grupo circular")]
    public int objectsPerCluster = 6;
    public float clusterRadius = 5f;

    [Header("Configuracion del conector")]
    [Tooltip("Que tan lejos del nodo de origen aparece el objeto puente")]
    public float connectorDistance = 8f;

    [Header("Punto de partida")]
    public NetworkTag startingTag;
    public Vector3 startingPosition = Vector3.zero;

    [Header("Deteccion de acercamiento (se aplica a TODOS los nodos desde aca)")]
    [Tooltip("Que tan cerca (distancia en unidades) tiene que estar el jugador de un nodo para que empiece a contar el tiempo de espera")]
    public float approachRadius = 2f;
    [Tooltip("Cuanto tiempo (segundos) se tiene que quedar cerca el jugador para que se genere una rama nueva")]
    public float approachDwellTime = 2f;

    [Header("Aparicion suave (se aplica a TODOS los nodos desde aca, incluso a los que usan Prefab Override)")]
    public float appearDuration = 0.6f;

    [Header("Deteccion de mirada / texto flotante (se aplica a TODOS los nodos, salvo los que tengan 'Use Custom Settings' tildado en su GazeInfoDisplay)")]
    [Tooltip("Angulo maximo (grados) para que el texto APAREZCA por primera vez")]
    public float gazeAngleThreshold = 10f;
    [Tooltip("Angulo maximo (grados) para MANTENER el texto visible una vez que ya aparecio (mas amplio, asi no se pierde por mirar el texto en vez del nodo)")]
    public float gazeAngleThresholdWhileVisible = 25f;
    [Tooltip("Distancia maxima a la que cuenta la mirada")]
    public float gazeMaxDistance = 15f;
    [Tooltip("Cuanto tiempo (segundos) hay que sostener la mirada para que aparezca el texto")]
    public float gazeDwellTime = 1.5f;
    [Tooltip("Tiempo de espera para que REAPAREZCA despues de la primera vez que ya se mostro (mas corto)")]
    public float gazeDwellTimeAfterFirstShow = 0.4f;

    [Header("Texto flotante - estilo (misma regla: se pisa salvo Use Custom Settings)")]
    public float gazeFontSize = 3f;
    public Color gazeTextColor = Color.white;
    public float gazeFadeDuration = 0.3f;
    [Tooltip("Relativo a la camara: x = derecha/izquierda, y = arriba/abajo, z = adelante/atras")]
    public Vector3 gazeTextOffset = new Vector3(0.8f, 0.3f, 0f);
    [Tooltip("Ancho maximo antes de pasar a la siguiente linea")]
    public float gazeTextMaxWidth = 4f;
    public bool gazeTextWordWrap = true;

    [Header("Texto flotante - fondo (misma regla: se pisa salvo Use Custom Settings)")]
    public bool gazeShowBackground = true;
    [Tooltip("Color del fondo. El alpha define la opacidad maxima (ej 0.55 = semi-transparente)")]
    public Color gazeBackgroundColor = new Color(0f, 0f, 0f, 0.55f);
    public float gazeBackgroundPadding = 0.15f;
    public float gazeBackgroundZOffset = 0.02f;
    [Tooltip("Cuanto se difumina el borde del fondo (0 = corte duro, 0.5 = maximo)")]
    [Range(0f, 0.5f)]
    public float gazeBackgroundFeatherAmount = 0.3f;
    [Tooltip("Forma del difuminado del fondo (0 = rectangular con esquinas suaves, 1 = ovalada)")]
    [Range(0f, 1f)]
    public float gazeBackgroundFeatherShape = 0.35f;

    [Header("Camino de micelio (visual)")]
    [Tooltip("Prefab vacio con el componente MyceliumLink y su material asignado. Si se deja vacio, no se dibujan caminos.")]
    public GameObject myceliumLinkPrefab;

    [Header("Brotes de micelio del origen (aspecto 'colonia en plato de petri')")]
    [Tooltip("Cuantos hilos cortos SIN conectar a ningun nodo se generan desde el centro de CADA cluster nuevo, repartidos en todas direcciones. Asi el origen se ve como una colonia creciendo tenga 1 nodo conectado o 'objectsPerCluster' -- no dependen de cuantos nodos se lograron ubicar. 0 = desactivado")]
    public int petriSproutCount = 6;
    [Tooltip("Largo minimo de cada brote decorativo")]
    public float petriSproutLengthMin = 1f;
    [Tooltip("Largo maximo de cada brote decorativo")]
    public float petriSproutLengthMax = 2.5f;
    [Tooltip("Cuanto puede variar (grados) cada brote respecto a su angulo parejo alrededor del circulo, para que no se vea mecanico")]
    public float petriSproutAngleJitter = 15f;

    [Header("Color por colonia (cada origen nuevo tiene un tinte ligeramente distinto)")]
    [Tooltip("Cuanto puede variar el TONO (hue) del micelio de cada colonia nueva respecto al color original del material del prefab. 0 = todas iguales, 0.5 = variacion maxima")]
    [Range(0f, 0.5f)] public float colonyHueVariation = 0.08f;
    [Tooltip("Cuanto puede variar la saturacion de cada colonia nueva (+/- este valor)")]
    [Range(0f, 0.5f)] public float colonySaturationVariation = 0.1f;

    [Header("Evitar superposicion")]
    [Tooltip("Distancia minima entre dos nodos. Si un nodo nuevo cae mas cerca que esto de uno existente, se reutiliza (si comparten tag) o se reubica (si no).")]
    public float minNodeSeparation = 1.8f;
    [Tooltip("Cuantas veces intenta reubicar un nodo antes de rendirse y saltearlo")]
    public int maxPlacementAttempts = 6;

    [Header("Reconexion con nodos existentes")]
    [Tooltip("Radio de busqueda de nodos 'ancla' cercanos, usado para orientar la ubicacion de un cluster nuevo hacia hubs vecinos")]
    public float anchorSearchRadius = 15f;
    [Tooltip("Distancia maxima para conectar automaticamente (con hilo de micelio) dos nodos que comparten tag, aunque no se hayan generado en el mismo momento")]
    public float adjacencyLinkRadius = 6f;

    [Header("Nuevo origen si el jugador se aleja demasiado")]
    [Tooltip("Si el nodo mas cercano a el jugador queda mas lejos que esto, se genera un cluster nuevo cerca suyo con un tag al azar")]
    public float wanderDistanceThreshold = 30f;
    [Tooltip("Cada cuantos segundos se revisa la distancia al jugador (no hace falta revisarlo todos los frames)")]
    public float wanderCheckInterval = 3f;
    [Tooltip("Que tan adelante del jugador (en la direccion que esta mirando) aparece el nuevo origen, en vez de encima suyo")]
    public float newOriginAheadDistance = 5f;

    // Tags que ya tienen un cluster generado, para no duplicar grupos sobre el mismo tag.
    private readonly HashSet<NetworkTag> visitedTags = new HashSet<NetworkTag>();

    // Registro global de todos los nodos instanciados (para deteccion de cercania/superposicion).
    private readonly List<SpawnedNode> allNodes = new List<SpawnedNode>();

    // Pares de nodos que ya tienen un hilo de micelio entre ellos, para no duplicar el link.
    private readonly HashSet<string> existingLinks = new HashSet<string>();

    // Para cada nodo, el MyceliumLink cuya punta (extremo "to") llega hasta el. Cuando un
    // hilo nuevo arranca DESDE ese nodo, el hilo aca guardado deja de ser una punta suelta
    // y se aplana.
    private readonly Dictionary<SpawnedNode, MyceliumLink> incomingLinkAtNode = new Dictionary<SpawnedNode, MyceliumLink>();

    private Transform wanderPlayerTransform;
    private float wanderCheckTimer = 0f;

    // Se incrementa una vez por cada GenerateCluster y una vez por cada conector suelto
    // -- identifica la "tanda" de generacion de cada nodo (ver SpawnedNode.GenerationId).
    private int nextGenerationId = 0;

    private Color originalMyceliumColor = Color.white;
    private bool originalMyceliumColorCached = false;

    private void Start()
    {
        if (database == null || nodePrefab == null)
        {
            Debug.LogError("NetworkGenerator: falta asignar 'database' o 'nodePrefab'.");
            return;
        }

        if (startingTag != null)
        {
            GenerateCluster(startingTag, startingPosition, null, null);
        }
    }

    private void Update()
    {
        wanderCheckTimer += Time.deltaTime;
        if (wanderCheckTimer < wanderCheckInterval) return;
        wanderCheckTimer = 0f;

        CheckIfPlayerIsWanderingAlone();
    }

    /// <summary>
    /// Si el jugador quedo mas lejos de "wanderDistanceThreshold" de CUALQUIER nodo
    /// existente, genera un cluster nuevo cerca suyo con un tag al azar -- asi nunca
    /// se queda caminando hacia la nada sin nada que encontrar.
    /// </summary>
    private void CheckIfPlayerIsWanderingAlone()
    {
        if (wanderPlayerTransform == null)
        {
            GameObject playerGO = GameObject.FindGameObjectWithTag("Player");
            if (playerGO == null) return;
            wanderPlayerTransform = playerGO.transform;
        }

        if (allNodes.Count > 0)
        {
            float nearestDistance = float.MaxValue;
            foreach (var node in allNodes)
            {
                if (node == null) continue;
                float dist = Vector3.Distance(node.transform.position, wanderPlayerTransform.position);
                if (dist < nearestDistance) nearestDistance = dist;
            }

            if (nearestDistance <= wanderDistanceThreshold) return; // hay algo cerca, no hace falta nada
        }

        Vector3 spawnPosition = wanderPlayerTransform.position + GetPlayerForwardFlat() * newOriginAheadDistance;
        GenerateNewRandomOrigin(spawnPosition);
    }

    /// <summary>
    /// Direccion "hacia adelante" del jugador, aplanada en el plano X/Z (ignora si esta
    /// mirando hacia arriba/abajo), para que el nuevo origen quede sobre el piso.
    /// </summary>
    private Vector3 GetPlayerForwardFlat()
    {
        Vector3 forward = wanderPlayerTransform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f) forward = wanderPlayerTransform.up; // caso raro: mirando derecho hacia arriba/abajo
        forward.y = 0f;

        return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
    }

    /// <summary>
    /// Genera un cluster nuevo en "position" usando un tag elegido al azar. Prefiere un tag
    /// que todavia no se haya usado en la red; si ya se usaron todos, repite uno al azar
    /// (en un lugar nuevo, asi que no es un problema que se repita).
    /// </summary>
    private void GenerateNewRandomOrigin(Vector3 position)
    {
        if (database == null) return;

        List<NetworkTag> allTags = database.GetAllTags();
        if (allTags.Count == 0) return;

        List<NetworkTag> freshTags = allTags.Where(t => !visitedTags.Contains(t)).ToList();
        bool repeatingTag = freshTags.Count == 0;
        NetworkTag chosenTag = repeatingTag
            ? allTags[Random.Range(0, allTags.Count)]
            : freshTags[Random.Range(0, freshTags.Count)];

        GenerateCluster(chosenTag, position, null, null, forceEvenIfVisited: repeatingTag);
    }

    /// <summary>
    /// Instancia un grupo circular de objetos que comparten "tag" alrededor de "center".
    /// "sourceData" (opcional) es el objeto que origino este grupo, para excluirlo de la seleccion.
    /// "centerNode" (opcional) es el nodo conector que esta en el centro, para conectar el
    /// anillo con el hilo de micelio; si es null, es un origen nuevo de verdad (cluster
    /// inicial, o generado porque el jugador se alejo) -- en ese caso, si "tag" tiene un
    /// NetworkTag.centerNodeOverride asignado, ESE objeto se instancia en el centro (ver
    /// mas abajo); si no, el centro queda vacio como antes.
    /// </summary>
    public void GenerateCluster(NetworkTag tag, Vector3 center, TaggedObjectData sourceData, SpawnedNode centerNode, bool forceEvenIfVisited = false)
    {
        if (visitedTags.Contains(tag) && !forceEvenIfVisited) return;
        visitedTags.Add(tag);

        // Todos los nodos de este anillo comparten "nivel" (para no conectarse entre si
        // en LinkToNearbySharedTagNodes) y color de colonia: si hay centerNode, es una
        // CONTINUACION de una colonia existente (hereda su color); si no, es un origen
        // nuevo (cluster inicial, o uno generado porque el jugador se alejo demasiado) y
        // le toca un color nuevo, con una variacion aleatoria de tono.
        int generationId = nextGenerationId++;
        Color colonyColor = centerNode != null ? centerNode.ColonyColor : GenerateColonyColor();

        // Si el tag tiene un objeto especial para el centro (NetworkTag.centerNodeOverride)
        // Y este cluster es un origen nuevo de verdad (sin centerNode existente -- el
        // cluster inicial, o uno generado porque el jugador se alejo), se instancia ESE
        // objeto especial justo en "center". De ahi en mas se lo trata como el centro real
        // del cluster: los hilos de micelio del anillo se conectan a el como a un nodo
        // real (CreateMyceliumLink), no a un punto vacio. Si el tag no tiene override, o
        // ya habia un centerNode (cluster generado alrededor de un conector existente),
        // sigue funcionando exactamente igual que antes.
        SpawnedNode effectiveCenterNode = centerNode;
        if (effectiveCenterNode == null && tag.centerNodeOverride != null)
        {
            effectiveCenterNode = PlaceNode(tag.centerNodeOverride, tag, center, center, isConnector: false, generationId, colonyColor);
        }

        List<TaggedObjectData> pool = database.GetByTag(tag);
        if (sourceData != null)
            pool = pool.Where(o => o != sourceData).ToList();
        if (tag.centerNodeOverride != null)
            pool = pool.Where(o => o != tag.centerNodeOverride).ToList(); // no lo sumes de nuevo como nodo del anillo

        if (pool.Count == 0)
        {
            Debug.LogWarning($"NetworkGenerator: no hay objetos con el tag '{tag}'.");
            return;
        }

        int count = Mathf.Min(objectsPerCluster, pool.Count);
        List<TaggedObjectData> selection = pool.OrderBy(_ => Random.value).Take(count).ToList();

        // Para cada "slot" del anillo, si hay un hub vecino en esa direccion, se prioriza
        // un objeto que comparta tag con el nodo de ese hub -> el puente se arma solo,
        // en vez de que dos clusters distintos terminen superpuestos sin relacion.
        for (int i = 0; i < selection.Count; i++)
        {
            float angle = (360f / selection.Count) * i;
            Vector3 dir = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad));

            SpawnedNode anchor = FindAnchorInDirection(center, dir, anchorSearchRadius);
            if (anchor == null) continue;

            TaggedObjectData better = pool.FirstOrDefault(o =>
                !selection.Contains(o) && o.tags.Intersect(anchor.Data.tags).Any());

            if (better != null) selection[i] = better;
        }

        for (int i = 0; i < selection.Count; i++)
        {
            float angle = (360f / selection.Count) * i;
            Vector3 offset = new Vector3(
                Mathf.Cos(angle * Mathf.Deg2Rad),
                0f,
                Mathf.Sin(angle * Mathf.Deg2Rad)
            ) * clusterRadius;

            Vector3 desiredPos = center + offset;
            SpawnedNode placed = PlaceNode(selection[i], tag, desiredPos, center, isConnector: false, generationId, colonyColor);
            if (placed == null) continue; // no se pudo ubicar sin superponerse, se salteo

            if (effectiveCenterNode != null)
            {
                CreateMyceliumLink(effectiveCenterNode, placed);
            }
            else
            {
                // No hay ningun nodo real en el centro (ni centerNode existente, ni
                // centerNodeOverride para este tag) -- igual registramos el hilo para
                // que, si "placed" mas adelante genera un conector, este hilo se
                // aplane en vez de quedar redondeado para siempre.
                MyceliumLink rootLink = CreateMyceliumLinkAt(center, placed.transform.position, colonyColor);
                if (rootLink != null) incomingLinkAtNode[placed] = rootLink;
            }

            LinkToNearbySharedTagNodes(placed);
        }

        SpawnPetriSprouts(center, colonyColor);
    }

    /// <summary>
    /// Genera hilos de micelio cortos SIN conectar a ningun nodo real, repartidos en
    /// todas direcciones alrededor de "center". Se llama al final de CADA cluster
    /// generado (tenga 1 nodo conectado o "objectsPerCluster"), asi el origen siempre
    /// se ve como una colonia creciendo en un plato de petri, independientemente de
    /// cuantos nodos se lograron ubicar. No se registran en "incomingLinkAtNode" (no
    /// hay ningun SpawnedNode en la punta) -- se quedan con la punta redondeada para
    /// siempre, que es lo correcto para un brote que no continua a ningun lado.
    /// </summary>
    private void SpawnPetriSprouts(Vector3 center, Color colonyColor)
    {
        if (petriSproutCount <= 0 || myceliumLinkPrefab == null) return;

        for (int i = 0; i < petriSproutCount; i++)
        {
            float baseAngle = (360f / petriSproutCount) * i;
            float angle = baseAngle + Random.Range(-petriSproutAngleJitter, petriSproutAngleJitter);
            float length = Random.Range(petriSproutLengthMin, petriSproutLengthMax);

            Vector3 dir = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad));
            CreateMyceliumLinkAt(center, center + dir * length, colonyColor);
        }
    }

    /// <summary>
    /// Intenta ubicar (o reutilizar) un nodo cerca de "desiredPosition".
    ///  - Si esa posicion esta libre, instancia el nodo ahi.
    ///  - Si esta demasiado cerca de un nodo existente que COMPARTE un tag, no crea uno
    ///    nuevo: devuelve el existente (la union pasa aca mismo, en el momento de crear
    ///    este nodo -- no despues, no en otro llamado).
    ///  - Si esta demasiado cerca de uno que NO comparte tag, empuja la posicion hacia
    ///    afuera y reintenta unas cuantas veces antes de rendirse.
    /// </summary>
    private SpawnedNode PlaceNode(TaggedObjectData data, NetworkTag clusterTag, Vector3 desiredPosition, Vector3 clusterCenter, bool isConnector, int generationId, Color colonyColor)
    {
        Vector3 position = desiredPosition;

        for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
        {
            SpawnedNode conflict = FindNodeWithin(position, minNodeSeparation);
            if (conflict == null)
            {
                return SpawnNode(data, clusterTag, position, clusterCenter, isConnector, generationId, colonyColor);
            }

            if (conflict.Data.tags.Intersect(data.tags).Any())
            {
                // Mismo lugar, tag en comun: no duplicamos, reconectamos con el nodo existente.
                return conflict;
            }

            // No comparten tag: empujamos la posicion hacia afuera del conflicto y reintentamos.
            Vector3 pushDir = position - conflict.transform.position;
            if (pushDir.sqrMagnitude < 0.0001f) pushDir = Random.insideUnitSphere;
            pushDir.y = 0f;
            pushDir = pushDir.sqrMagnitude > 0.0001f ? pushDir.normalized : Vector3.right;
            position += pushDir * minNodeSeparation;
        }

        Debug.LogWarning($"NetworkGenerator: no se pudo ubicar '{data.id}' sin superponerse, se salteo.");
        return null;
    }

    private SpawnedNode SpawnNode(TaggedObjectData data, NetworkTag clusterTag, Vector3 position, Vector3 clusterCenter, bool isConnector, int generationId, Color colonyColor)
    {
        GameObject prefabToUse = data.prefabOverride != null ? data.prefabOverride : nodePrefab;
        GameObject go = Instantiate(prefabToUse, position, Quaternion.identity, transform);
        go.name = (isConnector ? "Connector_" : "Node_") + data.id;

        SpawnedNode node = go.GetComponent<SpawnedNode>();
        if (node == null) node = go.AddComponent<SpawnedNode>();

        node.Initialize(data, clusterTag, clusterCenter, generationId, colonyColor);
        if (isConnector) node.MarkAsConnector();
        node.ConfigureApproach(approachRadius, approachDwellTime);
        node.ConfigureAppearance(appearDuration);

        GazeInfoDisplay gaze = go.GetComponent<GazeInfoDisplay>();
        if (gaze == null) gaze = go.AddComponent<GazeInfoDisplay>();
        gaze.ConfigureGaze(gazeAngleThreshold, gazeAngleThresholdWhileVisible, gazeMaxDistance, gazeDwellTime, gazeDwellTimeAfterFirstShow);
        gaze.ConfigureText(gazeFontSize, gazeTextColor, gazeFadeDuration, gazeTextOffset, gazeTextMaxWidth, gazeTextWordWrap);
        gaze.ConfigureBackground(gazeShowBackground, gazeBackgroundColor, gazeBackgroundPadding, gazeBackgroundZOffset, gazeBackgroundFeatherAmount, gazeBackgroundFeatherShape);

        node.OnDwellComplete += HandleNodeApproached;

        allNodes.Add(node);
        return node;
    }

    /// <summary>
    /// Se dispara cuando el jugador permanece el tiempo suficiente cerca de un nodo.
    /// </summary>
    private void HandleNodeApproached(SpawnedNode node)
    {
        node.OnDwellComplete -= HandleNodeApproached; // evita disparos multiples sobre el mismo nodo

        // Tags candidatos: los del objeto, salvo el tag por el cual ya fue agrupado/generado.
        List<NetworkTag> candidateTags = node.Data.tags.Where(t => t != node.ClusterTag).ToList();
        if (candidateTags.Count == 0) return; // este objeto no tiene por donde seguir expandiendo la red

        NetworkTag chosenTag = candidateTags[Random.Range(0, candidateTags.Count)];

        if (node.IsConnector)
        {
            // Ya era un puente: al acercarse, se genera el nuevo grupo centrado en su posicion.
            GenerateCluster(chosenTag, node.transform.position, node.Data, node);
        }
        else
        {
            // Es parte de un grupo circular: al acercarse, se genera UN solo objeto conector afuera.
            SpawnConnector(node, chosenTag);
        }
    }

    private void SpawnConnector(SpawnedNode originNode, NetworkTag tag)
    {
        TaggedObjectData connectorData = database.GetRandomByTag(
            tag, new List<TaggedObjectData> { originNode.Data });

        if (connectorData == null)
        {
            Debug.LogWarning($"NetworkGenerator: no se encontro objeto para el tag conector '{tag}'.");
            return;
        }

        // "Hacia afuera" se calcula desde el centro REAL del cluster al que pertenece
        // originNode, no desde el transform del generador -- asi el conector siempre
        // se aleja del anillo en vez de, a veces, apuntar para cualquier lado.
        Vector3 direction = (originNode.transform.position - originNode.ClusterCenter).normalized;
        if (direction == Vector3.zero) direction = Random.insideUnitSphere.normalized;

        Vector3 desiredPos = originNode.transform.position + direction * connectorDistance;

        // Un conector no es un origen nuevo -- es la MISMA colonia extendiendose, asi
        // que hereda su color (no genera uno nuevo al azar). Si le toca un "nivel"
        // propio (nextGenerationId++), asi el conector nunca cuenta como "hermano del
        // mismo anillo" de nada -- es un nodo suelto, no parte de un anillo circular.
        int generationId = nextGenerationId++;

        // PlaceNode decide aca mismo, en el momento de crear el nodo, si hace falta uno
        // nuevo o si ya hay uno existente con tag en comun para reutilizar/unir --
        // no se pospone para un llamado posterior.
        SpawnedNode placed = PlaceNode(connectorData, tag, desiredPos, desiredPos, isConnector: true, generationId, originNode.ColonyColor);
        if (placed == null) return;

        CreateMyceliumLink(originNode, placed);
        LinkToNearbySharedTagNodes(placed);
    }

    // ---------- Busquedas espaciales ----------

    private SpawnedNode FindNodeWithin(Vector3 position, float radius)
    {
        foreach (var node in allNodes)
        {
            if (node == null) continue; // por si fue destruido
            if (Vector3.Distance(node.transform.position, position) < radius)
                return node;
        }
        return null;
    }

    /// <summary>
    /// Busca el nodo existente mas cercano que quede aproximadamente en "direction" desde
    /// "origin" (usado para orientar un cluster nuevo hacia un hub vecino).
    /// </summary>
    private SpawnedNode FindAnchorInDirection(Vector3 origin, Vector3 direction, float maxDistance)
    {
        SpawnedNode best = null;
        float bestDot = 0.7f; // tolerancia angular (~45 grados)
        float bestDist = maxDistance;

        foreach (var node in allNodes)
        {
            if (node == null) continue;
            Vector3 toNode = node.transform.position - origin;
            float dist = toNode.magnitude;
            if (dist < 0.01f || dist > maxDistance) continue;

            float dot = Vector3.Dot(toNode.normalized, direction);
            if (dot > bestDot && dist <= bestDist)
            {
                best = node;
                bestDist = dist;
                bestDot = dot;
            }
        }
        return best;
    }

    /// <summary>
    /// Conecta "newNode" con cualquier nodo cercano que comparta al menos un tag,
    /// sin importar cuando fue generado ese otro nodo.
    /// </summary>
    private void LinkToNearbySharedTagNodes(SpawnedNode newNode)
    {
        foreach (var other in allNodes)
        {
            if (other == null || other == newNode) continue;

            // Nodos del MISMO anillo/tanda de generacion ya estan conectados via el
            // centro comun (o via un conector) -- no hace falta (ni se quiere) una
            // conexion lateral extra entre "hermanos". Si son de tandas distintas
            // (una anterior o posterior), la conexion lateral si debe crearse.
            if (other.GenerationId == newNode.GenerationId) continue;

            float dist = Vector3.Distance(other.transform.position, newNode.transform.position);
            if (dist > adjacencyLinkRadius) continue;

            if (!other.Data.tags.Intersect(newNode.Data.tags).Any()) continue;

            // isChainContinuation: false -- esta es una conexion lateral/retroactiva,
            // no forma parte de la cadena de crecimiento principal, asi que no debe
            // aplanar ni registrar puntas.
            CreateMyceliumLink(other, newNode, isChainContinuation: false);
        }
    }

    // ---------- Color de colonia ----------

    /// <summary>
    /// Lee el _Color original del material del prefab de micelio, una sola vez, para
    /// usarlo como base de la variacion aleatoria de cada colonia nueva.
    /// </summary>
    private Color GetOriginalMyceliumColor()
    {
        if (!originalMyceliumColorCached)
        {
            if (myceliumLinkPrefab != null)
            {
                MyceliumLink linkComp = myceliumLinkPrefab.GetComponent<MyceliumLink>();
                if (linkComp != null && linkComp.lineMaterial != null)
                    originalMyceliumColor = linkComp.lineMaterial.GetColor("_Color");
            }
            originalMyceliumColorCached = true;
        }
        return originalMyceliumColor;
    }

    /// <summary>
    /// Variacion aleatoria (en espacio HSV, no RGB directo -- da resultados mas
    /// naturales) del color original del micelio, para que cada colonia nueva se vea
    /// como "el mismo tipo de hongo" pero distinguible de las demas.
    /// </summary>
    private Color GenerateColonyColor()
    {
        Color baseColor = GetOriginalMyceliumColor();
        Color.RGBToHSV(baseColor, out float h, out float s, out float v);

        h = Mathf.Repeat(h + Random.Range(-colonyHueVariation, colonyHueVariation), 1f);
        s = Mathf.Clamp01(s + Random.Range(-colonySaturationVariation, colonySaturationVariation));

        Color result = Color.HSVToRGB(h, s, v);
        result.a = baseColor.a;
        return result;
    }

    // ---------- Micelio ----------

    private string LinkKey(SpawnedNode a, SpawnedNode b)
    {
        int idA = a.GetInstanceID();
        int idB = b.GetInstanceID();
        return idA < idB ? $"{idA}_{idB}" : $"{idB}_{idA}";
    }

    private void CreateMyceliumLink(SpawnedNode a, SpawnedNode b, bool isChainContinuation = true)
    {
        if (a == null || b == null || a == b) return;

        string key = LinkKey(a, b);
        if (existingLinks.Contains(key)) return;
        existingLinks.Add(key);

        // Se usa el color de colonia de "b" (el nodo nuevo/destino) -- en los casos
        // donde ambos ya comparten colonia (centro -> anillo, origen -> conector) da
        // exactamente lo mismo elegir a o b; en una conexion lateral entre colonias
        // distintas, esto hace que el hilo "crezca desde" la colonia del nodo nuevo.
        MyceliumLink newLink = CreateMyceliumLinkAt(a.transform.position, b.transform.position, b.ColonyColor);
        if (newLink == null) return;

        if (!isChainContinuation) return; // conexion lateral: no aplana ni registra puntas

        // "a" era la punta de un hilo anterior (si lo hubo): ese hilo ya no es una
        // punta suelta, porque de ahi arranca este nuevo -> se aplana.
        if (incomingLinkAtNode.TryGetValue(a, out MyceliumLink previousLink) && previousLink != null)
        {
            previousLink.SetTipRounded(false);
        }

        // Este hilo nuevo es ahora la punta que "llega" a b.
        incomingLinkAtNode[b] = newLink;
    }

    private MyceliumLink CreateMyceliumLinkAt(Vector3 from, Vector3 to, Color colorTint)
    {
        if (myceliumLinkPrefab == null) return null;

        GameObject go = Instantiate(myceliumLinkPrefab, Vector3.zero, Quaternion.identity, transform);
        MyceliumLink link = go.GetComponent<MyceliumLink>();
        if (link == null) link = go.AddComponent<MyceliumLink>();

        link.Build(from, to, colorTint);
        return link;
    }
}
