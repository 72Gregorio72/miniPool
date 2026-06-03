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

    private bool isAiming = false;
    private Vector3 dragStartPos;
    private float currentPower = 0f;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        mainCamera = Camera.main;

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
        }

        // Se la pallina si muove, non si può mirare
        if (rb.linearVelocity.magnitude > 0.1f)
        {
            if (isAiming) ResetAim();
            return;
        }

        // Il pivot segue sempre la pallina
        if (cuePivot != null)
        {
            cuePivot.position = transform.position;
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
                cueMesh.localPosition = new Vector3(0, 0, dragDistance);
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
            rb.AddForce(shootDirection * appliedForce, ForceMode.Impulse);
        }

        // Riposiziona la stecca
        if (cueMesh != null)
        {
            cueMesh.localPosition = Vector3.zero;
        }
    }

    private void ResetAim()
    {
        isAiming = false;
        lineRenderer.positionCount = 0;
        trajectoryLineRenderer.positionCount = 0;
        if (cueMesh != null) cueMesh.localPosition = Vector3.zero;
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