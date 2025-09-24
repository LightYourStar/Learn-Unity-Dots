using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

public enum MotionMode
{
    Bounce, //边界回弹
    Swarm, //往复聚拢
    Brownian, //扩散
    Galaxy //星系
}

public class BouncingBalls : MonoBehaviour
{
    [Header("数量与显示")] [Range(1000, 100000)]
    public int ballCount = 50000;

    public float ballRadius = 0.05f;
    public Mesh ballMesh; // 为空就用内置 Sphere
    public Material ballMaterial; // 需要开启 GPU Instancing 的材质

    [Header("物理参数")] public Vector2 boundsMin = new Vector2(-8f, -5f);
    public Vector2 boundsMax = new Vector2(8f, 5f);
    public float speedMin = 0.5f;
    public float speedMax = 3.0f;

    [Header("性能切换")] public bool useParallel = true; // 对比 Schedule 与 ScheduleParallel
    public bool useBurst = true; // 对比启用/禁用 Burst（编辑器下即时生效）

    public MotionMode mode = MotionMode.Bounce;

    public Text desc;

    private float totalTime = 0f;
    private int totalFrames = 0;

    private float avgFps = 0f;

    private MotionMode _lastMode;

    // 数据
    private NativeArray<float3> _positions;
    private NativeArray<float3> _velocities;
    private NativeArray<Matrix4x4> _matrices; // 渲染矩阵（由 Job 生成）
    private List<Matrix4x4[]> _batches; // 1023 一批
    private NativeArray<Unity.Mathematics.Random> _rngs;

    private void OnEnable()
    {
        // 运行时切换 Burst（只在 Editor/Development 下能立即生效）
#if UNITY_EDITOR
        BurstCompiler.Options.EnableBurstCompilation = useBurst;
        BurstCompiler.Options.EnableBurstSafetyChecks = false;
#endif
    }

    void Start()
    {
        if (ballMesh == null)
            ballMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        if (ballMaterial == null)
        {
            ballMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            ballMaterial.enableInstancing = true;
        }

        _positions = new NativeArray<float3>(ballCount, Allocator.Persistent);
        _velocities = new NativeArray<float3>(ballCount, Allocator.Persistent);
        _matrices = new NativeArray<Matrix4x4>(ballCount, Allocator.Persistent);

        var rnd = new Unity.Mathematics.Random(0xABCDEFu);
        float z = 0f; // 2D 平面里跑
        for (int i = 0; i < ballCount; i++)
        {
            float x = math.lerp(boundsMin.x + ballRadius, boundsMax.x - ballRadius, rnd.NextFloat());
            float y = math.lerp(boundsMin.y + ballRadius, boundsMax.y - ballRadius, rnd.NextFloat());
            _positions[i] = new float3(x, y, z);

            // 随机方向与速度
            float angle = rnd.NextFloat(0f, math.PI * 2f);
            float speed = rnd.NextFloat(speedMin, speedMax);
            _velocities[i] = new float3(math.cos(angle) * speed, math.sin(angle) * speed, 0f);
        }

        // 预分批（每批最多 1023）
        _batches = new List<Matrix4x4[]>();
        int idx = 0;
        while (idx < ballCount)
        {
            int count = Mathf.Min(1023, ballCount - idx);
            _batches.Add(new Matrix4x4[count]);
            idx += count;
        }

        ReinitRngs();
        _lastMode = mode;
    }

    private void ReinitRngs()
    {
        if (_rngs.IsCreated) _rngs.Dispose();
        _rngs = new NativeArray<Unity.Mathematics.Random>(ballCount, Allocator.Persistent);
        uint seed = (uint)UnityEngine.Random.Range(1, int.MaxValue); // 每次不同种子
        for (int i = 0; i < ballCount; i++)
        {
            _rngs[i] = new Unity.Mathematics.Random(seed + (uint)(i * 9973));
        }
    }

    void Update()
    {
        if (mode != _lastMode)
        {
            if (mode == MotionMode.Brownian)
            {
                ReinitRngs(); // 切回布朗运动时刷新随机序列
            }
            _lastMode = mode;
        }

        var bounds = new float4(boundsMin.x, boundsMin.y, boundsMax.x, boundsMax.y);

        float dt = math.min(Time.deltaTime, 0.033f);
        totalTime += dt;
        totalFrames++;

        if (totalTime > 0.5f) // 每 0.5s 更新一次平均值，避免抖动
        {
            avgFps = totalFrames / totalTime;
            totalTime = 0f;
            totalFrames = 0;
        }


        //物理更新（并行/串行）
        var moveJob = new MoveAndBounceJob
        {
            positions = _positions,
            velocities = _velocities,
            deltaTime = dt,
            boundsXYXY = bounds,
            radius = ballRadius,

            mode = this.mode,
            target = float3.zero, // 或 new float3(0, 0, 0) → Swarm / Galaxy 中心
            attractStrength = 0.5f, // Swarm 强度
            jitter = 2.0f, // Brownian 抖动幅度
            gravityStrength = 10f,

            rngs = _rngs
        };

        JobHandle handle;
        if (useParallel)
            handle = moveJob.Schedule(_positions.Length, 128); // IJobParallelFor
        else
            handle = moveJob.Schedule(_positions.Length, 1); // 退化成几乎串行

        // 生成渲染矩阵（并行）
        var matrixJob = new ToMatrixJob
        {
            positions = _positions,
            scale = new float3(ballRadius * 2f),
            matrices = _matrices
        };
        handle = matrixJob.Schedule(_positions.Length, 128, handle);

        handle.Complete();

        //提交渲染（按 1023 一批）
        int cursor = 0;
        for (int b = 0; b < _batches.Count; b++)
        {
            var batch = _batches[b];
            int count = batch.Length;
            // 把 NativeArray 的连续切片拷到托管数组（仅拷指针范围，不做装箱）
            _matrices.Slice(cursor, count).CopyTo(batch);
            Graphics.DrawMeshInstanced(ballMesh, 0, ballMaterial, batch);
            cursor += count;
        }

        // 在屏幕左上角打点信息
        desc.text = $"Balls: {ballCount} | Parallel: {useParallel} | Burst: {useBurst} | FPS: {(int)(1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f))} | AvgFps: {avgFps}";
    }

    void OnDestroy()
    {
        if (_positions.IsCreated) _positions.Dispose();
        if (_velocities.IsCreated) _velocities.Dispose();
        if (_matrices.IsCreated) _matrices.Dispose();
        if (_rngs.IsCreated) _rngs.Dispose();
    }

    // 负责移动与边界反弹
    [BurstCompile]
    private struct MoveAndBounceJob : IJobParallelFor
    {
        public NativeArray<float3> positions;
        public NativeArray<float3> velocities;
        public float deltaTime;
        public float4 boundsXYXY; // xMin,yMin,xMax,yMax
        public float radius;

        public MotionMode mode;
        public float3 target; // Swarm / Galaxy 中心
        public float attractStrength; // Swarm 吸引强度
        public float jitter; // Brownian 抖动强度
        public float gravityStrength; // Galaxy 引力强度

        [NativeDisableParallelForRestriction] public NativeArray<Unity.Mathematics.Random> rngs;

        public void Execute(int i)
        {
            float3 p = positions[i];
            float3 v = velocities[i];


            switch (mode)
            {
                case MotionMode.Bounce:
                    p += v * deltaTime;

                    // 左右
                    float xmin = boundsXYXY.x + radius;
                    float xmax = boundsXYXY.z - radius;
                    if (p.x < xmin)
                    {
                        p.x = xmin;
                        v.x = -v.x;
                    }
                    else if (p.x > xmax)
                    {
                        p.x = xmax;
                        v.x = -v.x;
                    }

                    // 上下
                    float ymin = boundsXYXY.y + radius;
                    float ymax = boundsXYXY.w - radius;
                    if (p.y < ymin)
                    {
                        p.y = ymin;
                        v.y = -v.y;
                    }
                    else if (p.y > ymax)
                    {
                        p.y = ymax;
                        v.y = -v.y;
                    }
                    break;

                case MotionMode.Swarm:
                    v += (target - p) * attractStrength * deltaTime;
                    p += v * deltaTime;
                    break;

                case MotionMode.Brownian:
                    var rng = rngs[i]; // 取出这个球的随机数发生器
                    v += new float3(
                        rng.NextFloat(-jitter, jitter),
                        rng.NextFloat(-jitter, jitter),
                        0) * deltaTime;
                    rngs[i] = rng; // 记得写回去，保持随机序列状态
                    p += v * deltaTime;
                    break;

                case MotionMode.Galaxy:
                    float3 dir = target - p;
                    float dist = math.length(dir);
                    float3 g = math.normalize(dir) * (gravityStrength / (dist * dist + 0.1f));
                    v += g * deltaTime;
                    p += v * deltaTime;
                    break;
            }

            positions[i] = p;
            velocities[i] = v;
        }
    }

    // 把位置转成渲染矩阵（缩放 = 直径）
    [BurstCompile]
    private struct ToMatrixJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> positions;
        [ReadOnly] public float3 scale;
        public NativeArray<Matrix4x4> matrices;

        public void Execute(int index)
        {
            float3 p = positions[index];
            matrices[index] = Matrix4x4.TRS(new Vector3(p.x, p.y, p.z), Quaternion.identity, new Vector3(scale.x, scale.y, scale.z));
        }
    }
}