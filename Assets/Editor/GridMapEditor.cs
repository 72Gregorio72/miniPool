using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(GridMapManager))]
public class GridMapEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GridMapManager manager = (GridMapManager)target;

        EditorGUILayout.Space();
        if (GUILayout.Button("Open Grid Tool Window"))
        {
            GridMapToolWindow.ShowWindow(manager);
        }

        if (GUILayout.Button("Select Grid Tool Window"))
        {
            GridMapToolWindow.FocusExistingWindow();
        }

        if (GUILayout.Button("Clear Grid"))
        {
            Undo.RecordObject(manager, "Clear Grid");
            manager.ClearGrid();
            EditorUtility.SetDirty(manager);
            // Force Inspector to repaint to reflect cleared state
            Repaint();
        }
    }
}

public class GridMapToolWindow : EditorWindow
{
    private enum BrushMode
    {
        Paint,
        ManualStamp,
        Eraser
    }

    private static GridMapToolWindow existingWindow;
    private GridMapManager manager;
    private BrushMode brushMode = BrushMode.Paint;
    private bool toolEnabled = true;
    private GridMapManager.ManualTileKind manualTileKind = GridMapManager.ManualTileKind.Bordo;
    private int manualRotationIndex = 0;

    private static Material previewMaterial;

    public static void ShowWindow(GridMapManager targetManager = null)
    {
        GridMapToolWindow window = GetWindow<GridMapToolWindow>("Grid Map Tool");
        window.minSize = new Vector2(320f, 180f);
        window.manager = targetManager;
        window.Show();
        existingWindow = window;
    }

    public static void FocusExistingWindow()
    {
        if (existingWindow != null)
        {
            existingWindow.Focus();
        }
        else
        {
            ShowWindow();
        }
    }

    private void OnEnable()
    {
        existingWindow = this;
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        if (existingWindow == this)
        {
            existingWindow = null;
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Grid Tool", EditorStyles.boldLabel);
        manager = (GridMapManager)EditorGUILayout.ObjectField("Target", manager, typeof(GridMapManager), true);

        if (GUILayout.Button("Use Selected GridMapManager"))
        {
            manager = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<GridMapManager>()
                : null;
            if (manager == null)
            {
                Debug.LogWarning("Seleziona un GameObject con GridMapManager.");
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Active Tile Set", EditorStyles.boldLabel);
        if (manager != null)
        {
            int setCount = manager.TileSets.Count;
            if (setCount == 0)
            {
                EditorGUILayout.HelpBox("Aggiungi almeno un Tile Set nel GridMapManager.", MessageType.Warning);
            }
            else
            {
                string[] labels = new string[setCount];
                for (int i = 0; i < setCount; i++)
                {
                    labels[i] = string.IsNullOrWhiteSpace(manager.TileSets[i].name) ? $"Tile Set {i + 1}" : manager.TileSets[i].name;
                }

                int selectedIndex = GUILayout.SelectionGrid(manager.ActiveTileSetIndex, labels, Mathf.Min(3, setCount));
                if (selectedIndex != manager.ActiveTileSetIndex)
                {
                    Undo.RecordObject(manager, "Change Active Tile Set");
                    manager.ActiveTileSetIndex = selectedIndex;
                    EditorUtility.SetDirty(manager);
                }
            }
        }

        toolEnabled = EditorGUILayout.Toggle("Tool Enabled", toolEnabled);
        brushMode = (BrushMode)GUILayout.Toolbar((int)brushMode, new[] { "Paint", "Manual Stamp", "Eraser" });

        EditorGUILayout.Space();
        if (brushMode == BrushMode.ManualStamp)
        {
            manualTileKind = (GridMapManager.ManualTileKind)EditorGUILayout.EnumPopup("Manual Tile(R to rotate)", manualTileKind);
            manualRotationIndex = GUILayout.SelectionGrid(manualRotationIndex, new[] { "0", "90", "180", "270" }, 4);
        }

        EditorGUILayout.HelpBox("Paint: usa l'autotiling. Manual Stamp: sostituisce la cella con il prefab scelto senza calcolare la bitmask. Eraser: cancella il tile sotto il cursore.", MessageType.Info);

        EditorGUILayout.Space();
        if (manager != null && GUILayout.Button("Clear Grid"))
        {
            Undo.RecordObject(manager, "Clear Grid");
            manager.ClearGrid();
            EditorUtility.SetDirty(manager);
        }
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (!toolEnabled || manager == null)
        {
            return;
        }

        Event e = Event.current;

        if (brushMode == BrushMode.ManualStamp && e.type == EventType.KeyDown && e.keyCode == KeyCode.R)
        {
            manualRotationIndex = (manualRotationIndex + 1) % 4;
            e.Use();
            Repaint();
            sceneView.Repaint();
            return;
        }

		if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
		{
			toolEnabled = false;
			e.Use();
			Repaint();
			sceneView.Repaint();
			return;
		}

		if (e.type == EventType.KeyDown && e.keyCode == KeyCode.P)
		{
			brushMode = BrushMode.Paint;
			e.Use();
			Repaint();
			sceneView.Repaint();
			return;
		}

		if (e.type == EventType.KeyDown && e.keyCode == KeyCode.E)
		{
			brushMode = BrushMode.Eraser;
			e.Use();
			Repaint();
			sceneView.Repaint();
			return;
		}

        Plane plane = new Plane(Vector3.up, Vector3.zero);
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

        if (!plane.Raycast(ray, out float enter))
        {
            return;
        }

        Vector3 hitPoint = ray.GetPoint(enter);
        int gridX = Mathf.RoundToInt(hitPoint.x / manager.cellSize);
        int gridZ = Mathf.RoundToInt(hitPoint.z / manager.cellSize);
        Vector3 cubeCenter = new Vector3(gridX * manager.cellSize, 0f, gridZ * manager.cellSize);

        if (brushMode == BrushMode.Paint)
        {
            if (manager.TryGetAutoTilePreview(gridX, gridZ, out GameObject prefab, out float rotationY))
            {
                DrawPrefabPreview(prefab, cubeCenter, rotationY, new Color(1f, 1f, 1f, 0.5f));
            }
        }
        else if (brushMode == BrushMode.ManualStamp)
        {
            GameObject prefab = manager.GetManualPrefab(manualTileKind);
            DrawPrefabPreview(prefab, cubeCenter, GetManualRotationY(), new Color(1f, 1f, 1f, 0.5f));
        }
        else
        {
            Handles.color = Color.red;
            Handles.DrawWireCube(cubeCenter, new Vector3(manager.cellSize, 0.1f, manager.cellSize));
        }

        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0)
        {
            Undo.RegisterCompleteObjectUndo(manager, "Grid Tool Action");

            switch (brushMode)
            {
                case BrushMode.Paint:
                    manager.SetBlock(gridX, gridZ);
                    break;
                case BrushMode.ManualStamp:
                    manager.PlaceManualTileAt(gridX, gridZ, manualTileKind, GetManualRotationY());
                    break;
                case BrushMode.Eraser:
                    manager.RemoveBlock(gridX, gridZ);
                    break;
            }

            EditorUtility.SetDirty(manager);
            e.Use();
            sceneView.Repaint();
        }
        else
        {
            HandleUtility.Repaint();
        }
    }

    private float GetManualRotationY()
    {
        switch (manualRotationIndex)
        {
            case 1:
                return 90f;
            case 2:
                return 180f;
            case 3:
                return 270f;
            default:
                return 0f;
        }
    }

    private void DrawPrefabPreview(GameObject prefab, Vector3 center, float rotationY, Color previewColor)
    {
        if (manager == null)
        {
            return;
        }

        if (prefab == null)
        {
            Handles.color = previewColor;
            Handles.DrawWireCube(center, new Vector3(manager.cellSize, 0.1f, manager.cellSize));
            return;
        }

        Material material = GetPreviewMaterial();
        if (material == null)
        {
            Handles.color = previewColor;
            Handles.DrawWireCube(center, new Vector3(manager.cellSize, 0.1f, manager.cellSize));
            return;
        }

        material.color = previewColor;

        Matrix4x4 rootMatrix = Matrix4x4.TRS(center, Quaternion.Euler(0f, rotationY, 0f), Vector3.one);
        MeshFilter[] meshFilters = prefab.GetComponentsInChildren<MeshFilter>(true);

        if (meshFilters.Length == 0)
        {
            Handles.color = previewColor;
            Handles.DrawWireCube(center, new Vector3(manager.cellSize, 0.1f, manager.cellSize));
            return;
        }

        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];
            if (meshFilter.sharedMesh == null)
            {
                continue;
            }

            material.SetPass(0);
            Graphics.DrawMeshNow(meshFilter.sharedMesh, rootMatrix * meshFilter.transform.localToWorldMatrix);
        }
    }

    private static Material GetPreviewMaterial()
    {
        if (previewMaterial != null)
        {
            return previewMaterial;
        }

        Shader shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            return null;
        }

        previewMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        return previewMaterial;
    }
}