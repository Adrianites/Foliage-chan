using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class FoliageChan : MonoBehaviour
{
    public List<GameObject> foliagePrefabs;
    public List<MeshRenderer> targetMeshes;
    public bool checkForOtherObjects = false;

    void Start()
    {
        if (foliagePrefabs == null)
            foliagePrefabs = new List<GameObject>();
        if (targetMeshes == null)
            targetMeshes = new List<MeshRenderer>();
    }

    void Update()
    {
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
                Vector3 randomPosition = GetRandomPointOnMesh(mesh);

                if (!IsPositionOnGround(randomPosition))
                {
                    continue;
                }

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
        public float placementDepth = 0.0f;
        public bool randomSize = false;
        public bool checkForOtherObjects = false;
        public List<GameObject> placedObjects = new List<GameObject>();
        public Stack<List<GameObject>> undoStack = new Stack<List<GameObject>>();
        public GameObject groupObject;
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
                    i--;
                    continue;
                }
                EditorGUILayout.EndHorizontal();

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
                        j--;
                        continue;
                    }
                    EditorGUILayout.EndHorizontal();

                    automaticParentObjectsData[i].objectDataList[j].quantity = (Quantity)EditorGUILayout.EnumPopup("Quantity", automaticParentObjectsData[i].objectDataList[j].quantity);

                    if (automaticParentObjectsData[i].objectDataList[j].quantity == Quantity.High)
                    {
                        automaticParentObjectsData[i].objectDataList[j].amountPerMesh = EditorGUILayout.IntField("Object Amount", automaticParentObjectsData[i].objectDataList[j].amountPerMesh);
                    }
                    else if (automaticParentObjectsData[i].objectDataList[j].quantity == Quantity.Low)
                    {
                        automaticParentObjectsData[i].objectDataList[j].placementProbability = EditorGUILayout.Slider("Object Placement Probability", automaticParentObjectsData[i].objectDataList[j].placementProbability, 0f, 1f);
                    }

                    automaticParentObjectsData[i].objectDataList[j].placementDepth = EditorGUILayout.FloatField("Placement Depth", automaticParentObjectsData[i].objectDataList[j].placementDepth);
                    automaticParentObjectsData[i].objectDataList[j].randomSize = EditorGUILayout.Toggle("Random Size", automaticParentObjectsData[i].objectDataList[j].randomSize);
                    automaticParentObjectsData[i].objectDataList[j].checkForOtherObjects = EditorGUILayout.Toggle("Check for Other Objects", automaticParentObjectsData[i].objectDataList[j].checkForOtherObjects);

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
                    GUILayout.Space(5);
                }

                EditorGUILayout.EndVertical();
                GUILayout.Space(10);
            }

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
                    i--;
                    continue;
                }
                EditorGUILayout.EndHorizontal();

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
                        j--;
                        continue;
                    }
                    EditorGUILayout.EndHorizontal();

                    manualParentObjectsData[i].objectDataList[j].quantity = (Quantity)EditorGUILayout.EnumPopup("Quantity", manualParentObjectsData[i].objectDataList[j].quantity);

                    if (manualParentObjectsData[i].objectDataList[j].quantity == Quantity.High)
                    {
                        manualParentObjectsData[i].objectDataList[j].amountPerMesh = EditorGUILayout.IntField("Object Amount", manualParentObjectsData[i].objectDataList[j].amountPerMesh);
                    }
                    else if (manualParentObjectsData[i].objectDataList[j].quantity == Quantity.Low)
                    {
                        manualParentObjectsData[i].objectDataList[j].placementProbability = EditorGUILayout.Slider("Object Placement Probability", manualParentObjectsData[i].objectDataList[j].placementProbability, 0f, 1f);
                    }

                    manualParentObjectsData[i].objectDataList[j].placementDepth = EditorGUILayout.FloatField("Placement Depth", manualParentObjectsData[i].objectDataList[j].placementDepth);
                    manualParentObjectsData[i].objectDataList[j].randomSize = EditorGUILayout.Toggle("Random Size", manualParentObjectsData[i].objectDataList[j].randomSize);

                    manualParentObjectsData[i].objectDataList[j].checkForOtherObjects = EditorGUILayout.Toggle("Check for Other Objects", manualParentObjectsData[i].objectDataList[j].checkForOtherObjects);

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Undo"))
                    {
                        UndoAllObjectsInSection(manualParentObjectsData[i].objectDataList[j]);
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.EndVertical();
                    GUILayout.Space(5);
                }

                EditorGUILayout.EndVertical();
                GUILayout.Space(10);
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
            objectData.groupObject = new GameObject(objectData.prefab.name + " Group");

            MeshRenderer[] childMeshes = parentData.parentObject.GetComponentsInChildren<MeshRenderer>();
            foreach (var mesh in childMeshes)
            {
                List<GameObject> currentPlacedFoliage = new List<GameObject>();

                if (objectData.quantity == Quantity.High)
                {
                    for (int i = 0; i < objectData.amountPerMesh; i++)
                    {
                        if (mesh != null && objectData.prefab != null)
                        {
                            Vector3 randomPosition = GetRandomPointOnMesh(mesh, objectData.placementDepth);

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
                                    continue;
                                }
                            }

                            if (Physics.Raycast(randomPosition, Vector3.down, out RaycastHit hit))
                            {
                                randomPosition = hit.point;
                            }

                            GameObject foliageInstance = (GameObject)PrefabUtility.InstantiatePrefab(objectData.prefab);
                            foliageInstance.transform.position = randomPosition;
                            foliageInstance.transform.parent = objectData.groupObject.transform;
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

                if (objectData.quantity == Quantity.Low && Random.value < objectData.placementProbability)
                {
                    if (mesh != null && objectData.prefab != null)
                    {
                        Vector3 randomPosition = GetRandomPointOnMesh(mesh, objectData.placementDepth);

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
                                continue;
                            }
                        }

                        if (Physics.Raycast(randomPosition, Vector3.down, out RaycastHit hit))
                        {
                            randomPosition = hit.point;
                        }

                        GameObject foliageInstance = (GameObject)PrefabUtility.InstantiatePrefab(objectData.prefab);
                        foliageInstance.transform.position = randomPosition;
                        foliageInstance.transform.parent = objectData.groupObject.transform;
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
        randomPoint -= new Vector3(0, depth, 0);
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
                                                continue;
                                            }
                                        }

                                        if (Physics.Raycast(position, Vector3.down, out RaycastHit hitInfo))
                                        {
                                            position = hitInfo.point;
                                        }

                                        GameObject foliageInstance = (GameObject)PrefabUtility.InstantiatePrefab(objectData.prefab);
                                        foliageInstance.transform.position = position - new Vector3(0, objectData.placementDepth, 0);
                                        foliageInstance.transform.parent = objectData.groupObject.transform;
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
