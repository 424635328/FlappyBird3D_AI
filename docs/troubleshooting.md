# 故障排查

按"症状 → 优先怀疑 → 处理"的顺序排列。每条都给出可验证的判断依据，避免盲改参数。

## 训练侧

| 症状 | 优先怀疑 | 处理 |
| --- | --- | --- |
| 终端停在 `Listening on port 5004`，Unity 没连上 | 启动顺序反了，或场景不在 Play | 先起训练器再按 Play；确认 GameScene 处于 Play 状态 |
| `Port 5004 is already in use` | 上次训练进程未退出 | 结束残留的 `mlagents-learn` 进程，或加 `--base-port 5005` |
| 观测恒为默认值、奖励不涨 | `PipePair` 预制体丢了 `PipePair` 标签 | 打开 `Assets/Prefabs/PipePair*.prefab`，Tag 设为 `PipePair`；用 Debug.Log 打印一次观测 |
| `VectorObservationSize` 警告 | Inspector 的 Space Size 与代码期望不一致 | 设为 `12`；若改了观测实现，同步修改 `FlappyAgent` 中的自检常量 |
| `Action Branch 0 Size` 警告 | Branch Size 不是 3 | 设为 `3`（0 不动作 / 1 振翅 / 2 加速下落） |
| 训练启动即崩溃，提示 OpenMP 重复加载 | Windows 下 PyTorch 与 MKL 运行库冲突 | `$env:KMP_DUPLICATE_LIB_OK = "TRUE"` 后重跑 |
| `mlagents-learn` 找不到 | 未激活虚拟环境 | 重新激活 `.venv`，`where mlagents-learn`（Linux 用 `which`）确认路径 |
| Python 版本报错 | mlagents 1.1.0 仅支持 3.10.1–3.10.12 | 用对应小版本重建虚拟环境 |
| TensorBoard 空白 | `--logdir` 指错，或还没有 `summary_freq` 步 | `tensorboard --logdir results` 指向 `results/` 根目录；等满一个记录周期 |
| 训练后策略"抽搐"或原地不动 | 模型没接上、模式不对、观测顺序变了 | 见下方"推理侧" |
| 训练几小时后卡住 | 内存增长 / 环境进程崩溃 | 检查 `results/<run>/run_logs/`，减少 `--num-envs` 后重启 |

## 推理与游戏侧

| 症状 | 优先怀疑 | 处理 |
| --- | --- | --- |
| 选 AI 模式后回到简单模式 | `Behavior Parameters.Model` 为空 | 把 `Assets/Model/*.onnx` 拖到 `Model` 槽位；控制台会有明确报错 |
| AI 不动，只受重力下落 | `Behavior Type` 不是 `Inference Only`，或 Behavior Name 不匹配 | 名字必须为 `FlappyBirdBrain` |
| 管道完全不动 | `GameManager.Instance` 为空，`PipeMovement` 取到速度 0 | 检查场景中是否有且仅有一个 `GameManager`（单例会销毁重复实例） |
| 撞管道不计分/不扣奖励 | `ScoreTrigger` 或 `Obstacle` 标签缺失 | `TagManager` 需包含 `PipePair`、`ScoreTrigger`、`Obstacle`、`Player` |
| 手动模式死亡后无法重开 | `isManualGameOver` 卡住，或 Agent 未重新启用 | 点重开按钮走 `RestartManualGame`；仍无效用 Esc 重载场景 |
| 游戏整体变快 | 训练时改过 `Time.timeScale` 没改回 | `Edit → Project Settings → Time → Time Scale` 恢复 `1` |
| 构建产物启动后直接报缺场景 | Build Settings 场景列表被改动 | 保留 `MainMenuScene`（索引 0）与 `GameScene`（索引 1） |
| 没有背景音乐 | OGG 转换后播放列表空槽，或音量为 0 | 在 `GameManager.prefab` 上检查 `musicPlaylist` 的 9 个引用；菜单里调回音量（`PlayerPrefs` 会记住 0） |
| 声音正常但菜单音量无效 | `AudioMixer` 未赋值或参数名不一致 | `MainMenuManager.mainMixer` 与 `masterVolumeParam` 需与 Mixer 暴露参数一致 |

## 工程与版本

| 症状 | 优先怀疑 | 处理 |
| --- | --- | --- |
| 打开工程报 UPM 解析失败 | registry 不可达，或本机缓存损坏 | 切换 Package Manager registry；删除 `Library/PackageCache/` 重开 |
| 满屏 `The referenced script ... is missing` | `.meta` 文件被误删，GUID 失配 | **永远不要单独删除 `.meta`**；从 Git 恢复对应 `.meta` |
| 提交后 diff 全是换行变化 | 本机 `core.autocrlf` 与仓库规则冲突 | 仓库已用 `.gitattributes` 统一 LF；执行 `git add --renormalize .` 后重新提交 |
| 仓库自检失败 | 敏感信息、超大文件、编码或链接问题 | 运行 `python scripts/check_repo.py`，按输出逐条修 |
| Windows PowerShell 5.1 执行脚本报语法错误 | `.ps1` 缺少 UTF-8 BOM，中文被按 ANSI 解码 | 用带 BOM 的 UTF-8 重新保存；`scripts/check_repo.py` 会检查这一点 |

## 定位问题的通用方法

1. **先证明观测是对的**：临时在 `CollectObservations` 末尾打一条 `Debug.Log`，确认 12 个值随游戏状态变化；若恒定，问题在检索（标签/预制体），不在训练。
2. **再证明奖励是对的**：在 `AddReward` 前后打日志，确认撞管给负、过管给正；两条通路（`OnCollisionEnter` / `OnTriggerEnter`）都要验。
3. **最后才怀疑超参数**：链路正确时，奖励曲线通常能在几十万步内看到方向性变化；链路上限不明时调参只会放大噪声。
