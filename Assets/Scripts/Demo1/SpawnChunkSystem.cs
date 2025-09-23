using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Rendering;
using UnityEngine;

public partial class SpawnChunkSystem : SystemBase
{
    private EntityArchetype archetype;
    private RenderMeshArray renderMeshArray;
    private RenderMeshDescription renderMeshDescription;
    public Unity.Mathematics.Random rand;

    private int totalToSpawn = 100000;   // 总数量
    private int spawned = 0;
    private int batchPerFrame = 300;   // 每帧生成一万

    protected override void OnCreate()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Demo1_Static")
        {
            Enabled = false;
        }

        var em = World.DefaultGameObjectInjectionWorld.EntityManager;

        // Archetype：初始就要有 LocalTransform，否则 JobEntity 不能跑
        archetype = em.CreateArchetype(
            typeof(LocalTransform),
            typeof(RenderMeshArray),
            typeof(WorldRenderBounds),
            typeof(RenderBounds)
        );

        // Mesh
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
    }

    // protected override void OnUpdate()
    // {
    //     if (spawned >= totalToSpawn) return;
    //
    //     var em = World.DefaultGameObjectInjectionWorld.EntityManager;
    //     int toSpawn = math.min(batchPerFrame, totalToSpawn - spawned);
    //
    //     for (int i = 0; i < toSpawn; i++)
    //     {
    //         var entity = em.CreateEntity(archetype);
    //
    //         RenderMeshUtility.AddComponents(
    //             entity,
    //             em,
    //             renderMeshDescription,
    //             renderMeshArray,
    //             MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)
    //         );
    //
    //         float3 pos = new float3(
    //             rand.NextFloat(-100f, 100f),
    //             rand.NextFloat(-100f, 100f),
    //             rand.NextFloat(-100f, 100f)
    //         );
    //
    //         em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(
    //             pos, quaternion.identity, 0.1f
    //         ));
    //     }
    //
    //     spawned += toSpawn;
    //     Debug.Log($"Spawned {spawned}/{totalToSpawn}");
    // }

    protected override void OnUpdate()
    {
        if (spawned >= totalToSpawn) return;

        var em = World.DefaultGameObjectInjectionWorld.EntityManager;
        int toSpawn = math.min(batchPerFrame, totalToSpawn - spawned);

        // 批量创建 Entity
        NativeArray<Entity> entities = new NativeArray<Entity>(toSpawn, Allocator.Temp);
        em.CreateEntity(archetype, entities);

        for (int i = 0; i < toSpawn; i++)
        {
            RenderMeshUtility.AddComponents(
                entities[i],
                em,
                renderMeshDescription,
                renderMeshArray,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)
            );

            // 给个默认 transform (0,0,0)，后面 JobEntity 再改
            em.SetComponentData(entities[i], LocalTransform.FromPositionRotationScale(
                float3.zero, quaternion.identity, 0.1f
            ));
        }

        entities.Dispose();

        // 调度一个并行 JobEntity 来初始化随机位置
        new InitEntityJob
        {
            Rand = new Unity.Mathematics.Random((uint)(spawned + 1) * 1234)
        }.ScheduleParallel();

        spawned += toSpawn;
        Debug.Log($"Spawned {spawned}/{totalToSpawn}");
    }

}

[BurstCompile]
public partial struct InitEntityJob : IJobEntity
{
    public Unity.Mathematics.Random Rand;

    void Execute(ref LocalTransform transform)
    {
        transform = LocalTransform.FromPositionRotationScale(
            new float3(
                Rand.NextFloat(-100f, 100f),
                Rand.NextFloat(-100f, 100f),
                Rand.NextFloat(-100f, 100f)
            ),
            quaternion.identity,
            0.1f
        );
    }
}