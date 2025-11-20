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
    private static Collider[] runtimeOverlapBuffer = new Collider[64];

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

                if (checkForOtherObjects && HasOtherObjects(randomPosition, 0.5f, mesh.transform)) continue;

                Instantiate(foliage, randomPosition, Quaternion.identity);
            }
        }
    }

    bool HasOtherObjects(Vector3 position, float radius, Transform meshTransform)
    {
        int count = Physics.OverlapSphereNonAlloc(position, radius, runtimeOverlapBuffer);
        for (int i = 0; i < count; i++)
        {
            var c = runtimeOverlapBuffer[i];
            if (c == null) continue;
            Transform t = c.transform;
            if (t != meshTransform && !t.IsChildOf(meshTransform)) return true;
        }
        return false;
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
    private enum BrushMode { Easy, Advanced }

    private PlacementMode placementMode = PlacementMode.Automatic;
    private BrushMode brushMode = BrushMode.Easy;

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
    private float softness = 0.5f;
    private float intensity = 1f;
    private float flow = 0.7f;
    private float separation = 0.25f;
    private bool alignToNormal = true;
    private bool randomRotation = true;
    private Vector3 lastPaintPos;
    private bool hasLastPaintPos = false;
    private float manualPlacementDepth = 0f;
    private bool manualRandomSize = false;
    private bool manualCheckForOtherObjects = false;
    private double lastPaintTime = 0.0;
    private Dictionary<GameObject, float> prefabRadiusCache = new Dictionary<GameObject, float>();
    private GUIStyle prefabBoxStyle;
    private GUIStyle sectionHeader;
    private GUIStyle boxHeader;
    private bool continuousPreview = true;
    private static Collider[] overlapBufferEditor = new Collider[256];

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


        placementMode = (PlacementMode)GUILayout.Toolbar(
            (int)placementMode,
            new GUIContent[]
            {
                new GUIContent("Automatic", "Automatically place objects."),
                new GUIContent("Manual", "Paint objects by hand.")
            }
        );

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

            GUILayout.Label(new GUIContent("Parent Objects", "Parent objects to populate."), EditorStyles.label);
            if (GUILayout.Button(new GUIContent("Add Parent Object", "Add a parent object.")))
            {
                automaticParentObjectsData.Add(new ParentObjectData());
            }
            int api = 0;
            while (api < automaticParentObjectsData.Count)
            {
                var parentData = automaticParentObjectsData[api];
                bool removeParent = false;
                EditorGUILayout.BeginVertical(prefabBoxStyle);
                parentData.foldout = EditorGUILayout.Foldout(
                    parentData.foldout,
                    new GUIContent(parentData.parentObject != null ? parentData.parentObject.name : "<Parent Object>", "Settings for this parent."),
                    true,
                    boxHeader
                );
                if (!parentData.foldout)
                {
                    EditorGUILayout.BeginHorizontal();
                    parentData.parentObject = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Parent", "Parent whose children are used."), parentData.parentObject, typeof(GameObject), true);
                    if (GUILayout.Button(new GUIContent("Remove", "Remove parent and placed items."), GUILayout.Width(60)))
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
                parentData.parentObject = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Parent", "Parent whose children are used."), parentData.parentObject, typeof(GameObject), true);
                if (GUILayout.Button(new GUIContent("Remove", "Remove parent and placed items."), GUILayout.Width(60)))
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
                    GUILayout.Label(new GUIContent($"Object Prefabs ({parentData.objectDataList.Count})", "Prefabs to place."), EditorStyles.label);
                    if (GUILayout.Button(new GUIContent("Add Object Prefab", "Add a prefab.")))
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
                        objData.foldout = EditorGUILayout.Foldout(objData.foldout, new GUIContent(objData.prefab != null ? objData.prefab.name : "<Prefab>", "Settings for this prefab."), true, boxHeader);
                        if (GUILayout.Button(new GUIContent("Remove", "Remove this prefab entry."), GUILayout.Width(60)))
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
                        objData.prefab = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Prefab", "Prefab asset to place."), objData.prefab, typeof(GameObject), false);

                        if (!removeObj)
                        {
                            objData.quantity = (Quantity)EditorGUILayout.EnumPopup(new GUIContent("Quantity", "High = set number. Low = random chance."), objData.quantity);
                            if (objData.quantity == Quantity.High)
                            {
                                objData.amountPerMesh = EditorGUILayout.IntField(new GUIContent("Object Amount", "Number per mesh."), objData.amountPerMesh);
                            }
                            else if (objData.quantity == Quantity.Low)
                            {
                                objData.placementProbability = EditorGUILayout.Slider(new GUIContent("Placement Chance", "Chance per mesh."), objData.placementProbability, 0f, 1f);
                            }
                            objData.placementDepth = EditorGUILayout.FloatField(new GUIContent("Depth", "Offset downwards."), objData.placementDepth);
                            objData.randomSize = EditorGUILayout.Toggle(new GUIContent("Random Size", "Slight size variation."), objData.randomSize);
                            objData.checkForOtherObjects = EditorGUILayout.Toggle(new GUIContent("Avoid Overlap", "Skip if colliding with others."), objData.checkForOtherObjects);

                            EditorGUILayout.BeginHorizontal();
                            if (GUILayout.Button(new GUIContent("Place", "Place this prefab now.")))
                            {
                                PlaceFoliageForObject(parentData, objData);
                            }
                            if (GUILayout.Button(new GUIContent("Undo", "Undo placements from this prefab.")))
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
                            continue;
                        }
                        j++;
                    }
                }
                EditorGUILayout.EndVertical();
                GUILayout.Space(10);

                if (removeParent)
                {
                    automaticParentObjectsData.RemoveAt(api);
                    continue;
                }
                api++;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Place All", "Place all prefabs.")))
            {
                PlaceAllFoliage();
            }
            if (GUILayout.Button(new GUIContent("Undo All", "Remove all placed prefabs.")))
            {
                UndoAllFoliage();
            }
            EditorGUILayout.EndHorizontal();
        }
        else if (placementMode == PlacementMode.Manual)
        {
            GUILayout.Label(new GUIContent("Manual Placement", "Paint prefabs with the brush."), sectionHeader);
            EditorGUILayout.BeginHorizontal();
            continuousPreview = EditorGUILayout.Toggle(new GUIContent("Speedy Brush", "Brush updates faster."), continuousPreview);
            GUILayout.Space(10);
            GUILayout.Label("Turn off if the performance is bad.", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            GUILayout.Label(new GUIContent("Parent Objects", "Parents you can paint on."), EditorStyles.label);
            if (GUILayout.Button(new GUIContent("Add Parent Object", "Add a paintable parent.")))
            {
                manualParentObjectsData.Add(new ParentObjectData());
            }
            int mpi = 0;
            while (mpi < manualParentObjectsData.Count)
            {
                var parentData = manualParentObjectsData[mpi];
                bool removeParent = false;
                EditorGUILayout.BeginVertical(prefabBoxStyle);
                parentData.foldout = EditorGUILayout.Foldout(
                    parentData.foldout,
                    new GUIContent(parentData.parentObject != null ? parentData.parentObject.name : "<Parent Object>", "Target parent for painting."),
                    true,
                    boxHeader
                );
                if (!parentData.foldout)
                {
                    EditorGUILayout.BeginHorizontal();
                    parentData.parentObject = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Parent", "Parent to paint on."), parentData.parentObject, typeof(GameObject), true);
                    if (GUILayout.Button(new GUIContent("Remove", "Remove parent and its placed items."), GUILayout.Width(60)))
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
                parentData.parentObject = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Parent", "Parent to paint on."), parentData.parentObject, typeof(GameObject), true);
                if (GUILayout.Button(new GUIContent("Remove", "Remove parent and its placed items."), GUILayout.Width(60)))
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
                    GUILayout.Label(new GUIContent($"Object Prefabs ({parentData.objectDataList.Count})", "Prefabs to paint."), EditorStyles.label);
                    if (GUILayout.Button(new GUIContent("Add Object Prefab", "Add a prefab to paint.")))
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
                        objData.foldout = EditorGUILayout.Foldout(objData.foldout, new GUIContent(objData.prefab != null ? objData.prefab.name : "<Prefab>", "Prefab entry details."), true, boxHeader);
                        if (GUILayout.Button(new GUIContent("Remove", "Remove this prefab entry."), GUILayout.Width(60)))
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
                        objData.prefab = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Prefab", "Prefab asset to paint."), objData.prefab, typeof(GameObject), false);
                        if (!removeObj)
                        {
                            EditorGUILayout.BeginHorizontal();
                            if (GUILayout.Button(new GUIContent("Undo", "Undo placements from this prefab.")))
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
                            continue;
                        }
                        j++;
                    }
                }
                EditorGUILayout.EndVertical();
                GUILayout.Space(10);

                if (removeParent)
                {
                    manualParentObjectsData.RemoveAt(mpi);
                    continue;
                }
                mpi++;
            }

            GUILayout.Label(new GUIContent("Paintbrush Tool", "Brush options."), EditorStyles.boldLabel);
            brushMode = (BrushMode)GUILayout.Toolbar(
                (int)brushMode,
                new GUIContent[]
                {
                    new GUIContent("Easy", "Basic controls."),
                    new GUIContent("Advanced", "Extra controls.")
                }
            );


            brushSize = EditorGUILayout.Slider(new GUIContent("Size", "Brush radius in world units."), brushSize, 0.1f, 25f);
            brushDensity = EditorGUILayout.IntSlider(new GUIContent("Density", "Maximum per stroke."), brushDensity, 1, 200);
            manualPlacementDepth = EditorGUILayout.FloatField(new GUIContent("Depth", "Offset downwards."), manualPlacementDepth);
            manualRandomSize = EditorGUILayout.Toggle(new GUIContent("Random Size", "Random size variation."), manualRandomSize);
            manualCheckForOtherObjects = EditorGUILayout.Toggle(new GUIContent("Avoid Overlap", "Try to avoid overlaps."), manualCheckForOtherObjects);
            alignToNormal = EditorGUILayout.Toggle(new GUIContent("Align Surface", "Align to surface normal."), alignToNormal);
            randomRotation = EditorGUILayout.Toggle(new GUIContent("Random Y", "Random rotation around up axis."), randomRotation);

            if (brushMode == BrushMode.Advanced)
            {
                GUILayout.Space(6);
                EditorGUILayout.LabelField(new GUIContent("Shape", "Brush shape controls."), EditorStyles.boldLabel);
                softness = EditorGUILayout.Slider(new GUIContent("Softness", "Edge softness."), softness, 0f, 1f);

                GUILayout.Space(4);
                EditorGUILayout.LabelField(new GUIContent("Distribution", "How objects appear."), EditorStyles.boldLabel);
                intensity = EditorGUILayout.Slider(new GUIContent("Intensity", "Probability per attempt."), intensity, 0f, 1f);
                flow = EditorGUILayout.Slider(new GUIContent("Flow", "How often while dragging."), flow, 0f, 1f);
                separation = EditorGUILayout.Slider(new GUIContent("Separation", "Minimum spacing."), separation, 0f, 2f);
            }
            else
            {
                softness = 0.3f;
                intensity = 1f;
                flow = 0.6f;
                separation = Mathf.Max(separation, 0.2f);
            }

            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        EditorGUILayout.EndScrollView();
    }

    private void OnEnable()
    {
        EditorApplication.update -= UpdateContinuousRepaint;
        EditorApplication.update += UpdateContinuousRepaint;
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

                            if (objectData.checkForOtherObjects && HasExternalOverlap(randomPosition, 0.5f, parentData, objectData)) continue;

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

                        if (objectData.checkForOtherObjects && HasExternalOverlap(randomPosition, 0.5f, parentData, objectData)) continue;

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

    private float GetApproxPrefabRadius(GameObject prefab, bool considerRandomSize)
    {
        if (prefab == null) return 0.4f;
        if (prefabRadiusCache == null) prefabRadiusCache = new Dictionary<GameObject, float>();
        if (prefabRadiusCache.TryGetValue(prefab, out float cached))
        {
            return considerRandomSize ? cached * 1.2f : cached;
        }
        float radius = 0.4f;
        var meshFilters = prefab.GetComponentsInChildren<MeshFilter>(true);
        foreach (var mf in meshFilters)
        {
            if (mf == null || mf.sharedMesh == null) continue;
            var b = mf.sharedMesh.bounds;
            float scaleXZ = Mathf.Max(mf.transform.localScale.x, mf.transform.localScale.z);
            radius = Mathf.Max(radius, Mathf.Max(b.extents.x, b.extents.z) * Mathf.Max(0.001f, scaleXZ));
        }
        var renderers = prefab.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null) continue;
            var b = r.bounds;
            radius = Mathf.Max(radius, Mathf.Max(b.extents.x, b.extents.z));
        }
        prefabRadiusCache[prefab] = radius;
        return considerRandomSize ? radius * 1.2f : radius;
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (placementMode != PlacementMode.Manual)
        {
            return;
        }

        Event e = Event.current;
        if (e.type == EventType.MouseUp)
        {
            hasLastPaintPos = false;
        }
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            float derivedHardness = Mathf.Lerp(0.9f, 0.15f, softness);
            int previewSteps = brushMode == BrushMode.Advanced ? 14 : 1;
            for (int s = previewSteps; s >= 1; s--)
            {
                float t = (float)s / previewSteps;
                float radius = brushSize * t;
                float norm = t;
                float strength;
                if (norm <= derivedHardness)
                {
                    strength = 1f;
                }
                else
                {
                    float edgeT = (norm - derivedHardness) / Mathf.Max(0.0001f, 1f - derivedHardness);
                    float exponent = Mathf.Lerp(8f, 1.2f, softness);
                    strength = Mathf.Pow(1f - edgeT, exponent);
                }
                float alpha = (previewSteps == 1 ? 0.25f : strength * 0.15f);
                Handles.color = new Color(0f, 1f, 0f, alpha);
                Handles.DrawSolidDisc(hit.point, hit.normal, radius);
            }
            Handles.color = new Color(0f, 1f, 0f, 0.45f);
            Handles.DrawWireDisc(hit.point, hit.normal, brushSize);
            if (brushMode == BrushMode.Advanced)
            {
                Handles.color = new Color(0f, 1f, 0f, 0.35f);
                Handles.DrawWireDisc(hit.point, hit.normal, brushSize * Mathf.Lerp(0.9f, 0.15f, softness));
            }

            float derivedSpacing = Mathf.Lerp(0.05f, 1.0f, 1f - flow);
            float derivedInterval = Mathf.Lerp(0.03f, 0.25f, 1f - flow);
            bool paintingEvent = (e.type == EventType.MouseDrag || e.type == EventType.MouseDown) && e.button == 0;
            bool spacingReady = (!hasLastPaintPos || Vector3.Distance(hit.point, lastPaintPos) >= derivedSpacing);
            double now = EditorApplication.timeSinceStartup;
            bool timeReady = (now - lastPaintTime) >= derivedInterval;
            if (paintingEvent && spacingReady && timeReady)
            {
                lastPaintPos = hit.point;
                hasLastPaintPos = true;
                lastPaintTime = now;
                int remainingGlobal = Mathf.Max(0, brushDensity);
                int safetyBudget = Mathf.Max(remainingGlobal * 30, 60);
                foreach (var parentData in manualParentObjectsData)
                {
                    List<GameObject> parentExisting = new List<GameObject>();
                    foreach (var odCollect in parentData.objectDataList)
                    {
                        parentExisting.AddRange(odCollect.placedObjects);
                    }
                    foreach (var objectData in parentData.objectDataList)
                    {
                        if (remainingGlobal <= 0) break;
                        if (objectData.prefab == null || parentData.parentObject == null) continue;
                        if (objectData.groupObject == null)
                        {
                            objectData.groupObject = new GameObject(objectData.prefab.name + " Group");
                        }
                        List<GameObject> currentPlacedFoliage = new List<GameObject>();
                        int attempts = 0;
                        while (remainingGlobal > 0 && attempts < safetyBudget)
                        {
                            attempts++;
                            Vector2 offset2D = Random.insideUnitCircle * brushSize;
                            float radialDist = offset2D.magnitude / Mathf.Max(0.0001f, brushSize);
                            bool placeCandidate = true;
                            if (brushMode == BrushMode.Advanced)
                            {
                                float hardnessFrac = Mathf.Lerp(0.9f, 0.15f, softness);
                                float candidateStrength = radialDist <= hardnessFrac ? 1f : Mathf.Pow(1f - ((radialDist - hardnessFrac) / Mathf.Max(0.0001f, 1f - hardnessFrac)), Mathf.Lerp(8f, 1.2f, softness));
                                float probability = candidateStrength * intensity;
                                if (Random.value > probability) placeCandidate = false;
                            }
                            if (!placeCandidate)
                            {
                                continue;
                            }
                            Vector3 basePos = hit.point + new Vector3(offset2D.x, 0f, offset2D.y) + Vector3.up * 1f;
                            if (!Physics.Raycast(basePos, Vector3.down, out RaycastHit surfaceHit, 3f)) continue;
                            if (surfaceHit.collider == null || (parentData.parentObject != null && !surfaceHit.collider.transform.IsChildOf(parentData.parentObject.transform) && surfaceHit.collider.transform != parentData.parentObject.transform))
                            {
                                continue;
                            }
                            Vector3 placePos = surfaceHit.point - new Vector3(0, manualPlacementDepth, 0);
                            if (manualCheckForOtherObjects)
                            {
                                float estimateRadius = GetApproxPrefabRadius(objectData.prefab, manualRandomSize);
                                float overlapRadius = Mathf.Max(separation, estimateRadius);
                                if (HasExternalOverlap(placePos, overlapRadius, parentData, objectData)) continue;
                            }
                            bool tooClose = false;
                            if (separation > 0f)
                            {
                                float sepSqr = separation * separation;
                                foreach (var existing in parentExisting)
                                {
                                    if (existing == null) continue;
                                    if ((existing.transform.position - placePos).sqrMagnitude < sepSqr)
                                    {
                                        tooClose = true; break;
                                    }
                                }
                                if (!tooClose)
                                {
                                    foreach (var temp in currentPlacedFoliage)
                                    {
                                        if ((temp.transform.position - placePos).sqrMagnitude < sepSqr)
                                        {
                                            tooClose = true; break;
                                        }
                                    }
                                }
                            }
                            if (tooClose) continue;

                            GameObject foliageInstance = (GameObject)PrefabUtility.InstantiatePrefab(objectData.prefab);
                            foliageInstance.transform.position = placePos;
                            Quaternion rot = Quaternion.identity;
                            if (brushMode == BrushMode.Advanced && alignToNormal || brushMode == BrushMode.Easy)
                            {
                                rot = Quaternion.FromToRotation(Vector3.up, surfaceHit.normal);
                            }
                            if (randomRotation || brushMode == BrushMode.Easy)
                            {
                                rot *= Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                            }
                            foliageInstance.transform.rotation = rot;
                            foliageInstance.transform.parent = objectData.groupObject.transform;
                            if (manualRandomSize)
                            {
                                float randomScale = Random.Range(0.8f, 1.2f);
                                foliageInstance.transform.localScale = new Vector3(randomScale, randomScale, randomScale);
                            }
                            objectData.placedObjects.Add(foliageInstance);
                            parentExisting.Add(foliageInstance);
                            currentPlacedFoliage.Add(foliageInstance);
                            remainingGlobal--;
                        }
                        if (currentPlacedFoliage.Count > 0)
                        {
                            objectData.undoStack.Push(currentPlacedFoliage);
                        }
                    }
                    if (remainingGlobal <= 0) break;
                }
                e.Use();
            }
        }
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        EditorApplication.update -= UpdateContinuousRepaint;
    }

    private void UpdateContinuousRepaint()
    {
        if (continuousPreview && placementMode == PlacementMode.Manual)
        {
            SceneView.RepaintAll();
        }
    }

    private bool HasExternalOverlap(Vector3 position, float radius, ParentObjectData parentData, ObjectData objectData)
    {
        int count = Physics.OverlapSphereNonAlloc(position, radius, overlapBufferEditor);
        for (int i = 0; i < count; i++)
        {
            var col = overlapBufferEditor[i];
            if (col == null) continue;
            Transform t = col.transform;
            if (parentData.parentObject != null && (t == parentData.parentObject.transform || t.IsChildOf(parentData.parentObject.transform))) continue;
            GameObject go = col.gameObject;
            if (go == objectData.groupObject || objectData.placedObjects.Contains(go)) continue;
            return true;
        }
        return false;
    }
}
#endif
