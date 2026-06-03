using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target da seguire")]
    [Tooltip("Trascina qui l'oggetto che la telecamera deve seguire (es. la pallina)")]
    public Transform target;

    [Header("Impostazioni Posizione")]
    [Tooltip("La distanza fissa (X, Y, Z) della telecamera rispetto all'oggetto")]
    public Vector3 offset = new Vector3(0f, 5f, -7f);

    [Range(0f, 1f)]
    [Tooltip("La fluidità dell'inseguimento. Valori più bassi = più fluido, 1 = istantaneo")]
    public float smoothness = 0.125f;

    void LateUpdate()
    {
        // Se non hai ancora assegnato l'oggetto nell'editor, interrompe l'esecuzione per evitare errori
        if (target == null) return;

        // 1. Calcola la posizione desiderata (Posizione Target + Offset)
        Vector3 desiredPosition = target.position + offset;

        // 2. Ammorbidisce il movimento usando Vector3.Lerp (Linear Interpolation)
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothness);

        // 3. Applica la posizione alla telecamera
        transform.position = smoothedPosition;

        // 4. (Opzionale) Forza la telecamera a guardare sempre verso il target
        transform.LookAt(target.position);
    }
}