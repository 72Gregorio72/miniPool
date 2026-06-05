using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshCollider))]
public class GridMapColliderGenerator : MonoBehaviour
{
    private MeshFilter _meshFilter;
    private MeshCollider _meshCollider;

    private void Awake()
    {
        _meshFilter = GetComponent<MeshFilter>();
        _meshCollider = GetComponent<MeshCollider>();
    }

    /// <summary>
    /// Prende tutti i MeshFilter dei figli (i prefab istanziati), li fonde in una mesh unica
    /// e la assegna al MeshCollider globale, eliminando i collider dai singoli prefab.
    /// </summary>
    public void GenerateCollider(Dictionary<Vector2Int, GameObject> gridData, float cellSize)
    {
        if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();
        if (_meshCollider == null) _meshCollider = GetComponent<MeshCollider>();
        if (_meshFilter == null || _meshCollider == null) return;

        // Lista che conterrà i dati di ogni singola mesh da fondere
        List<CombineInstance> combineList = new List<CombineInstance>();

        // Lista di supporto per disattivare temporaneamente i vecchi collider dei singoli prefab
        List<Collider> individualColliders = new List<Collider>();

        foreach (var kvp in gridData)
        {
            if (kvp.Value == null) continue;

            // Cerchiamo tutti i MeshFilter all'interno del prefab (può essercene uno o più nei figli)
            MeshFilter[] filters = kvp.Value.GetComponentsInChildren<MeshFilter>();
            
            // Troviamo anche i collider interni del prefab per disattivarli (altrimenti avremmo doppie collisioni)
            Collider[] colliders = kvp.Value.GetComponentsInChildren<Collider>();
            individualColliders.AddRange(colliders);

            foreach (var filter in filters)
            {
                // Salta il MeshFilter del Manager stesso per evitare loop infiniti
                if (filter == _meshFilter) continue; 
                if (filter.sharedMesh == null) continue;

                CombineInstance ci = new CombineInstance();
                ci.mesh = filter.sharedMesh;
                
                // Questo passaggio è fondamentale: dice a Unity come la mesh è posizionata,
                // scalata e ruotata nel mondo rispetto al GridManager principale
                ci.transform = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                
                combineList.Add(ci);
            }
        }

        if (combineList.Count == 0) return;

        // Generiamo la mesh fusa finale
        Mesh finalMesh = new Mesh();
        finalMesh.name = "GridMap_FusedPrefabCollider";
        
        // Uniamo le mesh (true = unisce i sub-mesh con lo stesso materiale, molto performante)
        finalMesh.CombineMeshes(combineList.ToArray(), true, true);
        finalMesh.RecalculateNormals();
        finalMesh.RecalculateBounds();

        // Assegniamo la mesh gigante al nostro MeshCollider
        _meshFilter.sharedMesh = finalMesh;
        _meshCollider.sharedMesh = finalMesh;

        // Spengiamo i collider dei singoli prefab istanziati così la fisica calcola SOLO la mesh fusa
        foreach (var col in individualColliders)
        {
            // Se sei nell'editor usiamo DestroyImmediate, altrimenti .enabled = false
            if (col != _meshCollider)
            {
                col.enabled = false; 
            }
        }
    }
}