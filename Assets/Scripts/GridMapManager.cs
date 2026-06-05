using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class GridMapManager : MonoBehaviour, ISerializationCallbackReceiver
{
    public float cellSize = 1f;
	
    [Min(0f)] public float seamOverlap = 0.01f;

    public enum ManualTileKind
    {
        Centro,
        Bordo,
        Angolo,
        DoubleEdge,
        TripleEdge,
        Closed
    }

    [System.Serializable]
    public class TileSet
    {
        public string name = "Tile Set";

        [Header("Prefabs Puliti (Rotazione 0,0,0)")]
        public GameObject prefabCentro;
        public GameObject prefabBordo;
        public GameObject prefabAngolo;
        public GameObject prefabDoubleEdge;
        public GameObject prefabTripleEdge;
        public GameObject prefabClosed;
    }

    [SerializeField]
    private List<TileSet> tileSets = new List<TileSet>();

    [SerializeField]
    private int activeTileSetIndex = 0;

    [System.Serializable]
    public struct GridCell
    {
        public Vector2Int position;
        public GameObject gameObject;
    }
    
    [HideInInspector] 
    [SerializeField] private List<GridCell> serializedGrid = new List<GridCell>();
    
    private Dictionary<Vector2Int, GameObject> gridData = new Dictionary<Vector2Int, GameObject>();

    public int ActiveTileSetIndex
    {
        get => tileSets.Count == 0 ? 0 : Mathf.Clamp(activeTileSetIndex, 0, tileSets.Count - 1);
        set => activeTileSetIndex = Mathf.Max(0, value);
    }

    public List<TileSet> TileSets => tileSets;

    public TileSet GetActiveTileSet()
    {
        if (tileSets.Count == 0)
        {
            tileSets.Add(new TileSet { name = "Default" });
        }

        activeTileSetIndex = Mathf.Clamp(activeTileSetIndex, 0, tileSets.Count - 1);
        return tileSets[activeTileSetIndex];
    }

    public bool HasBlockAt(int x, int z)
    {
        Vector2Int key = new Vector2Int(x, z);
        if (gridData.TryGetValue(key, out GameObject go))
        {
            return go != null;
        }
        return false;
    }

    private bool IsClearAroundHorizontal(int x, int z)
    {
        return !HasBlockAt(x - 1, z) && !HasBlockAt(x + 1, z)
            && !HasBlockAt(x - 2, z) && !HasBlockAt(x + 2, z);
    }

    private bool IsClearAroundVertical(int x, int z)
    {
        return !HasBlockAt(x, z - 1) && !HasBlockAt(x, z + 1)
            && !HasBlockAt(x, z - 2) && !HasBlockAt(x, z + 2);
    }

    public void SetBlock(int x, int z)
    {
        Vector2Int pos = new Vector2Int(x, z);
        
        if (!gridData.ContainsKey(pos))
        {
            gridData.Add(pos, null);
        }

        // Aggiorna la cella stessa
        AggiornaBloccoIn(x, z);

        RefreshAdjacentBlocks(x, z);
    }

    public void ReplaceBlockAt(int x, int z)
    {
        Vector2Int pos = new Vector2Int(x, z);

        if (!gridData.ContainsKey(pos))
        {
            gridData.Add(pos, null);
        }

        AggiornaBloccoIn(x, z);
    }

    public GameObject GetManualPrefab(ManualTileKind tileKind)
    {
        TileSet tileSet = GetActiveTileSet();

        switch (tileKind)
        {
            case ManualTileKind.Centro:
                return tileSet.prefabCentro;
            case ManualTileKind.Bordo:
                return tileSet.prefabBordo;
            case ManualTileKind.Angolo:
                return tileSet.prefabAngolo;
            case ManualTileKind.DoubleEdge:
                return tileSet.prefabDoubleEdge;
            case ManualTileKind.TripleEdge:
                return tileSet.prefabTripleEdge;
            case ManualTileKind.Closed:
                return tileSet.prefabClosed;
            default:
                return null;
        }
    }

    public bool TryGetAutoTilePreview(int x, int z, out GameObject prefabDaUsare, out float rotazioneY)
    {
        TileSet tileSet = GetActiveTileSet();
        int mask = 0;
        if (HasBlockAt(x, z + 1)) mask += 1;
        if (HasBlockAt(x + 1, z)) mask += 2;
        if (HasBlockAt(x, z - 1)) mask += 4;
        if (HasBlockAt(x - 1, z)) mask += 8;

        prefabDaUsare = null;
        rotazioneY = 0f;

        switch (mask)
        {
            case 15:
                prefabDaUsare = tileSet.prefabCentro;
                rotazioneY = 0f;
                return true;
            case 0:
                prefabDaUsare = tileSet.prefabClosed;
                rotazioneY = 0f;
                return true;
            case 6:
                prefabDaUsare = tileSet.prefabAngolo;
                rotazioneY = 180f;
                return true;
            case 12:
                prefabDaUsare = tileSet.prefabAngolo;
                rotazioneY = 270f;
                return true;
            case 9:
                prefabDaUsare = tileSet.prefabAngolo;
                rotazioneY = 0f;
                return true;
            case 3:
                prefabDaUsare = tileSet.prefabAngolo;
                rotazioneY = 90f;
                return true;
            case 14:
                prefabDaUsare = tileSet.prefabBordo;
                rotazioneY = 0f;
                return true;
            case 7:
                prefabDaUsare = tileSet.prefabBordo;
                rotazioneY = 270f;
                return true;
            case 11:
                prefabDaUsare = tileSet.prefabBordo;
                rotazioneY = 180f;
                return true;
            case 13:
                prefabDaUsare = tileSet.prefabBordo;
                rotazioneY = 90f;
                return true;
            case 4:
                prefabDaUsare = tileSet.prefabTripleEdge;
                rotazioneY = 0f;
                return true;
            case 2:
                prefabDaUsare = tileSet.prefabTripleEdge;
                rotazioneY = 270f;
                return true;
            case 1:
                prefabDaUsare = tileSet.prefabTripleEdge;
                rotazioneY = 180f;
                return true;
            case 8:
                prefabDaUsare = tileSet.prefabTripleEdge;
                rotazioneY = 90f;
                return true;
            case 5:
                prefabDaUsare = tileSet.prefabDoubleEdge;
                rotazioneY = 0f;
                return true;
            case 10:
                prefabDaUsare = tileSet.prefabDoubleEdge;
                rotazioneY = 90f;
                return true;
            default:
                prefabDaUsare = tileSet.prefabBordo;
                rotazioneY = 180f;
                return true;
        }
    }

    public void PlaceManualTileAt(int x, int z, ManualTileKind tileKind, float rotationY)
    {
        Vector2Int pos = new Vector2Int(x, z);

        if (!gridData.ContainsKey(pos))
        {
            gridData.Add(pos, null);
        }

        GameObject prefabDaUsare = GetManualPrefab(tileKind);

        if (prefabDaUsare == null)
        {
            return;
        }

        if (gridData[pos] != null)
        {
#if UNITY_EDITOR
            Undo.DestroyObjectImmediate(gridData[pos]);
#else
            Destroy(gridData[pos]);
#endif
            gridData[pos] = null;
        }

        Vector3 worldPos = new Vector3(x * cellSize, 0, z * cellSize);
        GameObject nuovoBlocco = null;

#if UNITY_EDITOR
        nuovoBlocco = (GameObject)PrefabUtility.InstantiatePrefab(prefabDaUsare, transform);
        nuovoBlocco.transform.position = worldPos;
        nuovoBlocco.transform.rotation = Quaternion.Euler(0, rotationY, 0);
    ApplySeamOverlap(nuovoBlocco);
        Undo.RegisterCreatedObjectUndo(nuovoBlocco, "Place Manual Tile");
#else
        nuovoBlocco = Instantiate(prefabDaUsare, worldPos, Quaternion.Euler(0, rotationY, 0), transform);
    ApplySeamOverlap(nuovoBlocco);
#endif

        gridData[pos] = nuovoBlocco;
    }

    public void RemoveBlock(int x, int z)
    {
        Vector2Int pos = new Vector2Int(x, z);
        if (!gridData.ContainsKey(pos)) return;

        if (gridData[pos] != null)
        {
#if UNITY_EDITOR
            Undo.DestroyObjectImmediate(gridData[pos]);
#else
            Destroy(gridData[pos]);
#endif
            gridData[pos] = null;
        }

        RefreshAdjacentBlocks(x, z);
    }

    private void RefreshAdjacentBlocks(int x, int z)
    {
        Vector2Int nord = new Vector2Int(x, z + 1);
        Vector2Int est = new Vector2Int(x + 1, z);
        Vector2Int sud = new Vector2Int(x, z - 1);
        Vector2Int ovest = new Vector2Int(x - 1, z);

        if (gridData.TryGetValue(nord, out GameObject gN) && gN != null) AggiornaBloccoIn(nord.x, nord.y);
        if (gridData.TryGetValue(est, out GameObject gE) && gE != null) AggiornaBloccoIn(est.x, est.y);
        if (gridData.TryGetValue(sud, out GameObject gS) && gS != null) AggiornaBloccoIn(sud.x, sud.y);
        if (gridData.TryGetValue(ovest, out GameObject gO) && gO != null) AggiornaBloccoIn(ovest.x, ovest.y);
    }

    private void AggiornaBloccoIn(int x, int z)
    {
        Vector2Int pos = new Vector2Int(x, z);
        if (!gridData.ContainsKey(pos)) return;

        if (gridData[pos] != null)
        {
            DestroyImmediate(gridData[pos]);
            gridData[pos] = null;
        }

        // 1. Calcola la Bitmask (N=1, E=2, S=4, O=8)
        int mask = 0;
        if (HasBlockAt(x, z + 1)) mask += 1;
        if (HasBlockAt(x + 1, z)) mask += 2;
        if (HasBlockAt(x, z - 1)) mask += 4;
        if (HasBlockAt(x - 1, z)) mask += 8;

        GameObject prefabDaUsare = null;
        float rotazioneY = 0f;
        TileSet tileSet = GetActiveTileSet();

        // 2. Mappatura Rotazioni Corretta per Muri verso l'ESTERNO
        switch (mask)
        {
            case 15: // Centro pieno, circondato da ogni lato
                prefabDaUsare = tileSet.prefabCentro;
                rotazioneY = 0f;
                break;

            case 0: // Nessun vicino: tile chiuso
                prefabDaUsare = tileSet.prefabClosed;
                rotazioneY = 0f;
                break;

            // --- ANGOLI ESTERNI ---
            case 6: // Vicini a Est (2) e Sud (4) -> Angolo Alto-Sinistra del perimetro
                prefabDaUsare = tileSet.prefabAngolo;
                rotazioneY = 180f; 
                break;
            case 12: // Vicini a Sud (4) e Ovest (8) -> Angolo Alto-Destra del perimetro
                prefabDaUsare = tileSet.prefabAngolo;
                rotazioneY = 270f; 
                break;
            case 9: // Vicini a Ovest (8) e Nord (1) -> Angolo Basso-Destra del perimetro
                prefabDaUsare = tileSet.prefabAngolo;
                rotazioneY = 0f; 
                break;
            case 3: // Vicini a Nord (1) e Est (2) -> Angolo Basso-Sinistra del perimetro
                prefabDaUsare = tileSet.prefabAngolo;
                rotazioneY = 90f; 
                break;

            // --- BORDI DIRITTI (Tre vicini o linee singole) ---
            case 14: // Vicini a E, S, O (Manca Nord -> Muro a Nord)
                prefabDaUsare = tileSet.prefabBordo;
                rotazioneY = 0f;
                break;

            case 7:  // Vicini a N, E, S (Manca Ovest -> Muro a Ovest)
                prefabDaUsare = tileSet.prefabBordo;
                rotazioneY = 270f;
                break;

            case 11: // Vicini a N, E, O (Manca Sud -> Muro a Sud)
                prefabDaUsare = tileSet.prefabBordo;
                rotazioneY = 180f;
                break;

            case 13: // Vicini a N, S, O (Manca Est -> Muro a Est)
                prefabDaUsare = tileSet.prefabBordo;
                rotazioneY = 90f;
                break;

            case 4:  // Solo vicino a Sud (Inizio linea da Sud verso Nord)
                prefabDaUsare = tileSet.prefabTripleEdge;
                rotazioneY = 0f; 
                break;

            case 2:  // Solo vicino a Est
                prefabDaUsare = tileSet.prefabTripleEdge;
                rotazioneY = 270f; 
                break;

            case 1:  // Solo vicino a Nord
                prefabDaUsare = tileSet.prefabTripleEdge;
                rotazioneY = 180f; 
                break;

            case 8:  // Solo vicino a Ovest
                prefabDaUsare = tileSet.prefabTripleEdge;
                rotazioneY = 90f; 
                break;

            // --- CORRIDOI / LINEE DRITTE ---
            case 5:  // Vicini a Nord e Sud (Due lati opposti verticali)
                prefabDaUsare = tileSet.prefabDoubleEdge; 
                rotazioneY = 0f; // verticale
                break;
            case 10: // Vicini a Est e Ovest (Due lati opposti orizzontali)
                prefabDaUsare = tileSet.prefabDoubleEdge;
                rotazioneY = 90f;
                break;

            default: // Caso 0 (Isolato)
                prefabDaUsare = tileSet.prefabBordo;
                rotazioneY = 180f;
                break;
        }

        // 3. Istanziazione pulita tramite PrefabUtility legato al file originale
        if (prefabDaUsare != null)
        {
            Vector3 worldPos = new Vector3(x * cellSize, 0, z * cellSize);
            GameObject nuovoBlocco = null;

#if UNITY_EDITOR
            nuovoBlocco = (GameObject)PrefabUtility.InstantiatePrefab(prefabDaUsare, transform);
            nuovoBlocco.transform.position = worldPos;
            nuovoBlocco.transform.rotation = Quaternion.Euler(0, rotazioneY, 0);
            ApplySeamOverlap(nuovoBlocco);
            
            Undo.RegisterCreatedObjectUndo(nuovoBlocco, "AutoTile Build");
#else
            nuovoBlocco = Instantiate(prefabDaUsare, worldPos, Quaternion.Euler(0, rotazioneY, 0), transform);
            ApplySeamOverlap(nuovoBlocco);
#endif
            gridData[pos] = nuovoBlocco;
        }
    }

    private void ApplySeamOverlap(GameObject block)
    {
        if (block == null || seamOverlap <= 0f)
        {
            return;
        }

        Vector3 scale = block.transform.localScale;
        float factor = 1f + seamOverlap;
        block.transform.localScale = new Vector3(scale.x * factor, scale.y, scale.z * factor);
    }

    // --- SERIALIZZAZIONE PER EVITARE CHE SCOMPAIA NELL'EDITOR ---
    public void OnBeforeSerialize()
    {
        serializedGrid.Clear();
        foreach (var kvp in gridData)
        {
            serializedGrid.Add(new GridCell { position = kvp.Key, gameObject = kvp.Value });
        }
    }

    public void OnAfterDeserialize()
    {
        gridData.Clear();
        foreach (var cell in serializedGrid)
        {
            if (cell.gameObject != null)
            {
                gridData[cell.position] = cell.gameObject;
            }
        }
    }

	public void ClearGrid()
	{
		foreach (var kvp in gridData)
		{
			if (kvp.Value != null)
			{
#if UNITY_EDITOR
				Undo.DestroyObjectImmediate(kvp.Value);
#else
				Destroy(kvp.Value);
#endif
			}
		}
		gridData.Clear();
	}
}