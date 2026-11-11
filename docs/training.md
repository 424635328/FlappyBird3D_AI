# 训练与部署

本文覆盖：怎么配、怎么跑、怎么看、怎么把训练结果接回 Unity。

## 1. 训练前检查

- [ ] Unity 工程能正常进入 Play，`GameScene` 无控制台报错
- [ ] `Behavior Parameters` 的 **Behavior Name** 为 `FlappyBirdBrain`（与配置文件第一层键一致）
- [ ] **Space Size** = `12`，**Branch 0 Size** = `3`
- [ ] `Bird` 预制体带 `PipePair` 标签的管道可被检索到（否则观测恒为默认值）
- [ ] `python scripts/check_env.py` 退出码为 `0`（无 CUDA 时为 `5`，可继续）

## 2. 配置项说明

配置文件：`config/flappybird_config.yaml`。当前取值与含义：

| 配置 | 当前值 | 作用 | 调整建议 |
| --- | --- | --- | --- |
| `trainer_type` | `ppo` | 训练算法 | 保持 |
| `batch_size` | `1024` | 每次梯度更新的样本数 | 观测维度增大时同步增大 |
| `buffer_size` | `20480` | 经验池容量，须为 `batch_size` 的整数倍 | 随机性越强，可适当增大 |
| `learning_rate` | `0.0003` | 学习率，线性衰减 | 训练震荡时降到 `0.0001` |
| `beta` | `0.05` | 熵正则强度，控制探索 | 策略过早收敛时增大 |
| `epsilon` | `0.2` | PPO 裁剪范围 | 一般不动 |
| `lambd` | `0.95` | GAE 折扣 | 一般不动 |
| `num_epoch` | `2` | 每批数据的更新轮数 | 过拟合时降到 `1`–`2` |
| `normalize` | `true` | 观测标准化 | 保持不变 |
| `hidden_units` / `num_layers` | `128` / `2` | 策略网络容量 | 学不动时试 `256` / `3` |
| `gamma` | `0.99` | 奖励折扣 | 需要更长远规划时接近 `0.995` |
| `keep_checkpoints` | `50` | 保留 checkpoint 数量 | 磁盘紧张时下调 |
| `max_steps` | `15000000` | 训练步数上限 | 冒烟阶段先改成 `50000` |
| `time_horizon` | `256` | 每个环境收集多少步后更新 | 与 `buffer_size` 协同 |
| `summary_freq` | `20000` | TensorBoard 记录间隔 | 调试时可降到 `2000` |
| `threaded` | `true` | 多线程训练 | 保持 |

## 3. 三阶段训练流程

### 阶段一：冒烟（5 万步，确认链路）

```bash
# 复制一份临时配置，把 max_steps 改成 50000，summary_freq 改成 2000
cp config/flappybird_config.yaml config/smoke.yaml
mlagents-learn config/smoke.yaml --run-id=smoke --force
```

期望：终端出现 `Listening on port 5004`，Unity 连接后开始输出 `Step`、`Mean Reward`、`Episode Length`；`results/smoke/` 下生成 `configuration.yaml`、`checkpoint.pt`、`events.out.tfevents.*`。链路跑通即可停止（`Ctrl+C`）。

### 阶段二：短程训练（100 万步，看趋势）

```bash
mlagents-learn config/flappybird_config.yaml --run-id=FlappyBird_Tune --force
```

观察 `Environment/Cumulative Reward` 是否整体上升、`Policy/Entropy` 是否缓慢下降。趋势不成立时先查奖励与观测，不要急着加步数。

### 阶段三：长程训练（千万步级）

环境随机化会显著拉长收敛所需样本量。长程训练建议：

- 用构建产物并行多环境（见下一节）；
- 保留 `checkpoint.pt`，中断后用 `--resume` 续训；
- 定期把 `FlappyBrain-<steps>.onnx` 复制到 `Assets/Model/`，在 Unity 里做人工验收。

## 4. 多环境与 headless

```powershell
./scripts/train.ps1 -RunId FlappyBird_Headless -NumEnvs 5 `
  -Env "<构建产物目录>\FlappyBird3D_AI.exe" -Headless
```

等价的手工命令：

```bash
mlagents-learn config/flappybird_config.yaml \
  --run-id=FlappyBird_Headless \
  --num-envs=5 \
  --env="<构建产物路径>" \
  --env-args -batchmode -nographics \
  --force
```

- `--num-envs` 超过可用 CPU 核心数时收益递减；`--num-cpu-threads` 可按核数分配。
- headless 构建不渲染画面，`CameraSensorComponent` 这类视觉观测不可用（本项目使用向量观测，不受影响）。
- 端口冲突时加 `--base-port 5005`。

## 5. 断点续训

```bash
mlagents-learn config/flappybird_config.yaml --run-id=FlappyBird_Headless --resume
```

- `--resume` 与 `--force` 互斥，且 `run-id` 必须对应已存在的目录；
- 续训沿用目录中的 `checkpoint.pt` 与配置；若改了网络结构（`hidden_units`/`num_layers`），需要新开 run-id。

## 6. 监控指标

```bash
tensorboard --logdir results
# 浏览器打开 http://localhost:6006
```

| 指标 | 含义 | 健康表现 |
| --- | --- | --- |
| `Environment/Cumulative Reward` | 每回合累计奖励 | 整体上升，允许因环境随机化而抖动 |
| `Environment/Episode Length` | 每回合存活步数 | 同步上升 |
| `Policy/Entropy` | 策略熵 | 缓慢下降，不归零 |
| `Losses/Policy Loss` | 策略损失 | 低位震荡，不应持续爆炸 |

## 7. 把模型接回 Unity

1. 在 `results/<run-id>/` 找到 `FlappyBirdBrain.onnx`（每 N 步的中间模型也在同目录，命名如 `FlappyBirdBrain-1000000.onnx`）。
2. 复制到 `Assets/Model/`（该目录已入库 3 个模型，可作为基线对照）。
3. 选中 `Assets/Prefabs/Bird.prefab`（或场景中的 `Bird`），在 **Behavior Parameters** 中：
   - `Model` 指向新模型；
   - `Behavior Type` 设为 `Inference Only`；
   - `Behavior Name` 保持 `FlappyBirdBrain`。
4. 运行 `MainMenuScene` → **AI 模式**，观察连续多回合表现。环境随机化下，单回合偶然成功没有意义，至少看 10 个回合的通过率。

> 场景内 `GameScene` 引用的模型与 `Bird.prefab` 上的模型是两处独立引用，替换时不要漏改。

## 8. 训练产物说明

`results/<run-id>/` 的结构（已被 `.gitignore` 忽略，不入库）：

| 路径 | 内容 |
| --- | --- |
| `configuration.yaml` | 本次运行实际使用的配置快照 |
| `checkpoint.pt` | 最近一次权重，用于 `--resume` |
| `events.out.tfevents.*` | TensorBoard 事件文件 |
| `FlappyBirdBrain.onnx` | 训练结束导出的推理模型 |
| `FlappyBirdBrain-<steps>.onnx/.pt` | 中间检查点 |
| `run_logs/timers.json`、`training_status.json` | 运行耗时与状态 |

需要长期保留的模型请复制到 `Assets/Model/` 并随提交入库；`results/` 只作为本地运行目录。
