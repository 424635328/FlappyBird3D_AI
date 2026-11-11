#Requires -Version 5.1
<#
.SYNOPSIS
    启动 FlappyBird3D_AI 的 ML-Agents 训练。

.DESCRIPTION
    对 mlagents-learn 做一层薄封装：校验配置、生成 run-id、拼接常用参数，
    并保证退出码能传递给调用方（CI 或批处理）。

.PARAMETER RunId
    训练运行 ID，也是 results/ 下的目录名。省略时按时间戳生成。

.PARAMETER Config
    训练配置路径，默认 config/flappybird_config.yaml。

.PARAMETER NumEnvs
    并行环境数量，需与 --env 指定构建产物或编辑器实例配合。

.PARAMETER Env
    环境可执行文件路径。省略时连接 Unity 编辑器中运行的游戏。

.PARAMETER Headless
    以无图形模式运行环境（-batchmode -nographics），必须同时指定 -Env。

.PARAMETER Resume
    从同名 run-id 的 checkpoint 续训，与 -Force 互斥。

.PARAMETER Force
    覆盖同名 run-id 的已有结果。

.PARAMETER BasePort
    训练器监听的起始端口，默认 5004。

.EXAMPLE
    .\scripts\train.ps1 -RunId FlappyBird_Smoke -NumEnvs 1

.EXAMPLE
    .\scripts\train.ps1 -RunId FlappyBird_Headless -NumEnvs 5 `
        -Env "<构建产物目录>\FlappyBird3D_AI.exe" -Headless
#>
[CmdletBinding()]
param(
    [string]$RunId,
    [string]$Config = 'config/flappybird_config.yaml',
    [ValidateRange(1, 64)]
    [int]$NumEnvs = 1,
    [string]$Env,
    [switch]$Headless,
    [switch]$Resume,
    [switch]$Force,
    [ValidateRange(1024, 65535)]
    [int]$BasePort
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

try {
    if (-not (Test-Path -LiteralPath $Config)) {
        Write-Host "[失败] 找不到训练配置：$Config" -ForegroundColor Red
        exit 2
    }

    if ($Resume -and $Force) {
        Write-Host "[失败] -Resume 与 -Force 互斥，请只保留一个。" -ForegroundColor Red
        exit 2
    }

    if ($Headless -and -not $Env) {
        Write-Host "[失败] -Headless 需要同时指定 -Env（构建产物路径）。" -ForegroundColor Red
        exit 2
    }

    $trainer = Get-Command -Name 'mlagents-learn' -ErrorAction SilentlyContinue
    if ($null -eq $trainer) {
        Write-Host "[失败] 未找到 mlagents-learn，请先激活已安装 mlagents 的虚拟环境。" -ForegroundColor Red
        Write-Host "       参考 docs/setup.md，或运行 python scripts/check_env.py 自检。" -ForegroundColor Yellow
        exit 3
    }

    if ([string]::IsNullOrWhiteSpace($RunId)) {
        $RunId = 'FlappyBird_' + (Get-Date -Format 'MMdd_HHmm')
    }
    if ($RunId -notmatch '^[A-Za-z0-9_.\-]+$') {
        Write-Host "[失败] -RunId 只允许字母、数字、下划线、点和短横线。" -ForegroundColor Red
        exit 2
    }

    $arguments = @(
        $Config,
        "--run-id=$RunId",
        "--num-envs=$NumEnvs"
    )
    if ($PSBoundParameters.ContainsKey('BasePort')) { $arguments += "--base-port=$BasePort" }
    if ($Env) { $arguments += "--env=$Env" }
    if ($Resume) { $arguments += '--resume' }
    if ($Force) { $arguments += '--force' }
    if ($Headless) { $arguments += @('--env-args', '-batchmode', '-nographics') }

    Write-Host "运行目录 : $repoRoot" -ForegroundColor Cyan
    Write-Host "配置     : $Config" -ForegroundColor Cyan
    Write-Host "Run ID   : $RunId" -ForegroundColor Cyan
    Write-Host "环境数   : $NumEnvs" -ForegroundColor Cyan
    Write-Host "命令     : mlagents-learn $($arguments -join ' ')" -ForegroundColor DarkGray
    Write-Host ''

    & $trainer.Source @arguments
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
