using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using Unity.MLAgents.Policies; // 引入 ML-Agents 行为类型定义
using System.Collections;      // 引入 Coroutine 功能

// 负责管理游戏的核心逻辑、状态、UI 和不同模式的切换
public class GameManager : MonoBehaviour
{
    // 单例实例，方便全局访问 GameManager
    public static GameManager Instance { get; private set; }

    [Header("游戏状态 (Game State)")]
    [Tooltip("当前游戏得分。")]
    public int score = 0; // 当前得分
    // 手动模式下的游戏结束标志 (与 AI 模式的自动重置区分)
    private bool isManualGameOver = false;

    // 游戏参数与随机化 (由难度设置或随机化函数赋值)
    [HideInInspector] public float currentPipeSpeed; // 当前回合实际使用的管道速度
    [HideInInspector] public float minPipeSpeed;     // 当前难度下的最小管道速度
    [HideInInspector] public float maxPipeSpeed;     // 当前难度下的最大管道速度

    [Header("UI 引用 (UI References)")]
    [Tooltip("用于显示分数的 TextMeshProUGUI 组件（拖拽赋值）。")]
    public TextMeshProUGUI scoreText; // 分数显示文本组件
    [Tooltip("游戏结束时显示的面板（拖拽赋值）。")]
    public GameObject gameOverPanel; // 游戏结束 UI 面板
    [Tooltip("在游戏结束面板上显示最终分数的文本（拖拽赋值）。")]
    public TextMeshProUGUI finalScoreText; // 游戏结束面板上的最终分数文本

    [Header("环境引用 (Environment References)")]
    [Tooltip("场景中的管道生成器组件（拖拽赋值）。")]
    public PipeSpawner pipeSpawner; // 管道生成器引用

    [Header("玩家/Agent 引用 (Player/Agent References)")]
    [Tooltip("场景中代表玩家/Agent 的 Bird GameObject（拖拽赋值）。")]
    public GameObject birdObject; // Bird (玩家/Agent) 的 GameObject 引用

    // Bird 上的组件引用 (在 Awake 中获取)
    private FlappyAgent flappyAgent;                // FlappyAgent 脚本引用
    private BehaviorParameters agentBehaviorParameters; // ML-Agents 行为参数组件引用

    [Header("配置 (Configuration)")]
    [Tooltip("Agent（小鸟）在每回合开始时的初始本地位置。")]
    public Vector3 birdStartPosition = new Vector3(-2f, 0f, 0f); // 小鸟初始位置

    // 难度设置结构体 (定义不同难度下的参数)
    [System.Serializable]
    public struct DifficultySettings {
        public string name; // 难度名称 (方便调试)
        [Header("管道速度 (Pipe Speed)")]
        public float minPipeSpeed;      // 最小管道速度
        public float maxPipeSpeed;      // 最大管道速度
        [Header("管道间隙 (Pipe Gap)")]
        public float minVisibleGapSize; // 最小可见管道间隙
        public float maxVisibleGapSize; // 最大可见管道间隙
        [Header("生成频率 (Spawn Rate)")]
        public float minSpawnRate;      // 最小生成间隔时间
        public float maxSpawnRate;      // 最大生成间隔时间
     }

    [Header("难度级别配置 (Difficulty Levels)")]
    [Tooltip("配置不同模式下的游戏参数。\n索引 0: 简单模式\n索引 1: 困难模式")]
    public DifficultySettings[] difficultyLevels; // 存储不同难度的配置

    // AI 模式特定配置 (可选，允许 AI 使用独立的或现有的难度设置)
    [Header("AI 模式配置 (AI Mode Configuration)")]
    public bool useSeparateAIParams = true;     // 是否为 AI 模式使用独立的难度设置
    public DifficultySettings aiDifficultySettings; // AI 模式的独立难度设置
    public int aiUsesDifficultyLevelIndex = 1; // 如果不使用独立设置，AI 模式使用哪个难度级别 (默认困难)

    // 存储当前选择的游戏模式 (从 SettingsManager 读取)
    private int currentGameMode;

    void Awake()
    {
        // --- 单例模式实现 ---
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Debug.LogWarning("发现重复的 GameManager 实例，销毁新的实例。");
            Destroy(gameObject);
            return;
        }

        // --- 获取关键组件引用 ---
        if (birdObject == null)
        {
            Debug.LogError("GameManager: Bird GameObject 引用未设置! 脚本将禁用。");
            enabled = false; // 禁用此脚本以防止后续错误
            return;
        }
        flappyAgent = birdObject.GetComponent<FlappyAgent>();
        agentBehaviorParameters = birdObject.GetComponent<BehaviorParameters>();

        // --- 检查必要的组件是否存在，若缺少则报错 ---
        if (flappyAgent == null)
        {
            Debug.LogError("GameManager: 在 Bird 上找不到 FlappyAgent 组件！游戏无法正常运行。");
            // 可以在这里选择禁用脚本或采取其他错误处理
        }
        if (agentBehaviorParameters == null)
        {
            Debug.LogError("GameManager: 在 Bird 上找不到 BehaviorParameters 组件！游戏无法正常运行。");
            // 可以在这里选择禁用脚本或采取其他错误处理
        }

        // --- 读取游戏模式 ---
        // 尝试从 SettingsManager 获取选择的游戏模式
        if (SettingsManager.Instance != null)
        {
            currentGameMode = SettingsManager.Instance.SelectedGameMode;
            Debug.Log($"从 SettingsManager 读取到游戏模式索引: {currentGameMode}");
        }
        else // 如果 SettingsManager 不存在，则使用默认模式 (简单模式)
        {
            Debug.LogWarning("GameManager: 未找到 SettingsManager 实例！将使用默认模式 (简单模式)。");
            currentGameMode = MainMenuManager.SimpleMode; // 假设 MainMenuManager.SimpleMode 是定义的常量
        }

        // --- 根据读取到的模式配置游戏 ---
        ConfigureGameForMode();

        // --- 初始化 UI 状态 ---
        // 确保游戏开始时 Game Over UI 是隐藏的
        HideGameOverUI(); // 在 Awake 中调用，确保在 Start 之前隐藏
    }

    void Start()
    {
        // --- 检查其他必要的引用 ---
        if (pipeSpawner == null) { Debug.LogError("PipeSpawner 引用未在 GameManager 中设置！"); }
        if (scoreText == null) { Debug.LogWarning("Score Text (TextMeshProUGUI) 引用未在 GameManager 中设置。"); }
        if (gameOverPanel == null) { Debug.LogWarning("GameOver Panel 引用未在 GameManager 中设置。"); }
        if (finalScoreText == null) { Debug.LogWarning("Final Score Text 引用未在 GameManager 中设置。"); }


        // --- 初始化游戏状态 ---
        isManualGameOver = false; // 确保游戏开始时不是手动模式的游戏结束状态
        ResetScoreAndUI();        // 重置分数并更新 UI
        RandomizeEnvironmentForNewEpisode(); // 随机化初始环境参数 (如管道速度)
        // UpdateScoreUI(); // ResetScoreAndUI 内部已调用，这里可省略
    }

    void Update()
    {
        // --- 调试/快捷功能：按下 ESC 键重新加载当前场景 ---
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            RestartScene();
        }
    }

    // 根据 currentGameMode 配置游戏环境和玩家/Agent 控制方式
    private void ConfigureGameForMode()
    {
        Debug.Log($"配置游戏，当前模式索引: {currentGameMode}");

        // --- 统一预处理：先禁用可能冲突的组件 ---
        if (flappyAgent != null) flappyAgent.enabled = false; // 先禁用 Agent
        if (agentBehaviorParameters != null) agentBehaviorParameters.enabled = false; // 先禁用行为参数

        DifficultySettings settingsToApply; // 用于存储将要应用的难度设置

        // --- 根据游戏模式选择配置 ---
        switch (currentGameMode)
        {
            case MainMenuManager.AImode: // AI 模式
                Debug.Log("设置为 AI 模式");
                if (agentBehaviorParameters != null && flappyAgent != null)
                {
                    // 检查 AI 模型是否存在
                    if (agentBehaviorParameters.Model == null)
                    {
                        Debug.LogError("AI 模式启动失败！未找到指定的神经网络模型 (NN Model)。请在 Bird 的 Behavior Parameters 中设置模型。将切换到简单模式。");
                        currentGameMode = MainMenuManager.SimpleMode; // 切换到安全模式
                        ConfigureGameForMode(); // 重新配置
                        return; // 退出当前配置流程
                    }
                    // 设置为 InferenceOnly (仅推理)，启用 Agent 和行为参数
                    agentBehaviorParameters.enabled = true;
                    agentBehaviorParameters.BehaviorType = BehaviorType.InferenceOnly;
                    flappyAgent.enabled = true;

                    // 选择 AI 难度设置
                    if (useSeparateAIParams)
                    {
                        settingsToApply = aiDifficultySettings;
                        Debug.Log("AI 模式使用独立的难度参数。");
                    }
                    else
                    {
                        if (aiUsesDifficultyLevelIndex >= 0 && aiUsesDifficultyLevelIndex < difficultyLevels.Length)
                        {
                            settingsToApply = difficultyLevels[aiUsesDifficultyLevelIndex];
                            Debug.Log($"AI 模式使用预设难度级别索引: {aiUsesDifficultyLevelIndex} ('{settingsToApply.name}')");
                        }
                        else
                        {
                            Debug.LogWarning($"AI 模式配置的难度索引 {aiUsesDifficultyLevelIndex} 无效！将使用第一个难度级别。");
                            settingsToApply = difficultyLevels.Length > 0 ? difficultyLevels[0] : default; // 备选方案
                        }
                    }
                }
                else
                {
                    Debug.LogError("AI 模式启动失败！必要的 AI 组件 (FlappyAgent 或 BehaviorParameters) 未找到。将切换到简单模式。");
                    currentGameMode = MainMenuManager.SimpleMode; // 切换到安全模式
                    ConfigureGameForMode(); // 重新配置
                    return; // 退出当前配置流程
                }
                break;

            case MainMenuManager.SimpleMode: // 简单模式 (手动)
            case MainMenuManager.HardMode:   // 困难模式 (手动)
                string modeName = (currentGameMode == MainMenuManager.SimpleMode) ? "简单" : "困难";
                Debug.Log($"设置为 {modeName} 模式 (使用 Heuristic)");
                if (agentBehaviorParameters != null && flappyAgent != null)
                {
                    // **关键：设置为 HeuristicOnly (启发式/手动输入)，并启用 Agent 来接收输入**
                    agentBehaviorParameters.enabled = true; // 行为参数组件需要启用才能设置模式
                    agentBehaviorParameters.BehaviorType = BehaviorType.HeuristicOnly;
                    flappyAgent.enabled = true; // Agent 脚本需要启用以处理 Heuristic 回调 (接收玩家输入)

                    // 选择对应的难度设置
                    int difficultyIndex = (currentGameMode == MainMenuManager.SimpleMode) ? 0 : 1;
                    if (difficultyIndex >= 0 && difficultyIndex < difficultyLevels.Length)
                    {
                        settingsToApply = difficultyLevels[difficultyIndex];
                    }
                    else
                    {
                         Debug.LogWarning($"{modeName} 模式配置的难度索引 {difficultyIndex} 无效！将使用第一个难度级别。");
                         settingsToApply = difficultyLevels.Length > 0 ? difficultyLevels[0] : default; // 备选方案
                    }
                }
                else
                {
                    Debug.LogError($"{modeName} 模式启动失败！必要的 Agent 组件 (FlappyAgent 或 BehaviorParameters) 未找到。无法处理输入。");
                    // 手动模式下，如果Agent组件缺失，游戏可能无法玩，这里可以考虑不应用任何难度设置或使用默认值
                    settingsToApply = default; // 应用默认空设置，防止错误
                }
                break;

            default: // 未知模式处理
                Debug.LogError($"未知的游戏模式索引: {currentGameMode}！将默认使用第一个难度级别。");
                // 默认尝试启用 Heuristic 模式，并使用第一个难度设置作为备选
                if (agentBehaviorParameters != null && flappyAgent != null)
                {
                    agentBehaviorParameters.enabled = true;
                    agentBehaviorParameters.BehaviorType = BehaviorType.HeuristicOnly;
                    flappyAgent.enabled = true;
                }
                settingsToApply = difficultyLevels.Length > 0 ? difficultyLevels[0] : default; // 使用第一个难度或默认值
                break;
        }

        // --- 应用最终选定的难度参数 ---
        ApplyDifficultyParameters(settingsToApply);
    }

    // 将指定的难度设置应用到游戏参数和相关组件 (如 PipeSpawner)
    private void ApplyDifficultyParameters(DifficultySettings settings)
    {
        // 检查是否是默认空结构体（可能在错误处理时赋值）
        if (string.IsNullOrEmpty(settings.name))
        {
            Debug.LogWarning("尝试应用一个空的或默认的难度设置。可能是由于之前的配置错误。");
            // 在此可以决定是中止、使用默认值还是忽略
            // 为安全起见，可以设置一些非常基础的默认值
            this.minPipeSpeed = 2f; // 示例默认值
            this.maxPipeSpeed = 3f; // 示例默认值
            // ... 其他参数的默认值 ...
            return; // 不继续应用空设置到 PipeSpawner
        }

        Debug.Log($"应用难度参数: '{settings.name}' - Speed:[{settings.minPipeSpeed}-{settings.maxPipeSpeed}], Gap:[{settings.minVisibleGapSize}-{settings.maxVisibleGapSize}], Spawn:[{settings.minSpawnRate}-{settings.maxSpawnRate}]");
        // 设置 GameManager 内的速度范围，供 RandomizeEnvironment 使用
        this.minPipeSpeed = settings.minPipeSpeed;
        this.maxPipeSpeed = settings.maxPipeSpeed;

        // 应用到 PipeSpawner (如果存在)
        if (pipeSpawner != null)
        {
            pipeSpawner.minVisibleGapSize = settings.minVisibleGapSize;
            pipeSpawner.maxVisibleGapSize = settings.maxVisibleGapSize;
            pipeSpawner.minSpawnRate = settings.minSpawnRate;
            pipeSpawner.maxSpawnRate = settings.maxSpawnRate;
        }
        else
        {
            Debug.LogError("无法应用难度参数到 PipeSpawner，因为 PipeSpawner 引用为空！");
        }
    }

    // 增加分数并更新 UI
    public void AddScore(int points)
    {
        score += points;
        UpdateScoreUI();
    }

    // 小鸟成功通过管道时调用，增加 1 分
    public void BirdScored()
    {
        AddScore(1);
    }

    // 重置分数为 0 并更新 UI 显示
    public void ResetScoreAndUI()
    {
        score = 0;
        UpdateScoreUI(); // 确保分数重置后 UI 立刻更新
    }

    // 更新 UI 上的分数显示
    public void UpdateScoreUI()
    {
        if (scoreText != null)
        {
            scoreText.text = $"Score: {score}"; // 使用 C# 字符串插值更新文本
        }
    }

    // 为新回合 (Episode) 随机化环境参数 (目前主要是管道速度)
    // 仅在游戏进行中调用 (避免在 Game Over 时调用)
    public void RandomizeEnvironmentForNewEpisode()
    {
        // 只有在游戏处于活动状态时才进行随机化
        if (!IsGameActive())
        {
            // Debug.Log("游戏非活动状态，跳过环境随机化。"); // 可选的调试信息
            return;
        }

        // 在最小和最大速度之间随机选择当前回合的速度
        currentPipeSpeed = Random.Range(this.minPipeSpeed, this.maxPipeSpeed);
        string modeName = GetCurrentModeName(); // 获取当前模式名称用于日志
        Debug.Log($"[{modeName} - 环境随机化] 新回合开始，管道速度设置为: {currentPipeSpeed:F2} (范围: {this.minPipeSpeed:F2}-{this.maxPipeSpeed:F2})");
    }

    // 处理玩家在手动模式下死亡的逻辑
    public void HandlePlayerDeath()
    {
        // 如果是 AI 模式 (由 ML-Agents 管理结束和重置) 或 手动模式已经结束，则不执行此逻辑
        if (currentGameMode == MainMenuManager.AImode || isManualGameOver) return;

        isManualGameOver = true; // 设置手动模式游戏结束标志
        Debug.Log("GameManager: 玩家死亡，手动模式游戏结束！");

        // --- 停止游戏活动元素 ---
        if (pipeSpawner != null)
        {
             pipeSpawner.enabled = false; // 停止生成管道
             pipeSpawner.StopAllCoroutines(); // 确保生成协程也停止
        }

        // 禁用 Agent 脚本，停止接收输入或执行动作
        if (flappyAgent != null)
        {
             flappyAgent.enabled = false;
             // 可选：显式停止Agent上的任何潜在协程
             // flappyAgent.StopAllCoroutines();
        }
        // 同样禁用行为参数组件，防止意外行为
        if (agentBehaviorParameters != null)
        {
            agentBehaviorParameters.enabled = false;
        }


        // --- 显示游戏结束 UI ---
        ShowGameOverUI();
    }

    // (供 UI 按钮调用) 重新开始手动模式游戏
    public void RestartManualGame()
    {
        // 仅在手动模式游戏结束后执行 (防止在游戏中或 AI 模式下误触)
        if (currentGameMode == MainMenuManager.AImode || !isManualGameOver) return;

        Debug.Log("GameManager: 重新开始手动游戏...");
        isManualGameOver = false; // 重置手动模式游戏结束标志

        HideGameOverUI(); // 隐藏游戏结束界面
        ResetScoreAndUI(); // 重置分数

        // --- 重置 Agent 和环境状态 ---
        if (flappyAgent != null)
        {
            // 调用 EndEpisode() 来触发 OnEpisodeBegin，这是重置 Agent 和环境的标准方式
            // OnEpisodeBegin 通常会重置小鸟位置、速度、清除现有管道、调用 RandomizeEnvironment 等
            flappyAgent.EndEpisode();

            // 因为 EndEpisode 后 Agent 可能被禁用，或者其内部状态需要一帧来稳定，
            // 启动一个协程来确保 Agent 在下一帧为 Heuristic 输入做好准备
            StartCoroutine(ReEnableAgentAfterReset());
        }
        else
        {
            // 如果 Agent 不存在，这是一个严重错误，作为备选方案重启整个场景
            Debug.LogError("无法重置 Agent，因为 FlappyAgent 引用为空！将尝试重启场景。");
            RestartScene();
            return; // 避免执行后续代码
        }

        // --- 重新启用管道生成 ---
        // 需要确保 PipeSpawner 在 Agent 重置 (特别是清除管道) 之后再启用
        // ReEnableAgentAfterReset 协程中处理 Agent 启用，这里可以立即启用 Spawner
        // 或者如果担心时序问题，也可以将其放入协程末尾
        if (pipeSpawner != null)
        {
            pipeSpawner.enabled = true;
            // 可选：如果 PipeSpawner 使用 StartCoroutine 生成管道，确保它会重新启动
            // 如果 Spawner 的逻辑依赖于 enabled 状态，这通常就足够了
        }
    }

    // 协程：在调用 EndEpisode() 重置后，等待一帧再重新启用 Agent 以接收 Heuristic 输入
    private IEnumerator ReEnableAgentAfterReset()
    {
        yield return null; // 等待下一帧，确保 EndEpisode/OnEpisodeBegin 的处理完成

        // 再次检查条件，确保在等待期间状态没有改变 (例如玩家快速再次点击)
        // 并且我们确实处于需要启用 Heuristic 的手动模式
        if (flappyAgent != null && !isManualGameOver &&
            (currentGameMode == MainMenuManager.SimpleMode || currentGameMode == MainMenuManager.HardMode))
        {
             // 重新启用 Agent 脚本以运行 Update 和 Heuristic 回调
             flappyAgent.enabled = true;

             // 确保 BehaviorParameters 组件也启用，并且模式正确设置为 HeuristicOnly
             if (agentBehaviorParameters != null)
             {
                 agentBehaviorParameters.enabled = true;
                 // 再次确认行为类型是 Heuristic，以防万一被意外更改
                 if(agentBehaviorParameters.BehaviorType != BehaviorType.HeuristicOnly)
                 {
                    Debug.LogWarning("Agent Behavior Type was not HeuristicOnly after reset. Forcing it back.");
                    agentBehaviorParameters.BehaviorType = BehaviorType.HeuristicOnly;
                 }
             }
             Debug.Log("Agent 和 Behavior Parameters 已在重置后重新启用，准备接收 Heuristic 输入。");
        }
        else
        {
             Debug.Log("ReEnableAgentAfterReset: 条件不满足，未重新启用 Agent。可能模式已更改或游戏已再次结束。");
        }
    }

    // 显示游戏结束 UI 面板并更新最终分数
    private void ShowGameOverUI()
    {
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true); // 显示面板
            if (finalScoreText != null)
            {
                finalScoreText.text = "最终得分: " + score; // 更新最终得分显示
            }
            else
            {
                Debug.LogWarning("Final Score Text 引用未设置，无法显示最终分数。");
            }
            Debug.Log("显示 Game Over UI");
        }
        else
        {
            Debug.LogWarning("GameOver Panel 引用未设置！无法显示游戏结束界面。");
        }
    }

    // 隐藏游戏结束 UI 面板
    private void HideGameOverUI()
    {
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(false); // 隐藏面板
            // Debug.Log("隐藏 Game Over UI"); // 通常不需要频繁记录隐藏操作
        }
        // 如果 gameOverPanel 为空，在 Start() 中已有警告，此处无需重复
    }

    // 判断当前游戏是否处于活动状态
    // 这对于决定是否应该进行某些操作（如随机化环境、处理输入）很有用
    public bool IsGameActive()
    {
        // AI 模式下，游戏循环由 ML-Agents 控制，通常只要 Agent 脚本启用就认为在进行中
        // Agent 的 EndEpisode 会处理回合结束
        if (currentGameMode == MainMenuManager.AImode)
        {
            // 检查 Agent 脚本是否启用可能是一个好的指标
            return flappyAgent != null && flappyAgent.enabled;
        }
        // 手动模式下，游戏是否活动取决于我们自己设置的 isManualGameOver 标志
        else
        {
            return !isManualGameOver; // 如果手动模式的游戏尚未结束，则视为活动
        }
    }

    // 重新加载当前活动的场景
    public void RestartScene()
    {
        Debug.Log("重新加载当前场景...");
        // 获取当前活动场景的索引并重新加载它
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // 获取当前游戏模式的可读名称 (主要用于调试日志)
    private string GetCurrentModeName()
    {
        switch(currentGameMode)
        {
            case MainMenuManager.AImode: return "AI 模式";
            case MainMenuManager.SimpleMode: return "简单模式";
            case MainMenuManager.HardMode: return "困难模式";
            default: return $"未知模式 ({currentGameMode})"; // 包含索引以便调试
        }
     }
}// GameManager.cs