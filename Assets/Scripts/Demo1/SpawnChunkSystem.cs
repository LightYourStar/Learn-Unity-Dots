using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Rendering;
using UnityEngine;

public partial class SpawnChunkSystem : SystemBase
{
    private EntityArchetype archetype;
    private RenderMeshArray renderMeshArray;
    private RenderMeshDescription renderMeshDescription;
    private Unity.Mathematics.Random rand;

    private int totalToSpawn = 100000;   // 总量
    private int spawned = 0;
    private int batchPerFrame = 2000;    // 每帧生成2千

    protected override void OnCreate()
    {
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;

        // Archetype
        archetype = em.CreateArchetype(
            typeof(LocalTransform),
            typeof(RenderMeshArray),
            typeof(WorldRenderBounds),
            typeof(RenderBounds)
        );

        // Cube Mesh
        var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var mesh = temp.GetComponent<MeshFilter>().sharedMesh;
        Object.Destroy(temp);

        // 材质
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));

        renderMeshArray = new RenderMeshArray(
            new Material[] { material },
            new Mesh[] { mesh }
        );

        renderMeshDescription = new RenderMeshDescription(
            shadowCastingMode: UnityEngine.Rendering.ShadowCastingMode.Off,
            receiveShadows: false
        );

        rand = new Unity.Mathematics.Random(12345);
    }

    protected override void OnUpdate()
    {
        if (spawned >= totalToSpawn) return;

        var em = World.DefaultGameObjectInjectionWorld.EntityManager;
        int toSpawn = math.min(batchPerFrame, totalToSpawn - spawned);

        for (int i = 0; i < toSpawn; i++)
        {
            var entity = em.CreateEntity(archetype);

            RenderMeshUtility.AddComponents(
                entity,
                em,
                renderMeshDescription,
                renderMeshArray,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)
            );

            float3 pos = new float3(
                rand.NextFloat(-100f, 100f),
                rand.NextFloat(-100f, 100f),
                rand.NextFloat(-100f, 100f)
            );

            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(
                pos, quaternion.identity, 0.1f
            ));
        }

        spawned += toSpawn;
        Debug.Log($"Spawned {spawned}/{totalToSpawn}");
    }
}