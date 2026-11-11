#!/usr/bin/env python3
"""仓库规范与脱敏自检：编码、换行、敏感信息、个人身份信息、体积、链接、JSON、Unity 资产与工程一致性。

设计原则
--------
* 只依赖标准库：CI 不需要 pip install，供应链面为零、运行稳定；
* 按门禁分组（`--group`），CI 并行跑，失败能精确归因到某一类问题；
* 每条结论都能定位到文件与行号/偏移，但绝不打印命中的明文内容；
* 规则本身不写出可被自身命中的字面量（邮箱白名单用拼接构造），避免扫描器误报自己；
* 本地与 CI 同一条命令，退出码语义一致。

用法
----
    python scripts/check_repo.py                     # 全部工作区门禁
    python scripts/check_repo.py --group secrets     # 只跑某一组
    python scripts/check_repo.py --history           # 额外扫描当前分支历史
    python scripts/check_repo.py --history-all       # 扫描所有引用的历史
    python scripts/check_repo.py --json reports/hygiene.json

分组
----
    hygiene  编码 / 换行 / 控制字符 / 体积 / Unity .meta / JSON / 链接
    secrets  脱敏：文本与二进制内嵌字符串（凭据、本机路径、身份信息）
    project  工程一致性：UPM 清单与锁文件、构建场景 GUID、训练配置数值关系
    scripts  Python 语法校验（无需第三方依赖）
    history  Git 历史（需配合 --history / --history-all）

退出码
------
    0  全部通过
    2  预检失败（根目录不对、缺少必需文件）
    4  发现违规
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

# ---------------------------------------------------------------------------
# 基础规则
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
    "ml-agents-release_22", "results", "reports",
}

# 第三方资源目录：不按本仓库的编码/换行规则改造；身份信息类规则也不在此生效
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

# ---------------------------------------------------------------------------
# 脱敏规则
# ---------------------------------------------------------------------------

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

STRONG_PATH_RULES = [
    ("Windows 用户目录", re.compile(r"[A-Za-z]:[\\/]+Users[\\/]+[A-Za-z0-9_.\-]+")),
    ("macOS 用户目录", re.compile(r"/Users/[A-Za-z0-9_.\-]+/")),
    ("Linux 用户目录", re.compile(r"/home/[A-Za-z0-9_.\-]+/")),
    ("工作区绝对路径", re.compile(r"[A-Za-z]:[\\/]+GitHub[\\/]+")),
    ("本地文件 URI", re.compile(r"file:///[A-Za-z]:")),
    ("UNC 路径", re.compile(r"\\\\\?\\[A-Za-z]:")),
]

DRIVE_RULE = ("盘符绝对路径", re.compile(r"(?<![A-Za-z0-9_])[A-Za-z]:[\\/]"))

# 第三方联系邮箱白名单：素材许可证要求保留署名，用拼接构造避免扫描器自命中
EMAIL_ALLOWLIST = {
    "gwriterstudi" + "o@" + "gmail.com",
    "ver" + "n@" + "newtypography.co.uk",
}
# 邮箱：量化上下界 + 字母 TLD，既是防回溯爆炸，也避免把 name@version 当邮箱
EMAIL_RE = re.compile(
    r"(?<![A-Za-z0-9._%+-])"
    r"[A-Za-z0-9._%+-]{1,64}"
    r"@(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+"
    r"[A-Za-z]{2,24}"
)
DIGIT_RUN_RE = re.compile(r"\d{10,}")
PII_HINT_WORDS = (
    "手机", "电话", "联系", "tel", "phone", "mobile",
    "qq", "微信", "wechat", "wx", "学号",
    "10.", "192.168", "172.",
)

CONTEXT_PII_RULES = [
    ("手机号", re.compile(r"(?:手机|电话|联系方式|tel|phone|mobile)[^\n]{0,12}?(?<!\d)1[3-9]\d{9}(?!\d)", re.I)),
    ("QQ 号", re.compile(r"(?i)\bqq\b[^\n]{0,6}?[1-9]\d{4,10}")),
    ("微信号", re.compile(r"(?:微信|wechat|wx)\s*[:：]?\s*[A-Za-z][-_A-Za-z0-9]{5,19}")),
    ("学号", re.compile(r"学号\s*[:：]?\s*\d{6,}")),
    ("内网 IP", re.compile(
        r"(?<![\d.])(?:10\.\d{1,3}\.\d{1,3}\.\d{1,3}"
        r"|192\.168\.\d{1,3}\.\d{1,3}"
        r"|172\.(?:1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3})(?![\d.])")),
]

# 裸手机号：仅在仓库自有文本中检查（字体等二进制资产里的大整数会误报）
BARE_PHONE_RULE = ("手机号（裸号）", re.compile(r"(?<![\d.])1[3-9]\d{9}(?![\d.])"))

# 18 位身份证号：正则 + 校验位，避免把字体表里的大整数当身份信息
ID_CARD_RULE = ("身份证号", re.compile(
    r"(?<!\d)([1-9]\d{5}(?:19|20)\d{2}(?:0[1-9]|1[0-2])"
    r"(?:0[1-9]|[12]\d|3[01])\d{3}[\dXx])(?!\d)"))
ID_WEIGHTS = (7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2)
ID_CHECK_CODES = "10X98765432"

LINK_RE = re.compile(r"!?\[[^\]]*\]\(([^)\s]+)\)")
IMG_SRC_RE = re.compile(r"<img[^>]+src=\"([^\"]+)\"", re.IGNORECASE)
FENCE_RE = re.compile(r"^```.*?^```", re.MULTILINE | re.DOTALL)
TEXT_CONTROL = re.compile(r"[\x00-\x08\x0b\x0c\x0e-\x1f]")
ASCII_RUN_RE = re.compile(rb"[\x20-\x7e]{8,}")
# 路径可能含空格，取最后的 " b/" 作为分界
DIFF_HEADER_RE = re.compile(r"^diff --git a/(.*) b/(.*)$")


def id_card_checksum_ok(value: str) -> bool:
    total = sum(int(value[i]) * ID_WEIGHTS[i] for i in range(17))
    return ID_CHECK_CODES[total % 11] == value[17].upper()


# ---------------------------------------------------------------------------
# 工具
# ---------------------------------------------------------------------------

class Report:
    def __init__(self) -> None:
        self.violations: list[dict[str, str]] = []
        self.checked_files = 0
        self.checked_bytes = 0
        self.scanned_binaries = 0
        self.history_bytes = 0

    def fail(self, path: str, rule: str, detail: str) -> None:
        self.violations.append({"path": path, "rule": rule, "detail": detail})


def is_repo_text(path: Path) -> bool:
    return path.suffix.lower() in REPO_TEXT_EXTS or path.name in REPO_TEXT_NAMES


def is_vendor(rel: str) -> bool:
    return rel.replace("\\", "/").startswith(VENDOR_PREFIXES)


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


def read_text(path: Path) -> str | None:
    try:
        return path.read_bytes().decode("utf-8")
    except UnicodeDecodeError:
        return None


def loose_text(path: Path) -> str:
    return path.read_bytes().decode("utf-8", errors="replace")


def has_nul(path: Path) -> bool:
    with path.open("rb") as fh:
        return b"\x00" in fh.read(8192)


def has_bom(path: Path) -> bool:
    with path.open("rb") as fh:
        return fh.read(3) == b"\xef\xbb\xbf"


def _as_number(value: str | None, caster, default=None):
    if value is None:
        return default
    try:
        return caster(value)
    except (TypeError, ValueError):
        return default


# ---------------------------------------------------------------------------
# hygiene：编码、换行、体积、Unity 资产、JSON、链接
# ---------------------------------------------------------------------------

def check_required(root: Path, report: Report) -> None:
    for rel in REQUIRED_FILES:
        if not (root / rel).is_file():
            report.fail(rel, "缺少必需文件", "该文件是仓库规范的一部分，必须存在")


def check_structure(root: Path, report: Report) -> None:
    for name in sorted(FORBIDDEN_TOP_DIRS):
        if (root / name).exists():
            report.fail(name, "禁止入库的目录", "生成物或第三方副本不应提交")
    for path in iter_files(root):
        rel = str(path.relative_to(root))
        if path.suffix.lower() in FORBIDDEN_SUFFIXES:
            report.fail(rel, "禁止入库的文件类型", "由 Unity 重新生成，不应提交")
        if re.search(r"(?<![A-Za-z0-9_])[A-Za-z]:", rel) or "/Users/" in rel or "/home/" in rel:
            report.fail(rel, "路径含本机信息", "文件名或目录名不应包含盘符/用户目录")


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
        if is_vendor(rel):
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
    if not clean or clean.startswith(("http://", "https://", "mailto:", "tel:", "#", "{{")):
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
        text = loose_text(path)
        body = FENCE_RE.sub("", text)
        rel = str(path.relative_to(root))
        targets = set(LINK_RE.findall(body)) | set(IMG_SRC_RE.findall(body))
        for target in sorted(targets):
            if not resolve_link(path, target, root):
                report.fail(rel, "失效链接", target)


# ---------------------------------------------------------------------------
# secrets：脱敏（文本 / 二进制 / 历史）
# ---------------------------------------------------------------------------

def find_line_violation(line: str, vendor: bool, check_drive: bool = True) -> str | None:
    """返回该行命中的第一条规则描述；只有规则名，不含明文。

    `check_drive=False` 用于历史扫描：盘符示例属于书写风格，历史无法回改，
    不应因此让门禁永久变红；凭据、用户目录与身份信息仍全量检查。
    """
    for label, pattern in SECRET_RULES:
        if pattern.search(line):
            return f"敏感信息：{label}"
    for label, pattern in STRONG_PATH_RULES:
        if pattern.search(line):
            return f"敏感信息：{label}"
    if check_drive and not vendor and ":" in line and DRIVE_RULE[1].search(line):
        return f"敏感信息：{DRIVE_RULE[0]}"

    if not vendor:
        # 身份信息类规则只作用于仓库自有内容：第三方素材的许可证/联系邮箱必须原样保留
        lowered = line.lower()
        if any(word in lowered for word in PII_HINT_WORDS):
            for label, pattern in CONTEXT_PII_RULES:
                if pattern.search(line):
                    return f"身份信息：{label}"

        if DIGIT_RUN_RE.search(line):
            for match in ID_CARD_RULE[1].finditer(line):
                if id_card_checksum_ok(match.group(1)):
                    return f"身份信息：{ID_CARD_RULE[0]}"
            if BARE_PHONE_RULE[1].search(line):
                return f"身份信息：{BARE_PHONE_RULE[0]}"

        if "@" in line:
            for match in EMAIL_RE.finditer(line):
                if match.group(0).lower() not in EMAIL_ALLOWLIST:
                    return "身份信息：邮箱地址"
    return None


def check_text_secrets(root: Path, report: Report) -> None:
    for path in iter_files(root):
        if has_nul(path):
            continue
        rel = str(path.relative_to(root))
        vendor = is_vendor(rel)
        for line_no, line in enumerate(loose_text(path).splitlines(), start=1):
            rule = find_line_violation(line, vendor)
            if rule:
                report.fail(rel, rule, f"第 {line_no} 行")


def check_binary_strings(root: Path, report: Report) -> None:
    """二进制文件不做整体解码，只抽取可打印字符串，套用高置信度规则。"""
    rules = SECRET_RULES + STRONG_PATH_RULES
    for path in iter_files(root):
        if not has_nul(path):
            continue
        rel = str(path.relative_to(root))
        data = path.read_bytes()[: 8 * 1024 * 1024]
        report.scanned_binaries += 1
        for run in ASCII_RUN_RE.findall(data):
            # 先做廉价的字符预筛，避免对海量随机串逐个跑正则
            if b":" not in run and b"@" not in run and b"_" not in run:
                continue
            chunk = run.decode("ascii", errors="replace")
            for label, pattern in rules:
                if pattern.search(chunk):
                    report.fail(rel, f"二进制内嵌敏感信息：{label}", "可打印字符串命中")
                    break


def check_history(root: Path, report: Report, all_refs: bool = False) -> None:
    """扫描 Git 历史的新增/删除行，覆盖"曾经提交过、后来删掉"的泄漏。

    默认只看当前分支（HEAD）的历史——这正是 PR 门禁需要覆盖的范围；
    `--history-all` 会连所有本地/远端引用一起扫，用于发布前的全量复核。
    注意：被 rebase / force-push 丢弃的提交不在任何引用里，需另行从远端按 SHA 取回。
    """
    dump = Path(tempfile.gettempdir()) / f"repo-history-{os.getpid()}.patch"
    scope = ["--all"] if all_refs else ["HEAD"]
    try:
        with dump.open("wb") as fh:
            rc = subprocess.call(
                ["git", "log"] + scope + ["--no-color", "--format=commit %H", "-p"],
                cwd=str(root), stdout=fh, stderr=fh,
            )
        if rc != 0:
            report.fail("git", "历史扫描", f"git log 退出码 {rc}")
            return
        text = dump.read_text(encoding="utf-8", errors="replace")
    finally:
        dump.unlink(missing_ok=True)

    report.history_bytes = len(text.encode("utf-8", errors="ignore"))
    current_commit = "unknown"
    current_path = ""
    for line in text.splitlines():
        if line.startswith("commit ") and len(line) > 7:
            current_commit = line.split()[1][:12]
            continue
        if line.startswith("diff --git "):
            match = DIFF_HEADER_RE.match(line)
            current_path = match.group(2) if match else ""
            continue
        if line.startswith(("+++ ", "--- ", "index ", "@@ ")):
            continue
        if not line.startswith(("+", "-")):
            continue
        rule = find_line_violation(line[1:], is_vendor(current_path), check_drive=False)
        if rule:
            report.fail(f"history:{current_commit} {current_path or '-'}", rule, "提交内容命中")


# ---------------------------------------------------------------------------
# project：工程一致性（UPM / 构建场景 / 训练配置）
# ---------------------------------------------------------------------------

def parse_yaml_scalars(text: str) -> dict[str, str]:
    """极简 YAML 标量解析：只处理「key: value」与缩进嵌套，足够校验本仓库配置。"""
    result: dict[str, str] = {}
    stack: list[tuple[int, str]] = []
    for raw in text.splitlines():
        if not raw.strip() or raw.lstrip().startswith("#"):
            continue
        indent = len(raw) - len(raw.lstrip(" "))
        body = raw.strip()
        if ":" not in body:
            continue
        key, _, value = body.partition(":")
        key = key.strip()
        value = value.split("#", 1)[0].strip()
        while stack and stack[-1][0] >= indent:
            stack.pop()
        path = ".".join([name for _, name in stack] + [key])
        if value == "":
            stack.append((indent, key))
        else:
            result[path] = value
    return result


def check_package_consistency(root: Path, report: Report) -> None:
    """UPM 清单与锁文件必须互相自洽，且不允许本机绝对路径 / local 源。"""
    manifest_path = root / "Packages" / "manifest.json"
    lock_path = root / "Packages" / "packages-lock.json"
    if not manifest_path.is_file():
        return
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8")).get("dependencies", {})
    except json.JSONDecodeError as exc:
        report.fail("Packages/manifest.json", "UPM 清单", f"无法解析：{exc}")
        return
    for name, spec in manifest.items():
        if isinstance(spec, str) and spec.startswith("file:"):
            report.fail("Packages/manifest.json", "UPM 清单", f"{name} 使用本机 file: 路径")
    if not lock_path.is_file():
        report.fail("Packages/packages-lock.json", "UPM 锁文件", "缺少锁文件")
        return
    try:
        lock = json.loads(lock_path.read_text(encoding="utf-8")).get("dependencies", {})
    except json.JSONDecodeError as exc:
        report.fail("Packages/packages-lock.json", "UPM 锁文件", f"无法解析：{exc}")
        return

    for name, spec in manifest.items():
        entry = lock.get(name)
        if entry is None:
            report.fail("Packages/packages-lock.json", "UPM 锁文件", f"{name} 未出现在锁文件中")
            continue
        if entry.get("source") == "local":
            report.fail("Packages/packages-lock.json", "UPM 锁文件", f"{name} 仍是 local 源")
        if isinstance(spec, str) and entry.get("version") != spec:
            report.fail(
                "Packages/packages-lock.json", "UPM 锁文件",
                f"{name} 版本不一致：manifest {spec}，lock {entry.get('version')}",
            )
    for name, entry in lock.items():
        if entry.get("source") == "local":
            report.fail("Packages/packages-lock.json", "UPM 锁文件", f"{name} 仍是 local 源")


def check_build_settings(root: Path, report: Report) -> None:
    """构建场景列表必须与磁盘一致，且 GUID 与 .meta 对得上（否则构建后无入口场景）。"""
    path = root / "ProjectSettings" / "EditorBuildSettings.asset"
    if not path.is_file():
        return
    entries = re.findall(
        r"-\s+enabled:\s*(\d+)\s*\n\s+path:\s*(.+?)\s*\n\s+guid:\s*([0-9a-f]{32})",
        loose_text(path),
    )
    if not entries:
        report.fail("ProjectSettings/EditorBuildSettings.asset", "构建场景", "未解析到任何场景条目")
        return
    enabled = 0
    for flag, scene_path, guid in entries:
        scene_path = scene_path.strip()
        target = root / scene_path
        if not target.is_file():
            report.fail("ProjectSettings/EditorBuildSettings.asset", "构建场景", f"{scene_path} 不存在")
            continue
        meta = target.with_name(target.name + ".meta")
        meta_guid = ""
        if meta.is_file():
            match = re.search(r"^guid:\s*([0-9a-f]{32})", loose_text(meta), re.MULTILINE)
            meta_guid = match.group(1) if match else ""
        if meta_guid != guid:
            report.fail(
                "ProjectSettings/EditorBuildSettings.asset", "构建场景",
                f"{scene_path} 的 GUID 与 .meta 不一致（{guid} vs {meta_guid or '缺失'}）",
            )
        if flag == "1":
            enabled += 1
    if enabled == 0:
        report.fail("ProjectSettings/EditorBuildSettings.asset", "构建场景", "没有任何启用的场景")


def check_training_config(root: Path, report: Report) -> None:
    """训练配置的语义校验：键名、数值关系、与工程内 Behavior Name 的一致性。"""
    path = root / "config" / "flappybird_config.yaml"
    if not path.is_file():
        return
    cfg = parse_yaml_scalars(loose_text(path))
    behaviors = sorted({key.split(".")[1] for key in cfg if key.startswith("behaviors.")})
    if len(behaviors) != 1:
        report.fail("config/flappybird_config.yaml", "训练配置", f"应只声明一个行为，实际 {behaviors}")
        return
    name = behaviors[0]
    prefix = f"behaviors.{name}."

    if cfg.get(prefix + "trainer_type") != "ppo":
        report.fail(path.name, "训练配置", "trainer_type 应为 ppo")

    batch = _as_number(cfg.get(prefix + "hyperparameters.batch_size"), int)
    buffer_size = _as_number(cfg.get(prefix + "hyperparameters.buffer_size"), int)
    max_steps = _as_number(cfg.get(prefix + "max_steps"), int)
    horizon = _as_number(cfg.get(prefix + "time_horizon"), int)
    gamma = _as_number(cfg.get(prefix + "reward_signals.extrinsic.gamma"), float)
    strength = _as_number(cfg.get(prefix + "reward_signals.extrinsic.strength"), float)
    layers = _as_number(cfg.get(prefix + "network_settings.num_layers"), int)
    units = _as_number(cfg.get(prefix + "network_settings.hidden_units"), int)
    checkpoints = _as_number(cfg.get(prefix + "keep_checkpoints"), int)

    if not batch or batch <= 0:
        report.fail(path.name, "训练配置", "batch_size 缺失或非正数")
    if not buffer_size or buffer_size <= 0:
        report.fail(path.name, "训练配置", "buffer_size 缺失或非正数")
    elif batch and buffer_size % batch != 0:
        report.fail(path.name, "训练配置", f"buffer_size({buffer_size}) 必须是 batch_size({batch}) 的整数倍")
    if not max_steps or max_steps < 1000:
        report.fail(path.name, "训练配置", "max_steps 缺失或过小")
    if buffer_size and max_steps and max_steps < buffer_size:
        report.fail(path.name, "训练配置", "max_steps 小于 buffer_size，训练无法完成一次完整更新")
    if horizon is None or not 32 <= horizon <= 4096:
        report.fail(path.name, "训练配置", "time_horizon 应在 32–4096")
    if gamma is None or not 0 < gamma <= 1:
        report.fail(path.name, "训练配置", "gamma 应在 (0, 1]")
    if strength is None or strength <= 0:
        report.fail(path.name, "训练配置", "extrinsic strength 应大于 0")
    if layers is None or layers < 1:
        report.fail(path.name, "训练配置", "num_layers 应 ≥ 1")
    if units is None or units < 8:
        report.fail(path.name, "训练配置", "hidden_units 应 ≥ 8")
    if checkpoints is None or checkpoints < 1:
        report.fail(path.name, "训练配置", "keep_checkpoints 应 ≥ 1")

    # 行为名必须与场景/预制体中的 Behavior Name 一致，否则训练器连不上
    found = set()
    for asset in list((root / "Assets").rglob("*.prefab")) + list((root / "Assets").rglob("*.unity")):
        for match in re.finditer(r"m_BehaviorName:\s*(\S+)", loose_text(asset)):
            found.add(match.group(1))
    if found and found != {name}:
        report.fail(path.name, "训练配置", f"Behavior Name 不一致：配置 {name}，工程 {sorted(found)}")


# ---------------------------------------------------------------------------
# scripts：Python 语法
# ---------------------------------------------------------------------------

def check_python_sources(root: Path, report: Report) -> None:
    """Python 侧只做无需依赖的语法校验，避免在 CI 引入第三方安装步骤。"""
    for path in iter_files(root):
        if path.suffix.lower() != ".py":
            continue
        rel = str(path.relative_to(root))
        try:
            compile(loose_text(path), rel, "exec")
        except SyntaxError as exc:
            report.fail(rel, "Python 语法", f"第 {exc.lineno} 行：{exc.msg}")


# ---------------------------------------------------------------------------
# 分组与入口
# ---------------------------------------------------------------------------

GROUP_CHECKS = {
    "hygiene": [
        check_structure, check_size, check_encoding_and_eol,
        check_unity_meta, check_json, check_markdown_links,
    ],
    "secrets": [check_text_secrets, check_binary_strings],
    "project": [check_package_consistency, check_build_settings, check_training_config],
    "scripts": [check_python_sources],
    "history": [],
}


def _write_step_summary(groups: list[str], report: Report, status: str) -> None:
    target = os.environ.get("GITHUB_STEP_SUMMARY")
    if not target:
        return
    lines = [
        f"### repo-guard · {', '.join(groups)}",
        "",
        f"- 结论：**{status}**",
        f"- 文本 {report.checked_files} 个 / 二进制 {report.scanned_binaries} 个 / 扫描 "
        f"{report.checked_bytes / 1024 / 1024:.1f} MB",
    ]
    if report.history_bytes:
        lines.append(f"- 历史 {report.history_bytes / 1024 / 1024:.1f} MB")
    if report.violations:
        lines += ["", "| 位置 | 规则 | 说明 |", "| --- | --- | --- |"]
        for item in report.violations[:20]:
            lines.append(f"| `{item['path']}` | {item['rule']} | {item['detail']} |")
        if len(report.violations) > 20:
            lines.append(f"| … | 共 {len(report.violations)} 项 | 见日志 |")
    try:
        with open(target, "a", encoding="utf-8") as fh:
            fh.write("\n".join(lines) + "\n")
    except OSError:
        pass


def main(argv: list[str] | None = None) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        # Windows 控制台默认使用本地编码，统一按 UTF-8 输出，避免中文乱码
        try:
            sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass

    parser = argparse.ArgumentParser(description="仓库规范与脱敏自检")
    parser.add_argument("--root", default=".", help="仓库根目录（默认当前目录）")
    parser.add_argument("--json", dest="json_out", help="把结果写成 JSON 报告")
    parser.add_argument("--group", action="append", default=[],
                        help="只跑指定分组，可重复：hygiene/secrets/project/scripts/history")
    parser.add_argument("--history", action="store_true", help="额外扫描当前分支的 Git 历史")
    parser.add_argument("--history-all", action="store_true", help="扫描所有引用的 Git 历史")
    parser.add_argument("--max-findings", type=int, default=200, help="最多输出多少条违规")
    args = parser.parse_args(argv)

    groups = args.group or [name for name in GROUP_CHECKS if name != "history"]
    unknown = [name for name in groups if name not in GROUP_CHECKS]
    if unknown:
        parser.error(f"未知分组：{', '.join(unknown)}")
    history_scope = args.history or args.history_all or "history" in groups

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

    for group in GROUP_CHECKS:
        if group == "history" or group not in groups:
            continue
        for check in GROUP_CHECKS[group]:
            check(root, report)

    if history_scope:
        check_history(root, report, all_refs=args.history_all)
        if "history" not in groups:
            groups = groups + ["history"]

    shown = report.violations[: args.max_findings]
    for item in shown:
        print(f"[FAIL] {item['path']} | {item['rule']} | {item['detail']}")
    if len(report.violations) > len(shown):
        print(f"... 其余 {len(report.violations) - len(shown)} 条已省略")

    status = "PASS" if not report.violations else "FAIL"
    scope_text = f"文本 {report.checked_files} 个、二进制 {report.scanned_binaries} 个，扫描 " \
                 f"{report.checked_bytes / 1024 / 1024:.1f} MB"
    if history_scope:
        scope_text += f"，历史 {report.history_bytes / 1024 / 1024:.1f} MB"
    print(f"[{status}] 分组 {'/'.join(groups)}：{scope_text}" if not report.violations
          else f"[FAIL] 共 {len(report.violations)} 项违规；{scope_text}")

    _write_step_summary(groups, report, status)

    if args.json_out:
        out = Path(args.json_out)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps({
            "repo": root.name,
            "groups": groups,
            "status": status,
            "text_files_checked": report.checked_files,
            "binaries_scanned": report.scanned_binaries,
            "history_bytes": report.history_bytes,
            "bytes_scanned": report.checked_bytes,
            "violations": report.violations,
        }, ensure_ascii=False, indent=2), encoding="utf-8")

    return 0 if status == "PASS" else 4


if __name__ == "__main__":
    sys.exit(main())
