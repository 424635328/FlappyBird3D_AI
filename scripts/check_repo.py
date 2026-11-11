#!/usr/bin/env python3
"""仓库规范与脱敏自检：编码、换行、敏感信息、个人身份信息、体积、链接、JSON、Unity 元数据。

设计原则
--------
* 只依赖标准库，本地与 CI 用同一条命令；
* 每条结论都能定位到文件与行号/偏移，但不打印命中的明文内容；
* 不同失败原因使用不同退出码，便于脚本串联；
* 规则本身不写出可被自身命中的字面量（邮箱白名单用拼接构造），否则扫描器会误报自己。

用法
----
    python scripts/check_repo.py
    python scripts/check_repo.py --root .
    python scripts/check_repo.py --history          # 额外扫描 Git 历史
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
    "ml-agents-release_22", "results",
}

# 第三方资源目录：不按本仓库的编码/换行规则改造；姓名/学号类规则也不在此生效
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

# 凭据：任何文件（含二进制内嵌字符串）都检查
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

# 高置信度路径：任何文件（含二进制内嵌字符串）都检查
STRONG_PATH_RULES = [
    ("Windows 用户目录", re.compile(r"[A-Za-z]:[\\/]+Users[\\/]+[A-Za-z0-9_.\-]+")),
    ("macOS 用户目录", re.compile(r"/Users/[A-Za-z0-9_.\-]+/")),
    ("Linux 用户目录", re.compile(r"/home/[A-Za-z0-9_.\-]+/")),
    ("工作区绝对路径", re.compile(r"[A-Za-z]:[\\/]+GitHub[\\/]+")),
    ("本地文件 URI", re.compile(r"file:///[A-Za-z]:")),
    ("UNC 路径", re.compile(r"\\\\\?\\[A-Za-z]:")),
]

# 仓库自有文本文件额外检查：盘符绝对路径（示例里也不允许，避免暴露本机布局）
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

# 个人身份信息：上下文锚定的规则，任何文本文件都检查
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


def id_card_checksum_ok(value: str) -> bool:
    total = sum(int(value[i]) * ID_WEIGHTS[i] for i in range(17))
    return ID_CHECK_CODES[total % 11] == value[17].upper()


LINK_RE = re.compile(r"!?\[[^\]]*\]\(([^)\s]+)\)")
IMG_SRC_RE = re.compile(r"<img[^>]+src=\"([^\"]+)\"", re.IGNORECASE)
FENCE_RE = re.compile(r"^```.*?^```", re.MULTILINE | re.DOTALL)
TEXT_CONTROL = re.compile(r"[\x00-\x08\x0b\x0c\x0e-\x1f]")
ASCII_RUN_RE = re.compile(rb"[\x20-\x7e]{8,}")
# 路径可能含空格，取最后的 " b/" 作为分界；两侧相同
DIFF_HEADER_RE = re.compile(r"^diff --git a/(.*) b/(.*)$")


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

    def ok(self) -> bool:
        return not self.violations


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


# ---------------------------------------------------------------------------
# 检查项
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


def find_line_violation(line: str, vendor: bool) -> str | None:
    """返回该行命中的第一条规则描述；只有规则名，不含明文。"""
    for label, pattern in SECRET_RULES:
        if pattern.search(line):
            return f"敏感信息：{label}"
    for label, pattern in STRONG_PATH_RULES:
        if pattern.search(line):
            return f"敏感信息：{label}"
    if not vendor and ":" in line and DRIVE_RULE[1].search(line):
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
        rule = find_line_violation(line[1:], is_vendor(current_path))
        if rule:
            report.fail(f"history:{current_commit} {current_path or '-'}", rule, "提交内容命中")


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

    parser = argparse.ArgumentParser(description="仓库规范与脱敏自检")
    parser.add_argument("--root", default=".", help="仓库根目录（默认当前目录）")
    parser.add_argument("--json", dest="json_out", help="把结果写成 JSON 报告")
    parser.add_argument("--history", action="store_true", help="额外扫描当前分支的 Git 历史")
    parser.add_argument("--history-all", action="store_true", help="扫描所有引用的 Git 历史")
    parser.add_argument("--max-findings", type=int, default=200, help="最多输出多少条违规")
    args = parser.parse_args(argv)

    root = Path(args.root).resolve()
    report = Report()
    history_scope = args.history or args.history_all

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
    check_text_secrets(root, report)
    check_binary_strings(root, report)
    check_json(root, report)
    check_markdown_links(root, report)
    if args.history or args.history_all:
        check_history(root, report, all_refs=args.history_all)

    shown = report.violations[: args.max_findings]
    for item in shown:
        print(f"[FAIL] {item['path']} | {item['rule']} | {item['detail']}")
    if len(report.violations) > len(shown):
        print(f"... 其余 {len(report.violations) - len(shown)} 条已省略")

    summary = {
        "root": str(root),
        "text_files_checked": report.checked_files,
        "binaries_scanned": report.scanned_binaries,
        "history_bytes": report.history_bytes,
        "bytes_scanned": report.checked_bytes,
        "violations": report.violations,
    }
    if args.json_out:
        out = Path(args.json_out)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")

    if report.violations:
        print(
            f"\n[FAIL] 共 {len(report.violations)} 项违规；文本 {report.checked_files} 个、"
            f"二进制 {report.scanned_binaries} 个，扫描 {report.checked_bytes / 1024 / 1024:.1f} MB"
            + (f"，历史 {report.history_bytes / 1024 / 1024:.1f} MB" if history_scope else "")
        )
        return 4

    print(
        f"[OK] 全部通过：文本 {report.checked_files} 个、二进制 {report.scanned_binaries} 个，"
        f"扫描 {report.checked_bytes / 1024 / 1024:.1f} MB"
        + (f"，历史 {report.history_bytes / 1024 / 1024:.1f} MB" if history_scope else "")
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
