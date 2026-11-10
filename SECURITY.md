# 安全策略

## 支持范围

| 版本 | 状态 |
| --- | --- |
| `1.x`（当前 `main`） | 接受安全报告 |
| 更早的实验性分支 | 不再维护 |

## 报告方式

- **请勿在公开 Issue 中披露漏洞细节。**
- 通过 GitHub 的 [Private vulnerability reporting](https://github.com/424635328/FlappyBird3D_AI/security/advisories/new) 提交；
- 报告中请包含：影响范围、复现步骤、可能的修复方向；
- 我们会在 7 天内确认收到，并在修复发布后于 Security Advisory 中致谢。

## 项目关注的安全面

| 范围 | 说明 |
| --- | --- |
| 凭据泄漏 | 仓库与提交历史中不得出现令牌、私钥、口令；CI 会扫描常见凭据模式 |
| 本机路径泄漏 | 文档与配置中不得出现带用户名的绝对路径，统一用占位符或相对路径 |
| 模型文件 | 只加载本仓库 `Assets/Model/` 或自己训练导出的 ONNX；来源不明的模型可能在推理运行时触发异常 |
| 第三方依赖 | UPM 依赖锁定在 `Packages/packages-lock.json`，升级请走 PR 并说明变更理由 |
| 运行时数据 | 设置项保存在 `PlayerPrefs`，不上传网络；工程本身不发起任何外部请求 |

## 已知的边界

- 训练链路通过本地 gRPC（默认端口 5004）通信，**不要**把该端口暴露到公网。
- `mlagents-learn` 由 PyPI 分发，请从官方源安装并校验版本（`mlagents==1.1.0`）。
- 第三方美术与音频资源（`Assets/8Bit Music - 062022/`、`Assets/3D Birds Prototype Pack/` 等）的版权归原作者，使用前请确认授权范围，见 [README](README.md) 的"致谢与许可"。
