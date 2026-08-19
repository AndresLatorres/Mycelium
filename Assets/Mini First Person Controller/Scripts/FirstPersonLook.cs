using UnityEngine;

public class FirstPersonLook : MonoBehaviour
{
    [SerializeField]
    Transform character;
    public float sensitivity = 2;
    public float smoothing = 1.5f;

    Vector2 velocity;
    Vector2 frameVelocity;
    Rigidbody characterRigidbody;


    void Reset()
    {
        // Get the character from the FirstPersonMovement in parents.
        character = GetComponentInParent<FirstPersonMovement>().transform;
    }

    void Start()
    {
        // Lock the mouse cursor to the game screen.
        Cursor.lockState = CursorLockMode.Locked;
        characterRigidbody = character.GetComponent<Rigidbody>();
    }

    void Update()
    {
        // Get smooth velocity.
        Vector2 mouseDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        Vector2 rawFrameVelocity = Vector2.Scale(mouseDelta, Vector2.one * sensitivity);
        frameVelocity = Vector2.Lerp(frameVelocity, rawFrameVelocity, 1 / smoothing);
        velocity += frameVelocity;
        velocity.y = Mathf.Clamp(velocity.y, -90, 90);

        // Camera pitch: no es un Rigidbody, rotarla directo en Update esta bien.
        transform.localRotation = Quaternion.AngleAxis(-velocity.y, Vector3.right);

        // El yaw del personaje (character) SI es un Rigidbody -- si no tiene uno,
        // se mantiene el comportamiento original (rotarlo directo) por compatibilidad.
        if (characterRigidbody == null)
            character.localRotation = Quaternion.AngleAxis(velocity.x, Vector3.up);
    }

    void FixedUpdate()
    {
        // Rotar el yaw del personaje ACA (via Rigidbody.MoveRotation) en vez de en
        // Update con transform.localRotation directo: cuando el Rigidbody tiene
        // Interpolation activado, Unity asume que SOLO el motor de fisica toca su
        // transform entre pasos de FixedUpdate. Pisarlo a mano en Update() (como
        // hacia antes) confunde esa interpolacion -- se notaba como jitter de
        // posicion en cualquier script que leyera el transform del personaje
        // (por ejemplo, el que sigue al jugador para la lampara flotante),
        // especificamente al girar.
        if (characterRigidbody != null)
            characterRigidbody.MoveRotation(Quaternion.AngleAxis(velocity.x, Vector3.up));
    }
}
