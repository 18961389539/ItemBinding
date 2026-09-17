# ============================================================
#  verify-vendor-dlls.ps1 —— 校验厂商（不入库）依赖是否就位且版本正确
#
#  为什么需要：本仓库刻意**不提交**两类厂商二进制（*.dll 被 .gitignore 排除）：
#    1) 海康读码器 SDK   HikCameraLib/HikScanner/DLLs/*.dll   —— 25 个文件，**编译期必需**
#       （缺了 MvCodeReaderSDK.Net，HikScanner 会报一串 CS0246）
#    2) CUDA 运行时      MainAPP/cuda_runtime_dlls/*.dll      —— 运行期可选，缺失时自动回退 CPU
#  两者都用『SHA256 清单』固定版本，本脚本按清单逐文件校验。
#
#  用法（在仓库根目录）：
#      pwsh scripts/verify-vendor-dlls.ps1
#      powershell -ExecutionPolicy Bypass -File scripts/verify-vendor-dlls.ps1
#
#  退出码：0 = 全部就位且哈希一致；1 = 有缺失或不匹配（信息里给出恢复办法）
# ============================================================

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# 清单：@(标签, 清单文件, 相对目录, 是否编译期必需, 恢复办法说明)
$manifests = @(
    @{
        Label    = '海康读码器 SDK'
        Manifest = Join-Path $root 'HikCameraLib\HikScanner\DLLs.sha256'
        BaseDir  = Join-Path $root 'HikCameraLib\HikScanner\DLLs'
        Required = $true
        Hint     = '编译期必需。请从已装的 MVS/读码器 SDK，或另一台已能构建的机器的 HikCameraLib\HikScanner\DLLs 目录复制这些文件过来。'
    },
    @{
        Label    = 'CUDA 运行时'
        Manifest = Join-Path $root 'cuda_runtime_dlls_SHA256.txt'
        BaseDir  = Join-Path $root 'MainAPP\cuda_runtime_dlls'
        Required = $false
        Hint     = '运行期可选（缺失时自动回退 OpenVINO/CPU）。恢复请运行 MainAPP\tools\extract_cuda_minimal.ps1。'
    }
)

$failures = 0

foreach ($m in $manifests) {
    Write-Host ''
    Write-Host "[$($m.Label)] $($m.BaseDir)" -ForegroundColor Cyan

    if (-not (Test-Path $m.Manifest)) {
        Write-Host "  ! 清单文件缺失：$($m.Manifest)" -ForegroundColor Yellow
        $failures++
        continue
    }

    # 清单格式：<sha256>  <文件名>（# 开头为注释）
    $entries = Get-Content $m.Manifest |
        Where-Object { $_ -match '^\s*[0-9a-fA-F]{64}' } |
        ForEach-Object {
            $parts = $_ -split '\s+', 2
            [pscustomobject]@{ Hash = $parts[0].ToLower(); Name = $parts[1].Trim() }
        }

    if ($entries.Count -eq 0) {
        Write-Host '  ! 清单里没有可解析的条目' -ForegroundColor Yellow
        $failures++
        continue
    }

    $missing = @()
    $mismatch = @()
    foreach ($e in $entries) {
        $path = Join-Path $m.BaseDir $e.Name
        if (-not (Test-Path $path)) { $missing += $e.Name; continue }
        $actual = (Get-FileHash -Algorithm SHA256 -Path $path).Hash.ToLower()
        if ($actual -ne $e.Hash) { $mismatch += $e.Name }
    }

    if ($missing.Count -eq 0 -and $mismatch.Count -eq 0) {
        Write-Host "  OK  $($entries.Count) 个文件全部就位且哈希一致" -ForegroundColor Green
        continue
    }

    if ($missing.Count -gt 0) {
        Write-Host "  ! 缺失 $($missing.Count)/$($entries.Count) 个文件：" -ForegroundColor Yellow
        $missing | Select-Object -First 8 | ForEach-Object { Write-Host "      - $_" }
        if ($missing.Count -gt 8) { Write-Host "      ...（其余 $($missing.Count - 8) 个略）" }
    }
    if ($mismatch.Count -gt 0) {
        Write-Host "  ! 版本不匹配 $($mismatch.Count) 个文件（SDK 版本与本仓库清单不一致）：" -ForegroundColor Yellow
        $mismatch | Select-Object -First 8 | ForEach-Object { Write-Host "      - $_" }
    }
    Write-Host "  → $($m.Hint)" -ForegroundColor Yellow

    if ($m.Required) { $failures++ }
}

Write-Host ''
if ($failures -gt 0) {
    Write-Host '结果：存在必需项缺失或不匹配 —— 构建 MainAPP 会失败（HikScanner 报 HIKSDK001）。' -ForegroundColor Red
    exit 1
}

Write-Host '结果：厂商依赖校验通过。' -ForegroundColor Green
exit 0
