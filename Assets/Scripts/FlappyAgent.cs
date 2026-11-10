using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using UnityEngine.Assertions;
using System.Collections.Generic;
using System.Linq;
using Unity.MLAgents.Policies; // 需要这个

/// <summary>
/// Flappy Bird 智能体脚本 (增加了加速下落功能, 并修复了编译错误)
/// </summary>
public class FlappyAgent : Agent
{
    #region Inspector 可配置参数 (保持不变)
    // ================== Inspector 可配置参数 (保持不变) ==================
    [Header("移动参数 (Movement)")]
    [SerializeField] private float jumpForce = 6f;
    [Tooltip("按下加速下落时施加的向下力的大小。")]
    [SerializeField] private float fastFallForce = 10f;
    [Tooltip("Agent 重置到的基础本地位置。")]
    [SerializeField] private Vector3 startPosition = new Vector3(0f, 0f, 0f);

    [Header("起始状态随机化 (Start State Randomization)")]
    [SerializeField] private float startPosXVariance = 0.1f;
    [SerializeField] private float startPosYVariance = 0.5f;
    [SerializeField] private float startVelYVariance = 0.5f;

    [Header("环境边界 (Environment Bounds)")]
    [SerializeField] private float upperBound = 7.0f;
    [SerializeField] private float lowerBound = -7.0f;

    [Header("奖励信号 (Reward Signals)")]
    [SerializeField] private float stepReward = 0.15f;
    [SerializeField] private float passReward = 0.20f;
    [SerializeField] private float collisionPenalty = -1.0f;
    [SerializeField] private float boundaryPenalty = -2.0f;

    [Header("观察设置 (Observation Settings)")]
    [SerializeField] private float defaultPipeDistance = 15f;
    [SerializeField] private float defaultRelativeY = 0f;
    [SerializeField] private float defaultGapSize = 0f;
    [SerializeField] private float defaultPipeSpeed = 0f;

    [Header("必需的引用 (Required References)")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private PipeSpawner pipeSpawner; // 保留引用，主要用于 ClearPipes
    [SerializeField] private GameManager gameManager; // 保留引用
    #endregion

    #region 内部状态与结构体
    // ================== 内部状态与结构体 ==================
    private bool isAlive = true;
    private bool isApplyingFastFall = false;
    private BehaviorParameters behaviorParameters; // 用于检查模式

    // 用于存储单个管道观察信息的结构体
    private struct PipeObservationInfo
    {
        public float DistanceX;         // 水平距离
        public float RelativeGapCenterY; // 相对缝隙中心 Y
        public float GapSize;           // 缝隙大小
    }
    #endregion

    #region Initialization and Episode Management
    // ================== 初始化与回合管理 ==================
    public override void Initialize()
    {
#if UNITY_EDITOR
        Debug.Log($"[Debug] {gameObject.name}: Initialize 方法被调用。");
#endif
        Assert.IsNotNull(rb, $"{gameObject.name}: Rigidbody 引用 (rb) 未设置!");
        Assert.IsNotNull(pipeSpawner, $"{gameObject.name}: PipeSpawner 引用未设置!");
        if (gameManager == null) gameManager = GameManager.Instance; // 尝试获取 GameManager
        if (gameManager == null) Debug.LogWarning($"{gameObject.name}: GameManager 引用未设置或找不到实例。");
        if (rb == null) rb = GetComponent<Rigidbody>();
        Assert.IsNotNull(rb, $"{gameObject.name}: 找不到 Rigidbody 组件。");

        // 获取 BehaviorParameters 组件
        behaviorParameters = GetComponent<BehaviorParameters>();
        if (behaviorParameters == null)
        {
            Debug.LogError("FlappyAgent 找不到 BehaviorParameters 组件！Agent 将无法正常工作。", gameObject);
        }

        // 检查行为参数配置
        if (behaviorParameters != null)
        {
            int expectedObsSize = 12; // 自身(2) + 3管道(3*3=9) + 速度(1) = 12
            if (behaviorParameters.BrainParameters.VectorObservationSize != expectedObsSize)
            {
                Debug.LogWarning($"行为参数 VectorObservationSize ({behaviorParameters.BrainParameters.VectorObservationSize}) 与预期 ({expectedObsSize}) 不符。请在 Inspector 中将 'Space Size' 改为 {expectedObsSize}！", gameObject);
            }
            int expectedActionSize = 3; // 0:无操作, 1:跳跃, 2:加速下落
            if (behaviorParameters.BrainParameters.ActionSpec.BranchSizes[0] != expectedActionSize)
            {
                Debug.LogWarning($"行为参数 Action Branch 0 Size ({behaviorParameters.BrainParameters.ActionSpec.BranchSizes[0]}) 与预期 ({expectedActionSize}) 不符。请在 Inspector 中将 'Branch 0 Size' 改为 {expectedActionSize}！", gameObject);
            }
        }
    }

    public override void OnEpisodeBegin()
    {
#if UNITY_EDITOR
        // Debug.Log($"[Debug] {gameObject.name}: OnEpisodeBegin 调用。重置并随机化状态。");
#endif
        // 1. 重置 Agent 状态
        float randomOffsetX = Random.Range(-startPosXVariance, startPosXVariance);
        float randomOffsetY = Random.Range(-startPosYVariance, startPosYVariance);
        transform.localPosition = startPosition + new Vector3(randomOffsetX, randomOffsetY, 0f);
        if (rb != null) { rb.isKinematic = false; float randomStartYVel = Random.Range(-startVelYVariance, startVelYVariance); rb.velocity = new Vector3(0, randomStartYVel, 0); rb.angularVelocity = Vector3.zero; rb.useGravity = true; }
        else { Debug.LogError($"{gameObject.name}: Rigidbody (rb) 在 OnEpisodeBegin 中为 null!", gameObject); return; }
        isAlive = true;
        isApplyingFastFall = false;

        // 2. 重置游戏环境 (GameManager 负责自己的重置，这里只清理管道)
        // if (GameManager.Instance != null) { ... } // 不需要在这里调用 GameManager 的重置

        // 3. 清理旧管道 (由 Agent 的回合控制)
        if (pipeSpawner != null) { pipeSpawner.ClearPipes(); }
        else { Debug.LogWarning($"{gameObject.name}: PipeSpawner 在 OnEpisodeBegin 中为 null。", gameObject); }
    }
    #endregion

    #region Observation Collection
    // ================== 观察信息收集 ==================
    public override void CollectObservations(VectorSensor sensor)
    {
        // sensor.Clear(); // <<--- 错误 CS0122 已修复：删除此行

        // 获取当前管道速度，若 GM 无效则用默认值
        float currentPipeSpeedObs = defaultPipeSpeed;
        // 尝试从 GameManager 获取速度，如果 GameManager 不存在则使用默认值
        if (GameManager.Instance != null) { currentPipeSpeedObs = GameManager.Instance.currentPipeSpeed; }
        else if (gameManager != null) { currentPipeSpeedObs = gameManager.currentPipeSpeed; } // 备用方案

        // Agent 死亡或无效时的默认观测值 (12个)
        if (!isAlive || rb == null)
        {
            sensor.AddObservation(0f);                  // 1: Y 速度
            sensor.AddObservation(0f);                  // 2: Y 位置
            for(int i = 0; i < 3; ++i)
            {
                sensor.AddObservation(defaultRelativeY);    // 3, 6, 9: P RelativeY
                sensor.AddObservation(defaultPipeDistance); // 4, 7, 10: P DistX
                sensor.AddObservation(defaultGapSize);      // 5, 8, 11: P GapSize
            }
            sensor.AddObservation(currentPipeSpeedObs); // 12: Pipe Speed
            return;
        }

        // --- 观察 1 & 2: Agent 自身状态 ---
        sensor.AddObservation(rb.velocity.y);             // 1: Y 速度
        sensor.AddObservation(transform.localPosition.y); // 2: Y 位置

        // --- 查找前方最多三个管道 ---
        // 使用 FindGameObjectsWithTag 查找，不再依赖 PipeSpawner 内部列表
        List<Transform> nextPipes = FindNextNPipePairs(3);

        // --- 观察 3-11: 三个管道的信息 (或默认值) ---
        for(int i=0; i<3; ++i)
        {
            if(i < nextPipes.Count) // 如果存在第 i+1 个管道 Transform
            {
                // 传递找到的 Transform 给 GetPipeInfo
                var info = GetPipeInfo(nextPipes[i], transform.position);
                sensor.AddObservation(info.RelativeGapCenterY); // 3, 6, 9: RelativeY
                sensor.AddObservation(info.DistanceX);        // 4, 7, 10: DistX
                sensor.AddObservation(info.GapSize);          // 5, 8, 11: GapSize
            }
            else // 否则使用默认值
            {
                sensor.AddObservation(defaultRelativeY);
                sensor.AddObservation(defaultPipeDistance);
                sensor.AddObservation(defaultGapSize);
            }
        }

        // --- 观察 12: 当前环境速度 ---
        sensor.AddObservation(currentPipeSpeedObs);       // 12: 管道速度
    }

    /// <summary>
    /// 查找并返回 Agent 前方最近的 n 个带有 "PipePair" 标签的游戏对象的 Transform。
    /// </summary>
    /// <param name="n">要查找的数量</param>
    /// <returns>找到的 Transform 列表 (按距离排序)</returns>
    private List<Transform> FindNextNPipePairs(int n)
    {
        Vector3 agentPos = transform.position;
        // 确保你的管道预制体在 Unity 编辑器中设置了 "PipePair" 标签!
        return GameObject.FindGameObjectsWithTag("PipePair")
                         .Select(go => go.transform)
                         .Where(t => t.position.x > agentPos.x) // 只找前方的
                         .OrderBy(t => t.position.x - agentPos.x) // 按 X 距离排序
                         .Take(n) // 取最近的 n 个
                         .ToList();
    }

    /// <summary>
    /// 计算并返回单个管道对游戏对象 (基于其 Transform) 的观测信息。
    /// </summary>
    /// <param name="pipePairTransform">管道对游戏对象的 Transform 组件。</param> <<--- 错误 CS0246 已修复：参数类型改为 Transform
    /// <param name="agentPosition">Agent 当前的位置。</param>
    /// <returns>包含距离、相对高度和间隙大小的结构体。</returns>
    private PipeObservationInfo GetPipeInfo(Transform pipePairTransform, Vector3 agentPosition) // <<--- 错误 CS0246 已修复：参数类型改为 Transform
    {
        PipeObservationInfo info = new PipeObservationInfo { DistanceX = defaultPipeDistance, RelativeGapCenterY = defaultRelativeY, GapSize = defaultGapSize };
        if (pipePairTransform == null) return info; // 如果传入的 Transform 无效，返回默认值

        // 使用传入的 Transform 计算信息
        info.DistanceX = pipePairTransform.position.x - agentPosition.x;

        // 通过 Transform 查找子对象
        Transform topPipe = pipePairTransform.Find("TopPipe");
        Transform bottomPipe = pipePairTransform.Find("BottomPipe");
        Transform scoreTrigger = pipePairTransform.Find("ScoreTrigger");
        BoxCollider triggerCollider = scoreTrigger?.GetComponent<BoxCollider>(); // 安全地获取碰撞器

        if (topPipe != null && bottomPipe != null)
        {
            // 计算缝隙中心 Y 坐标 (绝对世界坐标)
            float gapCenterYAbsolute = pipePairTransform.position.y + (topPipe.localPosition.y + bottomPipe.localPosition.y) / 2f;
            // 计算相对于 Agent 的 Y 坐标差
            info.RelativeGapCenterY = gapCenterYAbsolute - agentPosition.y;

            // 尝试从 ScoreTrigger 获取精确间隙大小
            if (triggerCollider != null)
            {
                info.GapSize = triggerCollider.size.y; // 使用调整后的触发器 Y 尺寸
            }
            else // 备选方案：通过计算上下管道边缘估计（可能不太精确）
            {
                // 注意: lossyScale 考虑了父对象的缩放，可能更准确，但通常管道不应有复杂缩放
                float topEdgeY = topPipe.position.y - (topPipe.lossyScale.y * 0.5f);
                float bottomEdgeY = bottomPipe.position.y + (bottomPipe.lossyScale.y * 0.5f);
                info.GapSize = Mathf.Max(0f, topEdgeY - bottomEdgeY);
                Debug.LogWarning($"管道 '{pipePairTransform.name}' 上的 ScoreTrigger 缺少 BoxCollider，间隙大小为估算值。", pipePairTransform);
            }
        }
        else // 如果找不到上下管道子对象，使用父对象位置近似相对高度
        {
            info.RelativeGapCenterY = pipePairTransform.position.y - agentPosition.y;
            // GapSize 保持默认 0 或其他默认值
            Debug.LogWarning($"在管道 '{pipePairTransform.name}' 上找不到 TopPipe 或 BottomPipe 子对象。", pipePairTransform);
        }
        return info;
    }
    #endregion

    #region Actions, Collisions, and Internal State (修改死亡处理)
    // ================== 动作、碰撞与内部状态 (修改死亡处理) ==================

    public override void OnActionReceived(ActionBuffers actions)
    {
         if (isAlive)
         {
             int action = actions.DiscreteActions[0];
             switch (action)
             {
                 case 0: isApplyingFastFall = false; break;
                 case 1: Jump(); isApplyingFastFall = false; break;
                 case 2: isApplyingFastFall = true; break;
             }

             // AI 模式下给予生存奖励
             if (behaviorParameters != null && behaviorParameters.BehaviorType != BehaviorType.HeuristicOnly)
             {
                  AddReward(stepReward);
             }
             // 边界检查移至 Update
         }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActionsOut = actionsOut.DiscreteActions;
        discreteActionsOut[0] = 0;
        if (Input.GetKey(KeyCode.Space)) { discreteActionsOut[0] = 1; }
        else if (Input.GetKey(KeyCode.LeftShift)) { discreteActionsOut[0] = 2; }
    }

    private void Jump()
    {
         if(!isAlive || rb == null) return;
         rb.velocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);
         rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
    }

    void FixedUpdate()
    {
        if (isAlive && isApplyingFastFall && rb != null)
        {
            rb.AddForce(Vector3.down * fastFallForce, ForceMode.Acceleration);
        }
    }

    // 将边界检查移至 Update
    void Update()
    {
        if (!isAlive) return; // 如果已死亡，则不进行检查
        // 边界检查
        if (transform.localPosition.y > upperBound || transform.localPosition.y < lowerBound)
        {
            HandleDeathOrEndEpisode(boundaryPenalty); // 调用统一处理方法
        }
    }

    // 处理碰撞
    void OnCollisionEnter(Collision collision)
    {
         if(isAlive && collision.gameObject.CompareTag("Obstacle"))
         {
             HandleDeathOrEndEpisode(collisionPenalty); // 调用统一处理方法
         }
    }

    // 处理得分触发器
    void OnTriggerEnter(Collider other)
    {
         if(isAlive && other.CompareTag("ScoreTrigger"))
         {
             // AI 模式下给予通过奖励
             if (behaviorParameters != null && behaviorParameters.BehaviorType != BehaviorType.HeuristicOnly)
             {
                  AddReward(passReward);
             }
             // 加分逻辑应由 ScoreTrigger.cs 通知 GameManager 处理
         }
    }

    // --- 新增：统一处理死亡/结束回合 ---
    private void HandleDeathOrEndEpisode(float penalty)
    {
        if (!isAlive) return; // 防止重复处理

        bool isHeuristic = behaviorParameters != null && behaviorParameters.BehaviorType == BehaviorType.HeuristicOnly;

        if (isHeuristic)
        {
            // --- 手动模式：通知 GameManager 游戏结束 ---
            InternalDie(); // 先标记死亡并停止物理
            Debug.Log("玩家死亡 (Heuristic Mode) - 通知 GameManager");
            if (GameManager.Instance != null)
            {
                GameManager.Instance.HandlePlayerDeath(); // 通知 GameManager
            }
            else { Debug.LogError("FlappyAgent 无法找到 GameManager 来通知死亡！"); }
        }
        else
        {
            // --- AI 模式：结束 ML-Agents 回合 ---
            // Debug.Log("Agent 死亡/出界 (AI Mode) - 结束 Episode"); // 这个日志太频繁，可以注释掉
            AddReward(penalty); // 施加惩罚
            InternalDie();      // 标记死亡并停止物理
            EndEpisode();       // 结束回合，触发 OnEpisodeBegin
        }
    }
    // --- 新增结束 ---

    // 内部标记死亡状态，并停止物理效果
    private void InternalDie()
    {
         if(!isAlive) return;
         isAlive = false;
         isApplyingFastFall = false;
         if (rb != null)
         {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.useGravity = false;
            rb.isKinematic = true; // 停止物理模拟
         }
    }
    #endregion
}