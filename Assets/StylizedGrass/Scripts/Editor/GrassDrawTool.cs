using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

[EditorTool("Draw Grass", typeof(Grass))]
class GrassDrawTool : EditorTool, IDrawSelectedHandles
{
    readonly Vector2 brushRadiusRange = new(0.1f, 10f);
    readonly Vector2 grassRadiusRange = new(0.1f, 1f);
    float eraserRadius = .25f;
    float grassRadius = .1f;

    bool altPressed;
    int lastButtonIndex = 0;
    List<Vector3> pointCache = new List<Vector3>();

    GUIContent m_Icon;
    public override GUIContent toolbarIcon => m_Icon;

    void OnEnable()
    {
        m_Icon = new GUIContent("Draw Grass", AssetDatabase.LoadAssetAtPath<Texture>("Assets/StylizedGrass/Examples/Textures/GrassDrawIcon.png"), "Grass Draw Tool");
    }

    void OnDisable()
    {
        m_Icon = null;
    }

    public override void OnToolGUI(EditorWindow window)
    {
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        Event e = Event.current;

        Grass targetGrass = (Grass)target;

        // Allow orbiting
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.LeftAlt)
            altPressed = true;
        else if (e.type == EventType.KeyUp && e.keyCode == KeyCode.LeftAlt)
            altPressed = false;

        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && !altPressed)
        {
            // Left click
            if(e.button == 0)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    // Limit the amount of points poisson has to compare against to the adjacent chunks
                    targetGrass.InstancePointData.GetPointsAdjacentChunks(hit.point, ref pointCache);

                    targetGrass.InstancePointData.AddPointsToChunk(
                        PoissonDiscSampling.GeneratePointsInDisc(pointCache, hit.point, grassRadius)
                    );

                    targetGrass.UpdateInstancePointsBuffer();
                }

                lastButtonIndex = e.button;

                e.Use();
            }
            else if(e.button == 1)
            {
                // Right click
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    targetGrass.InstancePointData.RemoveInstancePointsAroundPoint(hit.point, eraserRadius);

                    targetGrass.UpdateInstancePointsBuffer();
                }

                lastButtonIndex = e.button;

                e.Use();
            }
        }
    }

    public void OnDrawHandles()
    {
        // Only show if active
        if (!ToolManager.IsActiveTool(this))
            return;

        Event e = Event.current;

        Grass targetGrass = (Grass)target;

        GUIStyle horStyle = new()
        {
            padding = new(5, 5, 5, 5),
            stretchWidth = true,
        };

        // Painting disc gizmo
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

        if (!altPressed && Physics.Raycast(ray, out RaycastHit hit))
        {
            using (lastButtonIndex == 0 ? new Handles.DrawingScope(Color.blue) : new Handles.DrawingScope(Color.red))
            {
                Handles.DrawWireDisc(hit.point, hit.normal, lastButtonIndex == 0 ? grassRadius : eraserRadius);
            }
        }

        // Scene view UI
        Handles.BeginGUI();

        var saveButtonStyle = new GUIStyle(GUI.skin.button);
        saveButtonStyle.normal.textColor = targetGrass.InstancePointData.SaveNotInSync() ? Color.yellow : Color.white;

        GUILayout.FlexibleSpace();

        GUI.backgroundColor = Color.gray;

        GUILayout.BeginHorizontal(horStyle);

        GUILayout.FlexibleSpace();

        GUILayout.Label("Eraser Radius");
        eraserRadius = EditorGUILayout.Slider(eraserRadius, brushRadiusRange.x, brushRadiusRange.y);
        GUILayout.Label(new GUIContent("Grass Radius", "Radius for Poisson Disc Sampling technigue to use. Basically just the min distance from grass instance point origin to another."));
        grassRadius = EditorGUILayout.Slider(grassRadius, grassRadiusRange.x, grassRadiusRange.y);

        if (GUILayout.Button("Clear Instance Points"))
        {
            if (EditorUtility.DisplayDialog("Are you sure?", "Are you sure you want to delete all the instance points?", "Yes", "Cancel"))
            {
                targetGrass.InstancePointData.Chunks.Clear();

                targetGrass.UpdateInstancePointsBuffer();

                targetGrass.InstancePointData.SavePoints();
            }
        }

        if (GUILayout.Button("Save Instance Points", saveButtonStyle))
                targetGrass.InstancePointData.SavePoints();

        if (GUILayout.Button("Load Instance Points"))
            targetGrass.InstancePointData.LoadPoints();

        GUILayout.FlexibleSpace();

        GUILayout.EndHorizontal();


        Handles.EndGUI();
    }
}