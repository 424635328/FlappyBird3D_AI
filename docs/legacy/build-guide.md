# 从零搭建笔记（历史存档）

> **状态：归档。** 本文是工程早期的分步搭建记录，用于保留搭建思路与踩坑过程。
>
> - 文中部分实现已被后续版本替换：动作空间从 2 个扩展为 3 个（新增「加速下落」），管道缝隙由 `ScoreTrigger` 的碰撞体决定，环境随机化拆到了 `GameManager` / `PipeSpawner` / `FlappyAgent` 三处。
> - 代码以 `Assets/Scripts/` 为准，行为说明以 [架构说明](../architecture.md) 为准。
> - **不建议照抄文中的代码片段**；如需复现工程，直接使用仓库中的场景与预制体。

---

本指南详细介绍了使用 **Unity 2023.2.20f1c1** 创建 3D Flappy Bird 游戏，并使用 **Unity ML-Agents** 工具包训练 AI 自动玩游戏的步骤。此版本引入了环境随机化（管道速度、间隙、生成高度、Agent起始状态）以增强 AI 的鲁棒性。

**目标 Unity 版本:** `2023.2.20f1c1`

## 先决条件

1. 已安装 **Unity Hub**。
2. 通过 Unity Hub 安装了 **Unity Editor 2023.2.20f1c1**。
3. 已安装 **Python**（推荐 3.8 - 3.10 版本以兼容 ML-Agents）并安装了 `pip`。
4. **ML-Agents Toolkit:** 安装 `mlagents` Python 包：

    ```bash
    pip install mlagents
    ```

    *（可选但推荐：使用 Python 虚拟环境）*
5. **【必需】安装 PyTorch:** ML-Agents **训练** 需要 PyTorch。请访问 PyTorch 官网 ([https://pytorch.org/get-started/locally/](https://pytorch.org/get-started/locally/))，根据你的操作系统 (Windows/Mac/Linux) 和 CUDA 支持情况（如果你的 GPU 支持）选择合适的命令进行安装。通常类似（**请务必以官网生成的命令为准**）：

    ```bash
    # 示例命令 (请根据官网生成适合你系统的命令!)
    pip3 install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu118 # (这是带 CUDA 11.8 的示例)
    # 或者 CPU 版本示例:
    # pip3 install torch torchvision torchaudio
    ```

## 步骤 1：Unity 项目设置

预期使用的AI技术：

* **神经网络 (Neural Networks - NN):** PPO 算法内部使用神经网络来近似策略函数（决定采取哪个动作）和/或价值函数（评估状态的好坏）。Agent 的决策基于神经网络对当前观察信息的处理。
* **机器学习 (Machine Learning - ML):** 项目的核心是让 AI Agent 通过与环境的交互（玩游戏）和获得的奖励/惩罚信号，自主学习决策策略（何时跳跃），而非依赖硬编码规则。
* **强化学习 (Reinforcement Learning - RL):** 具体使用 **PPO (Proximal Policy Optimization)** 算法，通过 Unity ML-Agents 框架实现。Agent 在 Unity 环境中执行动作，环境反馈状态变化和奖励信号，Agent 根据这些反馈调整其策略网络，目标是最大化累积奖励。
* **Proximal Policy Optimization (PPO):** 在 `flappybird_config.yaml` 中由 `trainer_type: ppo` 指定。这是一种先进的策略梯度算法，用于在复杂的、带有随机性的环境中（如此项目中随机化的管道速度、间隙、高度等）稳定地学习有效策略。
* **观察与决策 (Observation & Decision Making):** Agent 收集关于自身状态（Y坐标、Y速度）和环境状态（最近的三个管道的距离、相对高度、间隙大小，以及当前管道速度）的观察信息，输入神经网络，输出离散动作（跳或不跳）。
* **策略部署 (ONNX):** 训练完成后，学习到的策略被保存为 ONNX (Open Neural Network Exchange) 格式的模型文件，可以直接在 Unity 中用于推理（即让 AI 自主玩游戏）。

1. **创建新项目:**
    * 打开 Unity Hub。
    * 点击“新建项目”(New project)。
    * 选择 **3D Core** 模板。
    * 设置**项目名称**(Project name) 为 `FlappyBird3D_AI`。
    * 确保**编辑器版本**(Editor Version) 为 `2023.2.20f1c1`。
    * 选择一个位置，然后点击“创建项目”(Create project)。
2. **安装 ML-Agents 包:**
    * 在 Unity 编辑器中，转到 `Window` -> `Package Manager` (包管理器)。
    * 点击左上角的 `+` 图标，选择 "Add package by name..."。
    * 输入 `com.unity.ml-agents` 并点击 "Add"。
    * 等待包安装完成。这可能需要几分钟。

## 步骤 2：创建 3D Flappy Bird 游戏

### 2.1 场景设置

1. **重命名场景:** 在 `Project` (项目) 窗口 -> `Assets` -> `Scenes` 中，将默认的 "SampleScene" 重命名为 "GameScene"。双击打开它。
2. **摄像机 (Camera):**
    * 在 `Hierarchy` (层级) 窗口中选择 `Main Camera`。
    * 在 `Inspector` (检视面板) 中，将其 `Projection` (投影) 设置为 `Orthographic` (正交)。
    * 调整 `Size` (大小)（例如 5 或 6）以获得良好的视野。
    * 适当地放置摄像机（例如 `Position` X=0, Y=0, Z=-10）。
    * 将 `Clear Flags` 设置为 `Solid Color` (纯色) 并选择浅蓝色背景。
3. **光照 (Lighting):** 你可以保留默认的 `Directional Light` (平行光) 或根据需要进行调整。

### 2.2 创建小鸟 (Bird)

1. **创建 Bird GameObject:**
    * `GameObject` -> `3D Object` -> `Sphere` (球体)。
    * 将其重命名为 `Bird`。
    * 重置其 `Transform` 组件，然后设置一个起始位置（例如 `Position` X=-2, Y=0, Z=0）。如果需要，调整 `Scale` (缩放)（例如 X=0.5, Y=0.5, Z=0.5）。
2. **添加物理效果:**
    * 选中 `Bird`。
    * `Component` (组件) -> `Physics` (物理) -> `Rigidbody` (刚体)。
    * 在 `Rigidbody` 组件中，勾选 `Use Gravity` (使用重力)。你可能想要稍微增加 `Drag` (阻力)（例如 0.5）和 `Angular Drag` (角阻力)（例如 1）。
    * 在 `Constraints` (约束) 下，冻结 X 和 Z 轴的旋转（`Freeze Rotation` -> 勾选 X 和 Z）以保持小鸟视觉上直立。
3. **创建 Bird 脚本 (用于手动测试):**
    * 在 `Project` (项目) 窗口中，右键 -> `Create` -> `Folder`，创建一个名为 `Scripts` 的新文件夹。
    * 在 `Scripts` 文件夹内，右键 -> `Create` -> `C# Script`。将其命名为 `BirdController`。
    * 将 `BirdController` 脚本拖到 `Bird` GameObject 上。（**注意：** 在使用 AI Agent 时，需要禁用或移除此脚本）。
    * 编辑 `BirdController.cs` (该脚本提供基础的手动控制和重置逻辑，AI Agent 会有自己的逻辑):

    ```csharp
    using UnityEngine;

    public class BirdController : MonoBehaviour
    {
        public float jumpForce = 5f; // 跳跃力度
        private Rigidbody rb;
        public bool isAlive = true; // 游戏状态标志

        void Awake() // 使用 Awake 确保 Rigidbody 在 Start 或 Update 之前被获取
        {
            rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                Debug.LogError("Rigidbody component not found on the Bird!", gameObject);
            }
        }

        // Update 每帧调用 - 用于手动输入测试
        void Update()
        {
            // 如果 Agent 存活 并且 按下了空格键 (GetKeyDown 只在按下的那一帧触发)
            if (isAlive && Input.GetKeyDown(KeyCode.Space))
            {
                Jump(); // 调用跳跃方法
            }
            // 简单的边界检查 (仅用于手动模式示例)
            if (transform.position.y < -5f || transform.position.y > 5f)
            {
                // 不过这里的 Die() 不会结束 ML-Agents 的回合
                Die();
            }
        }

        // 跳跃方法
        public void Jump()
        {
            if (rb == null) return; // 检查 Rigidbody 是否存在
            if (!isAlive) return; // 如果死亡则不跳跃

            // 重置垂直速度以获得一致跳跃
            rb.velocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);
            // 施加向上的力
            rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
        }

        // 碰撞检测
        void OnCollisionEnter(Collision collision)
        {
            // 如果存活且与障碍物碰撞
            if (isAlive && collision.gameObject.CompareTag("Obstacle"))
            {
                Die();
            }
        }

        // 死亡方法
        public void Die()
        {
            if (!isAlive) return; // 防止重复调用

            isAlive = false;
            Debug.Log("小鸟死亡! (from BirdController)"); // 标识来源
        }

        // 重置小鸟状态的方法 (Agent 脚本会使用类似逻辑)
        public void ResetBird(Vector3 startPosition)
        {
            if (rb == null)
            {
                Debug.LogError("ResetBird called but Rigidbody (rb) is null!", gameObject);
                rb = GetComponent<Rigidbody>();
                if (rb == null) return;
            }

            transform.position = startPosition; // 重置位置
            rb.velocity = Vector3.zero; // 重置速度
            rb.angularVelocity = Vector3.zero; // 重置角速度
            isAlive = true; // 重置存活状态
        }
    }
    ```

### 2.3 创建管道 (Pipe)

1. **创建 Pipe Segment GameObject:**
    * `GameObject` -> `3D Object` -> `Cylinder` (圆柱体)。重命名为 `PipeSegment`。
    * 调整其 `Scale` (缩放) 使其看起来像管道部分（例如 X=0.7, Y=3, Z=0.7）。 （Y 可以根据需要调整长度）。
    * 添加 `Rigidbody` (刚体) 组件。
        * **取消勾选** `Use Gravity` (使用重力)。
        * 勾选 `Is Kinematic` (运动学)。这使我们能通过脚本移动它，同时仍能进行碰撞检测。
    * 移除默认的 `Capsule Collider`。添加 `Box Collider` (盒状碰撞体) 并调整其大小以紧密贴合视觉形状。
    * **标签 (Tag):** 在 Inspector (检视面板) 中，点击 `Tag` 下拉菜单，选择 `Add Tag...`，创建一个名为 `Obstacle` 的新标签，并将此标签分配给 `PipeSegment`。
2. **创建管道对 Prefab (预制体):**
    * 创建一个空的 GameObject，重命名为 `PipePair`。重置其 Transform。
    * 将 `PipeSegment` GameObject 拖到 `PipePair` 下，使其成为子对象。将其重命名为 `BottomPipe`。
    * 复制 `BottomPipe` (`Ctrl+D` 或 `Cmd+D`)，将副本重命名为 `TopPipe`。
    * 调整管道位置以创建间隙：
        * 设置 `TopPipe` 的 `Position` Y 为类似 `4.5` (这个值会被 `PipeSpawner` 的随机化逻辑覆盖)。
        * 设置 `TopPipe` 的 `Rotation` Z 为 `180` (使其开口朝下)。
        * 设置 `BottomPipe` 的 `Position` Y 为类似 `-4.5` (同样会被覆盖)。
        * **关键:** `PipeSpawner` 脚本现在会根据 `minVisibleGapSize` 和 `maxVisibleGapSize` 动态计算并设置 `TopPipe` 和 `BottomPipe` 的 Y 位置，以实现随机间隙。
    * **得分触发器 (Score Trigger):** 在 `PipePair` 下创建一个空的 GameObject 子对象，命名为 `ScoreTrigger`。
        * 添加 `Box Collider` (盒状碰撞体) 组件。
        * **勾选** `Is Trigger` (是触发器)。
        * 调整该碰撞体的 `Center` (通常为 X=0, Y=0, Z=0 相对于 `PipePair`) 和 `Size` (例如 X=0.5, Y=3, Z=1)。**重要:** `PipeSpawner` 脚本会自动调整此碰撞体的 Y 轴 `Size` 以精确匹配当前随机生成的可见缝隙大小。
        * **标签 (Tag):** 创建并分配一个名为 `ScoreTrigger` 的新标签。
    * **创建 Prefab (预制体):**
        * 在 `Project` 窗口中创建一个 `Prefabs` 文件夹。
        * 将 `PipePair` GameObject 从 `Hierarchy` (层级) 窗口拖到 `Prefabs` 文件夹中。
        * 创建 Prefab 后，可以从 Hierarchy 中删除 `PipePair`。
3. **创建管道移动脚本:**
    * 在 `Scripts` 文件夹中创建一个名为 `PipeMovement` 的 C# 脚本。
    * **将此脚本添加到 `PipePair` Prefab 上** (在 `Prefabs` 文件夹中选中该 Prefab，然后 Add Component)。
    * 编辑 `PipeMovement.cs` (此脚本现在从 `GameManager` 获取速度):

    ```csharp
    using UnityEngine;

    public class PipeMovement : MonoBehaviour
    {
        // --- 速度变量已移除，由 GameManager 控制 ---

        [Tooltip("管道移动到此 X 坐标左侧时将被销毁。")]
        public float destroyXPosition = -10f;

        // --- Start 方法通常不再需要 ---

        void Update()
        {
            // --- 获取当前速度 ---
            // 从 GameManager 获取当前的管道速度
            float speedToUse = GetCurrentSpeedFromManager();

            // --- 使用获取到的速度进行移动 ---
            transform.position += Vector3.left * speedToUse * Time.deltaTime;

            // --- 检查销毁条件 (逻辑不变) ---
            if (transform.position.x < destroyXPosition)
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 从 GameManager 单例安全地获取当前的管道速度。
        /// </summary>
        /// <returns>当前的管道速度；如果 GameManager 不存在，则返回 0。</returns>
        private float GetCurrentSpeedFromManager()
        {
            // 检查 GameManager 的静态实例是否存在
            if (GameManager.Instance != null)
            {
                // 如果存在，返回其 currentPipeSpeed 属性
                return GameManager.Instance.currentPipeSpeed;
            }
            else
            {
                // 如果 GameManager 实例不存在
                if (!Application.isPlaying) return 0f; // 编辑器模式下非运行状态不报警告
                Debug.LogWarning($"GameManager.Instance 未找到！无法获取管道速度。管道将停止移动。 ({gameObject.name})", gameObject);
                return 0f; // 返回 0
            }
        }
    }
    ```

### 2.4 管道生成器 (Pipe Spawner)

1. **创建 Spawner GameObject:** 创建一个空的 GameObject，命名为 `PipeSpawner`。将其放置在管道应该生成的位置（例如 `Position` X=10, Y=0, Z=0）。
2. **创建 Spawner 脚本:**
    * 在 `Scripts` 文件夹中创建一个名为 `PipeSpawner` 的 C# 脚本。
    * 将此脚本添加到 `PipeSpawner` GameObject 上。
    * 编辑 `PipeSpawner.cs` (此脚本现在包含大量随机化逻辑):

    ```csharp
    using UnityEngine;
    using System.Collections.Generic;

    /// <summary>
    /// 负责生成和管理 Flappy Bird 游戏中的管道对，并引入随机化。
    /// </summary>
    public class PipeSpawner : MonoBehaviour
    {
        [Header("管道预制体列表 (Pipe Prefabs)")]
        [Tooltip("将所有可能生成的管道对预制体拖拽到这里。每次生成时会从中随机选择一个。")]
        public GameObject[] pipePrefabs; // 现在是数组，可放多种管道

        [Header("生成频率随机化 (Spawn Rate Randomization)")]
        [Tooltip("生成下一对管道所需的最短时间（秒）。")]
        public float minSpawnRate = 1.0f;
        [Tooltip("生成下一对管道所需的最长时间（秒）。")]
        public float maxSpawnRate = 2.5f;

        [Header("生成位置控制 (Spawn Position Control)")]
        [Tooltip("管道生成的固定 X 坐标。")]
        public float spawnXPosition = 10f;

        [Header("生成高度随机化 (Spawn Height Randomization)")]
        [Tooltip("管道中心点 Y 轴变化范围的最小值。")]
        public float minHeightVariance = 1.0f;
        [Tooltip("管道中心点 Y 轴变化范围的最大值。")]
        public float maxHeightVariance = 2.5f;

        [Header("管道间隙随机化 (Pipe Gap Randomization)")]
        [Tooltip("生成的管道【可见缝隙】的最小尺寸。")]
        public float minVisibleGapSize = 2.5f;
        [Tooltip("生成的管道【可见缝隙】的最大尺寸。")]
        public float maxVisibleGapSize = 4.5f;

        private float timer = 0f;
        private float currentSpawnInterval;
        private List<GameObject> activePipes = new List<GameObject>();
        private float? cachedSinglePipeHeight = null; // 缓存单个管道高度

        void Start()
        {
            SetNextSpawnInterval();
            timer = 0f;

            if (pipePrefabs == null || pipePrefabs.Length == 0)
            {
                Debug.LogError("Pipe Prefabs 数组未分配或为空！请在 Inspector 中至少分配一个管道预制体。", gameObject);
                this.enabled = false;
            }
        }

        void Update()
        {
            if (!this.enabled) return;

            timer += Time.deltaTime;
            if (timer >= currentSpawnInterval)
            {
                SpawnPipe();
                timer = 0f;
                SetNextSpawnInterval();
            }
        }

        private void SetNextSpawnInterval()
        {
            float clampedMinRate = Mathf.Max(0.1f, minSpawnRate);
            float clampedMaxRate = Mathf.Max(clampedMinRate, maxSpawnRate);
            currentSpawnInterval = Random.Range(clampedMinRate, clampedMaxRate);
        }

        /// <summary>
        /// 生成一个新的管道对，并随机化其高度和间隙大小
        /// </summary>
        void SpawnPipe()
        {
            if (pipePrefabs == null || pipePrefabs.Length == 0) return;
            // 随机选择预制体
            int randomIndex = Random.Range(0, pipePrefabs.Length);
            GameObject prefabToSpawn = pipePrefabs[randomIndex];
            if (prefabToSpawn == null) { Debug.LogWarning($"选中的管道预制体 (索引: {randomIndex}) 为空！"); return; }

            // 随机化生成高度
            float currentHeightVariance = Random.Range(minHeightVariance, maxHeightVariance);
            float lowestYPoint = transform.position.y - currentHeightVariance;
            float highestYPoint = transform.position.y + currentHeightVariance;
            Vector3 spawnPos = new Vector3(spawnXPosition, Random.Range(lowestYPoint, highestYPoint), transform.position.z);

            GameObject newPipe = Instantiate(prefabToSpawn, spawnPos, Quaternion.identity);

            Transform topPipe = newPipe.transform.Find("TopPipe");
            Transform bottomPipe = newPipe.transform.Find("BottomPipe");
            Transform scoreTrigger = newPipe.transform.Find("ScoreTrigger");

            if (topPipe != null && bottomPipe != null)
            {
                // 获取单个管道段的高度
                float singlePipeHeight = GetOrCacheSinglePipeHeight(topPipe);
                if (singlePipeHeight <= 0) { Destroy(newPipe); return; }

                // 随机化可见缝隙大小
                float clampedMinVisibleGap = Mathf.Max(0f, minVisibleGapSize);
                float clampedMaxVisibleGap = Mathf.Max(clampedMinVisibleGap, maxVisibleGapSize);
                float targetVisibleGap = Random.Range(clampedMinVisibleGap, clampedMaxVisibleGap);

                // 计算并设置上下管道位置以实现目标缝隙
                float targetCenterDistance = targetVisibleGap + singlePipeHeight;
                float halfCenterDistance = targetCenterDistance / 2.0f;
                topPipe.localPosition = new Vector3(topPipe.localPosition.x, halfCenterDistance, topPipe.localPosition.z);
                bottomPipe.localPosition = new Vector3(bottomPipe.localPosition.x, -halfCenterDistance, bottomPipe.localPosition.z);

                // 调整得分触发器大小以匹配缝隙
                if (scoreTrigger != null)
                {
                    BoxCollider triggerCollider = scoreTrigger.GetComponent<BoxCollider>();
                    if (triggerCollider != null)
                    {
                        triggerCollider.center = new Vector3(triggerCollider.center.x, 0, triggerCollider.center.z); // 确保中心在Y=0
                        triggerCollider.size = new Vector3(triggerCollider.size.x, targetVisibleGap, triggerCollider.size.z); // Y尺寸等于缝隙大小
                    }
                    else { Debug.LogWarning($"管道 '{newPipe.name}' 的 ScoreTrigger 上没有找到 BoxCollider！", scoreTrigger); }
                }
                // else { Debug.LogWarning($"管道 '{newPipe.name}' 上没有找到 ScoreTrigger！", newPipe); } // 可选警告
            }
            else
            {
                Debug.LogWarning($"在实例化的管道 '{prefabToSpawn.name}' 上找不到 TopPipe 或 BottomPipe 子对象！无法随机化间隙。", newPipe);
            }

            activePipes.Add(newPipe);
        }

        /// <summary>
        /// 获取或计算并缓存单个管道段的高度。
        /// </summary>
        private float GetOrCacheSinglePipeHeight(Transform pipeSegment)
        {
            if (cachedSinglePipeHeight.HasValue) { return cachedSinglePipeHeight.Value; }
            else
            {
                Collider pipeCollider = pipeSegment.GetComponent<Collider>();
                if (pipeCollider != null)
                {
                    float height = pipeCollider.bounds.size.y;
                    if (height > 0)
                    {
                        cachedSinglePipeHeight = height;
                        return height;
                    }
                    else { Debug.LogWarning($"计算出的管道高度 ({height}) 无效！检查 Collider。", pipeSegment); return 0f; }
                }
                else { Debug.LogError($"找不到 '{pipeSegment.name}' 上的 Collider！无法确定管道高度。", pipeSegment); return 0f; }
            }
        }

        /// <summary>
        /// 清除场景中所有由该 Spawner 生成的管道。
        /// </summary>
        public void ClearPipes()
        {
            for (int i = activePipes.Count - 1; i >= 0; i--)
            {
                if (activePipes[i] != null) { Destroy(activePipes[i]); }
            }
            activePipes.Clear();
            cachedSinglePipeHeight = null; // 清除缓存高度
            timer = 0f; // 重置计时器
            if (this.enabled) { SetNextSpawnInterval(); } // 重置后立即准备下一次生成
        }
    }
    ```

3. **配置 Spawner 组件:** 在 Hierarchy (层级) 中选择 `PipeSpawner` GameObject，检查 `Pipe Spawner (Script)` 组件。
    * **Pipe Prefabs:** 将 `PipePair` prefab 从 `Project` 窗口的 `Prefabs` 文件夹拖到 `Pipe Prefabs` 数组中（可以拖拽多个不同的管道预制体，如果创建了的话）。
    * **设置随机化范围:** 根据需要调整 `Min/Max Spawn Rate`, `Min/Max Height Variance`, `Min/Max Visible Gap Size` 的值。

### 2.5 游戏管理器 (Game Manager)

1. **创建 Manager GameObject:** 创建一个空的 GameObject，命名为 `GameManager`。
2. **创建 Manager 脚本:**
    * 在 `Scripts` 文件夹中创建一个名为 `GameManager` 的 C# 脚本。将其添加到 `GameManager` GameObject 上。
    * 编辑 `GameManager.cs` (现在负责随机化管道速度):

    ```csharp
    using UnityEngine;
    using UnityEngine.SceneManagement; // 用于场景重新加载
    using TMPro; // 确保包含 TextMeshPro 命名空间

    public class GameManager : MonoBehaviour
    {
        // 单例实例
        public static GameManager Instance { get; private set; }

        [Header("游戏状态 (Game State)")]
        [Tooltip("当前游戏得分。")]
        public int score = 0;

        // --- 新增：管道速度控制与随机化 ---
        [Header("游戏参数与随机化 (Gameplay & Randomization)")]
        [Tooltip("当前所有管道的统一移动速度。会在每回合开始时根据下方范围随机设置。")]
        public float currentPipeSpeed = 3.5f; // 当前速度
        [Tooltip("管道移动速度的最小值。")]
        public float minPipeSpeed = 2.5f; // 随机速度下限
        [Tooltip("管道移动速度的最大值。")]
        public float maxPipeSpeed = 6.0f; // 随机速度上限
        // --- 新增结束 ---

        [Header("UI 引用 (UI References)")]
        [Tooltip("用于显示分数的 TextMeshProUGUI 组件（拖拽赋值）。")]
        public TextMeshProUGUI scoreText; // 需要 TextMeshPro

        [Header("环境引用 (Environment References)")]
        [Tooltip("场景中的管道生成器组件（拖拽赋值）。")]
        public PipeSpawner pipeSpawner; // 引用 Spawner

        [Header("配置 (Configuration)")]
        [Tooltip("Agent（小鸟）在每回合开始时的初始本地位置。")]
        public Vector3 birdStartPosition = new Vector3(-2f, 0f, 0f); // Agent 会使用这个

        void Awake()
        {
            if (Instance == null) { Instance = this; }
            else { Debug.LogWarning("发现重复的 GameManager 实例，销毁新的实例。"); Destroy(gameObject); }
        }

        void Start()
        {
            if (pipeSpawner == null) { Debug.LogError("PipeSpawner 引用未在 GameManager 中设置！"); }
            if (scoreText == null) { Debug.LogWarning("Score Text (TextMeshProUGUI) 引用未在 GameManager 中设置。"); }
            // 不在 Start 中重置状态或随机化，由 Agent 的 OnEpisodeBegin 触发
            UpdateScoreUI(); // 确保初始分数显示为0
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Debug.Log("按下 ESC 键。重新加载当前场景...");
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }

        public void AddScore(int points)
        {
            score += points;
            UpdateScoreUI();
        }

        public void BirdScored()
        {
            AddScore(1);
        }

        // --- 修改：只重置分数和UI ---
        public void ResetScoreAndUI()
        {
            score = 0;
            UpdateScoreUI();
        }

        // --- 新增：在每回合开始时随机化环境 ---
        public void RandomizeEnvironmentForNewEpisode()
        {
            // 随机化管道速度
            currentPipeSpeed = Random.Range(minPipeSpeed, maxPipeSpeed);
            Debug.Log($"[环境随机化] 新回合开始，管道速度设置为: {currentPipeSpeed:F2}");

            // 未来可在此处添加更多全局环境随机化，如重力、风力等
        }
        // --- 新增结束 ---

        public void UpdateScoreUI()
        {
            if (scoreText != null)
            {
                scoreText.text = $"Score: {score}";
            }
        }
    }
    ```

3. **连接引用:** 在 Hierarchy (层级) 中选择 `GameManager` GameObject。
    * 将场景中的 `PipeSpawner` GameObject 拖到 `Game Manager (Script)` 组件的 `Pipe Spawner` 插槽中。
    * **创建 UI:** 如果还没有分数显示，可以在场景中创建一个 UI -> Text - TextMeshPro 对象，调整其位置和样式，然后将其拖到 `Game Manager` 的 `Score Text` 插槽中。（如果提示导入 TMP Essentials，请导入）。
    * **配置速度范围:** 在 `Game Manager` 组件上设置 `Min Pipe Speed` 和 `Max Pipe Speed` 的值。
4. **更新 ScoreTrigger 脚本:** 创建一个名为 `ScoreTrigger` 的新 C# 脚本，并将其添加到 `PipePair` prefab 内的 `ScoreTrigger` GameObject 上。

    ```csharp
    // ScoreTrigger.cs
    using UnityEngine;

    public class ScoreTrigger : MonoBehaviour
    {
        void OnTriggerEnter(Collider other)
        {
            // 确保是标记为 "Player" 的物体（即小鸟）首次触发
            if (other.gameObject.CompareTag("Player")) // 使用 Player Tag 识别小鸟
            {
                if (GameManager.Instance != null)
                {
                    GameManager.Instance.BirdScored(); // 通知 GameManager 计分
                }
                // 立即禁用碰撞体，防止同一对管道重复计分
                Collider col = GetComponent<Collider>();
                if(col != null) col.enabled = false; // 禁用自身collider
            }
        }

        // 当管道对被重用（例如对象池）或重新生成时，确保触发器被重新启用
        void OnEnable()
        {
            // 确保每次启用时触发器都是可用的
            var col = GetComponent<Collider>();
            if(col != null) col.enabled = true;
        }
    }
    ```

5. **【重要】设置 Bird 的 Tag:** 选中 `Bird` GameObject，在 Inspector (检视面板) 中：
    * 点击 `Tag` 下拉菜单。
    * 如果列表中没有 `Player`，选择 `Add Tag...`。
    * 点击 `+` 号，输入 `Player` 并保存。
    * 重新选中 `Bird` GameObject，再次点击 `Tag` 下拉菜单，选择刚刚创建的 `Player` 标签。

### 2.6 测试游戏

* 按下 Unity 编辑器顶部的 "Play" 按钮。
* 你应该能看到小鸟。如果 `BirdController` 脚本仍处于活动状态，你可以用空格键控制它跳跃。
* 管道应该从右侧生成，具有随机的高度、间隙大小，并以 `GameManager` 中设定的 `currentPipeSpeed`（初始值）向左移动。
* 小鸟穿过管道间隙时，分数应该增加。
* 小鸟碰到管道或飞出边界（如果 `BirdController` 中的逻辑触发）时，应有相应反应。

## 步骤 3：集成 ML-Agents

### 3.1 设置 Agent

1. **添加 Agent 相关组件:** 选中 `Bird` GameObject，在 Inspector 中：
    * 点击 `Add Component`，搜索并添加 `Decision Requester` 组件。
        * 设置 `Decision Period` (决策周期) 为 `5` (或根据需要调整，值越小反应越快但计算量越大)。
    * 点击 `Add Component`，搜索并添加 `Behavior Parameters` 组件。
        * 设置 `Behavior Name` 为 `FlappyBirdBrain`。
        * **`Space Size` (观察空间大小):** **【重要】** 根据 `FlappyAgent.cs` 的 `CollectObservations` 方法，这里需要设置为 **12**。观察值包括：
            1. Agent 的 Y 轴速度
            2. Agent 的 Y 轴本地位置
            3. 最近的第 1 个管道的相对缝隙中心 Y 坐标
            4. 最近的第 1 个管道的水平距离 X
            5. 最近的第 1 个管道的缝隙大小
            6. 最近的第 2 个管道的相对缝隙中心 Y 坐标
            7. 最近的第 2 个管道的水平距离 X
            8. 最近的第 2 个管道的缝隙大小
            9. 最近的第 3 个管道的相对缝隙中心 Y 坐标
            10. 最近的第 3 个管道的水平距离 X
            11. 最近的第 3 个管道的缝隙大小
            12. 当前环境的管道移动速度 (`GameManager.currentPipeSpeed`)
        * **`Actions` (动作空间):**
            * Type: `Discrete` (离散)。
            * Branches (分支数): `1`。
            * Branch 0 Size (分支 0 大小): `2` (动作 0: 不跳, 动作 1: 跳)。
2. **创建并添加 Agent 脚本:**
    * 在 `Scripts` 文件夹中创建一个新的 C# 脚本，命名为 `FlappyAgent`。
    * 将此脚本添加到 `Bird` GameObject 上。
    * **【重要】禁用或移除** `BirdController` 脚本组件，避免控制冲突。
    * 编辑 `FlappyAgent.cs` (这是 AI 的核心逻辑):

    ```csharp
    using UnityEngine;
    using Unity.MLAgents;
    using Unity.MLAgents.Sensors;
    using Unity.MLAgents.Actuators;
    using UnityEngine.Assertions;
    using System.Collections.Generic;
    using System.Linq;
    using Unity.MLAgents.Policies; // 需要这个 using

    /// <summary>
    /// Flappy Bird 智能体脚本, 包含环境随机化和多管道观察
    /// </summary>
    public class FlappyAgent : Agent
    {
        #region Inspector 可配置参数
        [Header("移动参数 (Movement)")]
        [SerializeField] private float jumpForce = 6f;
        [Tooltip("Agent 重置到的基础本地位置。")]
        [SerializeField] private Vector3 startPosition = new Vector3(0f, 0f, 0f);

        [Header("起始状态随机化 (Start State Randomization)")]
        [Tooltip("起始位置 X 轴偏移范围 (+/-)。")]
        [SerializeField] private float startPosXVariance = 0.1f;
        [Tooltip("起始位置 Y 轴偏移范围 (+/-)。")]
        [SerializeField] private float startPosYVariance = 0.5f;
        [Tooltip("起始垂直速度范围 (+/-)。")]
        [SerializeField] private float startVelYVariance = 0.5f;

        [Header("环境边界 (Environment Bounds)")]
        [SerializeField] private float upperBound = 7.0f;
        [SerializeField] private float lowerBound = -7.0f;

        [Header("奖励信号 (Reward Signals)")]
        [SerializeField] private float stepReward = 0.15f;     // 每步生存奖励
        [SerializeField] private float passReward = 0.20f;     // 通过管道奖励
        [SerializeField] private float collisionPenalty = -1.0f; // 碰撞惩罚
        [SerializeField] private float boundaryPenalty = -2.0f;  // 越界惩罚

        [Header("观察设置 (Observation Settings) - 默认值")]
        [SerializeField] private float defaultPipeDistance = 15f; // 默认管道距离
        [SerializeField] private float defaultRelativeY = 0f;     // 默认相对高度
        [SerializeField] private float defaultGapSize = 0f;       // 默认间隙大小
        [SerializeField] private float defaultPipeSpeed = 0f;     // 默认管道速度

        [Header("必需的引用 (Required References)")]
        [SerializeField] private Rigidbody rb;
        [SerializeField] private PipeSpawner pipeSpawner;
        [SerializeField] private GameManager gameManager;
        #endregion

        #region 内部状态与结构体
        private bool isAlive = true;

        private struct PipeObservationInfo
        {
            public float DistanceX;         // 水平距离
            public float RelativeGapCenterY; // 相对缝隙中心 Y
            public float GapSize;           // 缝隙大小
        }
        #endregion

        #region Initialization and Episode Management
        public override void Initialize()
        {
            #if UNITY_EDITOR
            Debug.Log($"[Debug] {gameObject.name}: Initialize 方法被调用。");
            #endif
            Assert.IsNotNull(rb, $"{gameObject.name}: Rigidbody 引用 (rb) 未设置!");
            Assert.IsNotNull(pipeSpawner, $"{gameObject.name}: PipeSpawner 引用未设置!");
            if (gameManager == null) Debug.LogWarning($"{gameObject.name}: GameManager 引用未设置。");
            if (rb == null) rb = GetComponent<Rigidbody>();
            Assert.IsNotNull(rb, $"{gameObject.name}: 找不到 Rigidbody 组件。");

            var behaviorParams = GetComponent<BehaviorParameters>();
            if (behaviorParams != null)
            {
                int expectedSpaceSize = 12; // 自身(2) + 3管道(3*3=9) + 速度(1) = 12
                if (behaviorParams.BrainParameters.VectorObservationSize != expectedSpaceSize)
                {
                    Debug.LogWarning($"行为参数 VectorObservationSize ({behaviorParams.BrainParameters.VectorObservationSize}) 与预期 ({expectedSpaceSize}) 不符。请在 Inspector 中将 'Space Size' 改为 {expectedSpaceSize}！", gameObject);
                }
            }
            else { Debug.LogError("找不到 BehaviorParameters 组件！ML-Agent 无法运行。", gameObject); }
        }

        public override void OnEpisodeBegin()
        {
            #if UNITY_EDITOR
            Debug.Log($"[Debug] {gameObject.name}: OnEpisodeBegin 调用。重置并随机化状态。");
            #endif
            // 1. 随机化 Agent 起始状态
            float randomOffsetX = Random.Range(-startPosXVariance, startPosXVariance);
            float randomOffsetY = Random.Range(-startPosYVariance, startPosYVariance);
            transform.localPosition = startPosition + new Vector3(randomOffsetX, randomOffsetY, 0f);
            if (rb != null) { rb.isKinematic = false; float randomStartYVel = Random.Range(-startVelYVariance, startVelYVariance); rb.velocity = new Vector3(0, randomStartYVel, 0); rb.angularVelocity = Vector3.zero; }
            else { Debug.LogError($"{gameObject.name}: Rigidbody (rb) 在 OnEpisodeBegin 中为 null!", gameObject); return; }
            isAlive = true;

            // 2. 重置游戏环境 (分数, UI, **并随机化管道速度**)
            if (gameManager != null) { gameManager.ResetScoreAndUI(); gameManager.RandomizeEnvironmentForNewEpisode(); } // <--- 调用环境随机化
            else { Debug.LogWarning($"{gameObject.name}: GameManager 在 OnEpisodeBegin 中为 null。", gameObject); }

            // 3. 清理旧管道 (Spawner 会处理自己的重置)
            if (pipeSpawner != null) { pipeSpawner.ClearPipes(); } // <--- 清理管道
            else { Debug.LogWarning($"{gameObject.name}: PipeSpawner 在 OnEpisodeBegin 中为 null。", gameObject); }

            #if UNITY_EDITOR
            Debug.Log($"[Debug] OnEpisodeBegin 结束: isAlive={isAlive}, Pos={transform.localPosition:F2}, VelY={rb.velocity.y:F2}, PipeSpeed={gameManager?.currentPipeSpeed ?? -1f:F2}");
            #endif
        }
        #endregion

        #region Observation Collection (观察3个管道 + 速度)
        public override void CollectObservations(VectorSensor sensor)
        {
            float currentPipeSpeedObs = (gameManager != null) ? gameManager.currentPipeSpeed : defaultPipeSpeed;

            // Agent 死亡或无效时的默认观测值 (12个)
            if (!isAlive || rb == null)
            {
                sensor.AddObservation(0f);                  // 1: Y Vel
                sensor.AddObservation(0f);                  // 2: Y Pos
                for(int i = 0; i < 3; ++i) // Pipe 1, 2, 3 defaults
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
            List<Transform> nextPipes = FindNextNPipePairs(3);

            // --- 观察 3-11: 三个管道的信息 (或默认值) ---
            for(int i=0; i<3; ++i)
            {
                if(i < nextPipes.Count) // 如果存在第 i+1 个管道
                {
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
        /// 查找并返回 Agent 前方最近的 n 个 "PipePair" 对象 (需要设置 PipePair Tag)。
        /// </summary>
        private List<Transform> FindNextNPipePairs(int n)
        {
            Vector3 agentPos = transform.position;
            // !! 确保你的 PipePair Prefab 有 "PipePair" Tag !!
            return GameObject.FindGameObjectsWithTag("PipePair") // 假设 Tag 是 "PipePair"
                            .Select(go => go.transform)
                            .Where(t => t.position.x > agentPos.x)
                            .OrderBy(t => t.position.x - agentPos.x)
                            .Take(n)
                            .ToList();
        }

        /// <summary>
        /// 计算并返回单个管道对的观测信息
        /// </summary>
        private PipeObservationInfo GetPipeInfo(Transform pipePair, Vector3 agentPosition)
        {
            PipeObservationInfo info = new PipeObservationInfo { DistanceX = defaultPipeDistance, RelativeGapCenterY = defaultRelativeY, GapSize = defaultGapSize };
            if (pipePair == null) return info;

            info.DistanceX = pipePair.position.x - agentPosition.x;

            Transform topPipe = pipePair.Find("TopPipe");
            Transform bottomPipe = pipePair.Find("BottomPipe");
            // 尝试从 ScoreTrigger 获取间隙大小（假设它已被 Spawner 正确设置）
            Transform scoreTrigger = pipePair.Find("ScoreTrigger");
            BoxCollider triggerCollider = scoreTrigger?.GetComponent<BoxCollider>();

            if (topPipe != null && bottomPipe != null)
            {
                // 使用 TopPipe 和 BottomPipe 的 Y position 计算中心（因为它们是相对于父对象设置的）
                float gapCenterYAbsolute = pipePair.position.y + (topPipe.localPosition.y + bottomPipe.localPosition.y) / 2f; // 近似中心
                info.RelativeGapCenterY = gapCenterYAbsolute - agentPosition.y;

                // 从 Trigger Collider 获取精确间隙大小
                if (triggerCollider != null)
                {
                    info.GapSize = triggerCollider.size.y; // 使用调整后的触发器 Y 尺寸作为间隙大小
                }
                else // Fallback: 通过计算上下管道边缘估计，可能不太精确
                {
                    float topEdgeY = topPipe.position.y - (topPipe.lossyScale.y * 0.5f);
                    float bottomEdgeY = bottomPipe.position.y + (bottomPipe.lossyScale.y * 0.5f);
                    info.GapSize = Mathf.Max(0f, topEdgeY - bottomEdgeY);
                }
            }
            else // 找不到子对象，使用父对象位置近似
            {
                info.RelativeGapCenterY = pipePair.position.y - agentPosition.y;
                // GapSize 保持默认 0 或其他默认值
            }
            return info;
        }

        #endregion

        #region Actions, Collisions, and Internal State
        public override void OnActionReceived(ActionBuffers actions)
        {
            if (isAlive)
            {
                int jumpAction = actions.DiscreteActions[0];
                if(jumpAction==1){ Jump(); }
                AddReward(stepReward); // 生存奖励

                // 检查边界
                if(transform.localPosition.y > upperBound || transform.localPosition.y < lowerBound)
                {
                    AddReward(boundaryPenalty);
                    InternalDie();
                    EndEpisode();
                }
            }
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var discreteActionsOut = actionsOut.DiscreteActions;
            discreteActionsOut[0] = Input.GetKey(KeyCode.Space) ? 1 : 0;
        }

        private void Jump()
        {
            if(!isAlive || rb == null) return;
            rb.velocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);
            rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
        }

        void OnCollisionEnter(Collision collision)
        {
            if(isAlive && collision.gameObject.CompareTag("Obstacle")) // 撞到障碍物
            {
                AddReward(collisionPenalty);
                InternalDie();
                EndEpisode();
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if(isAlive && other.CompareTag("ScoreTrigger")) // 通过得分点
            {
                AddReward(passReward); // 通过奖励
                // GameManager.BirdScored() 会在 ScoreTrigger 脚本中调用，这里不用重复加分
                // 如果 ScoreTrigger 脚本被移除，则需要在这里加分：
                // if(gameManager != null){ gameManager.BirdScored(); }
            }
        }

        private void InternalDie()
        {
            if(!isAlive) return;
            isAlive = false;
            // 可以选择性地让刚体变为 Kinematic 或禁用重力，防止尸体乱飞
            // if(rb != null) rb.isKinematic = true;
        }
        #endregion
    }
    ```

3. **配置 Agent 组件 (`FlappyAgent` Script):**
    * 选中 `Bird` GameObject。
    * 在 `Flappy Agent (Script)` 组件中：
        * **设置引用:** 将场景中的 `Rigidbody` (应该已经在 Bird 上了)、`Pipe Spawner` GameObject 和 `GameManager` GameObject 分别拖拽到对应的字段中。
        * **调整参数:** 根据需要调整 `Jump Force`、起始状态随机化范围 (`Start Pos X/Y Variance`, `Start Vel Y Variance`)、环境边界 (`Upper/Lower Bound`)、奖励/惩罚值以及观察默认值。
    * **【重要】** 确保你的 `PipePair` Prefab 具有 **`PipePair` Tag**，否则 `FindNextNPipePairs` 方法将无法找到管道。在 Project 窗口选中 `PipePair` Prefab，然后在 Inspector 中设置其 Tag。

4. **测试 Heuristic (手动控制 Agent):**
    * 选中 `Bird` GameObject。
    * 在 `Behavior Parameters` 组件中，将 `Behavior Type` 设置为 `Heuristic Only`。
    * 点击 "Play" 按钮。
    * 使用空格键控制小鸟跳跃。验证：
        * 跳跃是否正常 (`Jump()` 方法)。
        * 撞到管道 (`Obstacle` Tag) 是否结束回合 (`OnCollisionEnter`, `EndEpisode`) 并给予惩罚。
        * 通过管道间隙 (`ScoreTrigger` Tag) 是否给予奖励 (`OnTriggerEnter`, `AddReward`) 并且分数增加。
        * 飞出边界 (`upperBound`/`lowerBound`) 是否结束回合并给予惩罚 (`OnActionReceived`, `EndEpisode`)。
        * 每回合开始时 (`OnEpisodeBegin`) 是否：
            * 小鸟位置和速度被重置并有轻微随机化？
            * 分数重置为 0？
            * 旧管道被清除 (`pipeSpawner.ClearPipes`)？
            * 管道速度被随机设定 (检查 `GameManager.currentPipeSpeed` 的 Debug Log)？
    * 确认控制台没有错误，特别是关于找不到 Tag 或组件的错误。
    * 测试完毕后，将 `Behavior Type` 改回 `Default`。

### 3.2 创建训练配置文件

1. **创建 `config` 文件夹:** 在 Unity 项目的根目录 (与 `Assets`, `Packages` 等文件夹同级) 创建一个名为 `config` 的文件夹。
2. **创建 `yaml` 文件:** 在 `config` 文件夹中创建一个 YAML 文件 (纯文本文件，后缀为 `.yaml`)，例如 `flappybird_config.yaml`。
3. **编辑配置文件:** 将以下内容粘贴到 `flappybird_config.yaml` 中 (这是一个适用于当前 Agent 设置的起点配置):

    ```yaml
    behaviors:
      FlappyBirdBrain: # 必须与 Agent 的 Behavior Name 完全匹配
        trainer_type: ppo
        hyperparameters:
          batch_size: 256       # 增加一点批量大小，因为观察空间变大
          buffer_size: 4096     # 经验缓冲区大小
          learning_rate: 0.0003  # 学习率
          beta: 0.01           # 稍稍增加探索 (熵正则化)
          epsilon: 0.2         # PPO 裁剪参数
          lambd: 0.95          # GAE lambda
          num_epoch: 3         # 训练轮数
          learning_rate_schedule: linear # 学习率衰减
        network_settings:
          normalize: false       # 简单数值输入，可以先不标准化
          hidden_units: 128     # 隐藏层神经元数量 (可尝试 256)
          num_layers: 2         # 隐藏层数量 (可尝试 3)
          vis_encode_type: simple # 无视觉输入
        reward_signals:
          extrinsic:
            gamma: 0.99          # 折扣因子 (看重长期生存)
            strength: 1.0        # 外部奖励强度
        keep_checkpoints: 5
        max_steps: 10000000      # 增加总步数，因为问题更复杂了 (可先设小值测试)
        time_horizon: 256       # 收集经验的长度
        summary_freq: 10000      # TensorBoard 记录频率
        threaded: true         # 使用多线程训练
    ```

## 步骤 4：训练 AI

1. **打开终端/命令行:**
    * 导航到你的 Unity 项目的**根目录**。
    * *（推荐）* 激活你的 Python 虚拟环境。
2. **启动训练:** 运行以下命令：

    ```bash
    mlagents-learn config/flappybird_config.yaml --run-id=FlappyBird_Random_Run1 --force
    ```

    * `config/flappybird_config.yaml`: 确保指向你的配置文件。
    * `--run-id=FlappyBird_Random_Run1`: 给这次包含随机化的训练一个新的 ID。
    * `--force`: 如果需要覆盖之前的同名训练，则添加。

3. **等待 Unity 连接:** 终端显示 "Listening on port 5004..."。
4. **开始训练:** 返回 Unity 编辑器，确保 **GameScene** 打开，点击 "Play"。
    * Agent 开始与随机化的环境交互。由于初始策略随机且环境多变，早期失败会很频繁。
    * 观察终端输出，关注 `Mean Reward` 和 `Episode Length` 的变化。由于环境随机性，这些指标可能会比固定环境波动更大。
5. **监控训练 (TensorBoard):**
    * 在**另一个**终端中，导航到项目根目录，激活环境，运行 `tensorboard --logdir results`。
    * 在浏览器打开 `http://localhost:6006`。
    * 观察 `Environment/Cumulative Reward` 的趋势。理想情况下，尽管有波动，平均奖励应随时间推移而上升。同时关注 `Policy/Entropy` (衡量探索程度) 和 `Losses/Policy Loss` 等指标。
6. **停止训练:**
    * 达到 `max_steps` 自动停止，或在 `mlagents-learn` 终端按 `Ctrl+C` 手动停止。

## 步骤 5：使用训练好的模型

1. **找到模型文件:** 在 `results/FlappyBird_Random_Run1/` (或你指定的 run-id) 目录下找到 `FlappyBirdBrain.onnx` 文件。
2. **将模型导入 Unity:**
    * 在 Unity Project 窗口创建 `Models` 文件夹（如果还没有）。
    * 将 `.onnx` 文件拖拽到 `Models` 文件夹中。
3. **应用模型进行推理:**
    * 选中场景中的 `Bird` GameObject。
    * 在 `Behavior Parameters` 组件中：
        * 将 `Model` 字段旁边的目标图标点一下，或直接将 `Models` 文件夹中的 `FlappyBirdBrain.onnx` 模型拖拽到 `Model` 插槽中。
        * 将 `Behavior Type` 设置为 `Inference Only` （推荐）。
4. **测试 AI:**
    * 点击 "Play" 按钮。
    * 观察 AI Agent 如何在具有随机管道速度、间隙和高度的环境中自主玩游戏。由于训练环境的多样性，它应该能更好地适应不同的情况。

## 常见问题与调整

* **训练速度慢:**
  * **多 Agent 并行:** 在场景中复制多个 `Bird` Agent（可能需要给它们一个父级空对象，并稍微调整这个父级的位置以分散它们）。确保摄像机能看到所有 Agent 的交互区域。
  * **时间缩放:** `Edit` -> `Project Settings` -> `Time` -> `Time Scale` 设置为大于 1 的值 (如 10-20)。训练后改回 1。
  * **检查配置:** 确保 `threaded: true`。
* **AI 学不会 / 表现差:**
  * **检查奖励信号:** 随机环境下，奖励设计更关键。生存奖励 (`stepReward`) 是否太高，导致 Agent 不愿冒险通过窄缝？通过奖励 (`passReward`) 是否足够激励？惩罚 (`collisionPenalty`, `boundaryPenalty`) 是否足够强？可能需要反复调整这些值。
  * **检查观察空间:** 12 个观察值是否都有效？
    * `GetPipeInfo` 的计算是否准确（特别是 `RelativeGapCenterY` 和 `GapSize`）？添加 Debug.Log 检查观察值。
    * 观察值的范围是否过大？虽然 `normalize: false`，但如果距离、速度等差异巨大，可能影响学习。可以尝试在 `CollectObservations` 中手动将值归一化到 [-1, 1] 或 [0, 1]。例如 `normalized_speed = (currentPipeSpeedObs - minPipeSpeed) / (maxPipeSpeed - minPipeSpeed)`。
    * 是否需要更多/不同的观察？例如，Agent 的角速度？或者只观察最近的 1-2 个管道？
  * **调整超参数 (`.yaml`):**
    * `learning_rate`: 对于更难的问题，可能需要更小的学习率 (`0.0001`?) 或更慢的衰减。
    * `hidden_units`/`num_layers`: 增加网络容量 (`256` units, `3` layers) 可能有帮助。
    * `beta`: 调整熵正则化强度以控制探索。如果 Agent 太保守，增加 `beta`；如果太随机，减少 `beta`。
    * `batch_size`/`buffer_size`: 增加 buffer size 可以提供更多样化的经验，配合调整 batch size。
  * **增加训练时间:** 随机化环境需要更多样本来学习鲁棒策略，`max_steps` 可能需要远超 100 万。
  * **课程学习 (Curriculum Learning - 高级):** 先在简单环境下训练（例如，固定速度、大间隙），然后逐渐增加难度（扩大随机范围）。这需要在 ML-Agents 中配置 Curriculum。
  * **检查随机化范围:** 初始训练时，是否随机范围 (`min/maxPipeSpeed`, `min/maxVisibleGapSize`等) 设置得过大，导致难度过高？可以先缩小范围，训练成功后再扩大。
* **Agent 行为固定或重复:**
  * 可能是陷入局部最优。增加探索 (`beta`) 或重置训练。检查奖励函数是否无意中鼓励了某种简单策略。
* **Heuristic 测试正常，但训练后 Agent 不动或行为怪异:**
  * 确认 `.onnx` 模型已正确应用到 `Behavior Parameters`。
  * 确认 `Behavior Type` 是 `Inference Only` 或 `Default`。
  * 检查 TensorBoard 确认训练确实发生了学习（奖励上升）。
  * 确认 `CollectObservations` 的逻辑在训练和推理时完全一致。
  * **非常重要:** 确认 `PipePair` Prefab **已设置 `PipePair` Tag**，否则 Agent 无法观察到管道，观察向量会一直是默认值，导致无法学习。

---
