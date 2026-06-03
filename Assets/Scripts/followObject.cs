using UnityEngine;
using UnityEngine.InputSystem;

public class CameraFollow : MonoBehaviour
{
    [Header("Target da seguire")]
    public Transform target;

    [Header("Impostazioni Orbita")]
    public float rotationSpeed = 0.2f;
    public float zoomSpeed = 2f;
    public float minDistance = 2f;
    public float maxDistance = 15f;
    public float minPitch = 10f;
    public float maxPitch = 80f;

    [Header("Fluidità")]
    [Range(0f, 1f)]
    public float smoothness = 0.125f;

    private float currentYaw = 0f;
    private float currentPitch = 45f;
    private float currentDistance = 7f;

    private Vector3 currentVelocity = Vector3.zero;

    void Start()
    {
        if (target != null)
        {
            Vector3 angles = transform.eulerAngles;
            currentYaw = angles.y;
            currentPitch = angles.x;
            currentDistance = Vector3.Distance(transform.position, target.position);
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        // Rotazione con il tasto destro del mouse
        if (Mouse.current != null && Mouse.current.rightButton.isPressed)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            currentYaw += delta.x * rotationSpeed;
            currentPitch -= delta.y * rotationSpeed;
            currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);
        }

        // Zoom con la rotella del mouse
        if (Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                currentDistance -= (scroll / 120f) * zoomSpeed;
                currentDistance = Mathf.Clamp(currentDistance, minDistance, maxDistance);
            }
        }

        // Calcolo della rotazione e posizione desiderata
        Quaternion rotation = Quaternion.Euler(currentPitch, currentYaw, 0);
        Vector3 negDistance = new Vector3(0.0f, 0.0f, -currentDistance);
        Vector3 desiredPosition = rotation * negDistance + target.position;

        // Applicazione fluida
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref currentVelocity, smoothness);
        transform.LookAt(target.position);
    }
}
