using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class FoliageChan : MonoBehaviour
{
    public List<GameObject> foliagePrefabs; // List of foliage prefabs
    public List<MeshRenderer> targetMeshes; // List of target meshes
    public bool checkForOtherObjects = false; // Checkbox for checking other objects

    // Start is called before the first frame update
    void Start()
    {
        // Initialize lists if not set in the inspector
        if (foliagePrefabs == null)
            foliagePrefabs = new List<GameObject>();
        if (targetMeshes == null)
            targetMeshes = new List<MeshRenderer>();
    }

    // Update is called once per frame
    void Update()
    {
        // Check for user input to place foliage
        if (Input.GetKeyDown(KeyCode.P))
        {
            PlaceFoliage();
        }
    }

    void PlaceFoliage()
    {
        foreach (var mesh in targetMeshes)
        {
            foreach (var foliage in foliagePrefabs)
            {
                // Randomly place foliage on the mesh
                Vector3 randomPosition = GetRandomPointOnMesh(mesh);

                // Ensure the random position is on the ground
                if (!IsPositionOnGround(randomPosition))
                {
                    continue;
                }

                // Check for other objects if the checkbox is ticked
                if (checkForOtherObjects)
                {
                    Collider[] colliders = Physics.OverlapSphere(randomPosition, 0.5f);
                    bool skipPosition = false;
                    foreach (var collider in colliders)
                    {
                        if (collider.transform != mesh.transform && !collider.transform.IsChildOf(mesh.transform))
                        {
                            skipPosition = true;
                            break;
                        }
                    }
                    if (skipPosition)
                    {
                        continue;
                    }
                }

                Instantiate(foliage, randomPosition, Quaternion.identity);
            }
        }
    }

    bool IsPositionOnGround(Vector3 position)
    {
        Ray ray = new Ray(position + Vector3.up * 10, Vector3.down);
        return Physics.Raycast(ray, out RaycastHit hit, 20f);
    }

    Vector3 GetRandomPointOnMesh(MeshRenderer meshRenderer)
    {
        Mesh mesh = meshRenderer.GetComponent<MeshFilter>().mesh;
        int triangleIndex = Random.Range(0, mesh.triangles.Length / 3) * 3;
        Vector3 vertex1 = mesh.vertices[mesh.triangles[triangleIndex]];
        Vector3 vertex2 = mesh.vertices[mesh.triangles[triangleIndex + 1]];
        Vector3 vertex3 = mesh.vertices[mesh.triangles[triangleIndex + 2]];

        Vector3 randomPoint = vertex1 + Random.value * (vertex2 - vertex1) + Random.value * (vertex3 - vertex1);
        return meshRenderer.transform.TransformPoint(randomPoint);
    }
}

public class FoliageChanEditor : EditorWindow
{
    private enum PlacementMode { Automatic, Manual }
    private enum Quantity { High, Low }

    private PlacementMode placementMode = PlacementMode.Automatic;

    private class ObjectData
    {
        public GameObject prefab;
        public Quantity quantity = Quantity.High;
        public int amountPerMesh = 10;
        public float placementProbability = 0.1f;
        public float placementDepth = 0.0f; // Depth of placement below the mesh
        public bool randomSize = false;
        public bool checkForOtherObjects = false; // Checkbox for checking other objects
        public List<GameObject> placedObjects = new List<GameObject>();
        public Stack<List<GameObject>> undoStack = new Stack<List<GameObject>>();
        public GameObject groupObject; // Reference to the group object
    }

    private class ParentObjectData
    {
        public GameObject parentObject;
        public List<ObjectData> objectDataList = new List<ObjectData>();
    }

    private List<ParentObjectData> automaticParentObjectsData = new List<ParentObjectData>();
    private List<ParentObjectData> manualParentObjectsData = new List<ParentObjectData>();
    private Vector2 scrollPosition;
    private float brushSize = 1.0f;
    private int brushDensity = 10;

    [MenuItem("Tools/Foliage-chan")]
    public static void ShowWindow()
    {
        GetWindow<FoliageChanEditor>("Foliage-chan");
    }

    private void OnGUI()
    {
        GUILayout.Label("Foliage Tool", EditorStyles.boldLabel);

        placementMode = (PlacementMode)EditorGUILayout.EnumPopup("Placement Mode", placementMode);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        if (placementMode == PlacementMode.Automatic)
        {
            GUILayout.Label("Automatic Placement", EditorStyles.boldLabel);

            // Parent Objects for Automatic Placement
            GUILayout.Label("Parent Objects", EditorStyles.label);
            if (GUILayout.Button("Add Parent Object"))
            {
                automaticParentObjectsData.Add(new ParentObjectData());
            }
            for (int i = 0; i < automaticParentObjectsData.Count; i++)
            {
                EditorGUILayout.BeginVertical("box");
                GUILayout.Label("Parent Object", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                automaticParentObjectsData[i].parentObject = (GameObject)EditorGUILayout.ObjectField(automaticParentObjectsData[i].parentObject, typeof(GameObject), true);
                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    automaticParentObjectsData.RemoveAt(i);
                    i--; // Adjust index after removal
                    continue;
                }
                EditorGUILayout.EndHorizontal();

                // Object Prefabs
                GUILayout.Label("Object Prefabs", EditorStyles.label);
                if (GUILayout.Button("Add Object Prefab"))
                {
                    automaticParentObjectsData[i].objectDataList.Add(new ObjectData());
                }
                for (int j = 0; j < automaticParentObjectsData[i].objectDataList.Count; j++)
                {
                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.BeginHorizontal();
                    automaticParentObjectsData[i].objectDataList[j].prefab = (GameObject)EditorGUILayout.ObjectField(automaticParentObjectsData[i].objectDataList[j].prefab, typeof(GameObject), false);
                    if (GUILayout.Button("Remove", GUILayout.Width(60)))
                    {
                        automaticParentObjectsData[i].objectDataList.RemoveAt(j);
                        j--; // Adjust index after removal
                        continue;
                    }
                    EditorGUILayout.EndHorizontal();

                    // Quantity
                    automaticParentObjectsData[i].objectDataList[j].quantity = (Quantity)EditorGUILayout.EnumPopup("Quantity", automaticParentObjectsData[i].objectDataList[j].quantity);

                    // Amount per Mesh and Placement Probability
                    if (automaticParentObjectsData[i].objectDataList[j].quantity == Quantity.High)
                    {
                        automaticParentObjectsData[i].objectDataList[j].amountPerMesh = EditorGUILayout.IntField("Object Amount", automaticParentObjectsData[i].objectDataList[j].amountPerMesh);
                    }
                    else if (automaticParentObjectsData[i].objectDataList[j].quantity == Quantity.Low)
                    {
                        automaticParentObjectsData[i].objectDataList[j].placementProbability = EditorGUILayout.Slider("Object Placement Probability", automaticParentObjectsData[i].objectDataList[j].placementProbability, 0f, 1f);
                    }

                    // Placement Depth, Random Size, and Check for Other Objects
                    automaticParentObjectsData[i].objectDataList[j].placementDepth = EditorGUILayout.FloatField("Placement Depth", automaticParentObjectsData[i].objectDataList[j].placementDepth);
                    automaticParentObjectsData[i].objectDataList[j].randomSize = EditorGUILayout.Toggle("Random Size", automaticParentObjectsData[i].objectDataList[j].randomSize);
                    automaticParentObjectsData[i].objectDataList[j].checkForOtherObjects = EditorGUILayout.Toggle("Check for Other Objects", automaticParentObjectsData[i].objectDataList[j].checkForOtherObjects);

                    // Place Foliage and Undo Buttons for Object Prefab Section
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Place Foliage"))
                    {
                        PlaceFoliageForObject(automaticParentObjectsData[i], automaticParentObjectsData[i].objectDataList[j]);
                    }
                    if (GUILayout.Button("Undo"))
                    {
                        UndoAllObjectsInSection(automaticParentObjectsData[i].objectDataList[j]);
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.EndVertical();
                    GUILayout.Space(5); // Add space between object prefabs
                }

                EditorGUILayout.EndVertical();
                GUILayout.Space(10); // Add space between parent objects
            }

            // Place All Foliage and Undo All Foliage Buttons for Automatic Placement
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Place All Foliage"))
            {
                PlaceAllFoliage();
            }
            if (GUILayout.Button("Undo All Foliage"))
            {
                UndoAllFoliage();
            }
            EditorGUILayout.EndHorizontal();
        }
        else if (placementMode == PlacementMode.Manual)
        {
            GUILayout.Label("Manual Placement", EditorStyles.boldLabel);

            // Parent Objects for Manual Placement
            GUILayout.Label("Parent Objects", EditorStyles.label);
            if (GUILayout.Button("Add Parent Object"))
            {
                manualParentObjectsData.Add(new ParentObjectData());
            }
            for (int i = 0; i < manualParentObjectsData.Count; i++)
            {
                EditorGUILayout.BeginVertical("box");
                GUILayout.Label("Parent Object", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                manualParentObjectsData[i].parentObject = (GameObject)EditorGUILayout.ObjectField(manualParentObjectsData[i].parentObject, typeof(GameObject), true);
                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    manualParentObjectsData.RemoveAt(i);
                    i--; // Adjust index after removal
                    continue;
                }
                EditorGUILayout.EndHorizontal();

                // Object Prefabs
                GUILayout.Label("Object Prefabs", EditorStyles.label);
                if (GUILayout.Button("Add Object Prefab"))
                {
                    manualParentObjectsData[i].objectDataList.Add(new ObjectData());
                }
                for (int j = 0; j < manualParentObjectsData[i].objectDataList.Count; j++)
                {
                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.BeginHorizontal();
                    manualParentObjectsData[i].objectDataList[j].prefab = (GameObject)EditorGUILayout.ObjectField(manualParentObjectsData[i].objectDataList[j].prefab, typeof(GameObject), false);
                    if (GUILayout.Button("Remove", GUILayout.Width(60)))
                    {
                        manualParentObjectsData[i].objectDataList.RemoveAt(j);
                        j--; // Adjust index after removal
                        continue;
                    }
                    EditorGUILayout.EndHorizontal();

                    // Quantity
                    manualParentObjectsData[i].objectDataList[j].quantity = (Quantity)EditorGUILayout.EnumPopup("Quantity", manualParentObjectsData[i].objectDataList[j].quantity);

                    // Amount per Mesh and Placement Probability
                    if (manualParentObjectsData[i].objectDataList[j].quantity == Quantity.High)
                    {
                        manualParentObjectsData[i].objectDataList[j].amountPerMesh = EditorGUILayout.IntField("Object Amount", manualParentObjectsData[i].objectDataList[j].amountPerMesh);
                    }
                    else if (manualParentObjectsData[i].objectDataList[j].quantity == Quantity.Low)
                    {
                        manualParentObjectsData[i].objectDataList[j].placementProbability = EditorGUILayout.Slider("Object Placement Probability", manualParentObjectsData[i].objectDataList[j].placementProbability, 0f, 1f);
                    }

                    // Placement Depth and Random Size
                    manualParentObjectsData[i].objectDataList[j].placementDepth = EditorGUILayout.FloatField("Placement Depth", manualParentObjectsData[i].objectDataList[j].placementDepth);
                    manualParentObjectsData[i].objectDataList[j].randomSize = EditorGUILayout.Toggle("Random Size", manualParentObjectsData[i].objectDataList[j].randomSize);

                    // Check for Other Objects
                    manualParentObjectsData[i].objectDataList[j].checkForOtherObjects = EditorGUILayout.Toggle("Check for Other Objects", manualParentObjectsData[i].objectDataList[j].checkForOtherObjects);

                    // Undo Button for Object Prefab Section
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Undo"))
                    {
                        UndoAllObjectsInSection(manualParentObjectsData[i].objectDataList[j]);
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.EndVertical();
                    GUILayout.Space(5); // Add space between object prefabs
                }

                EditorGUILayout.EndVertical();
                GUILayout.Space(10); // Add space between parent objects
            }

            GUILayout.Label("Paintbrush Tool", EditorStyles.boldLabel);
            brushSize = EditorGUILayout.Slider("Brush Size", brushSize, 0.1f, 10f);
            brushDensity = EditorGUILayout.IntSlider("Brush Density", brushDensity, 1, 100);

            SceneView.duringSceneGui += OnSceneGUI;
        }

        EditorGUILayout.EndScrollView();
    }

    private void PlaceFoliageForObject(ParentObjectData parentData, ObjectData objectData)
    {
        if (parentData.parentObject != null)
        {
            // Create an empty parent object to group the placed foliage
            objectData.groupObject = new GameObject(objectData.prefab.name + " Group");

            MeshRenderer[] childMeshes = parentData.parentObject.GetComponentsInChildren<MeshRenderer>();
            foreach (var mesh in childMeshes)
            {
                List<GameObject> currentPlacedFoliage = new List<GameObject>();

                // Place objects based on amount per mesh
                if (objectData.quantity == Quantity.High)
                {
                    for (int i = 0; i < objectData.amountPerMesh; i++)
                    {
                        if (mesh != null && objectData.prefab != null)
                        {
                            Vector3 randomPosition = GetRandomPointOnMesh(mesh, objectData.placementDepth);

                            // Check for other objects if the checkbox is ticked
                            if (objectData.checkForOtherObjects)
                            {
                                Collider[] colliders = Physics.OverlapSphere(randomPosition, 0.5f);
                                bool hasOtherObjects = false;
                                foreach (var collider in colliders)
                                {
                                    if (collider.gameObject != objectData.groupObject && !objectData.placedObjects.Contains(collider.gameObject))
                                    {
                                        hasOtherObjects = true;
                                        break;
                                    }
                                }
                                if (hasOtherObjects)
                                {
                                    // Skip this position if other objects are found
                                    continue;
                                }
                            }

                            // Ensure the object is not spawned in the air
                            if (Physics.Raycast(randomPosition, Vector3.down, out RaycastHit hit))
                            {
                                randomPosition = hit.point;
                            }

                            GameObject foliageInstance = (GameObject)PrefabUtility.InstantiatePrefab(objectData.prefab);
                            foliageInstance.transform.position = randomPosition;
                            foliageInstance.transform.parent = objectData.groupObject.transform; // Set the parent to the group object
                            if (objectData.randomSize)
                            {
                                float randomScale = Random.Range(0.8f, 1.2f);
                                foliageInstance.transform.localScale = new Vector3(randomScale, randomScale, randomScale);
                            }
                            objectData.placedObjects.Add(foliageInstance);
                            currentPlacedFoliage.Add(foliageInstance);
                        }
                    }
                }

                // Place objects based on probability
                if (objectData.quantity == Quantity.Low && Random.value < objectData.placementProbability)
                {
                    if (mesh != null && objectData.prefab != null)
                    {
                        Vector3 randomPosition = GetRandomPointOnMesh(mesh, objectData.placementDepth);

                        // Check for other objects if the checkbox is ticked
                        if (objectData.checkForOtherObjects)
                        {
                            Collider[] colliders = Physics.OverlapSphere(randomPosition, 0.5f);
                            bool hasOtherObjects = false;
                            foreach (var collider in colliders)
                            {
                                if (collider.gameObject != objectData.groupObject && !objectData.placedObjects.Contains(collider.gameObject))
                                {
                                    hasOtherObjects = true;
                                    break;
                                }
                            }
                            if (hasOtherObjects)
                            {
                                // Skip this position if other objects are found
                                continue;
                            }
                        }

                        // Ensure the object is not spawned in the air
                        if (Physics.Raycast(randomPosition, Vector3.down, out RaycastHit hit))
                        {
                            randomPosition = hit.point;
                        }

                        GameObject foliageInstance = (GameObject)PrefabUtility.InstantiatePrefab(objectData.prefab);
                        foliageInstance.transform.position = randomPosition;
                        foliageInstance.transform.parent = objectData.groupObject.transform; // Set the parent to the group object
                        if (objectData.randomSize)
                        {
                            float randomScale = Random.Range(0.8f, 1.2f);
                            foliageInstance.transform.localScale = new Vector3(randomScale, randomScale, randomScale);
                        }
                        objectData.placedObjects.Add(foliageInstance);
                        currentPlacedFoliage.Add(foliageInstance);
                    }
                }

                if (currentPlacedFoliage.Count > 0)
                {
                    objectData.undoStack.Push(currentPlacedFoliage);
                }
            }
        }
    }

    private void PlaceAllFoliage()
    {
        foreach (var parentData in automaticParentObjectsData)
        {
            foreach (var objectData in parentData.objectDataList)
            {
                PlaceFoliageForObject(parentData, objectData);
            }
        }
    }

    private void UndoAllObjectsInSection(ObjectData objectData)
    {
        foreach (var foliage in objectData.placedObjects)
        {
            if (foliage != null)
            {
                DestroyImmediate(foliage);
            }
        }
        objectData.placedObjects.Clear();
        objectData.undoStack.Clear();

        // Destroy the group object
        if (objectData.groupObject != null)
        {
            DestroyImmediate(objectData.groupObject);
            objectData.groupObject = null;
        }
    }

    private void UndoAllFoliage()
    {
        foreach (var parentData in automaticParentObjectsData)
        {
            foreach (var objectData in parentData.objectDataList)
            {
                UndoAllObjectsInSection(objectData);
            }
        }

        foreach (var parentData in manualParentObjectsData)
        {
            foreach (var objectData in parentData.objectDataList)
            {
                UndoAllObjectsInSection(objectData);
            }
        }
    }

    private bool IsPositionOnGround(Vector3 position)
    {
        Ray ray = new Ray(position + Vector3.up * 10, Vector3.down);
        return Physics.Raycast(ray, out RaycastHit hit, 20f);
    }

    private Vector3 GetRandomPointOnMesh(MeshRenderer meshRenderer, float depth)
    {
        Mesh mesh = meshRenderer.GetComponent<MeshFilter>().sharedMesh;
        int triangleIndex = Random.Range(0, mesh.triangles.Length / 3) * 3;
        Vector3 vertex1 = mesh.vertices[mesh.triangles[triangleIndex]];
        Vector3 vertex2 = mesh.vertices[mesh.triangles[triangleIndex + 1]];
        Vector3 vertex3 = mesh.vertices[mesh.triangles[triangleIndex + 2]];

        Vector3 randomPoint = vertex1 + Random.value * (vertex2 - vertex1) + Random.value * (vertex3 - vertex1);
        randomPoint -= new Vector3(0, depth, 0); // Adjust for placement depth
        return meshRenderer.transform.TransformPoint(randomPoint);
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (placementMode != PlacementMode.Manual)
        {
            return;
        }

        Event e = Event.current;
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            Handles.color = new Color(0, 1, 0, 0.2f);
            Handles.DrawSolidDisc(hit.point, hit.normal, brushSize);

            if ((e.type == EventType.MouseDrag || e.type == EventType.MouseDown) && e.button == 0)
            {
                foreach (var parentData in manualParentObjectsData)
                {
                    foreach (var objectData in parentData.objectDataList)
                    {
                        // Create an empty parent object to group the placed foliage
                        if (objectData.groupObject == null)
                        {
                            objectData.groupObject = new GameObject(objectData.prefab.name + " Group");
                        }

                        List<GameObject> currentPlacedFoliage = new List<GameObject>();
                        for (int i = 0; i < brushDensity; i++)
                        {
                            Vector3 randomOffset = Random.insideUnitSphere * brushSize;
                            Vector3 position = hit.point + randomOffset;
                            MeshRenderer[] childMeshes = parentData.parentObject.GetComponentsInChildren<MeshRenderer>();
                            foreach (var mesh in childMeshes)
                            {
                                if (mesh.bounds.Contains(position))
                                {
                                    if (objectData.prefab != null)
                                    {
                                        // Check for other objects if the checkbox is ticked
                                        if (objectData.checkForOtherObjects)
                                        {
                                            Collider[] colliders = Physics.OverlapSphere(position, 0.5f);
                                            bool hasOtherObjects = false;
                                            foreach (var collider in colliders)
                                            {
                                                if (collider.gameObject != objectData.groupObject && !objectData.placedObjects.Contains(collider.gameObject))
                                                {
                                                    hasOtherObjects = true;
                                                    break;
                                                }
                                            }
                                            if (hasOtherObjects)
                                            {
                                                // Skip this position if other objects are found
                                                continue;
                                            }
                                        }

                                        // Ensure the object is not spawned in the air
                                        if (Physics.Raycast(position, Vector3.down, out RaycastHit hitInfo))
                                        {
                                            position = hitInfo.point;
                                        }

                                        GameObject foliageInstance = (GameObject)PrefabUtility.InstantiatePrefab(objectData.prefab);
                                        foliageInstance.transform.position = position - new Vector3(0, objectData.placementDepth, 0);
                                        foliageInstance.transform.parent = objectData.groupObject.transform; // Set the parent to the group object
                                        if (objectData.randomSize)
                                        {
                                            float randomScale = Random.Range(0.8f, 1.2f);
                                            foliageInstance.transform.localScale = new Vector3(randomScale, randomScale, randomScale);
                                        }
                                        objectData.placedObjects.Add(foliageInstance);
                                        currentPlacedFoliage.Add(foliageInstance);
                                    }
                                }
                            }
                        }
                        if (currentPlacedFoliage.Count > 0)
                        {
                            objectData.undoStack.Push(currentPlacedFoliage);
                        }
                    }
                }
                e.Use();
            }
        }
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }
}
