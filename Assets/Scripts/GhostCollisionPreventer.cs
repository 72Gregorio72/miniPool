using Unity.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class GhostCollisionPreventer : MonoBehaviour
{
    private Rigidbody _rigidbody;
    private int _colliderInstanceId;

    // Angolo massimo di inclinazione accettato come "pavimento". 
    // Se il terreno è piatto, la normale dovrebbe essere (0, 1, 0). 
    // Se la normale calcolata devia di pochissimo a causa del ghost bounce, la correggiamo.
    [SerializeField] private float maxFloorAngle = 1f; 

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        
        Collider playerCollider = GetComponent<Collider>();
        if (playerCollider != null)
        {
            _colliderInstanceId = playerCollider.GetInstanceID();
        }
    }

    private void OnEnable()
    {
        Physics.ContactModifyEventCCD += OnContactModify;
    }

    private void OnDisable()
    {
        // --- CORRETTO QUI ---
        // Ora disiscriviamo il metodo corretto (OnContactModify) e non OnDisable!
        Physics.ContactModifyEventCCD -= OnContactModify; 
    }

    private void OnContactModify(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
    {
        for (int i = 0; i < pairs.Length; i++)
        {
            ModifiableContactPair pair = pairs[i];

            if (pair.colliderInstanceID == _colliderInstanceId || pair.otherColliderInstanceID == _colliderInstanceId)
            {
                for (int j = 0; j < pair.contactCount; j++)
                {
                    Vector3 normal = pair.GetNormal(j);
                    
                    // Calcoliamo l'angolo tra la normale calcolata dall'impatto e il vettore World Up (Vector3.up)
                    float angle = Vector3.Angle(normal, Vector3.up);

                    // Se l'angolo è quasi verticale ma c'è una micro-deviazione causata dal ghost bounce...
                    if (angle > 0f && angle < maxFloorAngle)
                    {
                        // Costringiamo la fisica a vedere la superficie come perfettamente orizzontale
                        pair.SetNormal(j, Vector3.up);
                    }
                }
            }
        }
    }
}