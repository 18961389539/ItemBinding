# MainAPP CUDA 12 → 13 升级部署说明

> 日期：2026-09-05
> 根因：工位机 GPU 为 **RTX 5060（Blackwell 算力 12.0 / sm_120）**，而旧版 ORT 1.24.1（CUDA 12.8 构建）不含 sm_120 kernel，导致 Conv 走 cuDNN frontend 时 `build_operation_graph` 失败（`CUDNN_BACKEND_API_FAILED`）。

## 一、升级内容

| 组件 | 旧版本 | 新版本 |
|---|---|---|
| ONNX Runtime（NuGet） | Microsoft.ML.OnnxRuntime.Gpu.Windows **1.24.1** | **1.28.0**（CUDA 13.0 构建，含 sm_120）|
| CUDA runtime | 12.x（cudart64_12.dll 等）| **13.x**（cudart64_13.dll 等）|
| cuDNN | 9.18.1（8 文件）| **9.25.1**（10 文件，含 tensor_ir + ext）|

## 二、代码改动清单

| 文件 | 改动 |
|---|---|
| `MainAPP/MainAPP.csproj` | ORT 1.24.1 → 1.28.0；WarnCudaMissing 检查 cudart64_13.dll |
| `JinlongYolo/JinlongYolo.csproj` | OnnxRuntimeVersion 1.24.1 → 1.28.0 |
| `ConsoleApp.Test/ConsoleApp.Test.csproj` | 3 个 ORT 包 1.24.1 → 1.28.0 |
| `JinlongYolo.Tests/JinlongYolo.Tests.csproj` | Gpu.Windows 1.24.1 → 1.28.0 |
| `MainAPP/Services/CudaRuntimeLoader.cs` | s_cudaDlls 后缀 12→13（cufft 11→12、nvJitLink/nvrtc 120_0→130_0）；companion 文件 3→5 项（+tensor_ir +ext）|
| `MainAPP/tools/extract_cuda_minimal.ps1` | 重写为 CUDA 13 + pip download |
| `CudaVerify/verify_cuda_clean.py` | 更新 CUDA 13 + cuDNN 10 文件清单 |

## 三、cuda_runtime_dlls 最终清单（20 个文件）

**CUDA 13 runtime（10 个）**
```
cudart64_13.dll        cublas64_13.dll        cublasLt64_13.dll
nvblas64_13.dll        cufft64_12.dll         cufftw64_12.dll
curand64_10.dll        nvJitLink_130_0.dll    nvrtc64_130_0.dll
nvrtc-builtins64_133.dll
```

**cuDNN 9.25.1（10 个）**
```
cudnn64_9.dll              cudnn_ops64_9.dll            cudnn_cnn64_9.dll
cudnn_adv64_9.dll          cudnn_graph64_9.dll          cudnn_heuristic64_9.dll
cudnn_engines_precompiled64_9.dll
cudnn_engines_runtime_compiled64_9.dll
cudnn_engines_tensor_ir64_9.dll      ← 9.25.1 需要，勿删！
cudnn_ext64_9.dll                     ← 9.25.1 新增，勿删！
```

完整 SHA256 校验见 `cuda_runtime_dlls_SHA256.txt`。

## 四、剩余操作（需在 VS 中完成，本机沙箱无法 restore）

1. **VS 打开解决方案 → 还原 NuGet 包**（下载 ORT 1.28.0，约 224 MB）
2. **重新生成 MainAPP**（0 警告 0 错误）
3. **发布 MainAPP**（net8.0-windows，Release）
4. **同步工位机** `D:\Programs\0905\publish`：
   - 覆盖发布目录所有文件
   - **整个替换** `cuda_runtime_dlls\` 目录（20 个新文件，删掉旧的 cudart64_12.dll 等 9 个 CUDA12 文件）

## 五、工位机验证

1. 启动日志应出现 `[预热] CUDA 空白图推理完成，耗时 XXXms`（不再报 CUDNN_BACKEND_API_FAILED）
2. 用 `cuda_runtime_dlls_SHA256.txt` 逐文件比对工位机目录
3. 跑一次真实主检测，第一个工件耗时应进入稳态（~160-270ms），无首帧尖峰

## 六、关键注意

- **cuDNN 9.25.1 是 10 文件**，与 9.18.1 的 8 文件不同。之前的「删除 tensor_ir」只适用于 9.18.1，**升级后必须保留 tensor_ir 和 ext**。
- CUDA 13 的 PyPI 包已改名（`nvidia-cuda-runtime` 无后缀），重建 DLL 用更新后的 `extract_cuda_minimal.ps1`。
- Gpu.Windows 1.28.0 原生 DLL 不含 openvino provider（移到 Intel.ML.OnnxRuntime.OpenVino 包），OpenVINO 降级链不受影响。

## 七、2026-09-07 补充（实测验证 + 伴生库预载修复）

### 7.1 本机 CUDA 可用性判定误区
- **`nvidia-smi` 报 "Failed to initialize NVML: Unknown Error" ≠ CUDA 不可用**。本机实测：NVML `nvmlInit_v2` 返回 0、设备可枚举；nvidia-smi 失败源于本机无 `NVDisplay.Container` 服务（工具层问题），不影响 ORT CUDA EP 推理。
- **权威验证方式 = 跑 CudaVerify**（见 7.2），不要用 nvidia-smi 判断。

### 7.2 CudaVerify 已同步 CUDA 13 / cuDNN 9.25.1
- `CudaVerify/Program.cs` 清单与审计升级：核心 12 DLL（CUDA13 命名）、cuDNN 官方集合 9.25.1 的 10 文件（tensor_ir + ext **必需**，删除旧"拒绝 tensor_ir"逻辑）。
- `CudaVerify/cuda_runtime_dlls/` 已整套替换为 20 个 CUDA13 文件（与 MainAPP 发布包同源）。
- 修复预存编译错误（字符串 ASCII 引号、条件表达式 long[] 推断）。
- **实测 PASS（退出码 0）**：18/18 DLL 预载、CUDA 会话创建成功、Conv 推理 Run 成功——部署机可用性的金标准。

### 7.3 CudaRuntimeLoader 伴生子库预载修复（部署必须同步）
- **现象**：cuDNN 9.25.1 frontend 在 `build_operation_graph` 阶段按文件名 `LoadLibrary` 动态加载 engines 系子库；DLL 搜索路径不含应用子目录 `cuda_runtime_dlls\`。旧代码只预载核心 12 个、伴生 5 个仅检查不加载 → 首个 Conv 抛 `Could not locate cudnn_engines_runtime_compiled64_9.dll` → `CUDNN_FE failure 11 / CUDNN_BACKEND_API_FAILED`。
- **修复**：`MainAPP/Services/CudaRuntimeLoader.cs` 将伴生 5 文件（heuristic / engines_precompiled / engines_runtime_compiled / tensor_ir / ext）升级为**必需预载**（`TryPreloadCudaDlls` + `IsCudaAvailable` 清单同步，缺任一即判定 CUDA 不可用）。
- **部署要求**：重发 MainAPP 使修复生效；`cuda_runtime_dlls\` 目录必须整套替换（20 文件），勿混用 CUDA12 旧文件。
- **部署机验证**：启动日志出现 `[CUDA] 成功预加载 17/17 个 DLL`（12 核心 + 5 伴生，加可选 nvrtc-builtins 提示）→ `[预热] CUDA 空白图推理完成` 即确认。
