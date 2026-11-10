# 变更记录

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [Unreleased]

### 计划中

- 管道对象池与管道注册表，替代每步 `FindGameObjectsWithTag`
- 观测归一化与 EditMode 回归测试
- 课程学习（`environment_parameters`）配置示例

## [1.0.0] - 2025-05-05

### 新增

- 3D Flappy Bird 对局：`MainMenuScene`（模式、音量）与 `GameScene`（得分、难度、环境随机化）。
- 基于 ML-Agents 的强化学习环境：12 维向量观测、3 个离散动作、4 项奖励信号。
- 环境随机化：管道速度、生成间隔、生成高度、可见缝隙、智能体出生位置与初速度。
- PPO 训练配置 `config/flappybird_config.yaml`，训练上限 1500 万步。
- `Assets/Model/` 下 3 个可推理的 ONNX 模型。

### 变更

- 依赖改为 UPM registry：`com.unity.ml-agents@3.0.0`（传递依赖 `com.unity.sentis@2.1.0`），不再把 ML-Agents 源码副本拷进仓库。
- 背景音乐由 WAV 转为 OGG Vorbis：资源体积从 221 MB 降至 22 MB，保留原 GUID 与播放列表引用。
- `ProjectSettings/EditorBuildSettings.asset` 修正为 `MainMenuScene`(0) → `GameScene`(1)，移除 4 个已不存在的场景条目。

### 修复

- 训练配置由 GBK 转为 UTF-8，中文注释不再乱码。
- 移除 `Packages/manifest.json` 与锁文件中的本机绝对路径，克隆后可直接解析依赖。

### 文档

- 重写 `README.md`：项目定位、系统结构、训练闭环、观测/动作/奖励规格、文档地图。
- 新增 `docs/setup.md`、`docs/architecture.md`、`docs/training.md`、`docs/troubleshooting.md`、`docs/roadmap.md`、`docs/report.md`。
- 原分步搭建教程归档为 `docs/legacy/build-guide.md`，并标注与当前实现的差异。

### 工程

- 新增 `.gitattributes`、`.editorconfig`、Unity 工程专用 `.gitignore`。
- 新增 `scripts/check_env.py`（训练环境自检）、`scripts/check_repo.py`（仓库规范自检）、`scripts/train.ps1`（训练封装）。
- 新增 CI 门禁 `.github/workflows/repo-guard.yml`，以及 Issue / PR 模板。
