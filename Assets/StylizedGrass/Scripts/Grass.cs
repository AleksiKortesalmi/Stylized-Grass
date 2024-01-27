using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class Grass : MonoBehaviour
{
    const string enableHeightString = "RANDOM_HEIGHT_ON";
    const string enableRotString = "RANDOM_ROTATION_ON";
    const string enableBillboardString = "BILLBOARD_ON";

    [SerializeField] InstancePointData instancePointData;
    public InstancePointData InstancePointData { get => instancePointData; }

    [Header("Instance")]
    [SerializeField] Mesh mesh;
    [SerializeField] Material material;
    [Header("Wind")]
    [SerializeField] float windStrength = 1;
    [SerializeField] float windSpeed = 1;
    [SerializeField] float windNoiseScale = 1;
    [SerializeField] Vector3 windDirection = Vector3.one;
    [Header("Geometry")]
    [SerializeField] Vector3 scale = Vector3.one;
    [SerializeField] bool billboard = true;
    [SerializeField] float geometryNoiseScale = 1;
    [Space(10)]
    [SerializeField] bool enableRandomHeight = true;
    [SerializeField] Vector2 yScaleRange = new(0.75f, 1.25f);
    [Space(10)]
    [SerializeField] bool enableRandomRotation = true;
    [SerializeField] Vector2 rotRange = new(-15, 15);
    [Header("Interactions")]
    [SerializeField] float interactorStrength = 1;

    List<Vector3> instancePoints = new();
    GrassInteractor[] grassInteractors;
    Vector4[] interactorVectors;

    GraphicsBuffer instancePointsBuffer;
    GraphicsBuffer interactorsBuffer;

    GraphicsBuffer meshTrianglesBuffer;
    GraphicsBuffer meshPositionsBuffer;
    GraphicsBuffer meshNormalsBuffer;
    GraphicsBuffer meshUVsBuffer;

    RenderParams rp;
    LocalKeyword enableHeightKeyword;
    LocalKeyword enableRotationKeyword;
    LocalKeyword enableBillboardKeyword;
    Matrix4x4 objectToWorld = Matrix4x4.identity;

    private void OnValidate()
    {
        if (material)
        {
            InitializeRenderParams(out rp);

            // Local shader keywords
            enableHeightKeyword = new(material.shader, enableHeightString);
            enableRotationKeyword = new(material.shader, enableRotString);
            enableBillboardKeyword = new(material.shader, enableBillboardString);
            material.SetKeyword(enableHeightKeyword, enableRandomHeight);
            material.SetKeyword(enableRotationKeyword, enableRandomRotation);
            material.SetKeyword(enableBillboardKeyword, billboard);
        }
    }

    private void Start()
    {
        if(instancePointData)
            instancePointData.Initialize();

        Initialize();
    }

    private void OnDisable()
    {
        ReleaseBuffers();
    }

    public void Initialize()
    {
        grassInteractors = FindObjectsByType<GrassInteractor>(FindObjectsSortMode.None);

        InitializeBuffers();

        if(rp.matProps == null)
            InitializeRenderParams(out rp);

        rp.matProps.SetBuffer("_InstancePoints", instancePointsBuffer);
        rp.matProps.SetBuffer("_Interactors", interactorsBuffer);

        rp.matProps.SetBuffer("_Triangles", meshTrianglesBuffer);
        rp.matProps.SetBuffer("_Positions", meshPositionsBuffer);
        rp.matProps.SetBuffer("_Normals", meshNormalsBuffer);
        rp.matProps.SetBuffer("_UVs", meshUVsBuffer);
        rp.matProps.SetInt("_StartIndex", (int)mesh.GetIndexStart(0));
        rp.matProps.SetInt("_BaseVertexIndex", (int)mesh.GetBaseVertex(0));
        rp.matProps.SetInt("_NumInteractors", grassInteractors.Length);

        var rampTexture = material.GetTexture("_RampTexture");
        if(rampTexture != null)
            rp.matProps.SetInt("_RampTexWidth", rampTexture.width);

        rp.matProps.SetFloat("_WindStrength", windStrength);
        rp.matProps.SetFloat("_WindSpeed", windSpeed);
        rp.matProps.SetFloat("_NoiseScale", windNoiseScale);
        rp.matProps.SetVector("_WindDirection", windDirection.normalized);
        rp.matProps.SetFloat("_GeometryNoiseScale", geometryNoiseScale);
        rp.matProps.SetVector("_Scale", scale);
        rp.matProps.SetVector("_YScaleRange", yScaleRange);
        rp.matProps.SetVector("_RotRange", rotRange);
        rp.matProps.SetFloat("_InteractorStrength", interactorStrength);
        rp.matProps.SetMatrix("_ObjectToWorld", objectToWorld);
    }

    void InitializeBuffers()
    {
        // Lighten the load for editormode updates
        if (GraphicsBuffersInitialized())
            return;

        if (instancePointData && instancePointData.TotalPointAmount > 0)
        {
            instancePointsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, instancePointData.TotalPointAmount, 3 * sizeof(float));
        }

        if (grassInteractors != null && grassInteractors.Length > 0)
        {
            interactorsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, grassInteractors.Length, 4 * sizeof(float));
        }

        // Remember to check "Read/Write" on the mesh asset to get access to the geometry data
        meshTrianglesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, mesh.triangles.Length, sizeof(int));
        meshPositionsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, mesh.vertices.Length, 3 * sizeof(float));
        meshNormalsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, mesh.normals.Length, 3 * sizeof(float));
        meshUVsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, mesh.uv.Length, 2 * sizeof(float));

        meshTrianglesBuffer.SetData(mesh.triangles);
        meshPositionsBuffer.SetData(mesh.vertices);
        meshNormalsBuffer.SetData(mesh.normals);
        meshUVsBuffer.SetData(mesh.uv);
    }

    void InitializeRenderParams(out RenderParams renderParams)
    {
        // Assign material properties
        renderParams = new(material)
        {
            worldBounds = instancePointData.Bounds,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = true,
            matProps = new MaterialPropertyBlock()
        };
    }

    void Update()
    {
        // Allow live editing in the editor
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            if(instancePointData)
                instancePointData.Initialize();

            Initialize();
        }
#endif

        if (grassInteractors.Length > 0 && interactorsBuffer != null)
        {
            UpdateInteractorVectors();

            interactorsBuffer.SetData(interactorVectors);
        }

        // Update visible instance points and render if any points are visible
        if (instancePointData.GetVisiblePoints(ref instancePoints))
        {
            instancePointsBuffer.SetData(instancePoints);
           
            Render();
        }
    }

    void Render()
    {
        Graphics.RenderPrimitives(rp, MeshTopology.Triangles, (int)mesh.GetIndexCount(0), instancePoints.Count);
    }

    void UpdateInteractorVectors()
    {
        if(interactorVectors == null || interactorVectors.Length != grassInteractors.Length)
            interactorVectors = new Vector4[grassInteractors.Length];

        for (int i = 0; i < grassInteractors.Length; i++)
        {
            InteractorToVector4(ref interactorVectors[i], grassInteractors[i]);
        }
    }

    void InteractorToVector4(ref Vector4 vector, GrassInteractor interactor)
    {
        vector.x = interactor.transform.position.x;
        vector.y = interactor.transform.position.y;
        vector.z = interactor.transform.position.z;
        vector.w = interactor.radius;
    }

    bool GraphicsBuffersInitialized()
    {
        return instancePointsBuffer != null &&
            instancePointData.TotalPointAmount != 0 &&
            meshTrianglesBuffer != null &&
            meshPositionsBuffer != null &&
            meshNormalsBuffer != null &&
            meshUVsBuffer != null;
    }

    void ReleaseBuffers()
    {
        instancePointsBuffer?.Release();
        instancePointsBuffer = null;
        interactorsBuffer?.Release();
        interactorsBuffer = null;
        meshTrianglesBuffer?.Release();
        meshTrianglesBuffer = null;
        meshPositionsBuffer?.Release();
        meshPositionsBuffer = null;
        meshNormalsBuffer?.Release();
        meshNormalsBuffer = null;
        meshUVsBuffer?.Release();
        meshUVsBuffer = null;
    }

#if UNITY_EDITOR
    // "Resizes" instance point buffer and renders immediately when total point amount changes
    public void UpdateInstancePointsBuffer()
    {
        instancePointsBuffer?.Release();
        instancePointsBuffer = null;

        if (instancePointData.GetVisiblePoints(ref instancePoints))
        {
            instancePointsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, instancePoints.Count, 3 * sizeof(float));
            instancePointsBuffer.SetData(instancePoints);

            rp.matProps?.SetBuffer("_InstancePoints", instancePointsBuffer);

            Render();
        }
    }

    private void OnDrawGizmosSelected()
    {
        if(instancePointData)
            instancePointData.DrawGizmos();
    }
#endif
}