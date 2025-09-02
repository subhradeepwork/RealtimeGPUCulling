using UnityEngine;

public class FrustumCuller : MonoBehaviour
{
    public ComputeShader frustumCullingShader;
    //public ComputeShader occlusionCullingShader; // Reference to the occlusion culling compute shader
    private ComputeBuffer frustumPlanesBuffer, visibilityBuffer, occlusionVisibilityBuffer;
    public MeshRenderer[] meshRenderers; // Assign this array to match the child meshes order in AABBCalculator
    public Camera mainCam;
    //public RenderTexture depthTexture; // The depth texture used for occlusion culling

    private int numObjects; // The number of objects being culled

    void Awake()
    {
        AABBCalculator.OnAABBsCalculated += InitializeCulling;
    }

    void InitializeCulling()
    {
        // Assuming meshRenderers array size matches the number of child meshes processed in AABBCalculator
        numObjects = AABBCalculator.aabbMins.Count;
        if (meshRenderers.Length != numObjects)
        {
            Debug.LogError("FrustumCuller: meshRenderers array size does not match the number of processed child meshes.");
            return;
        }

        // Initialize buffers for frustum planes and visibility results
        frustumPlanesBuffer = new ComputeBuffer(6, sizeof(float) * 4);
        visibilityBuffer = new ComputeBuffer(meshRenderers.Length, sizeof(int));
        occlusionVisibilityBuffer = new ComputeBuffer(meshRenderers.Length, sizeof(int), ComputeBufferType.Default);

        // Perform occlusion culling after initialization
        //PerformOcclusionCulling();
    }

    void Update()
    {
        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(mainCam);
        frustumPlanesBuffer.SetData(frustumPlanes);

        // Set compute shader buffers and parameters for frustum culling
        frustumCullingShader.SetBuffer(0, "frustumPlanes", frustumPlanesBuffer);
        frustumCullingShader.SetBuffer(0, "aabbMin", AABBCalculator.aabbMinBuffer);
        frustumCullingShader.SetBuffer(0, "aabbMax", AABBCalculator.aabbMaxBuffer);
        frustumCullingShader.SetBuffer(0, "visibilityResults", visibilityBuffer);

        // Dispatch frustum culling compute shader
        int threadGroups = Mathf.CeilToInt(meshRenderers.Length / 64f);
        frustumCullingShader.Dispatch(0, threadGroups, 1, 1);

        ApplyVisibilityResults();
    }

    /*
    void PerformOcclusionCulling()
    {
        // Setup and dispatch the occlusion culling compute shader
        occlusionCullingShader.SetTexture(0, "depthTexture", depthTexture);
        occlusionCullingShader.SetBuffer(0, "aabbMin", AABBCalculator.aabbMinBuffer);
        occlusionCullingShader.SetBuffer(0, "aabbMax", AABBCalculator.aabbMaxBuffer);
        occlusionCullingShader.SetBuffer(0, "visibilityResults", occlusionVisibilityBuffer);

        // Dispatch occlusion culling compute shader
        occlusionCullingShader.Dispatch(0, Mathf.CeilToInt(meshRenderers.Length / 64f), 1, 1);
    }
    */

    void ApplyVisibilityResults()
    {
        // Read back visibility results from occlusion culling (and potentially frustum culling)
        int[] visibilityResults = new int[meshRenderers.Length];
        visibilityBuffer.GetData(visibilityResults);

        //occlusionVisibilityBuffer.GetData(visibilityResults);

        // Apply visibility results to meshRenderers
        for (int i = 0; i < visibilityResults.Length; i++)
        {
            meshRenderers[i].enabled = visibilityResults[i] == 1;
        }
    }

    void OnDestroy()
    {
        if (frustumPlanesBuffer != null) frustumPlanesBuffer.Release();
        if (visibilityBuffer != null) visibilityBuffer.Release();
        if (occlusionVisibilityBuffer != null) occlusionVisibilityBuffer.Release();
        AABBCalculator.OnAABBsCalculated -= InitializeCulling;
    }
}
