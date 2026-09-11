using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace MainAPP.Services
{
    /// <summary>
    /// CUDA 运行时 DLL 按需加载器。
    /// <para>在 ONNX Runtime CUDA EP 初始化前，从以下路径按优先级搜索并预加载 CUDA DLL：</para>
    /// <para>1. 应用程序目录下的 cuda_runtime_dlls/ 子目录</para>
    /// <para>2. CUDA_PATH 环境变量指向的 bin 目录</para>
    /// <para>3. 系统 PATH 中的 CUDA DLL</para>
    /// <para>加载失败时返回 false，调用方应回退到 OpenVINO/CPU 执行提供者。</para>
    /// </summary>
    /// <remarks>
    /// REVIEW(2026-09-04): 为什么有些 DLL 必须显式预载？
    /// <para>
    /// `cuda_runtime_dlls` 是应用目录下的**子目录**，而 Windows 的 DLL 搜索顺序只含
    /// 「应用根目录 / System32 / PATH」，不会递归进子目录。
    /// </para>
    /// <para>
    /// cuDNN 9 的 `cudnn_graph64_9.dll` 内部是按**文件名**调用 LoadLibrary 去加载
    /// `nvrtc64_120_0.dll` 的（运行时编译引擎需要它现场 JIT）。若该 DLL 只存在于子目录且未被预载，
    /// 加载就会失败。此时只要 cuDNN 命中「预编译引擎」分支就一切正常，一旦回退到
    /// 「运行时编译引擎」分支，就会在卷积节点抛：
    /// <c>CUDNN_FE failure 7: GRAPH_EXECUTION_FAILED</c>。
    /// 表现是「同一份程序，这台机器正常、那台机器报错」，取决于 GPU 架构 / 驱动版本 / 卷积参数。
    /// </para>
    /// </remarks>
    public static class CudaRuntimeLoader
    {
        /// <summary>
        /// CUDA 运行时所需的核心 DLL 列表（按依赖顺序排列）。
        /// <para>任一缺失即视为 CUDA 不可用，调用方应回退到非 CUDA 设备。</para>
        /// </summary>
        private static readonly string[] s_cudaDlls =
        [
            "cudart64_13.dll",
            "cublas64_13.dll",
            "cublasLt64_13.dll",
            "cufft64_12.dll",
            // REVIEW(2026-09-05): 以下由 cuDNN / NVRTC 按文件名动态加载，必须显式预载。
            // CUDA 12 → 13 升版（配合 ORT 1.28.0，其 GPU 包默认 CUDA 13.0 构建）：
            //   cudart 12→13、cublas 12→13、cublasLt 12→13、cufft 11→12、
            //   nvJitLink 120_0→130_0、nvrtc 120_0→130_0；curand 仍为 10。
            "curand64_10.dll",
            "nvJitLink_130_0.dll",
            "nvrtc64_130_0.dll",
            "cudnn64_9.dll",
            "cudnn_ops64_9.dll",
            "cudnn_cnn64_9.dll",
            "cudnn_adv64_9.dll",
            "cudnn_graph64_9.dll",
        ];

        /// <summary>
        /// 文件名含 CUDA 小版本号、不便硬编码的可选组件。
        /// <para>缺了不致命：nvrtc 会按自身模块所在目录解析 builtins，与 nvrtc64_120_0.dll 同目录即可。</para>
        /// </summary>
        private static readonly string[] s_optionalCudaDllPatterns =
        [
            "nvrtc-builtins64_*.dll",
        ];

        /// <summary>
        /// cuDNN 9 的伴生子库。
        /// <para>注意（2026-09-05）：cuDNN 9.25.1.1（nvidia-cudnn-cu13==9.25.1.1，CUDA 13 时代）
        /// 官方发行版含 10 个文件，比 9.18.1.3 的 8 个多出 cudnn_engines_tensor_ir64_9.dll 与
        /// cudnn_ext64_9.dll——二者自 9.2x 起重新引入，9.25.1 需要它们。此前"删除 tensor_ir"是
        /// 针对 9.18.1 集合的做法，升级到 9.25.1 后须整套替换（10 文件），勿沿用旧删除法。</para>
        /// <para>注意（2026-09-07）：本清单文件已升级为"必需预载"（见 <see cref="TryPreloadCudaDlls"/>）。
        /// cuDNN frontend 在 build_operation_graph 阶段按文件名 LoadLibrary 动态加载这些子库，
        /// 当部署为应用子目录 cuda_runtime_dlls\ 形态时 DLL 搜索路径不含该目录，
        /// 仅预载主库会导致首个 Conv 抛 CUDNN_FE failure 11（CudaVerify 实测）。</para>
        /// </summary>
        private static readonly string[] s_cudnnCompanionFiles =
        [
            "cudnn_heuristic64_9.dll",
            "cudnn_engines_precompiled64_9.dll",
            "cudnn_engines_runtime_compiled64_9.dll",
            "cudnn_engines_tensor_ir64_9.dll",
            "cudnn_ext64_9.dll",
        ];

        /// <summary>
        /// 尝试预加载 CUDA 运行时 DLL。
        /// </summary>
        /// <returns>true 表示所有核心 DLL 加载成功；false 表示部分或全部失败，应回退到非 CUDA 设备</returns>
        public static bool TryPreloadCudaDlls()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var localCudaDir = Path.Combine(baseDir, "cuda_runtime_dlls");
            var cudaPathEnv = Environment.GetEnvironmentVariable("CUDA_PATH");
            var cudaToolkitBin = string.IsNullOrEmpty(cudaPathEnv) ? null : Path.Combine(cudaPathEnv, "bin");

            int loadedCount = 0;
            // REVIEW(2026-09-07): cuDNN 9.25.1 的 engines/heuristic/ext 等伴生子库由 cuDNN frontend
            // 在 build_operation_graph 阶段按文件名 LoadLibrary 动态加载，搜索路径不含应用子目录
            // cuda_runtime_dlls\。实测仅预载 12 个核心（伴生只查不载）会在首个 Conv 抛
            // "Could not locate cudnn_engines_runtime_compiled64_9.dll" → CUDNN_FE failure 11。
            // 故伴生 5 文件升级为必需预载（CudaVerify 已实证可修复）。
            var preloadDlls = new string[s_cudaDlls.Length + s_cudnnCompanionFiles.Length];
            Array.Copy(s_cudaDlls, preloadDlls, s_cudaDlls.Length);
            Array.Copy(s_cudnnCompanionFiles, 0, preloadDlls, s_cudaDlls.Length, s_cudnnCompanionFiles.Length);

            foreach (var dll in preloadDlls)
            {
                if (TryLoadFromPaths(dll, localCudaDir, cudaToolkitBin))
                    loadedCount++;
                else
                {
                    LogService.Instance.Warning($"[CUDA] 未能预加载 {dll}，CUDA 加速将不可用");
                    return false;
                }
            }

            LoadOptionalCudaDlls(localCudaDir, cudaToolkitBin);

            // 伴生 5 文件已随主循环预载（上面任一失败即返回 false）。
            // 此兜底仅针对"从 CUDA Toolkit bin 加载成功但本地子目录缺少同文件"的边界情况再提示一次。
            var missing = GetMissingCudnnCompanionFiles();
            if (missing.Count > 0)
            {
                LogService.Instance.Warning(
                    $"[CUDA] 本地 cuda_runtime_dlls 目录缺少伴生文件: {string.Join(", ", missing)}。" +
                    "当前可能改由 CUDA Toolkit 路径加载，发布部署时请确保与主库同目录整套替换。");
            }

            LogService.Instance.Info($"[CUDA] 成功预加载 {loadedCount}/{preloadDlls.Length} 个 DLL");
            return true;
        }

        /// <summary>
        /// 检测 CUDA 运行时是否可用（不实际加载，仅检测文件是否存在）。
        /// 清单含核心 12 + cuDNN 伴生 5（与 <see cref="TryPreloadCudaDlls"/> 的必需预载集一致）。
        /// </summary>
        public static bool IsCudaAvailable()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var localCudaDir = Path.Combine(baseDir, "cuda_runtime_dlls");
            var cudaPathEnv = Environment.GetEnvironmentVariable("CUDA_PATH");
            var cudaToolkitBin = string.IsNullOrEmpty(cudaPathEnv) ? null : Path.Combine(cudaPathEnv, "bin");

            var checkDlls = new string[s_cudaDlls.Length + s_cudnnCompanionFiles.Length];
            Array.Copy(s_cudaDlls, checkDlls, s_cudaDlls.Length);
            Array.Copy(s_cudnnCompanionFiles, 0, checkDlls, s_cudaDlls.Length, s_cudnnCompanionFiles.Length);

            foreach (var dll in checkDlls)
            {
                if (!FileExistsInPaths(dll, localCudaDir, cudaToolkitBin))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 返回缺失的 cuDNN 伴生文件清单（相对文件名）。空列表表示齐全。
        /// <para>用于在 CUDA 初始化前做一次完整性自检，避免问题延迟到推理期才暴露。</para>
        /// </summary>
        public static List<string> GetMissingCudnnCompanionFiles()
        {
            var missing = new List<string>();
            var localCudaDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cuda_runtime_dlls");

            foreach (var file in s_cudnnCompanionFiles)
            {
                // 伴生文件由 cuDNN 按 cudnn_graph64_9.dll 所在目录解析，因此只检查本地目录。
                if (!File.Exists(Path.Combine(localCudaDir, file)))
                    missing.Add(file);
            }

            return missing;
        }

        /// <summary>
        /// 加载版本相关的可选组件（文件名含小版本号，使用通配匹配）。失败只告警，不影响 CUDA 可用性判定。
        /// </summary>
        private static void LoadOptionalCudaDlls(string localCudaDir, string? cudaToolkitBin)
        {
            foreach (var pattern in s_optionalCudaDllPatterns)
            {
                var candidate = FindFirst(localCudaDir, pattern) ?? FindFirst(cudaToolkitBin, pattern);
                if (candidate is null)
                {
                    LogService.Instance.Warning($"[CUDA] 未找到可选组件 {pattern}，若 cuDNN 走运行时编译分支可能失败");
                    continue;
                }

                if (NativeLibrary.TryLoad(candidate, out _))
                {
                    LogService.Instance.Info($"[CUDA] 已预载可选组件 {Path.GetFileName(candidate)}");
                }
                else
                {
                    LogService.Instance.Warning($"[CUDA] 可选组件加载失败: {Path.GetFileName(candidate)}");
                }
            }
        }

        private static string? FindFirst(string? directory, string pattern)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return null;

            foreach (var file in Directory.EnumerateFiles(directory, pattern))
                return file;

            return null;
        }

        private static bool TryLoadFromPaths(string dllName, params string?[] searchPaths)
        {
            // 先尝试从指定路径加载（使用绝对路径）
            foreach (var path in searchPaths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                var fullPath = Path.Combine(path, dllName);
                if (File.Exists(fullPath))
                {
                    if (NativeLibrary.TryLoad(fullPath, out var handle))
                    {
                        return true;
                    }
                }
            }

            // 回退到系统搜索路径（PATH）
            if (NativeLibrary.TryLoad(dllName, out _))
                return true;

            return false;
        }

        private static bool FileExistsInPaths(string dllName, params string?[] searchPaths)
        {
            foreach (var path in searchPaths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                if (File.Exists(Path.Combine(path, dllName)))
                    return true;
            }
            // 也检查系统 PATH 中的 DLL
            return NativeLibrary.TryLoad(dllName, out _);
        }
    }
}
