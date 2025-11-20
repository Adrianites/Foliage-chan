using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

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

﻿#if UNITY_EDITOR
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
        public bool foldout = true;
    }

    private class ParentObjectData
    {
        public GameObject parentObject;
        public List<ObjectData> objectDataList = new List<ObjectData>();
        public bool foldout = true;
    }

    private List<ParentObjectData> automaticParentObjectsData = new List<ParentObjectData>();
    private List<ParentObjectData> manualParentObjectsData = new List<ParentObjectData>();
    private Vector2 scrollPosition;
    private float brushSize = 1.0f;
    private int brushDensity = 10;
    private double lastPaintTime = 0.0;
    private float paintInterval = 0.05f;
    private GUIStyle prefabBoxStyle;
    private GUIStyle sectionHeader;
    private GUIStyle boxHeader;

    private Texture2D MakeTex(int width, int height, Color col)
    {
        Texture2D tex = new Texture2D(width, height);
        Color[] colors = new Color[width * height];
        for (int i = 0; i < colors.Length; i++) colors[i] = col;
        tex.SetPixels(colors);
        tex.Apply();
        return tex;
    }

    [MenuItem("Tools/Foliage-chan")]
    public static void ShowWindow()
    {
        GetWindow<FoliageChanEditor>("Foliage-chan");
    }

    private void OnGUI()
    {
        GUILayout.Label("Foliage Tool", EditorStyles.boldLabel);

        placementMode = (PlacementMode)GUILayout.Toolbar((int)placementMode, new[] { "Automatic", "Manual" });

        if (sectionHeader == null)
        {
            sectionHeader = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
        }
        if (boxHeader == null)
        {
            boxHeader = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
        }
        if (prefabBoxStyle == null)
        {
            prefabBoxStyle = new GUIStyle("box");
            prefabBoxStyle.padding = new RectOffset(6, 6, 4, 6);
            prefabBoxStyle.margin = new RectOffset(4, 4, 4, 4);
            prefabBoxStyle.normal.background = MakeTex(1, 1, new Color(0.22f, 0.28f, 0.35f, 0.95f));
        }

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        if (placementMode == PlacementMode.Automatic)
        {
            GUILayout.Label("Automatic Placement", sectionHeader);

            GUILayout.Label("Parent Objects", EditorStyles.label);
            if (GUILayout.Button("Add Parent Object"))
            {
                automaticParentObjectsData.Add(new ParentObjectData());
            }
            int api = 0;
            while (api < automaticParentObjectsData.Count)
            {
                var parentData = automaticParentObjectsData[api];
                bool removeParent = false;
                EditorGUILayout.BeginVertical("box");
                parentData.foldout = EditorGUILayout.Foldout(parentData.foldout, parentData.parentObject != null ? parentData.parentObject.name : "<Parent Object>", true, boxHeader);
                if (!parentData.foldout)
                {
                    EditorGUILayout.BeginHorizontal();
                    parentData.parentObject = (GameObject)EditorGUILayout.ObjectField(parentData.parentObject, typeof(GameObject), true);
                    if (GUILayout.Button("Remove", GUILayout.Width(60)))
                    {
                        foreach (var od in parentData.objectDataList) { UndoAllObjectsInSection(od); }
                        removeParent = true;
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    GUILayout.Space(6);
                    if (removeParent)
                    {
                        automaticParentObjectsData.RemoveAt(api);
                        continue;
                    }
                    api++;
                    continue;
                }
                EditorGUILayout.BeginHorizontal();
                parentData.parentObject = (GameObject)EditorGUILayout.ObjectField(parentData.parentObject, typeof(GameObject), true);
                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    foreach (var od in parentData.objectDataList)
                    {
                        UndoAllObjectsInSection(od);
                    }
                    removeParent = true;
                }
                EditorGUILayout.EndHorizontal();

                if (!removeParent)
                {
                    GUILayout.Label($"Object Prefabs ({parentData.objectDataList.Count})", EditorStyles.label);
                    if (GUILayout.Button("Add Object Prefab"))
                    {
                        parentData.objectDataList.Add(new ObjectData());
                    }

                    int j = 0;
                    while (j < parentData.objectDataList.Count)
                    {
                        var objData = parentData.objectDataList[j];
                        bool removeObj = false;
                        EditorGUILayout.BeginVertical(prefabBoxStyle);
                        EditorGUILayout.BeginHorizontal();
                        objData.foldout = EditorGUILayout.Foldout(objData.foldout, objData.prefab != null ? objData.prefab.name : "<Prefab>", true, boxHeader);
                        if (GUILayout.Button("Remove", GUILayout.Width(60)))
                        {
                            removeObj = true;
                        }
                        EditorGUILayout.EndHorizontal();
                        if (!objData.foldout)
                        {
                            if (removeObj)
                            {
                                parentData.objectDataList.RemoveAt(j);
                                EditorGUILayout.EndVertical();
                                continue;
                            }
                            EditorGUILayout.EndVertical();
                            GUILayout.Space(4);
                            j++;
                            continue;
                        }
                        objData.prefab = (GameObject)EditorGUILayout.ObjectField("Prefab", objData.prefab, typeof(GameObject), false);

                        if (!removeObj)
                        {
                            objData.quantity = (Quantity)EditorGUILayout.EnumPopup("Quantity", objData.quantity);
                            if (objData.quantity == Quantity.High)
                            {
                                objData.amountPerMesh = EditorGUILayout.IntField("Object Amount", objData.amountPerMesh);
                            }
                            else if (objData.quantity == Quantity.Low)
                            {
                                objData.placementProbability = EditorGUILayout.Slider("Object Placement Probability", objData.placementProbability, 0f, 1f);
                            }
                            // Always visible essential settings
                            objData.placementDepth = EditorGUILayout.FloatField("Placement Depth", objData.placementDepth);
                            objData.randomSize = EditorGUILayout.Toggle("Random Size", objData.randomSize);
                            objData.checkForOtherObjects = EditorGUILayout.Toggle("Check for Other Objects", objData.checkForOtherObjects);

                            EditorGUILayout.BeginHorizontal();
                            if (GUILayout.Button("Place Foliage"))
                            {
                                PlaceFoliageForObject(parentData, objData);
                            }
                            if (GUILayout.Button("Undo"))
                            {
                                UndoAllObjectsInSection(objData);
                            }
                            EditorGUILayout.EndHorizontal();
                        }
                        EditorGUILayout.EndVertical();
                        GUILayout.Space(5);

                        if (removeObj)
                        {
                            parentData.objectDataList.RemoveAt(j);
                            continue; // do not increment j
                        }
                        j++;
                    }
                }
                EditorGUILayout.EndVertical();
                GUILayout.Space(10);

                if (removeParent)
                {
                    automaticParentObjectsData.RemoveAt(api);
                    continue; // do not increment api
                }
                api++;
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
            GUILayout.Label("Manual Placement", sectionHeader);

            GUILayout.Label("Parent Objects", EditorStyles.label);
            if (GUILayout.Button("Add Parent Object"))
            {
                manualParentObjectsData.Add(new ParentObjectData());
            }
            int mpi = 0;
            while (mpi < manualParentObjectsData.Count)
            {
                var parentData = manualParentObjectsData[mpi];
                bool removeParent = false;
                EditorGUILayout.BeginVertical("box");
                parentData.foldout = EditorGUILayout.Foldout(parentData.foldout, parentData.parentObject != null ? parentData.parentObject.name : "<Parent Object>", true, boxHeader);
                if (!parentData.foldout)
                {
                    EditorGUILayout.BeginHorizontal();
                    parentData.parentObject = (GameObject)EditorGUILayout.ObjectField(parentData.parentObject, typeof(GameObject), true);
                    if (GUILayout.Button("Remove", GUILayout.Width(60)))
                    {
                        foreach (var od in parentData.objectDataList) { UndoAllObjectsInSection(od); }
                        removeParent = true;
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    GUILayout.Space(6);
                    if (removeParent)
                    {
                        manualParentObjectsData.RemoveAt(mpi);
                        continue;
                    }
                    mpi++;
                    continue;
                }
                EditorGUILayout.BeginHorizontal();
                parentData.parentObject = (GameObject)EditorGUILayout.ObjectField(parentData.parentObject, typeof(GameObject), true);
                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    foreach (var od in parentData.objectDataList)
                    {
                        UndoAllObjectsInSection(od);
                    }
                    removeParent = true;
                }
                EditorGUILayout.EndHorizontal();

                if (!removeParent)
                {
                    GUILayout.Label($"Object Prefabs ({parentData.objectDataList.Count})", EditorStyles.label);
                    if (GUILayout.Button("Add Object Prefab"))
                    {
                        parentData.objectDataList.Add(new ObjectData());
                    }

                    int j = 0;
                    while (j < parentData.objectDataList.Count)
                    {
                        var objData = parentData.objectDataList[j];
                        bool removeObj = false;
                        EditorGUILayout.BeginVertical(prefabBoxStyle);
                        EditorGUILayout.BeginHorizontal();
                        objData.foldout = EditorGUILayout.Foldout(objData.foldout, objData.prefab != null ? objData.prefab.name : "<Prefab>", true, boxHeader);
                        if (GUILayout.Button("Remove", GUILayout.Width(60)))
                        {
                            removeObj = true;
                        }
                        EditorGUILayout.EndHorizontal();
                        if (!objData.foldout)
                        {
                            if (removeObj)
                            {
                                parentData.objectDataList.RemoveAt(j);
                                EditorGUILayout.EndVertical();
                                continue;
                            }
                            EditorGUILayout.EndVertical();
                            GUILayout.Space(4);
                            j++;
                            continue;
                        }
                        objData.prefab = (GameObject)EditorGUILayout.ObjectField("Prefab", objData.prefab, typeof(GameObject), false);

                        if (!removeObj)
                        {
                            objData.quantity = (Quantity)EditorGUILayout.EnumPopup("Quantity", objData.quantity);
                            if (objData.quantity == Quantity.High)
                            {
                                objData.amountPerMesh = EditorGUILayout.IntField("Object Amount", objData.amountPerMesh);
                            }
                            else if (objData.quantity == Quantity.Low)
                            {
                                objData.placementProbability = EditorGUILayout.Slider("Object Placement Probability", objData.placementProbability, 0f, 1f);
                            }
                            objData.placementDepth = EditorGUILayout.FloatField("Placement Depth", objData.placementDepth);
                            objData.randomSize = EditorGUILayout.Toggle("Random Size", objData.randomSize);
                            objData.checkForOtherObjects = EditorGUILayout.Toggle("Check for Other Objects", objData.checkForOtherObjects);

                            EditorGUILayout.BeginHorizontal();
                            if (GUILayout.Button("Undo"))
                            {
                                UndoAllObjectsInSection(objData);
                            }
                            EditorGUILayout.EndHorizontal();
                        }
                        EditorGUILayout.EndVertical();
                        GUILayout.Space(5);

                        if (removeObj)
                        {
                            parentData.objectDataList.RemoveAt(j);
                            continue; // do not increment j
                        }
                        j++;
                    }
                }
                EditorGUILayout.EndVertical();
                GUILayout.Space(10);

                if (removeParent)
                {
                    manualParentObjectsData.RemoveAt(mpi);
                    continue; // do not increment mpi
                }
                mpi++;
            }

            GUILayout.Label("Paintbrush Tool", EditorStyles.boldLabel);
            brushSize = EditorGUILayout.Slider("Brush Size", brushSize, 0.1f, 10f);
            brushDensity = EditorGUILayout.IntSlider("Brush Density", brushDensity, 1, 100);

            // Ensure we only subscribe once
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        EditorGUILayout.EndScrollView();
    }

    private void PlaceFoliageForObject(ParentObjectData parentData, ObjectData objectData)
    {
        if (parentData.parentObject != null)
        {
            if (objectData.groupObject == null)
            {
                objectData.groupObject = new GameObject(objectData.prefab != null ? objectData.prefab.name + " Group" : "Foliage Group");
            }

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
                                    if (collider.transform == parentData.parentObject.transform || collider.transform.IsChildOf(parentData.parentObject.transform))
                                        continue;
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
                                if (collider.transform == parentData.parentObject.transform || collider.transform.IsChildOf(parentData.parentObject.transform))
                                    continue;
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
            bool paintingEvent = (e.type == EventType.MouseDrag || e.type == EventType.MouseDown) && e.button == 0;
            double now = EditorApplication.timeSinceStartup;
            if (paintingEvent && now - lastPaintTime >= paintInterval)
            {
                lastPaintTime = now;
                foreach (var parentData in manualParentObjectsData)
                {
                    foreach (var objectData in parentData.objectDataList)
                    {
                        if (objectData.prefab == null || parentData.parentObject == null)
                            continue;

                        if (objectData.groupObject == null)
                        {
                            objectData.groupObject = new GameObject(objectData.prefab.name + " Group");
                        }

                        List<GameObject> currentPlacedFoliage = new List<GameObject>();

                        for (int i = 0; i < brushDensity; i++)
                        {
                            Vector2 offset2D = Random.insideUnitCircle * brushSize;
                            Vector3 basePos = hit.point + new Vector3(offset2D.x, 0f, offset2D.y) + Vector3.up * 1f;
                            if (Physics.Raycast(basePos, Vector3.down, out RaycastHit surfaceHit, 3f))
                            {
                                Vector3 placePos = surfaceHit.point - new Vector3(0, objectData.placementDepth, 0);

                                if (objectData.checkForOtherObjects)
                                {
                                    Collider[] colliders = Physics.OverlapSphere(placePos, 0.4f);
                                    bool hasOtherObjects = false;
                                    foreach (var collider in colliders)
                                    {
                                        if (parentData.parentObject != null && (collider.transform == parentData.parentObject.transform || collider.transform.IsChildOf(parentData.parentObject.transform)))
                                            continue;
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

                                GameObject foliageInstance = (GameObject)PrefabUtility.InstantiatePrefab(objectData.prefab);
                                foliageInstance.transform.position = placePos;
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
                e.Use();
            }
        }
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }
}
#endif
