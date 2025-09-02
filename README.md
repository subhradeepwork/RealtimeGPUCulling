# Realtime GPU Culling for Unity

> **Realtime Frustum culling + GPU‑computed AABBs, wired for expansion to occlusion culling and GPU‑driven rendering.**

This repository demonstrates a practical end‑to‑end pipeline for performing visibility culling on the GPU in Unity at runtime. Mesh bounds are computed in parallel via a compute shader, then frustum tests run on the GPU every frame. The CPU only applies the visibility results to renderers. The result is a lightweight, scalable culling system you can grow into Hi‑Z occlusion and indirect drawing.

---

##  Highlights
- **All the heavy math on GPU**: AABB min/max for each mesh is computed with a compute shader.
- **Per‑frame GPU frustum tests**: Camera frustum planes are uploaded each frame and tested against AABBs on the GPU.
- **Minimal CPU work**: CPU just dispatches compute and applies on/off to renderers.
- **Scene‑agnostic**: Works with any hierarchy of child meshes.
- **Extensible**: Hooks and scaffolding for occlusion culling and GPU‑driven rendering.

---

##  Architecture at a Glance
```
[Mesh vertices (CPU)] --upload--> [GPU: AABB.compute]
                                  └─► per-mesh MIN / MAX (object space)
                                       └─► transformed to world space (CPU once)

[Camera (CPU)] --planes/frm--> [GPU: FrustumCulling.compute]
                                └─► visibility[meshIndex] ∈ {0,1}

[CPU] Apply visibility -> MeshRenderer.enabled
```

Key design choices:
- **AABBs computed once at scene start** and transformed to world space. Ideal for static geometry.
- **Frustum culling dispatched every frame**, using current camera planes.
- **Compute buffers** are reused; only plane data and visibility result flow each frame.

---

##  How It Works (Deep Dive)

### 1) GPU AABB computation (one‑time)
- Collect all `MeshFilter` components under a parent.
- For each mesh, upload vertex positions to a structured buffer.
- Dispatch a reduction compute kernel to compute `min`/`max` (object space).
- Transform each pair to world space using the mesh transform.
- Write packed `Vector3` arrays to `aabbMinBuffer` / `aabbMaxBuffer` for later use.

**Expected HLSL (AABB.compute)**
```hlsl
// Kernel 0: parallel reduction to find min/max of vertexPositions
StructuredBuffer<float3> vertexPositions;
RWStructuredBuffer<float3> intermediateMin; // size 1
RWStructuredBuffer<float3> intermediateMax; // size 1
cbuffer Params { uint numVertices; };

// ... typical tree reduction or chunked min/max accumulation ...
// Pseudocode: each thread reads vertices, computes local min/max, then atomically reduce.
```

> For dynamic objects, recompute on demand or move the world‑space transform step into a per‑frame pass.

### 2) Per‑frame frustum culling on GPU
- Each frame, compute 6 frustum planes from the active camera.
- Upload the planes to a GPU buffer.
- Dispatch a compute kernel that tests each AABB against all 6 planes.
- Write 1 if inside / intersecting, 0 if completely outside.

**Expected HLSL (FrustumCulling.compute)**
```hlsl
StructuredBuffer<float4> frustumPlanes;   // 6 planes: (xyz = normal, w = -distance)
StructuredBuffer<float3> aabbMin;         // world-space AABB mins
StructuredBuffer<float3> aabbMax;         // world-space AABB maxs
RWStructuredBuffer<uint> visibilityResults; // 0/1 per mesh

[numthreads(64,1,1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    uint i = id.x; // mesh index
    // load aabb
    float3 bmin = aabbMin[i];
    float3 bmax = aabbMax[i];
    // test against 6 planes using AABB-plane separating axis (optimizable via positive vertex trick)
    bool visible = true;
    [unroll]
    for (uint p = 0; p < 6; ++p) {
        float3 n = frustumPlanes[p].xyz;
        float  d = frustumPlanes[p].w;
        // Select vertex most outside the plane (AABB positive vertex test)
        float3 v = float3(n.x > 0 ? bmax.x : bmin.x,
                          n.y > 0 ? bmax.y : bmin.y,
                          n.z > 0 ? bmax.z : bmin.z);
        if (dot(n, v) + d < 0) { visible = false; break; }
    }
    visibilityResults[i] = visible ? 1u : 0u;
}
```

### 3) Apply results on CPU
- Read back the `visibilityResults` buffer.
- Toggle `MeshRenderer.enabled` per object.

> You can avoid CPU readback entirely by switching to **GPU‑driven rendering** (e.g., `DrawMeshInstancedIndirect`) and having the compute shader compact visible instances.

---

##  Quick Start

1. **Add scripts to your scene**
   - Create an empty GameObject (e.g., `CullingRoot`) and parent all static meshes under it.
   - Add **`AABBCalculator`** to `CullingRoot`.
   - Add **`FrustumCuller`** to any convenient GameObject (often `CullingRoot`).

2. **Provide compute shaders**
   - Create `AABB.compute` and `FrustumCulling.compute` (as per skeletons above) and assign them in the inspector.

3. **Assign camera & renderers**
   - In `FrustumCuller`, set `mainCam` to your active camera.
   - Fill the `meshRenderers` array in the *same order* as the child meshes processed by `AABBCalculator`.

4. **(Optional) Movement controls**
   - Add `SimpleMovement` to a camera rig or capsule to test culling by moving/rotating in play mode.
   - Ensure an input axis named `Rotation` exists (or modify the script to your input system).

5. **Press Play**
   - You should see distant/out‑of‑view meshes disable themselves automatically.

---

##  Public API & Buffers

### Buffers
- `aabbMinBuffer` : `ComputeBuffer` of `float3` mins, 1 per mesh
- `aabbMaxBuffer` : `ComputeBuffer` of `float3` maxs, 1 per mesh
- `frustumPlanesBuffer` : 6 × `float4` planes (xyz = normal, w = -distance)
- `visibilityBuffer` : `uint` per mesh (0/1)

### Dispatch
- **AABB.compute**: `numThreadGroups = ceil(vertices/256)` (typical chunk size; tune as needed)
- **FrustumCulling.compute**: `numThreadGroups = ceil(meshCount/64)`

> Thread group sizes are conservative defaults; tweak to your GPU.

---

##  Benchmarking Guide

To assess gains and scaling:
1. Create scenes with 1k, 10k, 50k static meshes (vary triangle counts).
2. Compare **baseline** (all renderers enabled) vs **GPU culling enabled**.
3. Track:
   - CPU frame time (main thread)
   - GPU time for `AABB.compute` (once) + per‑frame culling
   - SetPass Calls / Batches (Unity Profiler)
4. Move the camera through dense occluders to amplify savings.

> Expect the biggest wins when a large fraction of objects are culled.

---

##  Extending the System

### 1) Hi‑Z Occlusion Culling (planned)
- Build a depth pyramid (MIP chain) from the camera depth.
- In compute, test AABB’s screen‑space bounds against the depth pyramid.
- Mark invisible if fully behind previously written depth.

**Compute sketch**
```hlsl
Texture2D<float> DepthPyramid; // mipmapped
// For each mesh AABB -> project to screen rect -> test against coarsest necessary mip
```

### 2) GPU‑Driven Rendering
- Replace per‑renderer toggles with an **instance list**.
- Have the culling compute **compact visible instances** into an append/consume buffer.
- Issue `DrawMeshInstancedIndirect` using the compacted count.

### 3) Hierarchical Culling / BVH
- Group meshes into clusters (cells, BVH, or grid).
- Cull clusters first; only descend when a cluster is visible.

### 4) Dynamic Objects
- Recompute AABBs when transforms change, or compute world‑space AABBs in a per‑frame kernel.

---

##  Troubleshooting

- **Everything is invisible**
  - Verify frustum planes are uploaded each frame and the plane equation sign convention matches the shader.
  - Double‑check the `meshRenderers` order matches the buffers’ mesh order.

- **Objects pop or clip incorrectly**
  - Ensure AABBs are in **world space** before frustum tests.
  - For skinned meshes: compute skinned bounds or fallback to Unity’s `Renderer.bounds` prepass.

- **Compute errors / mis‑sized buffers**
  - Visibility buffer must have **exactly** one entry per mesh.
  - AABB buffers must match the mesh count and be set before dispatch.

- **No movement / rotation** (sample controller)
  - Add an input axis named `Rotation`, or modify `SimpleMovement` to the new Input System.

---

##  Constraints & Assumptions
- Geometry is treated as **static** by default (AABBs computed once).
- The sample uses `MeshRenderer.enabled` toggling (simple but CPU‑visible). For maximum throughput, switch to indirect draws.
- Skinned meshes and procedurals require specialized AABB strategies.

---

##  Design Notes
- Keep compute kernels **data‑oriented** and **branch‑light**.
- Use **positive‑vertex** plane tests for AABB vs plane (fewer ops).
- Avoid CPU readbacks on hot paths—prefer GPU‑only pipelines.
- Reserve CPU for scene logic; let the GPU decide visibility.

---

##  Roadmap
- [ ] Hi‑Z occlusion pass (depth pyramid + screen‑space tests)
- [ ] Instance compaction + `DrawMeshInstancedIndirect`
- [ ] BVH/clustered culling to reduce per‑object tests
- [ ] Skinned mesh support
- [ ] Editor tools (buffer visualizer, per‑object stats)

---

##  Requirements
- Unity 2021+ (Compute Shader support)
- DX11+/Metal/Vulkan‑capable GPU

---

##  License
MIT (or select your preferred license).

---

##  What to Read Next
- **AABB.compute** & **FrustumCulling.compute** implementation details
- Indirect instancing samples in Unity manual
- GPU culling talks/papers (Hi‑Z, clustered rendering)

> PRs welcome—let’s grow this into a complete GPU‑driven visibility stack.
