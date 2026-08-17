using UnityEngine;

/// <summary>
/// Niebla de post-proceso por distancia + altura + ruido animado, mas "atmosferica" que la
/// niebla plana de Unity. Va en la camara (normalmente la Main Camera).
///
/// No usa [ImageEffectOpaque] a proposito: se aplica DESPUES de dibujar todo (incluidas las
/// cosas transparentes -- el micelio, los sprites de los nodos, sus fantasmas) para que esos
/// elementos tambien se desvanezcan con la distancia en vez de quedar "por encima" de la niebla.
///
/// Si tambien tenes activada la niebla plana de Unity (RenderSettings.fog), conviene bajarle
/// la densidad o desactivarla del todo para no duplicar el efecto.
/// </summary>
[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
public class VolumetricHeightFog : MonoBehaviour
{
    [Header("Shader (arrastrar el asset Hidden/Network/VolumetricHeightFog)")]
    public Shader fogShader;

    [Header("Niebla por distancia")]
    public Color fogColor = new Color(0.65f, 0.7f, 0.75f, 1f);
    [Tooltip("Densidad general: mayor = niebla mas cerrada")]
    public float fogDensity = 0.03f;
    [Tooltip("Distancia desde la camara a partir de la cual empieza a acumularse niebla")]
    public float fogStartDistance = 5f;

    [Header("Niebla por altura")]
    [Tooltip("Densidad extra cerca del piso (se suma a la de distancia)")]
    public float heightFogDensity = 0.08f;
    [Tooltip("Altura de referencia -- normalmente la altura de tu piso")]
    public float baseHeight = 0f;
    [Tooltip("Que tan rapido se aclara la niebla al subir en altura")]
    public float heightFalloff = 0.2f;

    [Header("Ruido (para que no sea un degradado perfecto)")]
    [Tooltip("Si se deja vacio, se genera una textura de ruido Perlin automaticamente")]
    public Texture2D noiseTexture;
    [Tooltip("Tiling del ruido sobre el mundo (numeros chicos = 'nubes' de niebla mas grandes)")]
    public float noiseScale = 0.05f;
    [Tooltip("Velocidad de desplazamiento del ruido en X/Z")]
    public Vector2 noiseScrollSpeed = new Vector2(0.01f, 0.008f);
    [Tooltip("Cuanto afecta el ruido a la densidad final (0 = plano, 1 = bien marcado)")]
    [Range(0f, 1f)] public float noiseStrength = 0.5f;

    private Camera cam;
    private Material fogMaterial;
    private Texture2D generatedNoiseTexture;

    private void OnEnable()
    {
        cam = GetComponent<Camera>();
        cam.depthTextureMode |= DepthTextureMode.Depth;
    }

    private Material GetMaterial()
    {
        if (fogShader == null) return null;

        if (fogMaterial == null)
        {
            fogMaterial = new Material(fogShader);
            fogMaterial.hideFlags = HideFlags.HideAndDontSave;
        }
        return fogMaterial;
    }

    private Texture2D GetNoiseTexture()
    {
        if (noiseTexture != null) return noiseTexture;

        if (generatedNoiseTexture == null)
        {
            int size = 256;
            generatedNoiseTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            generatedNoiseTexture.wrapMode = TextureWrapMode.Repeat;
            generatedNoiseTexture.filterMode = FilterMode.Bilinear;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.06f, y * 0.06f);
                    generatedNoiseTexture.SetPixel(x, y, new Color(n, n, n, 1f));
                }
            }
            generatedNoiseTexture.Apply();
        }

        return generatedNoiseTexture;
    }

    /// <summary>
    /// [ImageEffectOpaque] hace que esto corra DESPUES de la geometria opaca (el piso) pero
    /// ANTES de los objetos transparentes (sprites, micelio). Es necesario: como esos objetos
    /// no escriben en el depth buffer, si la niebla corriera despues de dibujarlos, calcularia
    /// su distancia usando el depth de lo que hay DETRAS suyo -- haciendolos ver "traslucidos"
    /// aunque esten cerca. Con este orden, los transparentes se dibujan encima de un fondo ya
    /// niebloso, y no los toca directamente.
    ///
    /// Contrapartida: los objetos transparentes en si NO reciben este efecto (ni la niebla por
    /// altura ni el ruido). Si queres que tambien se atenuen con la distancia, usa la niebla
    /// plana de Unity (RenderSettings.fog) -- MyceliumLine y SoftEdgeSprite ya la soportan.
    /// </summary>
    [ImageEffectOpaque]
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        Material mat = GetMaterial();
        if (mat == null || cam == null)
        {
            Graphics.Blit(source, destination);
            return;
        }

        mat.SetColor("_FogColor", fogColor);
        mat.SetFloat("_FogDensity", fogDensity);
        mat.SetFloat("_FogStartDistance", fogStartDistance);
        mat.SetFloat("_HeightFogDensity", heightFogDensity);
        mat.SetFloat("_BaseHeight", baseHeight);
        mat.SetFloat("_HeightFalloff", heightFalloff);
        mat.SetFloat("_NoiseScale", noiseScale);
        mat.SetVector("_NoiseScroll", noiseScrollSpeed * Time.time);
        mat.SetFloat("_NoiseStrength", noiseStrength);
        mat.SetTexture("_NoiseTex", GetNoiseTexture());

        SetFrustumCorners(mat);
        CustomGraphicsBlit(source, destination, mat);
    }

    /// <summary>
    /// Calcula el rayo (direccion, ya escalado hasta el far clip plane) de cada una de las
    /// 4 esquinas del frustum de la camara, en espacio de mundo. El shader usa estos rayos
    /// para reconstruir la posicion en el mundo de cada pixel a partir de su profundidad.
    /// </summary>
    private void SetFrustumCorners(Material mat)
    {
        float fovHalfRad = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float farClip = cam.farClipPlane;

        Vector3 toRight = cam.transform.right * Mathf.Tan(fovHalfRad) * cam.aspect;
        Vector3 toTop = cam.transform.up * Mathf.Tan(fovHalfRad);

        Vector3 topLeft = cam.transform.forward - toRight + toTop;
        float camScale = topLeft.magnitude * farClip;

        topLeft.Normalize();
        topLeft *= camScale;

        Vector3 topRight = cam.transform.forward + toRight + toTop;
        topRight.Normalize();
        topRight *= camScale;

        Vector3 bottomRight = cam.transform.forward + toRight - toTop;
        bottomRight.Normalize();
        bottomRight *= camScale;

        Vector3 bottomLeft = cam.transform.forward - toRight - toTop;
        bottomLeft.Normalize();
        bottomLeft *= camScale;

        mat.SetVector("_FrustumCorner0", bottomLeft);
        mat.SetVector("_FrustumCorner1", bottomRight);
        mat.SetVector("_FrustumCorner2", topRight);
        mat.SetVector("_FrustumCorner3", topLeft);
    }

    /// <summary>
    /// Como Graphics.Blit normal no permite mandar un indice de esquina distinto por vertice,
    /// dibujamos el quad de pantalla completa a mano con GL, mandando en TEXCOORD1 cual de las
    /// 4 esquinas del frustum le corresponde a cada vertice.
    /// </summary>
    private void CustomGraphicsBlit(RenderTexture source, RenderTexture dest, Material fxMaterial)
    {
        RenderTexture.active = dest;
        fxMaterial.SetTexture("_MainTex", source);

        GL.PushMatrix();
        GL.LoadOrtho();
        fxMaterial.SetPass(0);

        GL.Begin(GL.QUADS);

        GL.MultiTexCoord2(0, 0.0f, 0.0f);
        GL.MultiTexCoord2(1, 0.0f, 0.0f); // esquina 0 = bottom left
        GL.Vertex3(0.0f, 0.0f, 0.0f);

        GL.MultiTexCoord2(0, 1.0f, 0.0f);
        GL.MultiTexCoord2(1, 1.0f, 0.0f); // esquina 1 = bottom right
        GL.Vertex3(1.0f, 0.0f, 0.0f);

        GL.MultiTexCoord2(0, 1.0f, 1.0f);
        GL.MultiTexCoord2(1, 2.0f, 0.0f); // esquina 2 = top right
        GL.Vertex3(1.0f, 1.0f, 0.0f);

        GL.MultiTexCoord2(0, 0.0f, 1.0f);
        GL.MultiTexCoord2(1, 3.0f, 0.0f); // esquina 3 = top left
        GL.Vertex3(0.0f, 1.0f, 0.0f);

        GL.End();
        GL.PopMatrix();
    }
}
