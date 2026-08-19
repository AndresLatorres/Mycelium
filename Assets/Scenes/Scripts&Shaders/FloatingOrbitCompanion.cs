using UnityEngine;

/// <summary>
/// Hace que este objeto (pensado para la lampara) acompañe al jugador flotando y
/// orbitando lentamente a su alrededor, en vez de seguirlo pegado. El movimiento se
/// arma en capas independientes, cada una editable por separado:
///
///  1. ORBITA: un punto que gira lentamente alrededor del jugador. Es el objetivo que
///     persigue la lampara -- asi, aunque el jugador este quieto, el punto sigue
///     moviendose y la lampara orbita igual (no queda fija).
///
///  2. RESORTE: en vez de seguir ese punto de orbita de forma directa, se simula un
///     sistema masa-resorte amortiguado (NO Vector3.SmoothDamp -- ese esta pensado
///     para NUNCA pasarse del objetivo). Con damping menor a 1, si el jugador se
///     mueve rapido la lampara se queda atras, "acelera" al acercarse, se pasa de
///     largo un poco y vuelve, como algo colgado de un resorte real.
///
///  3. FLOTE: un vaiven vertical lento (seno), sumado ENCIMA de la posicion del
///     resorte -- a proposito no pasa por el resorte, para que se sienta como un
///     flote constante en vez de una reaccion al movimiento del jugador.
///
///  4. TAMBALEO: inclinacion (pitch/roll) con ruido Perlin -- suave y organico, no
///     random puro (que se veria tembloroso/discontinuo) -- mas un giro lento y
///     constante sobre su propio eje (yaw).
///
/// El resorte se integra con Euler semi-implicito (velocidad primero, despues
/// posicion): no es la solucion exacta del oscilador armonico, pero es estable a
/// framerates normales y mucho mas simple de leer/ajustar.
/// </summary>
public class FloatingOrbitCompanion : MonoBehaviour
{
    [Header("Objetivo")]
    [Tooltip("Si se deja vacio, busca el objeto con tag 'Player'.")]
    public Transform target;

    [Header("Orbita alrededor del jugador")]
    [Tooltip("Radio de la orbita, en unidades de mundo")]
    public float orbitRadius = 2f;
    [Tooltip("Altura de la orbita respecto al jugador")]
    public float orbitHeight = 1.5f;
    [Tooltip("Velocidad angular de la orbita, en grados por segundo. Aunque el jugador este quieto, la lampara sigue moviendose alrededor gracias a esto")]
    public float orbitSpeed = 15f;

    [Header("Suavizado de la lectura de posicion del jugador")]
    [Tooltip("Suaviza la posicion del jugador ANTES de usarla para la orbita/resorte (con SmoothDamp, que a diferencia del resorte de abajo nunca se pasa de largo -- aca no lo queremos, solo limpiar ruido). Util si el jugador se mueve por fisica y la lectura cuadro a cuadro no sale perfectamente lisa. 0 = sin suavizado")]
    public float targetPositionSmoothing = 0.08f;

    [Header("Seguimiento tipo resorte (delay + rebote)")]
    [Tooltip("Que tan 'rigido' es el resorte -- mas alto, alcanza al punto de orbita mas rapido")]
    public float springFrequency = 0.8f;
    [Tooltip("Amortiguacion del resorte. 1 = llega justo sin pasarse (critico). Menos de 1 = se pasa de largo y rebota (el efecto resorte que buscas). Mas de 1 = mas lento y sin rebote")]
    [Range(0.05f, 2f)]
    public float springDamping = 0.5f;

    [Header("Flote (subir/bajar lento, tipo boya en agua)")]
    public float floatAmplitude = 0.15f;
    [Tooltip("Ciclos completos (subir y bajar) por segundo")]
    public float floatSpeed = 0.25f;

    [Header("Tambaleo (inclinacion aleatoria suave + giro)")]
    [Tooltip("Grados maximos de inclinacion aleatoria (pitch/roll)")]
    public float wobbleAmount = 4f;
    [Tooltip("Que tan rapido cambia el tambaleo -- mas alto, se tambalea mas seguido")]
    public float wobbleSpeed = 0.3f;
    [Tooltip("Grados por segundo de giro constante sobre el eje Y (yaw). 0 = no gira")]
    public float spinSpeed = 5f;

    // Cuantos sub-pasos de integracion por frame usa el resorte (ver LateUpdate). No
    // se expone como campo publico porque es un detalle numerico interno, no algo que
    // cambie la sensacion del movimiento.
    private const int SpringSubsteps = 8;

    private Vector3 springPosition;
    private Vector3 springVelocity;
    private Vector3 smoothedTargetPos;
    private Vector3 smoothedTargetVel;
    private float orbitAngle;
    private float spinAngle;
    private bool initialized;

    // Semillas para que el ruido de X y Z no queden identicos -- si no, el tambaleo
    // se mueve siempre igual en las dos direcciones a la vez y se ve artificial.
    private float noiseSeedX;
    private float noiseSeedZ;

    private void Start()
    {
        noiseSeedX = Random.value * 1000f;
        noiseSeedZ = Random.value * 1000f;

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
            if (playerGO == null) return; // todavia no hay Player en la escena
            target = playerGO.transform;
        }

        // Clampeado: si en algun frame el deltaTime es inusualmente grande (una pausa
        // del Garbage Collector, un Instantiate pesado de NetworkGenerator generando un
        // cluster, carga de nivel, etc.), integrar el resorte con ese salto grande lo
        // puede hacer "explotar" un instante -- se ve como un salto/crop intermitente,
        // justamente el sintoma reportado. Limitar el dt evita la inestabilidad sin
        // cambiar el comportamiento en frames normales.
        float dt = Mathf.Min(Time.deltaTime, 0.05f);

        if (!initialized)
        {
            // Arranca ya en la posicion/orbita real, no en el origen -- si no, el
            // primer frame hace un salto grande desde donde este el objeto.
            smoothedTargetPos = target.position;
            orbitAngle = (orbitAngle + orbitSpeed * dt) % 360f;
            springPosition = ComputeOrbitTarget();
            initialized = true;
        }

        // Filtra la lectura de target.position ANTES de usarla -- si venia con ruido
        // cuadro a cuadro (por ejemplo, un jugador movido por fisica cuyo Transform no
        // se lee perfectamente liso incluso con Rigidbody Interpolation activado), este
        // filtro lo limpia sin agregarle overshoot (para eso ya esta el resorte, mas
        // abajo). Subdividir la integracion del resorte NO alcanza para esto -- ese
        // problema es de la ENTRADA, no de como se integra.
        smoothedTargetPos = Vector3.SmoothDamp(smoothedTargetPos, target.position, ref smoothedTargetVel, targetPositionSmoothing);

        orbitAngle = (orbitAngle + orbitSpeed * dt) % 360f;
        Vector3 orbitTarget = ComputeOrbitTarget();

        // Se integra en varios sub-pasos chicos en vez de uno solo con todo el dt: un
        // Euler semi-implicito de un paso es sensible a que dt varie levemente entre
        // frames (nunca es perfectamente constante), y eso se notaba como un jitter
        // chico pero constante SOLO mientras el objetivo se mueve (con el objetivo
        // quieto, delta=0 y no hay nada que amplificar). Mas sub-pasos = mismo
        // resultado final, mucho mas estable, sin cambiar la sensacion del resorte.
        float omega = 2f * Mathf.PI * springFrequency;
        float subDt = dt / SpringSubsteps;
        for (int i = 0; i < SpringSubsteps; i++)
        {
            Vector3 delta = orbitTarget - springPosition;
            Vector3 acceleration = delta * (omega * omega) - springVelocity * (2f * springDamping * omega);
            springVelocity += acceleration * subDt;
            springPosition += springVelocity * subDt;
        }

        float floatOffset = Mathf.Sin(Time.time * floatSpeed * Mathf.PI * 2f) * floatAmplitude;
        transform.position = springPosition + Vector3.up * floatOffset;

        float wobblePitch = (Mathf.PerlinNoise(Time.time * wobbleSpeed, noiseSeedX) - 0.5f) * 2f * wobbleAmount;
        float wobbleRoll = (Mathf.PerlinNoise(noiseSeedZ, Time.time * wobbleSpeed) - 0.5f) * 2f * wobbleAmount;
        spinAngle = (spinAngle + spinSpeed * dt) % 360f;

        transform.rotation = Quaternion.Euler(wobblePitch, spinAngle, wobbleRoll);
    }

    private Vector3 ComputeOrbitTarget()
    {
        float rad = orbitAngle * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * orbitRadius;
        return smoothedTargetPos + offset + Vector3.up * orbitHeight;
    }
}
