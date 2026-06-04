using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Zona trigger di una buca. Quando una pallina entra nella zona viene "imbucata":
/// scende dentro la buca con una breve animazione e poi viene rimossa dal tavolo.
/// La cueball invece viene rimessa in gioco nella posizione di break (fallo).
/// </summary>
[RequireComponent(typeof(Collider))]
public class PocketTrigger : MonoBehaviour
{
    [Tooltip("Posizione in cui rimettere la cueball se finisce in buca (fallo).")]
    public static Vector3 CueRespawn = new Vector3(-4f, 0.5f, 0f);

    [Tooltip("Numero di palline (non cueball) imbucate finora.")]
    public static int PocketedCount = 0;

    [Tooltip("Durata dello scivolamento verso il centro buca.")]
    public float slideDuration = 0.12f;
    [Tooltip("Durata della discesa dentro la buca.")]
    public float dropDuration = 0.18f;
    [Tooltip("Profondita' di caduta dentro la buca.")]
    public float dropDepth = 1.5f;

    // Palline attualmente in fase di imbucamento (evita doppie elaborazioni tra piu' buche).
    private static readonly HashSet<int> s_processing = new HashSet<int>();

    // All'avvio della scena memorizza la posizione iniziale della cueball come punto di respawn.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CaptureCueRespawn()
    {
        PocketedCount = 0;
        s_processing.Clear();
        var cue = GameObject.Find("cueball");
        if (cue != null) CueRespawn = cue.transform.position;
    }

    private void OnTriggerEnter(Collider other) => TryPocket(other);
    private void OnTriggerStay(Collider other) => TryPocket(other);

    private void TryPocket(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null) return;

        string n = rb.gameObject.name;
        bool isBall = n == "cueball" || n.StartsWith("poolBall");
        if (!isBall) return;

        if (!rb.gameObject.activeInHierarchy) return;
        if (s_processing.Contains(rb.GetInstanceID())) return;

        StartCoroutine(Pocket(rb));
    }

    private IEnumerator Pocket(Rigidbody rb)
    {
        int id = rb.GetInstanceID();
        if (!s_processing.Add(id)) yield break;

        bool isCue = rb.gameObject.name == "cueball";

        // Blocca la fisica e disattiva i collider cosi' la pallina non spinge le altre mentre scende.
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
        Collider[] cols = rb.GetComponentsInChildren<Collider>();
        foreach (var c in cols) c.enabled = false;

        Transform t = rb.transform;
        Vector3 start = t.position;
        Vector3 over = new Vector3(transform.position.x, start.y, transform.position.z);

        // 1) Scivola verso il centro della buca.
        float p = 0f;
        while (p < 1f)
        {
            p += Time.deltaTime / Mathf.Max(0.01f, slideDuration);
            t.position = Vector3.Lerp(start, over, p);
            yield return null;
        }

        // 2) Cade dentro la buca.
        Vector3 down = over + Vector3.down * dropDepth;
        p = 0f;
        while (p < 1f)
        {
            p += Time.deltaTime / Mathf.Max(0.01f, dropDuration);
            t.position = Vector3.Lerp(over, down, p);
            yield return null;
        }

        if (isCue)
        {
            // Fallo: rimetti la cueball in gioco.
            t.position = CueRespawn;
            foreach (var c in cols) c.enabled = true;
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            Debug.Log("[Pocket] Cueball imbucata (fallo) -> rimessa in gioco.");
        }
        else
        {
            PocketedCount++;
            rb.gameObject.SetActive(false);
            Debug.Log("[Pocket] Pallina imbucata: " + rb.gameObject.name + " (totale: " + PocketedCount + ")");
        }

        s_processing.Remove(id);
    }
}
