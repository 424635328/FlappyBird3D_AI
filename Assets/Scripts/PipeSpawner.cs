using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 负责生成和管理 Flappy Bird 游戏中的管道对。
/// 注意：此版本不再修改 ScoreTrigger 的 Collider 大小，其大小由预制体决定。
/// </summary>
public class PipeSpawner : MonoBehaviour
{
    [Header("管道预制体列表 (Pipe Prefabs)")]
    [Tooltip("将所有可能生成的管道对预制体拖拽到这里。每次生成时会从中随机选择一个。")]
    public GameObject[] pipePrefabs; // 可供选择的管道预制体数组

    [Header("生成频率随机化 (Spawn Rate Randomization)")]
    [Tooltip("生成下一对管道所需的最短时间（秒）。")]
    public float minSpawnRate = 1.0f; // 最小生成间隔
    [Tooltip("生成下一对管道所需的最长时间（秒）。")]
    public float maxSpawnRate = 2.5f; // 最大生成间隔

    [Header("生成位置控制 (Spawn Position Control)")]
    [Tooltip("管道生成的固定 X 坐标。")]
    public float spawnXPosition = 10f; // 生成管道的 X 坐标

    [Header("生成高度随机化 (Spawn Height Randomization)")]
    [Tooltip("管道中心点 Y 轴变化范围的最小值。")]
    public float minHeightVariance = 1.0f; // 管道中心 Y 坐标相对于 Spawner 的最小偏移
    [Tooltip("管道中心点 Y 轴变化范围的最大值。")]
    public float maxHeightVariance = 2.5f; // 管道中心 Y 坐标相对于 Spawner 的最大偏移

    [Header("管道间隙随机化 (Pipe Gap Randomization)")]
    [Tooltip("生成的管道【可见缝隙】的最小尺寸。")]
    public float minVisibleGapSize = 2.5f; // 可见缝隙的最小值
    [Tooltip("生成的管道【可见缝隙】的最大尺寸。")]
    public float maxVisibleGapSize = 4.5f; // 可见缝隙的最大值

    // --- 移除：得分触发器优化参数相关的代码 ---
    // [Header("得分触发器优化 (Score Trigger Optimization)")]
    // [Tooltip("...")]
    // public float scoreTriggerVerticalPadding = 0.1f;
    // --- 移除结束 ---

    private float timer = 0f; // 计时器，用于追踪下次生成时间
    private float currentSpawnInterval; // 当前随机确定的生成间隔
    private List<GameObject> activePipes = new List<GameObject>(); // 存储当前场景中所有活动管道的列表
    private float? cachedSinglePipeHeight = null; // 缓存的单个管道段的高度，避免重复计算

    /// <summary>
    /// 初始化，设置第一次生成间隔并检查预制体
    /// </summary>
    void Start()
    {
        SetNextSpawnInterval(); // 设置第一次生成的时间间隔
        timer = 0f; // 重置计时器

        // 检查是否有可用的管道预制体
        if (pipePrefabs == null || pipePrefabs.Length == 0)
        {
            Debug.LogError("Pipe Prefabs 数组未分配或为空！请在 Inspector 中至少分配一个管道预制体。", gameObject);
            this.enabled = false; // 如果没有预制体，禁用此脚本
        }
    }

    /// <summary>
    /// 每帧更新，检查是否到达生成时间
    /// </summary>
    void Update()
    {
        if (!this.enabled) return; // 如果脚本被禁用，则不执行

        timer += Time.deltaTime; // 更新计时器
        // 如果计时器达到或超过当前生成间隔
        if (timer >= currentSpawnInterval)
        {
            SpawnPipe(); // 生成新管道
            timer = 0f; // 重置计时器
            SetNextSpawnInterval(); // 设置下一次生成的时间间隔
        }
    }

    /// <summary>
    /// 计算并设置下一次生成管道的时间间隔
    /// </summary>
    private void SetNextSpawnInterval()
    {
        // 确保最小生成率不小于0.1秒，最大生成率不小于最小生成率
        float clampedMinRate = Mathf.Max(0.1f, minSpawnRate);
        float clampedMaxRate = Mathf.Max(clampedMinRate, maxSpawnRate);
        // 在有效范围内随机选择一个生成间隔
        currentSpawnInterval = Random.Range(clampedMinRate, clampedMaxRate);
    }

    /// <summary>
    /// 生成一个新的管道对，并随机化其高度和间隙大小
    /// </summary>
    void SpawnPipe()
    {
        if (pipePrefabs == null || pipePrefabs.Length == 0) return; // 安全检查，确保有预制体
        // 随机选择一个预制体
        int randomIndex = Random.Range(0, pipePrefabs.Length);
        GameObject prefabToSpawn = pipePrefabs[randomIndex];
        if (prefabToSpawn == null) { Debug.LogWarning($"选中的管道预制体 (索引: {randomIndex}) 为空！"); return; } // 检查选中的预制体是否有效

        // 随机化生成高度
        float currentHeightVariance = Random.Range(minHeightVariance, maxHeightVariance); // 在设定的范围内随机选择高度偏移量
        float lowestYPoint = transform.position.y - currentHeightVariance; // 计算可能的最低 Y 坐标
        float highestYPoint = transform.position.y + currentHeightVariance; // 计算可能的最高 Y 坐标
        // 确定生成位置，X 坐标固定，Y 坐标在范围内随机
        Vector3 spawnPos = new Vector3(spawnXPosition, Random.Range(lowestYPoint, highestYPoint), transform.position.z);

        // 实例化管道预制体
        GameObject newPipe = Instantiate(prefabToSpawn, spawnPos, Quaternion.identity);

        // 获取新生成管道的子对象引用 (上下管道)
        Transform topPipe = newPipe.transform.Find("TopPipe");
        Transform bottomPipe = newPipe.transform.Find("BottomPipe");
        // Transform scoreTrigger = newPipe.transform.Find("ScoreTrigger"); // 不再需要查找 ScoreTrigger 来修改它

        // 如果找到了上下管道子对象，则进行间隙随机化
        if (topPipe != null && bottomPipe != null)
        {
            // 获取单个管道段的高度 (使用缓存或计算)
            float singlePipeHeight = GetOrCacheSinglePipeHeight(topPipe);
            if (singlePipeHeight <= 0) { Destroy(newPipe); return; } // 如果高度无效，销毁刚创建的管道

            // 随机化可见缝隙大小
            float clampedMinVisibleGap = Mathf.Max(0f, minVisibleGapSize); // 确保最小缝隙不小于0
            float clampedMaxVisibleGap = Mathf.Max(clampedMinVisibleGap, maxVisibleGapSize); // 确保最大缝隙不小于最小缝隙
            float targetVisibleGap = Random.Range(clampedMinVisibleGap, clampedMaxVisibleGap); // 在有效范围内随机选择一个可见缝隙大小

            // 计算上下管道中心点之间的距离 (等于 可见缝隙 + 单个管道高度)
            float targetCenterDistance = targetVisibleGap + singlePipeHeight;
            // 计算上下管道各自距离父对象中心点的距离 (即 targetCenterDistance 的一半)
            float halfCenterDistance = targetCenterDistance / 2.0f;

            // 根据计算出的距离设置上下管道的局部 Y 坐标
            topPipe.localPosition = new Vector3(topPipe.localPosition.x, halfCenterDistance, topPipe.localPosition.z);
            bottomPipe.localPosition = new Vector3(bottomPipe.localPosition.x, -halfCenterDistance, bottomPipe.localPosition.z);

            // --- 移除：不再修改 ScoreTrigger 的大小 ---
            // if (scoreTrigger != null)
            // {
            //     BoxCollider triggerCollider = scoreTrigger.GetComponent<BoxCollider>();
            //     if (triggerCollider != null)
            //     {
            //         // float triggerHeight = Mathf.Max(0f, targetVisibleGap - (2 * scoreTriggerVerticalPadding));
            //         // triggerCollider.center = new Vector3(triggerCollider.center.x, 0, triggerCollider.center.z);
            //         // triggerCollider.size = new Vector3(triggerCollider.size.x, triggerHeight, triggerCollider.size.z);
            //     }
            //      else { Debug.LogWarning($"管道 '{newPipe.name}' 的 ScoreTrigger 上没有找到 BoxCollider！", scoreTrigger); }
            // }
            // --- 移除结束 ---
        }
        else // 如果找不到上下管道子对象
        {
            Debug.LogWarning($"在实例化的管道 '{prefabToSpawn.name}' 上找不到 TopPipe 或 BottomPipe 子对象！无法随机化间隙。", newPipe);
        }

        // 将新生成的管道添加到活动管道列表中进行管理
        activePipes.Add(newPipe);
    }

    /// <summary>
    /// 获取或计算并缓存单个管道段的高度。
    /// 使用 Collider 的 bounds 来确定高度。
    /// </summary>
    /// <param name="pipeSegment">上管道或下管道的 Transform。</param>
    /// <returns>管道段的高度，如果无法计算则返回 0。</returns>
    private float GetOrCacheSinglePipeHeight(Transform pipeSegment)
    {
         // 如果已经缓存了高度，直接返回缓存值
         if (cachedSinglePipeHeight.HasValue)
         {
             return cachedSinglePipeHeight.Value;
         }
         else // 否则，计算高度
         {
             Collider pipeCollider = pipeSegment.GetComponent<Collider>(); // 获取管道段上的 Collider
             if (pipeCollider != null)
             {
                 float height = pipeCollider.bounds.size.y; // 获取 Collider 边界框的 Y 轴尺寸
                 if (height > 0) // 如果高度有效
                 {
                     cachedSinglePipeHeight = height; // 缓存计算出的高度
                     return height; // 返回高度
                 }
                 else // 如果计算出的高度无效
                 {
                      Debug.LogWarning($"计算出的管道高度 ({height}) 无效！检查 Collider。", pipeSegment);
                      return 0f; // 返回 0 表示无效
                 }
             }
             else // 如果找不到 Collider
             {
                 Debug.LogError($"找不到 '{pipeSegment.name}' 上的 Collider！无法确定管道高度。", pipeSegment);
                 return 0f; // 返回 0 表示无法计算
             }
         }
    }

    /// <summary>
    /// 清除场景中所有由该 Spawner 生成的管道。
    /// 通常在回合开始时调用。
    /// </summary>
    public void ClearPipes()
    {
        // 从后往前遍历列表并销毁管道游戏对象
        for (int i = activePipes.Count - 1; i >= 0; i--)
        {
            if (activePipes[i] != null) // 检查对象是否已被销毁
            {
                Destroy(activePipes[i]); // 销毁管道对象
            }
        }
        activePipes.Clear(); // 清空活动管道列表
        cachedSinglePipeHeight = null; // 清除缓存的管道高度
        timer = 0f; // 重置计时器
        // 如果 Spawner 仍然启用，立即设置下一次生成间隔，避免等待过久
        if (this.enabled) {
             SetNextSpawnInterval();
        }
    }
}