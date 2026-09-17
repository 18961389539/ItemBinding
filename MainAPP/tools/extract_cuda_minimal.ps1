<#
.SYNOPSIS
    补全 MainAPP 的 CUDA 运行时依赖（cuda_runtime_dlls 目录）。CUDA 13 + cuDNN 9.25.1 版。

.DESCRIPTION
    ONNX Runtime 的 CUDA EP 依赖一组 NVIDIA redistributable DLL，而 ORT 的 NuGet 包
    自身不携带这些文件。历史上该目录由手工拷贝生成，容易遗漏组件。

    ORT 1.28.0 起 GPU 包默认以 CUDA 13.0 构建，故本脚本按 CUDA 13 配套提取：
      - nvidia-cuda-runtime / cublas / cufft / curand / nvjitlink / nvrtc（CUDA 13 runtime）
      - nvidia-cudnn-cu13 9.25.1.1（cuDNN 9.25.1，10 个文件）

    注意（2026-09-05）：cuDNN 9.25.1 官方集合为 10 个文件，含 cudnn_engines_tensor_ir64_9.dll
    与 cudnn_ext64_9.dll；与 9.18.1 的 8 文件集合不同，切勿沿用旧"删除 tensor_ir"的做法。

    本脚本从 NVIDIA 官方 PyPI 轮子提取 DLL，无需安装 CUDA Toolkit。

.PARAMETER Mirror
    PyPI 镜像基址。默认清华镜像；官方源在国内约 20 KB/s，清华镜像约 3 MB/s。

.PARAMETER Python
    python 可执行文件路径；默认从 PATH 解析（也可传完整路径）。

.EXAMPLE
    .\extract_cuda_minimal.ps1
    .\extract_cuda_minimal.ps1 -Python C:\python313\python.exe
#>
[CmdletBinding()]
param(
    [string]$Mirror = 'https://mirrors.tuna.tsinghua.edu.cn/pypi/web/simple',
    [string]$Python = 'python'
)

$ErrorActionPreference = 'Stop'

$TargetDir = Join-Path $PSScriptRoot '..\cuda_runtime_dlls'
$TargetDir = [System.IO.Path]::GetFullPath($TargetDir)
New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null

# 包清单：Name = pip 规格；Dlls = 该包内需要提取的 DLL 文件名
$Packages = @(
    @{ Name = 'nvidia-cuda-runtime==13.3.29'
       Dlls = @('cudart64_13.dll') }
    @{ Name = 'nvidia-cublas==13.6.0.2'
       Dlls = @('cublas64_13.dll', 'cublasLt64_13.dll', 'nvblas64_13.dll') }
    @{ Name = 'nvidia-cufft==12.3.0.29'
       Dlls = @('cufft64_12.dll', 'cufftw64_12.dll') }
    @{ Name = 'nvidia-curand==10.4.3.29'
       Dlls = @('curand64_10.dll') }
    @{ Name = 'nvidia-nvjitlink==13.3.33'
       Dlls = @('nvJitLink_130_0.dll') }
    @{ Name = 'nvidia-cuda-nvrtc==13.3.33'
       Dlls = @('nvrtc64_130_0.dll', 'nvrtc-builtins64_133.dll') }
    @{ Name = 'nvidia-cudnn-cu13==9.25.1.1'
       Dlls = @(
           'cudnn64_9.dll', 'cudnn_ops64_9.dll', 'cudnn_cnn64_9.dll', 'cudnn_adv64_9.dll',
           'cudnn_graph64_9.dll', 'cudnn_heuristic64_9.dll',
           'cudnn_engines_precompiled64_9.dll', 'cudnn_engines_runtime_compiled64_9.dll',
           'cudnn_engines_tensor_ir64_9.dll', 'cudnn_ext64_9.dll') }
)

$TempDir = Join-Path $env:TEMP ("cuda_dl_" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $TempDir | Out-Null

# 需要删除的旧 CUDA 12 文件（升级 CUDA 大版本时清理残留）
$Obsolete = @(
    'cudart64_12.dll', 'cublas64_12.dll', 'cublasLt64_12.dll', 'nvblas64_12.dll',
    'cufft64_11.dll', 'cufftw64_11.dll',
    'nvJitLink_120_0.dll', 'nvrtc64_120_0.dll', 'nvrtc-builtins64_129.dll'
)

try {
    foreach ($pkg in $Packages) {
        Write-Host "下载 $($pkg.Name) ..." -ForegroundColor Yellow
        & $Python -m pip download $pkg.Name --no-deps `
            -d $TempDir `
            -i $Mirror `
            --platform win_amd64 --only-binary=:all:
        if ($LASTEXITCODE -ne 0) { throw "pip download 失败: $($pkg.Name)" }

        $wheel = Get-ChildItem -Path $TempDir -Filter "*.whl" -File | Where-Object {
            $_.Name -like ($pkg.Name.Split('==')[0] + '*')
        } | Select-Object -First 1
        if (-not $wheel) { throw "未找到 wheel: $($pkg.Name)" }

        $extractDir = Join-Path $TempDir ([System.IO.Path]::GetFileNameWithoutExtension($wheel.Name))
        Expand-Archive -Path $wheel.FullName -DestinationPath $extractDir -Force

        foreach ($dll in $pkg.Dlls) {
            $found = Get-ChildItem -Path $extractDir -Filter $dll -Recurse -File | Select-Object -First 1
            if (-not $found) { throw "在 $($wheel.Name) 中未找到 $dll" }
            Copy-Item $found.FullName (Join-Path $TargetDir $dll) -Force
            Write-Host "  已安装 $dll" -ForegroundColor Cyan
        }

        # 清理临时 wheel，避免下一轮误匹配
        Remove-Item $wheel.FullName -Force
    }

    # 清理旧 CUDA 12 文件
    foreach ($name in $Obsolete) {
        $p = Join-Path $TargetDir $name
        if (Test-Path $p) {
            Remove-Item $p -Force
            Write-Host "  已删除旧文件 $name" -ForegroundColor DarkGray
        }
    }

    Write-Host ""
    Write-Host "完成。目标目录: $TargetDir" -ForegroundColor Green
    Write-Host "文件总数: $((Get-ChildItem $TargetDir -File).Count)（期望 20）"
    Write-Host "提示: 改动后请重新生成，MSBuild 会把该目录按 Content 通配符复制到输出与发布目录。"
}
finally {
    Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
}
