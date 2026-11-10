# 环境准备与工程启动

本文覆盖从零把工程跑起来所需的最小步骤：安装引擎、解析依赖、构建可执行文件、准备 Python 训练环境。

## 1. 版本矩阵

| 组件 | 版本 | 用途 | 是否必须 |
| --- | --- | --- | --- |
| Unity Editor | `2023.2.20f1c1` | 打开与运行工程 | 是 |
| com.unity.ml-agents | `3.0.0` | C# 侧 Agent 接口、推理运行时 | 是（UPM 自动安装） |
| com.unity.sentis | `2.1.0` | ONNX 推理后端（ML-Agents 的传递依赖） | 是（自动安装） |
| Python | `3.10.1` – `3.10.12` | 运行 `mlagents-learn` | 训练时 |
| mlagents（pip） | `1.1.0` | PPO 训练器，对应 ML-Agents `release_22` | 训练时 |
| PyTorch | `>= 2.1.1` | 训练器后端 | 训练时 |
| TensorBoard | `>= 2.14`（随 mlagents 安装） | 曲线监控 | 可选 |

> Python 与 C# 包的版本必须配套：C# 侧 `com.unity.ml-agents` 为 `3.0.0`，对应 Python 侧 `mlagents==1.1.0`（`release_22`）。混用不同发布版本的训练器与运行时，协议字段可能对不上。

## 2. 打开 Unity 工程

1. 安装 [Unity Hub](https://unity.com/download)，在 **Installs** 中安装 `2023.2.20f1c1`。
   - 工程记录的是 `f1c1` 构建（Unity 中国版）；国际版同版本号为 `2023.2.20f1`。用其它版本打开会提示版本不一致，允许打开但会触发一次依赖重解析，结果不保证与锁文件一致。
2. Unity Hub → **Add** → 选择仓库根目录（包含 `Assets/`、`Packages/` 的那一层）。
3. 首次打开会：
   - 解析 `Packages/manifest.json`，从 Unity registry 下载 `com.unity.ml-agents` 与 `com.unity.sentis`；
   - 重建 `Library/`、`Temp/` 等生成目录（已被 `.gitignore` 忽略）；
   - 重新导入音频资源（背景音乐以 OGG 入库）。
4. 打开 `Assets/Scenes/MainMenuScene.unity`，点击 **Play**。

### 依赖解析失败怎么办

| 现象 | 处理 |
| --- | --- |
| Package Manager 报 `com.unity.ml-agents@3.0.0` 找不到 | 检查 Package Manager 的 registry 是否为 Unity 官方源；企业/校园网络可切换到就近镜像，`Packages/packages-lock.json` 中的 `url` 字段会随之更新 |
| 报 `com.unity.sentis` 版本冲突 | 删除 `Library/PackageCache/` 与 `Packages/packages-lock.json` 后重新打开，让 UPM 重新求解 |
| 控制台报缺少 `Unity.MLAgents` 命名空间 | 依赖尚未解析完成，等待编译结束；仍报错则执行 **Assets → Reimport All** |

## 3. 构建可执行文件（用于 headless 训练）

训练需要在 Unity 之外运行一个环境进程，因此要先把场景打成可执行文件。

1. **File → Build Settings**
2. Build Settings 中已配置两个场景，顺序为 `MainMenuScene` → `GameScene`。训练时请让环境直接从 `GameScene` 启动，或通过启动参数控制。
3. 目标平台选 **Windows / x86_64**，**Development Build 关闭**，输出到 `Builds/FlappyBird3D_AI.exe`（`Builds/` 已被忽略）。
4. 构建完成后记下可执行文件绝对路径，训练时通过 `--env` 传入。

> 训练用的构建不需要图形界面：配合 `-batchmode -nographics` 可以在无 GPU 的机器上并行跑多个环境实例。

## 4. Python 训练环境

### Windows（PowerShell）

```powershell
py -3.10 -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
pip install "mlagents==1.1.0"
python scripts/check_env.py
```

### macOS / Linux

```bash
python3.10 -m venv .venv
source .venv/bin/activate
python -m pip install --upgrade pip
pip install "mlagents==1.1.0"
python scripts/check_env.py
```

`pip install mlagents` 会带入 `torch>=2.1.1` 与 `tensorboard>=2.14`。需要 GPU 训练时，按 [PyTorch 官方安装页](https://pytorch.org/get-started/locally/) 选择与显卡驱动匹配的 CUDA 版本重装 `torch`。

### 环境自检

```bash
python scripts/check_env.py
```

输出示例（版本号以本机为准）：

```text
[ok]   Python            3.10.12
[ok]   mlagents          1.1.0
[ok]   mlagents_envs     1.1.0
[ok]   torch             2.1.1+cu118
[ok]   CUDA              11.8 (GPU: NVIDIA GeForce RTX ...)
[ok]   tensorboard       2.14.1
```

退出码：`0` 全部就绪；`2` Python 版本不在支持区间；`3` 缺少 `mlagents`；`4` 缺少 `torch`；`5` 无可用 CUDA（可继续用 CPU 训练，仅提示）。

### Windows 常见前置处理

部分机器上 PyTorch 与 MKL 的 OpenMP 运行库冲突，训练启动即崩溃时先设置环境变量：

```powershell
$env:KMP_DUPLICATE_LIB_OK = "TRUE"
```

## 5. 启动顺序

训练是**两个进程互相等待**的过程，顺序不能反：

1. 终端运行 `mlagents-learn ...`，出现 `Listening on port 5004`；
2. 回到 Unity 按 **Play**（或启动带 `--env` 的构建产物）；
3. 两者握手成功后，终端开始打印 `Mean Reward` 等指标。

若先按 Play 再起训练器，Unity 会一直等待连接。端口被占用时，训练器会提示 `Port 5004 is in use`，可通过 `--base-port` 换端口。

## 下一步

- 跑通第一轮训练：[training.md](training.md)
- 了解脚本职责与数据流：[architecture.md](architecture.md)
- 卡住了：[troubleshooting.md](troubleshooting.md)
