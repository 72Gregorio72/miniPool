using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

[RequireComponent(typeof(LineRenderer))]
public class ThrowBall : MonoBehaviour
{
    private Rigidbody rb;
    private Camera mainCamera;
    private LineRenderer lineRenderer;
    private LineRenderer trajectoryLineRenderer;
    
    [Header("Riferimento Stecca")]
    [Tooltip("Trascina qui l'oggetto 'CuePivot' (il padre della stecca)")]
    public Transform cuePivot;
    [Tooltip("Trascina qui il modello 3D della stecca (il figlio)")]
    public Transform cueMesh;

    [Header("Impostazioni di Tiro")]
    public float forceMultiplier = 50f;
    public float maxDragDistance = 2f;
    public float maxForce = 100f;

    [Header("Impostazioni Fisica")]
    public float sleepVelocityThreshold = 0.05f; // Velocità sotto cui la pallina si ferma completamente

    [Header("Impostazioni Linea di Mira")]
    public Color lineColor = new Color(1f, 1f, 0f, 0.8f); // Giallo visibile come in 8 Ball Pool
    public float lineWidth = 0.08f;
    public Color trajectoryColor = new Color(0f, 1f, 0f, 0.4f);
    public float trajectoryLineWidth = 0.04f;
    public int trajectorySegments = 20;
    public float trajectoryLength = 15f;

    [Header("Spin (English) - Intento di tiro")]
    [Range(-1f, 1f)] public float spinX = 0f; // english laterale: -1 sinistra, +1 destra
    [Range(-1f, 1f)] public float spinY = 0f; // verticale: -1 draw (sotto), +1 follow (sopra)
    public float visualSpinSpeed = 8f;        // velocità della rotazione visiva della pallina

    [Header("Spin su Sponde (Cushion English)")]
    [Tooltip("Quanto l'english devia il rimbalzo sulle sponde")]
    public float cushionEnglishStrength = 0.35f;
    [Tooltip("Frazione di english che resta dopo il rimbalzo su sponda (0-1)")]
    [Range(0f, 1f)] public float cushionEnglishRetention = 0.6f;

    [Header("Spin su Palline")]
    [Tooltip("Forza del follow (avanti) / draw (indietro) sulla cueball dopo l'urto")]
    public float followDrawStrength = 0.7f;
    [Tooltip("Quanto l'english devia la pallina colpita (throw)")]
    public float throwStrength = 0.18f;
    [Tooltip("Frazione di follow/draw che resta dopo aver colpito una pallina (0-1)")]
    [Range(0f, 1f)] public float verticalSpinRetentionOnBall = 0.15f;
    [Tooltip("Frazione di english che resta dopo aver colpito una pallina (0-1)")]
    [Range(0f, 1f)] public float englishRetentionOnBall = 0.7f;

    [Header("Decadimento Spin")]
    [Tooltip("Quanto velocemente svanisce il backspin (draw) mentre la palla viaggia")]
    public float drawDecayPerSecond = 0.6f;

    private bool isAiming = false;
    private Vector3 dragStartPos;
    private float currentPower = 0f;
    private float ballRadius = 0.5f;
    private float initialCueOffset = 0f;

    // Stato dello spin attivo per il tiro corrente (lo spin "intento" si azzera dopo il tiro)
    private float activeEnglish = 0f;   // english laterale attualmente sulla palla
    private float activeVertical = 0f;  // follow(+)/draw(-) attualmente sulla palla
    private Vector3 lastVelocity = Vector3.zero; // velocità pre-collisione (direzione/impatto reali)

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        mainCamera = Camera.main;

        if (cueMesh != null)
        {
            initialCueOffset = cueMesh.localPosition.z;
        }

        SphereCollider sc = GetComponent<SphereCollider>();
if (sc != null) ballRadius = sc.radius * transform.localScale.x;

        // Configurazione LineRenderer per linea di mira principale
lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startColor = lineColor;
        lineRenderer.endColor = lineColor;
        lineRenderer.positionCount = 0; // Nascosta di default

        // Creazione LineRenderer per la traiettoria prevista
        GameObject trajectoryObj = new GameObject("TrajectoryLine");
        trajectoryObj.transform.SetParent(transform);
        trajectoryLineRenderer = trajectoryObj.AddComponent<LineRenderer>();
        trajectoryLineRenderer.startWidth = trajectoryLineWidth;
        trajectoryLineRenderer.endWidth = trajectoryLineWidth;
        trajectoryLineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        trajectoryLineRenderer.startColor = trajectoryColor;
        trajectoryLineRenderer.endColor = trajectoryColor;
        trajectoryLineRenderer.positionCount = 0;

        // La stecca rimane sempre attiva e visibile nella scena
        if (cuePivot != null) cuePivot.gameObject.SetActive(true);
    }

    void Update()
    {
        if (Pointer.current == null) return;

        // Se la pallina si muove molto lentamente, fermarla completamente
        if (rb.linearVelocity.magnitude < sleepVelocityThreshold)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            activeEnglish = 0f;
            activeVertical = 0f;
        }

        // Se la pallina si muove, non si può mirare
        if (rb.linearVelocity.magnitude > 0.1f)
        {
            if (isAiming) ResetAim();
            if (cuePivot != null) cuePivot.gameObject.SetActive(false); // Nascondi stecca se pallina in movimento
            return;
        }
        else
        {
            if (cuePivot != null) cuePivot.gameObject.SetActive(true);
        }

        // Il pivot segue sempre la pallina
        if (cuePivot != null)
        {
            cuePivot.position = transform.position;
            
            // Se non stiamo caricando il tiro, facciamo ruotare la stecca verso il mouse
            if (!isAiming)
            {
                RotateCueTowardsMouse();
            }
        }

        // 1. Inizio mira (click)
        if (Pointer.current.press.wasPressedThisFrame)
        {
            isAiming = true;
            dragStartPos = GetPointerWorldPosition();
            lineRenderer.positionCount = 2;
            currentPower = 0f;
        }

        // 2. Aggiornamento mira (Drag) - simile a 8 Ball Pool
        if (isAiming && Pointer.current.press.isPressed)
        {
            UpdateAiming();
        }

        // 3. Rilascio del tiro
        if (Pointer.current.press.wasReleasedThisFrame && isAiming)
        {
            Shoot();
        }
    }

    void FixedUpdate()
    {
        if (rb == null) return;

        // Memorizza la velocità pre-collisione: serve per ricavare direzione e forza d'impatto reali
        // nelle collisioni (OnCollisionEnter viene chiamato dopo che la fisica ha gia' risolto l'urto).
        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        if (horizontalVelocity.sqrMagnitude > 0.0001f)
        {
            lastVelocity = rb.linearVelocity;
        }

        // Il backspin (draw) svanisce mentre la palla percorre il tavolo: piu' lunga la corsa, meno draw.
        // (L'english laterale invece non curva la palla sul panno, quindi non viene applicato qui.)
        if (activeVertical < 0f)
        {
            activeVertical = Mathf.MoveTowards(activeVertical, 0f, drawDecayPerSecond * Time.fixedDeltaTime);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (rb == null || collision.contactCount == 0) return;

        if (collision.rigidbody != null)
            HandleBallCollision(collision);    // urto contro un'altra pallina
        else
            HandleCushionCollision(collision); // urto contro una sponda/muro
    }

    // --- Sponda: l'english cambia l'angolo di rimbalzo (effetto reale dell'english sulla gomma) ---
    private void HandleCushionCollision(Collision collision)
    {
        if (Mathf.Approximately(activeEnglish, 0f)) return;

        Vector3 normal = collision.GetContact(0).normal;
        normal.y = 0f;
        if (normal.sqrMagnitude < 0.0001f) return;
        normal.Normalize();

        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        float speed = horizontalVelocity.magnitude;
        if (speed < 0.05f) return;

        // Direzione tangenziale lungo la sponda data dal verso dell'english (attrito al contatto):
        // l'english "morde" la gomma e spinge la palla lungo la sponda, allargando o stringendo il rimbalzo.
        Vector3 cushionDir = Vector3.Cross(Vector3.up, normal).normalized;
        rb.linearVelocity += cushionDir * activeEnglish * cushionEnglishStrength * speed;

        // L'english si riduce dopo aver morso la sponda, ma NON viene azzerato.
        activeEnglish *= cushionEnglishRetention;

        ApplyVisualSpin(rb.linearVelocity);
    }

    // --- Pallina: follow/draw sulla cueball e throw sulla pallina colpita ---
    private void HandleBallCollision(Collision collision)
    {
        Vector3 travelDir = new Vector3(lastVelocity.x, 0f, lastVelocity.z);
        float impactSpeed = travelDir.magnitude;
        if (impactSpeed < 0.05f) return;
        travelDir /= impactSpeed;

        Vector3 sideDir = Vector3.Cross(Vector3.up, travelDir).normalized; // destra rispetto al moto

        // FOLLOW / DRAW: dopo l'urto la cueball prosegue in avanti (follow, spinY>0)
        // oppure torna indietro (draw, spinY<0).
        rb.linearVelocity += travelDir * activeVertical * followDrawStrength * impactSpeed;

        // THROW: l'english devia leggermente la pallina colpita...
        Rigidbody other = collision.rigidbody;
        if (other != null)
        {
            other.linearVelocity += sideDir * activeEnglish * throwStrength * impactSpeed;
        }
        // ...e fa curvare un po' anche la cueball dopo il contatto.
        rb.linearVelocity += sideDir * activeEnglish * throwStrength * 0.5f * impactSpeed;

        // Lo spin viene in gran parte consumato dall'urto, ma non azzerato del tutto.
        activeVertical *= verticalSpinRetentionOnBall;
        activeEnglish *= englishRetentionOnBall;

        ApplyVisualSpin(rb.linearVelocity);
    }

    // Imposta una rotazione visiva coerente con lo spin attivo (non influisce sulla fisica del gioco).
    private void ApplyVisualSpin(Vector3 directionHint)
    {
        Vector3 dir = new Vector3(directionHint.x, 0f, directionHint.z);
        if (dir.sqrMagnitude < 0.0001f) dir = new Vector3(lastVelocity.x, 0f, lastVelocity.z);
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        Vector3 sideAxis = Vector3.Cross(Vector3.up, dir).normalized;
        // english attorno all'asse verticale + follow/draw attorno all'asse laterale.
        Vector3 angular = (Vector3.up * activeEnglish - sideAxis * activeVertical) * visualSpinSpeed;
        rb.angularVelocity = angular;
    }

    private void RotateCueTowardsMouse()
    {
        Vector3 mousePos = GetPointerWorldPosition();
        Vector3 dir = (transform.position - mousePos).normalized;
        dir.y = 0;
        if (dir.sqrMagnitude > 0.001f)
        {
            cuePivot.forward = dir;
        }
    }

    private void UpdateAiming()
{
        Vector3 currentMousePos = GetPointerWorldPosition();
        
        // Direzione di trascinamento (dal punto di click al mouse attuale)
        Vector3 dragVector = dragStartPos - currentMousePos;
        dragVector.y = 0;
        
        // La potenza è basata sulla distanza di trascinamento (8 Ball Pool style)
        float dragDistance = Mathf.Clamp(dragVector.magnitude, 0f, maxDragDistance);
        currentPower = (dragDistance / maxDragDistance) * 100f; // Percentuale 0-100%

        if (dragDistance > 0.05f)
        {
            // Direzione del tiro (dalla pallina verso dove stiamo trascinando)
            Vector3 shootDirection = dragVector.normalized;
            
            // La stecca guarda nella direzione opposta al tiro (dietro la pallina)
            if (cuePivot != null)
            {
                cuePivot.forward = -shootDirection;
            }

            // Arretrare la stecca lungo il suo asse Z per effetto carica
            if (cueMesh != null)
            {
                cueMesh.localPosition = new Vector3(0, 0, initialCueOffset + dragDistance);
            }

            // Disegnare la linea di mira principale (da pallina verso direzione tiro)
            lineRenderer.SetPosition(0, transform.position);
            lineRenderer.SetPosition(1, transform.position + shootDirection * 8f);
            
            // Disegnare la traiettoria prevista della pallina
            DrawTrajectoryPreview(shootDirection, dragDistance);
        }
        else
        {
            trajectoryLineRenderer.positionCount = 0;
        }
    }

    private void DrawTrajectoryPreview(Vector3 shootDirection, float dragDistance)
    {
        // Calcola la forza che verrà applicata
        float appliedForce = dragDistance * forceMultiplier;
        appliedForce = Mathf.Min(appliedForce, maxForce);
        
        // Simula la traiettoria della pallina
        Vector3 currentPos = transform.position;
        Vector3 currentVel = shootDirection * (appliedForce / rb.mass);
        
        List<Vector3> trajectoryPoints = new List<Vector3>();
        trajectoryPoints.Add(currentPos);

        float timeStep = 0.05f;
        float gravity = Physics.gravity.y;

        for (int i = 0; i < trajectorySegments; i++)
        {
            // Applica gravità e attrito
            currentVel.y += gravity * timeStep;
            currentVel *= 0.98f; // Attrito dell'aria
            
            currentPos += currentVel * timeStep;

            // Se la pallina colpisce il terreno, ferma la simulazione
            if (currentPos.y <= 0.01f)
            {
                currentPos.y = 0.01f;
                break;
            }

            trajectoryPoints.Add(currentPos);

            // Ferma se la velocità è troppo bassa
            if (currentVel.magnitude < 0.1f)
                break;
        }

        // Disegna la traiettoria
        trajectoryLineRenderer.positionCount = trajectoryPoints.Count;
        for (int i = 0; i < trajectoryPoints.Count; i++)
        {
            trajectoryLineRenderer.SetPosition(i, trajectoryPoints[i]);
        }
    }

    private void Shoot()
    {
        isAiming = false;
        lineRenderer.positionCount = 0; // Nascondi linea principale
        trajectoryLineRenderer.positionCount = 0; // Nascondi traiettoria

        // Direzione del tiro (opposta a dove guarda la stecca)
        Vector3 shootDirection = -cuePivot.forward;

        // Calcola la forza basata sul trascinamento
        float appliedForce = currentPower / 100f * maxForce;

        if (appliedForce > 0.2f)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            float clampedSpinX = Mathf.Clamp(spinX, -1f, 1f);
            float clampedSpinY = Mathf.Clamp(spinY, -1f, 1f);

            // Velocità lineare diretta: predicibile e coerente con l'anteprima di traiettoria.
            float speed = appliedForce / rb.mass;
            Vector3 velocity = shootDirection * speed;
            rb.linearVelocity = velocity;
            lastVelocity = velocity;

            // Cattura lo spin "intento" come spin attivo del tiro corrente.
            activeEnglish = clampedSpinX;
            activeVertical = clampedSpinY;

            // Rotazione visiva iniziale della pallina.
            ApplyVisualSpin(shootDirection);

            // Lo spin impostato si azzera dopo il tiro.
            spinX = 0f;
            spinY = 0f;
        }

        // Riposiziona la stecca
        if (cueMesh != null)
        {
            cueMesh.localPosition = new Vector3(0, 0, initialCueOffset);
        }
    }

    public void SetSpin(float x, float y)
    {
        spinX = Mathf.Clamp(x, -100f, 100f) / 100f;
        spinY = Mathf.Clamp(y, -100f, 100f) / 100f;
    }

    private void ResetAim()
    {
        isAiming = false;
        lineRenderer.positionCount = 0;
        trajectoryLineRenderer.positionCount = 0;
        if (cueMesh != null) cueMesh.localPosition = new Vector3(0, 0, initialCueOffset);
    }

    private Vector3 GetPointerWorldPosition()
    {
        Vector2 pointerPosition = Pointer.current.position.ReadValue();
        Ray ray = mainCamera.ScreenPointToRay(pointerPosition);
        Plane plane = new Plane(Vector3.up, transform.position);
        
        if (plane.Raycast(ray, out float distance))
        {
            return ray.GetPoint(distance);
        }
        return Vector3.zero;
    }
}