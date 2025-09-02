using UnityEngine;
using System.Collections.Generic;
using System;

public class AABBCalculator : MonoBehaviour
{
    public static AABBCalculator _instance;

    public ComputeShader aabbComputeShader;

    public static ComputeBuffer aabbMinBuffer;
    public static ComputeBuffer aabbMaxBuffer;

    public static event Action OnAABBsCalculated;


    // Lists to store the world space min and max points of AABBs for visualization
    public static List<Vector3> aabbMins = new List<Vector3>();
    public static List<Vector3> aabbMaxs = new List<Vector3>();

    private List<ComputeBuffer> vertexBuffers = new List<ComputeBuffer>();


    private void Awake()
    {
        _instance = this;
    }

    void Start()
    {
        // Correctly initializing the meshFilters list here
        List<MeshFilter> meshFilters = new List<MeshFilter>(GetComponentsInChildren<MeshFilter>());
        int meshCount = meshFilters.Count;

        Vector3[] aabbMinsArray = new Vector3[meshCount];
        Vector3[] aabbMaxsArray = new Vector3[meshCount];

        for (int i = 0; i < meshCount; i++)
        {
            Mesh mesh = meshFilters[i].mesh;
            if (mesh == null) continue;

            Vector3[] vertices = mesh.vertices;
            ComputeBuffer vertexBuffer = new ComputeBuffer(vertices.Length, sizeof(float) * 3);
            vertexBuffer.SetData(vertices);
            vertexBuffers.Add(vertexBuffer);

            int numThreadGroups = Mathf.CeilToInt(vertices.Length / 256.0f);

            ComputeBuffer intermediateMinBuffer = new ComputeBuffer(1, sizeof(float) * 3, ComputeBufferType.Raw);
            ComputeBuffer intermediateMaxBuffer = new ComputeBuffer(1, sizeof(float) * 3, ComputeBufferType.Raw);

            aabbComputeShader.SetBuffer(0, "vertexPositions", vertexBuffer);
            aabbComputeShader.SetBuffer(0, "intermediateMin", intermediateMinBuffer);
            aabbComputeShader.SetBuffer(0, "intermediateMax", intermediateMaxBuffer);
            aabbComputeShader.SetInt("numVertices", vertices.Length);

            aabbComputeShader.Dispatch(0, numThreadGroups, 1, 1);

            Vector3[] minResult = new Vector3[1];
            Vector3[] maxResult = new Vector3[1];
            intermediateMinBuffer.GetData(minResult);
            intermediateMaxBuffer.GetData(maxResult);

            // Transform the results back into world space
            aabbMinsArray[i] = meshFilters[i].transform.TransformPoint(minResult[0]);
            aabbMaxsArray[i] = meshFilters[i].transform.TransformPoint(maxResult[0]);

            intermediateMinBuffer.Release();
            intermediateMaxBuffer.Release();
        }

        aabbMinBuffer = new ComputeBuffer(meshCount, sizeof(float) * 3, ComputeBufferType.Default);
        aabbMaxBuffer = new ComputeBuffer(meshCount, sizeof(float) * 3, ComputeBufferType.Default);
        aabbMinBuffer.SetData(aabbMinsArray);
        aabbMaxBuffer.SetData(aabbMaxsArray);

        // Store the AABBs for visualization
        aabbMins.AddRange(aabbMinsArray);
        aabbMaxs.AddRange(aabbMaxsArray);


        OnAABBsCalculated?.Invoke();
    }

    void OnDestroy()
    {
        foreach (var buffer in vertexBuffers)
        {
            if (buffer != null) buffer.Release();
        }
        if (aabbMinBuffer != null) aabbMinBuffer.Release();
        if (aabbMaxBuffer != null) aabbMaxBuffer.Release();

        aabbMins.Clear();
        aabbMaxs.Clear();
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.gray;
        for (int i = 0; i < aabbMins.Count && i < aabbMaxs.Count; i++)
        {
            Vector3 min = aabbMins[i];
            Vector3 max = aabbMaxs[i];
            Gizmos.DrawWireCube((min + max) / 2, max - min);
        }
    }
}
