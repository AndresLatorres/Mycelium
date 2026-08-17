using UnityEngine;
using System.Collections;
using TMPro;

/// <summary>
/// Muestra un texto flotando al lado del nodo cuando el jugador lo mira fijo (con la
/// camara) durante un tiempo sostenido. A diferencia de SpawnedNode (que reacciona a la
/// DISTANCIA del jugador), esto reacciona a hacia donde apunta la camara -- si el nodo
/// entra en un cono de vision angosto frente a la camara y se sostiene ahi el tiempo
/// suficiente, aparece el texto de "infoText" del TaggedObjectData con un fundido (nunca
/// de golpe); si se deja de mirar, se desvanece de la misma forma.
///
/// Una vez que el texto ya aparecio, se usa un angulo de tolerancia mas amplio para
/// mantenerlo visible (y tambien cuenta mirar directamente al texto, no solo al nodo) --
/// asi no desaparece por dejar de enfocar tan preciso la imagen despues de haber
/// "activado" el texto mirandola fijo.
///
/// Usa TextMeshPro (world space, no UI/Canvas) -- requiere el paquete TextMeshPro
/// instalado en el proyecto.
/// </summary>
[RequireComponent(typeof(SpawnedNode))]
public class GazeInfoDisplay : MonoBehaviour
{
    [Header("Deteccion de mirada")]
    [Tooltip("Si esta tildado, el NetworkGenerator NO pisa estos valores en este prefab en particular -- quedan los que pongas aca a mano")]
    public bool useCustomSettings = false;
    [Tooltip("Angulo maximo (grados) para que el texto APAREZCA por primera vez -- conviene que sea angosto, para que haga falta enfocar bien el nodo")]
    public float gazeAngleThreshold = 10f;
    [Tooltip("Angulo maximo (grados) para MANTENER el texto visible una vez que ya aparecio -- conviene mas amplio, asi no se pierde por mirar el texto en vez del nodo")]
    public float gazeAngleThresholdWhileVisible = 25f;
    [Tooltip("Distancia maxima a la que cuenta la mirada")]
    public float gazeMaxDistance = 15f;
    [Tooltip("Cuanto tiempo (segundos) hay que sostener la mirada inicial para que aparezca el texto")]
    public float gazeDwellTime = 1.5f;
    [Tooltip("Tiempo de espera para que REAPAREZCA despues de la primera vez que ya se mostro (mas corto, se siente mas responsivo que repetir la espera completa)")]
    public float gazeDwellTimeAfterFirstShow = 0.4f;

    [Header("Texto flotante")]
    [Tooltip("Fuente a usar. Si se deja vacio, usa la fuente default de TextMeshPro (TMP Settings) -- IMPORTANTE: como el componente se agrega por codigo, Unity no le asigna la fuente sola como cuando lo arrastras en el Editor")]
    public TMP_FontAsset fontAsset;
    [Tooltip("Desplazamiento RELATIVO A LA CAMARA: x = derecha/izquierda desde tu punto de vista, y = arriba/abajo, z = adelante/atras (normalmente en 0)")]
    public Vector3 textOffset = new Vector3(0.8f, 0.3f, 0f);
    [Tooltip("Tamano de fuente de TextMeshPro (probalo con valores entre 2 y 6)")]
    public float fontSize = 3f;
    public Color textColor = Color.white;
    [Tooltip("Cuanto tarda en aparecer/desaparecer el fundido de opacidad")]
    public float fadeDuration = 0.3f;
    [Tooltip("Ancho maximo (en unidades locales) antes de pasar a la siguiente linea")]
    public float maxWidth = 4f;
    [Tooltip("Si esta activo, corta el texto en la siguiente linea al llegar al ancho maximo (corta por caracter si una palabra sola no entra, por ejemplo si el texto no tiene espacios)")]
    public bool wordWrap = true;

    [Header("Fondo del texto (opcional)")]
    public bool showBackground = true;
    [Tooltip("Color del fondo. El alpha define la opacidad maxima (ej 0.55 = semi-transparente)")]
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.55f);
    [Tooltip("Margen extra del fondo alrededor del texto")]
    public float backgroundPadding = 0.15f;
    [Tooltip("Cuanto se aleja el fondo de la camara respecto al texto, para que no se pisen (z-fighting)")]
    public float backgroundZOffset = 0.02f;

    private SpawnedNode node;
    private Transform cameraTransform;

    private float gazeTimer = 0f;
    private bool isShowing = false;
    private bool hasShownBefore = false;

    private GameObject textObject;
    private TextMeshPro textMesh;
    private SpriteRenderer backgroundRenderer;
    private Coroutine fadeCoroutine;
    private static Sprite whitePixelSprite;

    private void Start()
    {
        node = GetComponent<SpawnedNode>();
        if (Camera.main != null) cameraTransform = Camera.main.transform;
    }

    /// <summary>
    /// Permite que el NetworkGenerator configure estos valores desde un solo lugar,
    /// igual que hace con SpawnedNode. Si "Use Custom Settings" esta tildado en este
    /// prefab en particular, no se pisa nada -- quedan los valores puestos a mano.
    /// </summary>
    public void ConfigureGaze(float angleThreshold, float angleThresholdWhileVisible, float maxDistance, float dwellTime, float dwellTimeAfterFirstShow)
    {
        if (useCustomSettings) return;

        gazeAngleThreshold = angleThreshold;
        gazeAngleThresholdWhileVisible = angleThresholdWhileVisible;
        gazeMaxDistance = maxDistance;
        gazeDwellTime = dwellTime;
        gazeDwellTimeAfterFirstShow = dwellTimeAfterFirstShow;
    }

    /// <summary>
    /// Igual que ConfigureGaze pero para el estilo del texto (tamano, color, ancho de
    /// wrap, etc). Separado en otro metodo para que quede mas ordenado, pero respeta el
    /// mismo "Use Custom Settings" -- un solo tilde cubre las dos cosas.
    /// </summary>
    public void ConfigureText(float size, Color color, float fade, Vector3 offset, float wrapWidth, bool wrap)
    {
        if (useCustomSettings) return;

        fontSize = size;
        textColor = color;
        fadeDuration = fade;
        textOffset = offset;
        maxWidth = wrapWidth;
        wordWrap = wrap;
    }

    /// <summary>
    /// Igual patron: se pisa salvo Use Custom Settings.
    /// </summary>
    public void ConfigureBackground(bool show, Color color, float padding, float zOffset)
    {
        if (useCustomSettings) return;

        showBackground = show;
        backgroundColor = color;
        backgroundPadding = padding;
        backgroundZOffset = zOffset;
    }

    private void Update()
    {
        if (cameraTransform == null)
        {
            if (Camera.main == null) return;
            cameraTransform = Camera.main.transform;
        }

        if (CheckGaze())
        {
            gazeTimer += Time.deltaTime;
            float requiredDwell = hasShownBefore ? gazeDwellTimeAfterFirstShow : gazeDwellTime;
            if (gazeTimer >= requiredDwell && !isShowing)
            {
                ShowText();
            }
        }
        else
        {
            gazeTimer = 0f;
            if (isShowing)
            {
                HideText();
            }
        }

        if (textObject != null && textObject.activeSelf)
        {
            UpdateTextTransform();
        }
    }

    /// <summary>
    /// Mientras el texto no aparecio, solo cuenta mirar el nodo, con el angulo angosto.
    /// Una vez que ya aparecio, cuenta mirar el nodo O el texto, con el angulo amplio.
    /// </summary>
    private bool CheckGaze()
    {
        float threshold = isShowing ? gazeAngleThresholdWhileVisible : gazeAngleThreshold;

        if (CheckGazeAt(transform.position, threshold)) return true;

        if (isShowing && textObject != null && CheckGazeAt(textObject.transform.position, threshold))
            return true;

        return false;
    }

    private bool CheckGazeAt(Vector3 worldPos, float angleThreshold)
    {
        Vector3 toTarget = worldPos - cameraTransform.position;
        float distance = toTarget.magnitude;
        if (distance > gazeMaxDistance || distance < 0.01f) return false;

        float angle = Vector3.Angle(cameraTransform.forward, toTarget);
        return angle <= angleThreshold;
    }

    private void EnsureTextObjectExists()
    {
        if (textObject != null) return;

        textObject = new GameObject("GazeInfoText");
        textObject.transform.SetParent(transform, worldPositionStays: false);

        textMesh = textObject.AddComponent<TextMeshPro>();
        textMesh.font = fontAsset != null ? fontAsset : TMP_Settings.defaultFontAsset;
        textMesh.text = (node.Data != null) ? node.Data.infoText : "";
        textMesh.fontSize = fontSize;
        textMesh.alignment = TextAlignmentOptions.MidlineLeft;
        textMesh.color = textColor;
        textMesh.alpha = 0f; // arranca invisible, el fade lo va subiendo

        textMesh.enableWordWrapping = wordWrap;
        textMesh.overflowMode = TextOverflowModes.Overflow;
        textMesh.rectTransform.sizeDelta = new Vector2(maxWidth, 10f); // alto generoso, que no trunque de arriba/abajo

        textMesh.ForceMeshUpdate(); // necesario para que "textMesh.bounds" ya de el tamano real, no el de un frame viejo

        if (showBackground)
        {
            CreateBackground();
        }
    }

    private void CreateBackground()
    {
        GameObject bgObject = new GameObject("Background");
        bgObject.transform.SetParent(textObject.transform, worldPositionStays: false);
        bgObject.transform.localRotation = Quaternion.identity;

        backgroundRenderer = bgObject.AddComponent<SpriteRenderer>();
        backgroundRenderer.sprite = GetWhitePixelSprite();
        backgroundRenderer.color = new Color(backgroundColor.r, backgroundColor.g, backgroundColor.b, 0f); // arranca invisible, el fade lo sube junto con el texto

        // Dimensiona el fondo al tamano real del texto (mas el padding), usando los
        // bounds locales que ya calculo TextMeshPro para el contenido actual.
        Bounds bounds = textMesh.bounds;
        Vector2 size = new Vector2(bounds.size.x + backgroundPadding * 2f, bounds.size.y + backgroundPadding * 2f);

        bgObject.transform.localScale = new Vector3(size.x, size.y, 1f);
        // Z positivo = "hacia atras" en el espacio local del texto (que ya mira a camara),
        // asi el fondo queda detras sin pisarse con las letras (z-fighting).
        bgObject.transform.localPosition = new Vector3(bounds.center.x, bounds.center.y, backgroundZOffset);
    }

    private static Sprite GetWhitePixelSprite()
    {
        if (whitePixelSprite != null) return whitePixelSprite;

        Texture2D tex = new Texture2D(4, 4);
        Color[] pixels = new Color[16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();

        whitePixelSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        return whitePixelSprite;
    }

    private void ShowText()
    {
        isShowing = true;
        hasShownBefore = true;
        EnsureTextObjectExists();
        textObject.SetActive(true);

        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeTo(1f, false));
    }

    private void HideText()
    {
        isShowing = false;
        if (textObject == null) return;

        if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeTo(0f, true));
    }

    private IEnumerator FadeTo(float targetAlpha, bool disableWhenDone)
    {
        float startAlpha = textMesh.alpha;
        float startBgAlpha = backgroundRenderer != null ? backgroundRenderer.color.a : 0f;
        float targetBgAlpha = backgroundColor.a * targetAlpha;

        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            textMesh.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);

            if (backgroundRenderer != null)
            {
                Color c = backgroundColor;
                c.a = Mathf.Lerp(startBgAlpha, targetBgAlpha, t);
                backgroundRenderer.color = c;
            }

            yield return null;
        }

        textMesh.alpha = targetAlpha;

        if (backgroundRenderer != null)
        {
            Color finalC = backgroundColor;
            finalC.a = targetBgAlpha;
            backgroundRenderer.color = finalC;
        }

        if (disableWhenDone) textObject.SetActive(false);
    }

    /// <summary>
    /// El offset se aplica en ejes RELATIVOS A LA CAMARA (derecha/arriba/adelante desde
    /// tu punto de vista), no en ejes de mundo fijos -- asi el texto siempre aparece "al
    /// costado" de forma consistente sin importar desde donde estes mirando el nodo.
    /// </summary>
    private void UpdateTextTransform()
    {
        Vector3 camRight = cameraTransform.right;
        Vector3 camUp = cameraTransform.up;
        Vector3 camForward = cameraTransform.forward;

        Vector3 worldOffset = camRight * textOffset.x + camUp * textOffset.y + camForward * textOffset.z;
        textObject.transform.position = transform.position + worldOffset;

        Vector3 lookDir = textObject.transform.position - cameraTransform.position;
        if (lookDir.sqrMagnitude > 0.0001f)
            textObject.transform.rotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
    }
}
