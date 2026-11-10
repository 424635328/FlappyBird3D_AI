#!/usr/bin/env python3
"""训练环境自检：Python / mlagents / PyTorch / CUDA / TensorBoard。

用法
----
    python scripts/check_env.py

退出码
------
    0  全部就绪
    2  Python 版本不在 mlagents 1.1.0 的支持区间
    3  未安装 mlagents
    4  未安装 torch
    5  没有可用的 CUDA 设备（可继续用 CPU 训练，仅提示）
"""

from __future__ import annotations

import importlib.metadata as metadata
import sys

MIN_PY = (3, 10, 1)
MAX_PY = (3, 10, 12)


def package_version(name: str) -> str | None:
    try:
        return metadata.version(name)
    except metadata.PackageNotFoundError:
        return None


def main() -> int:
    exit_code = 0
    print("FlappyBird3D_AI · 环境自检")
    print("-" * 52)

    # --- Python ---------------------------------------------------------
    current = sys.version_info[:3]
    if MIN_PY <= current <= MAX_PY:
        print(f"[ok]   Python            {'.'.join(map(str, current))}")
    else:
        print(
            f"[fail] Python            {'.'.join(map(str, current))}"
            f"（需要 {'.'.join(map(str, MIN_PY))} – {'.'.join(map(str, MAX_PY))}）"
        )
        exit_code = 2

    # --- mlagents -------------------------------------------------------
    mlagents_version = package_version("mlagents")
    mlagents_envs_version = package_version("mlagents_envs")
    if mlagents_version:
        print(f"[ok]   mlagents          {mlagents_version}")
        if mlagents_envs_version:
            print(f"[ok]   mlagents_envs     {mlagents_envs_version}")
        if not mlagents_version.startswith("1.1"):
            print("[warn] mlagents 1.1.x 对应 com.unity.ml-agents 3.0.0，版本不一致可能导致协议错误")
    else:
        print("[fail] mlagents          未安装（pip install \"mlagents==1.1.0\"）")
        exit_code = exit_code or 3

    # --- PyTorch --------------------------------------------------------
    torch_version = package_version("torch")
    if not torch_version:
        print("[fail] torch             未安装（pip install \"torch>=2.1.1\"）")
        print("-" * 52)
        return exit_code or 4

    print(f"[ok]   torch             {torch_version}")
    try:
        import torch  # noqa: PLC0415  (只在需要时导入，避免拖慢失败路径)

        if torch.cuda.is_available():
            gpu = torch.cuda.get_device_name(0)
            print(f"[ok]   CUDA              {torch.version.cuda}（{gpu}）")
        else:
            print("[warn] CUDA              不可用，将使用 CPU 训练")
            exit_code = exit_code or 5
    except Exception as exc:  # pragma: no cover - 环境相关
        print(f"[warn] CUDA              检查失败：{exc}")
        exit_code = exit_code or 5

    # --- TensorBoard ----------------------------------------------------
    tb_version = package_version("tensorboard")
    if tb_version:
        print(f"[ok]   tensorboard       {tb_version}")
    else:
        print("[warn] tensorboard       未安装，无法查看训练曲线")

    print("-" * 52)
    if exit_code == 0:
        print("结论：环境就绪，可以开始训练。")
    elif exit_code == 5:
        print("结论：可用 CPU 训练；如需 GPU，请安装对应的 CUDA 版本 PyTorch。")
    else:
        print("结论：存在阻塞项，请按下方修复建议处理后重跑。")
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
