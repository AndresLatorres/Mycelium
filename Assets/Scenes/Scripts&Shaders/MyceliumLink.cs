using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Dibuja un camino organico tipo "hilo de micelio" entre dos puntos: no es una linea recta,
/// tiene desvios tipo ruido Perlin, un par de ramificaciones cortas que no llevan a ningun lado
/// (solo decorativas, alineadas con la direccion original como ramifica el micelio real) y una
/// animacion de crecimiento progresivo usando el shader "Network/MyceliumLine".
///
/// El crecimiento tiene DOS relojes distintos:
///  - El hilo PRINCIPAL crece lineal de 0 a 1 durante "growDuration".
///  - Cada RAMA arranca recien cuando el hilo principal llega, en su crecimiento, hasta el
///    punto donde esa rama nace (asi no aparecen de la nada antes de tiempo). A partir de ahi
///    crece con una curva LOGARITMICA (rapido al principio, cada vez mas lento, pero sin
///    frenar del todo) durante "branchGrowDuration". Como TODAS las ramas comparten el mismo
///    instante final de corte (growDuration + branchExtraGrowTime), las que arrancaron antes
///    (mas cerca del origen) tienen mas tiempo real para crecer y terminan mas desarrolladas
///    que las que arrancaron tarde (cerca de la punta), que quedan mas cortas.
///
/// El redondeo de la punta que esta creciendo se hace en el shader (no con numCapVertices de
/// Unity, que redondea los DOS extremos por igual). SetTipRounded(false) aplana la punta del
/// hilo principal -- se usa cuando este hilo deja de ser una punta suelta porque de ahi
/// arranca una continuacion.
/// </summary>
public class MyceliumLink : MonoBehaviour
{
    [Header("Forma del camino principal")]
    public int segments = 20;
    [Tooltip("Cuanto se desvia el camino de la linea recta")]
    public float wobbleAmount = 0.8f;
    [Tooltip("Escala del ruido perlin usado para el desvio (numeros chicos = curvas mas largas)")]
    public float noiseScale = 0.5f;
    public float lineWidth = 0.08f;

    [Header("Ramificaciones decorativas")]
    public int branchCount = 3;
    public float branchLengthMin = 0.8f;
    public float branchLengthMax = 2.5f;
    public float branchWidth = 0.03f;
    [Tooltip("Angulo minimo respecto a la direccion original (el micelio real ramifica en angulos cerrados, no perpendiculares)")]
    public float branchAngleMin = 12f;
    [Tooltip("Angulo maximo respecto a la direccion original")]
    public float branchAngleMax = 40f;

    [Header("Animacion - hilo principal")]
    [Tooltip("Cuanto tarda en crecer del todo el hilo PRINCIPAL (lineal)")]
    public float growDuration = 1.5f;

    [Header("Animacion - ramas secundarias")]
    [Tooltip("Tiempo extra despues de que el hilo principal termine, durante el cual las ramas siguen creciendo")]
    public float branchExtraGrowTime = 1f;
    [Tooltip("Duracion de referencia de la curva de cada rama UNA VEZ QUE ARRANCA. Las que arrancan tarde no llegan a completarla antes del corte final, por eso terminan mas cortas que las que arrancaron temprano")]
    public float branchGrowDuration = 1.5f;
    [Tooltip("Que tan logaritmica es la curva de crecimiento de las ramas: valores altos = crecen rapido al principio y muy lento despues; valores bajos (cerca de 0) = casi lineal")]
    public float branchGrowthSharpness = 6f;

    [Header("Material (instancia del shader Network/MyceliumLine)")]
    public Material lineMaterial;

    private struct BranchInfo
    {
        public LineRenderer renderer;
        public float startFraction; // 0-1: en que punto del hilo principal nace esta rama
    }

    private LineRenderer mainLine;
    private Material mainMaterial;
    private readonly List<BranchInfo> branches = new List<BranchInfo>();

    /// <summary>
    /// Genera y anima el camino entre "from" y "to". Llamar una sola vez despues de instanciar.
    /// </summary>
    public void Build(Vector3 from, Vector3 to)
    {
        float seed = Random.Range(0f, 1000f);
        List<Vector3> mainPoints = GenerateOrganicPath(from, to, seed);

        mainLine = CreateLineRenderer("MainThread", lineWidth);
        mainLine.positionCount = mainPoints.Count;
        mainLine.SetPositions(mainPoints.ToArray());
        ConfigureLineMaterial(mainLine, ComputePathLength(mainPoints), lineWidth);
        mainMaterial = mainLine.material;

        for (int b = 0; b < branchCount; b++)
        {
            CreateBranch(mainPoints, seed + b * 17f);
        }

        StartCoroutine(GrowRoutine());
    }

    /// <summary>
    /// Aplana (true->false) o redondea (false->true) la punta del hilo PRINCIPAL.
    /// Se usa cuando este hilo deja de ser una punta suelta: si un hilo nuevo arranca
    /// justo donde este terminaba, esta union ya no deberia verse como una punta creciendo.
    /// </summary>
    public void SetTipRounded(bool rounded)
    {
        if (mainMaterial != null)
            mainMaterial.SetFloat("_TipCapRounded", rounded ? 1f : 0f);
    }

    private List<Vector3> GenerateOrganicPath(Vector3 from, Vector3 to, float seed)
    {
        List<Vector3> points = new List<Vector3>();
        Vector3 dir = to - from;
        Vector3 perpendicular = Vector3.Cross(dir.normalized, Vector3.up);
        if (perpendicular == Vector3.zero) perpendicular = Vector3.right;

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            Vector3 basePos = Vector3.Lerp(from, to, t);

            // El desvio es maximo en el medio del camino y cero en los extremos,
            // para que el hilo nazca y termine justo en los objetos conectados.
            float taper = Mathf.Sin(t * Mathf.PI);
            float noise = (Mathf.PerlinNoise(seed, t / Mathf.Max(noiseScale, 0.001f)) - 0.5f) * 2f;

            Vector3 offset = perpendicular * noise * wobbleAmount * taper;
            points.Add(basePos + offset);
        }
        return points;
    }

    private void CreateBranch(List<Vector3> mainPoints, float seed)
    {
        if (mainPoints.Count < 5) return;

        // Elige un punto al azar (no en los extremos) del camino principal como origen de la rama
        int startIndex = Random.Range(2, mainPoints.Count - 2);
        float startFraction = (float)startIndex / (mainPoints.Count - 1);
        Vector3 start = mainPoints[startIndex];

        Vector3 mainDir = (mainPoints[startIndex + 1] - mainPoints[startIndex - 1]).normalized;

        // Angulo cerrado respecto a la direccion original: el micelio real ramifica
        // siguiendo mas o menos hacia adelante, no en cualquier direccion.
        float angle = Random.Range(branchAngleMin, branchAngleMax) * (Random.value > 0.5f ? 1f : -1f);
        Vector3 branchDir = Quaternion.AngleAxis(angle, Vector3.up) * mainDir;

        float length = Random.Range(branchLengthMin, branchLengthMax);
        int branchSegments = Mathf.Max(3, segments / 3);
        Vector3 perpendicular = Vector3.Cross(branchDir, Vector3.up);

        List<Vector3> branchPoints = new List<Vector3> { start };
        for (int i = 1; i <= branchSegments; i++)
        {
            float t = (float)i / branchSegments;
            Vector3 basePos = start + branchDir * length * t;
            float noise = (Mathf.PerlinNoise(seed, t * 3f) - 0.5f) * 2f;
            branchPoints.Add(basePos + perpendicular * noise * wobbleAmount * 0.5f * t);
        }

        LineRenderer branchLine = CreateLineRenderer("Branch", branchWidth);
        branchLine.positionCount = branchPoints.Count;
        branchLine.SetPositions(branchPoints.ToArray());
        ConfigureLineMaterial(branchLine, ComputePathLength(branchPoints), branchWidth);

        branches.Add(new BranchInfo { renderer = branchLine, startFraction = startFraction });
    }

    private float ComputePathLength(List<Vector3> points)
    {
        float length = 0f;
        for (int i = 1; i < points.Count; i++)
            length += Vector3.Distance(points[i - 1], points[i]);
        return Mathf.Max(length, 0.001f);
    }

    private void ConfigureLineMaterial(LineRenderer lr, float pathLength, float width)
    {
        lr.material.SetFloat("_LineLength", pathLength);
        lr.material.SetFloat("_LineWidthWorld", width);
    }

    private LineRenderer CreateLineRenderer(string childName, float width)
    {
        GameObject go = new GameObject(childName);
        go.transform.SetParent(transform, worldPositionStays: true);

        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.widthMultiplier = width;
        lr.textureMode = LineTextureMode.Stretch; // clave: asi uv.x recorre 0->1 en TODO el largo
        lr.numCapVertices = 0; // el redondeo de la punta lo hace el shader, no Unity
        lr.alignment = LineAlignment.View;
        lr.useWorldSpace = true;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        Material matInstance = new Material(lineMaterial);
        matInstance.SetFloat("_GrowAmount", 0f);
        matInstance.SetFloat("_TipCapRounded", 1f); // por defecto, punta redondeada mientras crece
        lr.material = matInstance;

        return lr;
    }

    /// <summary>
    /// Curva logaritmica normalizada: entrada y salida entre 0 y 1. "sharpness" alto hace
    /// que crezca rapido al principio y muy lento (pero sin frenar del todo) hacia el final.
    /// </summary>
    private float LogEase(float t, float sharpness)
    {
        t = Mathf.Clamp01(t);
        sharpness = Mathf.Max(sharpness, 0.01f);
        return Mathf.Log(1f + sharpness * t) / Mathf.Log(1f + sharpness);
    }

    /// <summary>
    /// Cuanto crecio una rama dada, en un instante "elapsed" (segundos desde que arranco
    /// TODA la animacion, no desde que arranco la rama).
    /// </summary>
    private float ComputeBranchGrowAmount(float elapsed, float branchStartFraction)
    {
        float branchStartTime = branchStartFraction * growDuration;
        float branchElapsed = elapsed - branchStartTime;

        if (branchElapsed <= 0f) return 0f;

        float localT = Mathf.Clamp01(branchElapsed / Mathf.Max(branchGrowDuration, 0.001f));
        return LogEase(localT, branchGrowthSharpness);
    }

    private IEnumerator GrowRoutine()
    {
        float totalDuration = growDuration + branchExtraGrowTime;
        float elapsed = 0f;

        while (elapsed < totalDuration)
        {
            elapsed += Time.deltaTime;

            float mainGrow = Mathf.Clamp01(elapsed / Mathf.Max(growDuration, 0.001f));
            mainMaterial.SetFloat("_GrowAmount", mainGrow);

            foreach (var branch in branches)
            {
                float branchGrow = ComputeBranchGrowAmount(elapsed, branch.startFraction);
                branch.renderer.material.SetFloat("_GrowAmount", branchGrow);
            }

            yield return null;
        }

        // Valores finales prolijos (por si el ultimo frame se paso de "totalDuration")
        mainMaterial.SetFloat("_GrowAmount", 1f);
        foreach (var branch in branches)
        {
            float branchGrow = ComputeBranchGrowAmount(totalDuration, branch.startFraction);
            branch.renderer.material.SetFloat("_GrowAmount", branchGrow);
        }
    }
}
