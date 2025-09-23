using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

public class GoSpawner : MonoBehaviour
{
    public Mesh bulletMesh;
    public Material bulletMaterial;
    public int bulletCount = 1000000;

    void Start()
    {
        var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        var renderMeshArray = new RenderMeshArray(
            new Material[] { bulletMaterial },
            new Mesh[] { bulletMesh }
        );

        // 渲染描述
        var renderMeshDescription = new RenderMeshDescription(
            shadowCastingMode: UnityEngine.Rendering.ShadowCastingMode.Off,
            receiveShadows: false
        );

        // 定义 Archetype（拥有 Transform + 渲染）
        var archetype = entityManager.CreateArchetype(
            typeof(LocalTransform),
            typeof(RenderMeshArray),
            typeof(WorldRenderBounds),
            typeof(RenderBounds)
        );

        // 批量创建 Entity
        NativeArray<Entity> entities = new NativeArray<Entity>(bulletCount, Allocator.Temp);
        entityManager.CreateEntity(archetype, entities);

        // 随机数生成器
        Unity.Mathematics.Random rand = new Unity.Mathematics.Random(12345);

        for (int i = 0; i < bulletCount; i++)
        {
            var entity = entities[i];

            // 添加渲染需要的所有组件
            RenderMeshUtility.AddComponents(
                entity,
                entityManager,
                renderMeshDescription,
                renderMeshArray,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)
            );

            // 设置位置
            float3 pos = new float3(
                rand.NextFloat(-100f, 100f),
                rand.NextFloat(-100f, 100f),
                rand.NextFloat(-100f, 100f)
            );

            entityManager.SetComponentData(entity, LocalTransform.FromPositionRotationScale(
                pos, quaternion.identity, 0.1f
            ));
        }

        entities.Dispose();
    }
}