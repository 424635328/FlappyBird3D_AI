# 参与贡献

感谢你愿意改进这个项目。本文说明本地开发、提交与评审的约定；新增功能请先看 [docs/roadmap.md](docs/roadmap.md)，避免与他人重复。

## 1. 本地准备

```bash
git clone https://github.com/424635328/FlappyBird3D_AI.git
cd FlappyBird3D_AI
python scripts/check_repo.py        # 仓库规范自检，提交前必跑
```

Unity 侧按 [docs/setup.md](docs/setup.md) 用 `2023.2.20f1c1` 打开工程。首次打开会重建 `Library/`，属正常现象。

## 2. 分支与提交

- 分支：`feat/<主题>`、`fix/<主题>`、`docs/<主题>`、`chore/<主题>`，基于 `main`。
- 提交信息：`type(scope): 描述`，主题不超过 72 字符，正文不超过 12 行。

| type | 用途 |
| --- | --- |
| `feat` | 新功能（新的动作、观测、训练范式） |
| `fix` | 修复缺陷 |
| `docs` | 仅文档 |
| `refactor` | 不改变行为的结构调整 |
| `perf` | 性能优化（对象池、观测采样） |
| `build` | UPM 依赖、工程设置、构建脚本 |
| `ci` | CI 与门禁 |
| `chore` | 杂项 |

示例：

```text
feat(agent): 增加加速下落动作

- 动作空间扩为 3 个离散分支
- 同步更新 Behavior Parameters 说明与文档
```

## 3. Unity 资产约定

- **永远不要单独删除 `.meta` 文件**：GUID 一旦丢失，场景与预制体引用全部断开。移动或改名请在 Unity 内操作，或 `.asset` 与 `.meta` 一起移动。
- 不要提交 `Library/`、`Temp/`、`Logs/`、`UserSettings/`、`Builds/`、`*.csproj`、`*.sln`：这些由编辑器生成，`.gitignore` 已覆盖。
- 修改预制体或场景前先确认没有未保存的 `Play` 模式改动；`Play` 中的修改不会被保存。
- 新增资源请放在语义明确的目录下，并在 PR 描述中说明来源与许可证。
- 大文件（音频、模型、贴图）入库前确认体积：单文件不超过 40 MB，`scripts/check_repo.py` 会拦截。

## 4. 编码与换行

| 文件 | 编码 | 换行 | 备注 |
| --- | --- | --- | --- |
| `.md` / `.cs` / `.py` / `.yaml` / `.json` | UTF-8 无 BOM | LF | 由 `.gitattributes` 统一 |
| `.ps1` | **UTF-8 带 BOM** | CRLF | 无 BOM 时 Windows PowerShell 5.1 会按 ANSI 解码中文 |
| `.bat` / `.cmd` | 随系统 | CRLF | 同上 |
| `.unity` / `.prefab` / `.asset` / `.meta` | UTF-8 | LF | 保持文本序列化，便于 review |

改完换行后如出现整文件 diff，执行：

```bash
git add --renormalize .
```

## 5. 提交前检查清单

- [ ] `python scripts/check_repo.py` 退出码为 0
- [ ] Unity 控制台无新增报错，`MainMenuScene` → 三种模式都能进入
- [ ] 若改动观测/动作/奖励，同步更新 `Behavior Parameters` 设置与 [docs/architecture.md](docs/architecture.md)
- [ ] 若改动训练参数，同步更新 [docs/training.md](docs/training.md) 的配置表
- [ ] 没有提交凭据、本机绝对路径、`results/` 与第三方源码副本

## 6. PR 流程

1. 推送分支后发起 PR，填写模板中的"改了什么 / 怎么验证 / 风险"。
2. CI（`repo-guard`）会自动运行仓库规范检查，红叉时按日志修复。
3. 涉及行为变更的 PR，请附一段可复现的验证记录（命令、观察到的指标、截图均可）。
4. 合并方式为 Squash，合并信息使用 PR 标题。

## 7. 文档同步

代码与文档分叉是本项目最容易被忽视的问题。以下改动必须同步文档：

| 改动 | 需更新的文档 |
| --- | --- |
| 观测维度、动作分支、奖励值 | `README.md` 的"强化学习设计"、`docs/architecture.md` |
| 训练超参数、步数上限 | `docs/training.md` 的配置表 |
| 依赖版本、Unity 版本 | `README.md` 的"一眼看懂"、`docs/setup.md` 的版本矩阵 |
| 新增脚本或目录 | `README.md` 的"项目结构" |
