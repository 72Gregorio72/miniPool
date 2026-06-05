using UnityEngine;

public class PoolTablePhysicsGenerator : MonoBehaviour
{
    [Header("Impostazioni Griglia")]
    public float tileSize = 1f; // La dimensione di un singolo quadrato (es: 1x1)
    
    // Puoi impostare queste misure a mano o fartele passare dal tuo GridManager
    public int width = 10; 
    public int length = 20;

    void Start()
    {
        GeneratePhysicsBounds();
    }

    void GeneratePhysicsBounds()
    {
        // Creiamo un oggetto contenitore per la fisica invisibile
        GameObject physicsContainer = new GameObject("Invisible_Physics_Collider");
        physicsContainer.transform.SetParent(this.transform);
        physicsContainer.transform.localPosition = Vector3.zero;

        // 1. IL PAVIMENTO (Un unico grande Box Collider liscio)
        GameObject floor = new GameObject("Floor_Collider");
        floor.transform.SetParent(physicsContainer.transform);
        
        // Posizioniamo il pavimento al centro della griglia
        float centerX = (width * tileSize) / 2f - (tileSize / 2f);
        float centerZ = (length * tileSize) / 2f - (tileSize / 2f);
        floor.transform.localPosition = new Vector3(centerX, -0.1f, centerZ); // leggermente sotto la superficie

        BoxCollider floorCol = floor.AddComponent<BoxCollider>();
        floorCol.size = new Vector3(width * tileSize, 0.2f, length * tileSize);


        // 2. I MURI (4 Box Collider lunghi e lisci per i bordi)
        // Se la tua mappa ha una forma a L o strana, possiamo adattarlo, 
        // ma per un rettangolo standard si fa così:
        
        CreateWall("Wall_Left", new Vector3(-tileSize/2f, 0.5f, centerZ), new Vector3(0.2f, 1f, length * tileSize), physicsContainer.transform);
        CreateWall("Wall_Right", new Vector3((width - 0.5f) * tileSize, 0.5f, centerZ), new Vector3(0.2f, 1f, length * tileSize), physicsContainer.transform);
        CreateWall("Wall_Bottom", new Vector3(centerX, 0.5f, -tileSize/2f), new Vector3(width * tileSize, 1f, 0.2f), physicsContainer.transform);
        CreateWall("Wall_Top", new Vector3(centerX, 0.5f, (length - 0.5f) * tileSize), new Vector3(width * tileSize, 1f, 0.2f), physicsContainer.transform);
    }

    void CreateWall(string name, Vector3 pos, Vector3 size, Transform parent)
    {
        GameObject wall = new GameObject(name);
        wall.transform.SetParent(parent);
        wall.transform.localPosition = pos;
        BoxCollider col = wall.AddComponent<BoxCollider>();
        col.size = size;
    }
}