#!/usr/bin/env python3
"""仓库规范自检：编码、换行、敏感信息、文件体积、Markdown 链接、JSON 可解析性。

设计原则
--------
* 只依赖标准库，本地与 CI 用同一条命令；
* 每条结论都能定位到文件与行号；
* 不同失败原因使用不同退出码，便于脚本串联。

用法
----
    python scripts/check_repo.py
    python scripts/check_repo.py --root .
    python scripts/check_repo.py --json reports/hygiene.json

退出码
------
    0  全部通过
    2  预检失败（根目录不对、缺少必需文件）
    4  发现违规
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

# ---------------------------------------------------------------------------
# 规则表
# ---------------------------------------------------------------------------

MAX_FILE_MB = 40

# 由仓库自己维护的文本文件：要求 UTF-8、LF、无 BOM（.ps1 例外，必须有 BOM）
REPO_TEXT_EXTS = {
    ".md", ".markdown", ".cs", ".py", ".ps1", ".psm1", ".yaml", ".yml",
    ".json", ".txt", ".sh", ".asmdef", ".xml", ".csv", ".cfg", ".ini",
}
REPO_TEXT_NAMES = {".gitignore", ".gitattributes", ".editorconfig"}

# 额外扫描敏感信息：Unity 资产是文本序列化的 YAML，同样可能夹带本机路径
SCAN_TEXT_EXTS = REPO_TEXT_EXTS | {
    ".unity", ".prefab", ".asset", ".meta", ".mat", ".controller", ".anim",
}

# 换行不做要求（由 .gitattributes 按平台决定）
EOL_EXEMPT_EXTS = {".ps1", ".psm1", ".sh", ".bat", ".cmd"}

SKIP_DIRS = {
    ".git", "Library", "Temp", "obj", "Builds", "Logs", "UserSettings",
    ".venv", "venv", "__pycache__", ".mypy_cache", "node_modules",
    "ml-agents-release_22", "results",
}

# 第三方资源目录：不按本仓库的编码/换行规则改造，避免与上游版本产生无意义差异
VENDOR_PREFIXES = (
    "Assets/TextMesh Pro/",
    "Assets/3D Birds Prototype Pack/",
    "Assets/Free 2D Cartoon Parallax Background/",
)

FORBIDDEN_TOP_DIRS = {"results", "ml-agents-release_22", "Library", "Temp", "obj"}
FORBIDDEN_SUFFIXES = {".csproj", ".sln", ".unitypackage", ".apk", ".aab"}

REQUIRED_FILES = [
    "README.md",
    "LICENSE",
    "CHANGELOG.md",
    "CONTRIBUTING.md",
    "CODE_OF_CONDUCT.md",
    "SECURITY.md",
    ".gitattributes",
    ".gitignore",
    ".editorconfig",
    "Packages/manifest.json",
    "config/flappybird_config.yaml",
    "docs/setup.md",
    "docs/architecture.md",
    "docs/training.md",
    "docs/troubleshooting.md",
    "docs/roadmap.md",
    "docs/report.md",
    "scripts/check_env.py",
    "scripts/check_repo.py",
    ".github/workflows/repo-guard.yml",
]

# (说明, 正则)
SECRET_RULES = [
    ("GitHub 经典令牌", re.compile(r"ghp_[A-Za-z0-9]{20,}")),
    ("GitHub 细粒度令牌", re.compile(r"github_pat_[A-Za-z0-9_]{20,}")),
    ("AWS Access Key", re.compile(r"AKIA[0-9A-Z]{16}")),
    ("Slack 令牌", re.compile(r"xox[baprs]-[A-Za-z0-9-]{10,}")),
    ("私钥块", re.compile(r"-----BEGIN [A-Z ]*PRIVATE KEY-----")),
    ("OpenAI 风格密钥", re.compile(r"\bsk-[A-Za-z0-9]{32,}")),
    ("Google API Key", re.compile(r"AIza[0-9A-Za-z_\-]{35}")),
    (
        "疑似明文口令",
        re.compile(r"(?i)\b(password|passwd|secret|token)\b\s*[:=]\s*['\"][^'\"\s]{8,}['\"]"),
    ),
]

PATH_RULES = [
    ("Windows 用户目录", re.compile(r"[A-Za-z]:[\\/]+Users[\\/]+[A-Za-z0-9_.\-]+")),
    ("macOS 用户目录", re.compile(r"/Users/[A-Za-z0-9_.\-]+/")),
    ("Linux 用户目录", re.compile(r"/home/[A-Za-z0-9_.\-]+/")),
    ("本机工作区路径", re.compile(r"[A-Za-z]:[\\/]+GitHub[\\/]+")),
    ("本地文件 URI", re.compile(r"file:///[A-Za-z]:")),
]

LINK_RE = re.compile(r"!?\[[^\]]*\]\(([^)\s]+)\)")
IMG_SRC_RE = re.compile(r"<img[^>]+src=\"([^\"]+)\"", re.IGNORECASE)
FENCE_RE = re.compile(r"^```.*?^```", re.MULTILINE | re.DOTALL)

TEXT_CONTROL = re.compile(r"[\x00-\x08\x0b\x0c\x0e-\x1f]")


# ---------------------------------------------------------------------------
# 工具
# ---------------------------------------------------------------------------

class Report:
    def __init__(self) -> None:
        self.violations: list[dict[str, str]] = []
        self.checked_files = 0
        self.checked_bytes = 0

    def fail(self, path: str, rule: str, detail: str) -> None:
        self.violations.append({"path": path, "rule": rule, "detail": detail})

    def ok(self) -> bool:
        return not self.violations


def is_repo_text(path: Path) -> bool:
    return path.suffix.lower() in REPO_TEXT_EXTS or path.name in REPO_TEXT_NAMES


def is_scanned(path: Path) -> bool:
    return path.suffix.lower() in SCAN_TEXT_EXTS or path.name in REPO_TEXT_NAMES


def iter_files(root: Path):
    for path in sorted(root.rglob("*")):
        if not path.is_file():
            continue
        rel_parts = path.relative_to(root).parts
        if any(part in SKIP_DIRS for part in rel_parts[:-1]):
            continue
        if rel_parts and rel_parts[0] in SKIP_DIRS:
            continue
        yield path


def read_text(path: Path, strict: bool = True) -> str | None:
    data = path.read_bytes()
    try:
        return data.decode("utf-8")
    except UnicodeDecodeError:
        if strict:
            return None
        return data.decode("utf-8", errors="replace")


def has_nul(path: Path) -> bool:
    with path.open("rb") as fh:
        return b"\x00" in fh.read(8192)


def has_bom(path: Path) -> bool:
    with path.open("rb") as fh:
        return fh.read(3) == b"\xef\xbb\xbf"


# ---------------------------------------------------------------------------
# 检查项
# ---------------------------------------------------------------------------

def check_required(root: Path, report: Report) -> None:
    for rel in REQUIRED_FILES:
        if not (root / rel).is_file():
            report.fail(rel, "缺少必需文件", "该文件是仓库规范的一部分，必须存在")


def check_structure(root: Path, report: Report) -> None:
    # 目录单独判断：这些目录若存在，说明生成物或第三方副本混进了工作区
    for name in sorted(FORBIDDEN_TOP_DIRS):
        if (root / name).exists():
            report.fail(name, "禁止入库的目录", "生成物或第三方副本不应提交")
    for path in iter_files(root):
        if path.suffix.lower() in FORBIDDEN_SUFFIXES:
            report.fail(str(path.relative_to(root)), "禁止入库的文件类型",
                        "由 Unity 重新生成，不应提交")


def check_size(root: Path, report: Report) -> None:
    limit = MAX_FILE_MB * 1024 * 1024
    for path in iter_files(root):
        size = path.stat().st_size
        report.checked_bytes += size
        if size > limit:
            report.fail(
                str(path.relative_to(root)),
                "文件过大",
                f"{size / 1024 / 1024:.1f} MB > {MAX_FILE_MB} MB",
            )


def check_encoding_and_eol(root: Path, report: Report) -> None:
    for path in iter_files(root):
        if not is_repo_text(path) or has_nul(path):
            continue
        rel = str(path.relative_to(root))
        if rel.replace("\\", "/").startswith(VENDOR_PREFIXES):
            continue
        data = path.read_bytes()
        report.checked_files += 1

        try:
            text = data.decode("utf-8")
        except UnicodeDecodeError as exc:
            report.fail(rel, "编码", f"不是合法 UTF-8（{exc.reason}，偏移 {exc.start}）")
            continue

        if path.suffix.lower() in {".ps1", ".psm1"}:
            if not has_bom(path):
                report.fail(rel, "编码", "PowerShell 脚本必须以 UTF-8 BOM 保存")
        elif has_bom(path):
            report.fail(rel, "编码", "文本文件不应带 BOM")

        if path.suffix.lower() not in EOL_EXEMPT_EXTS and "\r\n" in text:
            line = text[: text.index("\r\n")].count("\n") + 1
            report.fail(rel, "换行", f"第 {line} 行为 CRLF，仓库统一使用 LF")

        if TEXT_CONTROL.search(text):
            report.fail(rel, "控制字符", "包含不可见控制字符")


def check_unity_meta(root: Path, report: Report) -> None:
    """Unity 以 .meta 中的 GUID 建立引用，缺失或孤儿都会造成引用断裂。"""
    assets = root / "Assets"
    if not assets.is_dir():
        return
    for path in assets.rglob("*"):
        if ".git" in path.parts:
            continue
        rel = str(path.relative_to(root))
        if path.suffix == ".meta":
            if not path.with_suffix("").exists():
                report.fail(rel, "Unity 元数据", "资源已不存在，属于孤儿 .meta")
        elif not path.with_name(path.name + ".meta").exists():
            report.fail(rel, "Unity 元数据", "缺少 .meta，GUID 会丢失")


def check_secrets(root: Path, report: Report) -> None:
    for path in iter_files(root):
        if has_nul(path):
            continue
        text = read_text(path, strict=False)
        if text is None:
            continue
        rel = str(path.relative_to(root))
        for line_no, line in enumerate(text.splitlines(), start=1):
            for label, pattern in SECRET_RULES:
                if pattern.search(line):
                    report.fail(rel, f"敏感信息：{label}", f"第 {line_no} 行")
                    break
            else:
                for label, pattern in PATH_RULES:
                    if pattern.search(line):
                        report.fail(rel, f"敏感信息：{label}", f"第 {line_no} 行")
                        break


def check_json(root: Path, report: Report) -> None:
    for path in iter_files(root):
        if path.suffix.lower() != ".json":
            continue
        try:
            json.loads(path.read_text(encoding="utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as exc:
            report.fail(str(path.relative_to(root)), "JSON", f"无法解析：{exc}")


def resolve_link(source: Path, target: str, root: Path) -> bool:
    clean = target.split("#", 1)[0].split("?", 1)[0].strip().strip("<>")
    if not clean or clean.startswith(("http://", "https://", "mailto:", "tel:", "#")):
        return True
    if clean.startswith("/"):
        candidate = root / clean.lstrip("/")
    else:
        candidate = source.parent / clean
    return candidate.exists()


def check_markdown_links(root: Path, report: Report) -> None:
    for path in iter_files(root):
        if path.suffix.lower() not in {".md", ".markdown"}:
            continue
        text = read_text(path, strict=False) or ""
        body = FENCE_RE.sub("", text)
        rel = str(path.relative_to(root))
        targets = set(LINK_RE.findall(body)) | set(IMG_SRC_RE.findall(body))
        for target in sorted(targets):
            if not resolve_link(path, target, root):
                report.fail(rel, "失效链接", target)


# ---------------------------------------------------------------------------
# 入口
# ---------------------------------------------------------------------------

def main(argv: list[str] | None = None) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        # Windows 控制台默认使用本地编码，统一按 UTF-8 输出，避免中文乱码
        try:
            sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass

    parser = argparse.ArgumentParser(description="仓库规范自检")
    parser.add_argument("--root", default=".", help="仓库根目录（默认当前目录）")
    parser.add_argument("--json", dest="json_out", help="把结果写成 JSON 报告")
    args = parser.parse_args(argv)

    root = Path(args.root).resolve()
    report = Report()

    if not (root / "Packages" / "manifest.json").is_file():
        print(f"[FAIL] 预检：{root} 不像 Unity 工程根目录（缺少 Packages/manifest.json）")
        return 2

    check_required(root, report)
    if report.violations:
        for item in report.violations:
            print(f"[FAIL] {item['path']} | {item['rule']} | {item['detail']}")
        print(f"\n预检失败：缺少 {len(report.violations)} 个必需文件")
        return 2

    check_structure(root, report)
    check_size(root, report)
    check_encoding_and_eol(root, report)
    check_unity_meta(root, report)
    check_secrets(root, report)
    check_json(root, report)
    check_markdown_links(root, report)

    for item in report.violations:
        print(f"[FAIL] {item['path']} | {item['rule']} | {item['detail']}")

    summary = {
        "root": str(root),
        "text_files_checked": report.checked_files,
        "bytes_scanned": report.checked_bytes,
        "violations": report.violations,
    }
    if args.json_out:
        out = Path(args.json_out)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")

    if report.violations:
        print(f"\n[FAIL] 共 {len(report.violations)} 项违规；文本文件 {report.checked_files} 个，"
              f"扫描 {report.checked_bytes / 1024 / 1024:.1f} MB")
        return 4

    print(f"[OK] 全部通过：文本文件 {report.checked_files} 个，"
          f"扫描 {report.checked_bytes / 1024 / 1024:.1f} MB")
    return 0


if __name__ == "__main__":
    sys.exit(main())
