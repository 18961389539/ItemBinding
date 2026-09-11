using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;

namespace CudaVerify
{
    /// <summary>
    /// cuDNN 干净环境验证器（免 NuGet、无 CUDA Toolkit 依赖）。
    ///
    /// 用途：在"无 CUDA Toolkit / 无系统 cuDNN"的干净机器上，仅靠应用目录 cuda_runtime_dlls/
    /// 子目录验证：
    ///   Step 0  环境快照（是否干净、有无 NVIDIA 驱动）
    ///   Step 1  cuDNN 集合审计：必须恰为官方 9.25.1 的 10 个文件（含 tensor_ir + ext，勿删）
    ///   Step 2  核心 12 DLL 按名预载（与 MainAPP CudaRuntimeLoader 同清单，CUDA 13）
    ///   Step 3  （有驱动时）ORT CUDA 会话 + 与生产同形态首层 Conv 推理，判定是否复现
    ///           CUDNN_FE failure / CUDNN_BACKEND_API_FAILED
    ///
    /// 退出码：0 = PASS（加载级全过；无驱动时为加载级 PASS）；1 = FAIL；2 = GPU 推理 SKIP（无驱动）。
    /// </summary>
    internal static class Program
    {
        // 与 MainAPP CudaRuntimeLoader.s_cudaDlls 保持一致（缺任一即 CUDA 不可用）
        // CUDA 13 时代（2026-09-05 升级，配合 ORT 1.28.0 GPU 包 CUDA 13.0 构建）
        private static readonly string[] CoreCudaDlls =
        [
            "cudart64_13.dll",
            "cublas64_13.dll",
            "cublasLt64_13.dll",
            "cufft64_12.dll",
            "curand64_10.dll",
            "nvJitLink_130_0.dll",
            "nvrtc64_130_0.dll",
            "cudnn64_9.dll",
            "cudnn_ops64_9.dll",
            "cudnn_cnn64_9.dll",
            "cudnn_adv64_9.dll",
            "cudnn_graph64_9.dll",
        ];

        // 文件名含小版本号、不便硬编码的可选组件（nvrtc 按自身目录解析 builtins）
        private static readonly string OptionalCudnnNvrtcPattern = "nvrtc-builtins64_*.dll";

        // cuDNN 9.25.1 伴生子库：由 cuDNN frontend 在 build_operation_graph 阶段按文件名
        // LoadLibrary 动态加载（DLL 搜索路径不含应用子目录 cuda_runtime_dlls\）。
        // 实测缺失（仅预载主库）→ CUDNN_FE failure 11 / SUBLIBRARY_LOADING_FAILED，
        // 故必须与核心 DLL 一并显式预载。与 MainAPP CudaRuntimeLoader.s_cudnnCompanionFiles 对齐。
        private static readonly string[] CudnnCompanionDlls =
        [
            "cudnn_heuristic64_9.dll",
            "cudnn_engines_precompiled64_9.dll",
            "cudnn_engines_runtime_compiled64_9.dll",
            "cudnn_engines_tensor_ir64_9.dll",
            "cudnn_ext64_9.dll",
        ];

        // cuDNN 9.25.1 官方发行版集合（nvidia-cudnn-cu13==9.25.1.1）——恰好 10 个文件。
        // 注意：比 9.18.1.3 多出 cudnn_engines_tensor_ir64_9.dll 与 cudnn_ext64_9.dll，
        // 二者自 9.2x 起重新引入且 9.25.1 需要它们，不得删除。
        private static readonly string[] OfficialCudnnSet =
        [
            "cudnn64_9.dll",
            "cudnn_adv64_9.dll",
            "cudnn_cnn64_9.dll",
            "cudnn_engines_precompiled64_9.dll",
            "cudnn_engines_runtime_compiled64_9.dll",
            "cudnn_engines_tensor_ir64_9.dll",
            "cudnn_ext64_9.dll",
            "cudnn_graph64_9.dll",
            "cudnn_heuristic64_9.dll",
            "cudnn_ops64_9.dll",
        ];

        private static int _failCount;

        private static int Main()
        {
            var baseDir = AppContext.BaseDirectory;
            var cudaDir = Path.Combine(baseDir, "cuda_runtime_dlls");
            Console.WriteLine("============================================================");
            Console.WriteLine(" cuDNN 干净环境验证器 (CudaVerify)");
            Console.WriteLine(" 程序目录 : " + baseDir);
            Console.WriteLine("============================================================");

            Step0_EnvironmentSnapshot();
            Step1_CudnnSetAudit(cudaDir);
            Step2_PreloadCoreDlls(cudaDir);

            if (_failCount > 0)
            {
                Console.WriteLine($"\n[RESULT] FAIL：{_failCount} 项未通过。");
                return 1;
            }

            Console.WriteLine("\n[RESULT] 加载级验证 PASS（cuDNN 集合自洽、核心 DLL 全量可加载）。");
            Console.WriteLine("         无 NVIDIA 驱动时，真实 GPU 推理无法在本机执行（见 Step 3）。");
            return Step3_GpuInferenceIfDriverPresent(baseDir, cudaDir);
        }

        // ---------- Step 0 ----------
        private static void Step0_EnvironmentSnapshot()
        {
            Console.WriteLine("\n--- Step 0 环境快照（判断是否“干净”） ---");
            var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
            Console.WriteLine($"  CUDA_PATH      = {(string.IsNullOrEmpty(cudaPath) ? "<空>" : cudaPath)}");

            var sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var sysCudnn = Directory.EnumerateFiles(sys32, "cudnn*.dll").ToArray();
            Console.WriteLine($"  System32 cuDNN = {(sysCudnn.Length == 0 ? "<无>（干净）" : string.Join(", ", sysCudnn.Select(Path.GetFileName)))}");

            var appRootCudnn = Directory.EnumerateFiles(AppContext.BaseDirectory, "cudnn*.dll").ToArray();
            Console.WriteLine($"  程序根 cuDNN   = {(appRootCudnn.Length == 0 ? "<无>（干净）" : string.Join(", ", appRootCudnn.Select(Path.GetFileName)))}");

            var nvcuda = Path.Combine(sys32, "nvcuda.dll");
            Console.WriteLine($"  NVIDIA 驱动    = {((File.Exists(nvcuda)) ? "存在（可做 GPU 推理验证）" : "不存在（本机无 GPU，Step 3 将 SKIP）")}");
        }

        // ---------- Step 1 ----------
        private static void Step1_CudnnSetAudit(string cudaDir)
        {
            Console.WriteLine("\n--- Step 1 cuDNN 集合审计（期望：官方 9.25.1 的 10 个文件，含 tensor_ir + ext） ---");
            if (!Directory.Exists(cudaDir))
            {
                Fail($"缺少目录 {cudaDir}");
                return;
            }

            var actual = Directory.EnumerateFiles(cudaDir, "cudnn*.dll")
                                  .Select(Path.GetFileName)
                                  .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                                  .ToArray();
            Console.WriteLine($"  实际文件({actual.Length}): {string.Join(", ", actual)}");

            // ① 恰好 = 官方集合
            var expected = OfficialCudnnSet.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            if (!actual.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
            {
                Fail("cuDNN 文件集合与官方 9.25.1 不一致——存在多余子库或缺失文件。");
                return;
            }

            // ② 明确要求 tensor_ir 与 ext（9.25.1 官方集合包含，缺失则推理期 3009/error 7）
            var missing = new[] { "cudnn_engines_tensor_ir64_9.dll", "cudnn_ext64_9.dll" }
                .Where(f => !actual.Contains(f, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (missing.Length > 0)
            {
                Fail($"缺少 cuDNN 9.25.1 必需子库: {string.Join(", ", missing)}——请勿沿用 9.18.1 的删除 tensor_ir 做法。");
            }

            // ③ 主库与带版本资源子库版本一致性（9.25.1 部分子库无版本资源属正常，仅提示不判失败）
            var main = FileVersionInfo.GetVersionInfo(Path.Combine(cudaDir, "cudnn64_9.dll"));
            var mainVer = main.FileVersion ?? "<n/a>";
            Console.WriteLine($"  主库 cudnn64_9.dll 版本 = {mainVer}");

            foreach (var f in actual.Where(x => !x.Equals("cudnn64_9.dll", StringComparison.OrdinalIgnoreCase)))
            {
                var vi = FileVersionInfo.GetVersionInfo(Path.Combine(cudaDir, f));
                var ver = vi.FileVersion;
                if (ver is null)
                {
                    // graph / heuristic 官方即无版本资源；9.25.1 中部分子库同样无版本资源，属正常
                    Console.WriteLine($"  {f,-48} 无版本资源（官方同款，正常）");
                }
                else
                {
                    Console.WriteLine($"  {f,-48} {ver}");
                    if (mainVer != "<n/a>" && !ver.StartsWith(mainVer.Split('.')[0] + "." + mainVer.Split('.')[1], StringComparison.Ordinal))
                    {
                        Fail($"{f} 版本 {ver} 与主库 {mainVer} 大版本不一致。");
                    }
                }
            }
        }

        // ---------- Step 2 ----------
        private static void Step2_PreloadCoreDlls(string cudaDir)
        {
            Console.WriteLine("\n--- Step 2 核心 12 + cuDNN 伴生 5 DLL 按名预载（绝对路径，与 MainAPP 同清单） ---");
            var loaded = 0;
            var allDlls = CoreCudaDlls.Concat(CudnnCompanionDlls).ToArray();
            foreach (var dll in allDlls)
            {
                var full = Path.Combine(cudaDir, dll);
                if (File.Exists(full) && NativeLibrary.TryLoad(full, out _))
                {
                    loaded++;
                    Console.WriteLine($"  [OK] {dll}");
                }
                else
                {
                    Fail($"预载失败: {dll}");
                }
            }

            // 可选：nvrtc-builtins64_*.dll（文件名带小版本号，按模式扫描；缺了不致命但会有 NVRTC 告警）
            var builtins = Directory.EnumerateFiles(cudaDir, OptionalCudnnNvrtcPattern).ToArray();
            if (builtins.Length > 0 && NativeLibrary.TryLoad(builtins[0], out _))
            {
                loaded++;
                Console.WriteLine($"  [OK] {Path.GetFileName(builtins[0])}（可选 nvrtc-builtins）");
            }
            else if (builtins.Length == 0)
            {
                Console.WriteLine($"  [--] 未找到 nvrtc-builtins64_*.dll（可选，可能影响动态编译路径）");
            }
            else
            {
                Fail($"预载失败: nvrtc-builtins ({builtins[0]})");
            }

            Console.WriteLine($"  预载 {loaded}/{allDlls.Length + 1}");
        }

        // ---------- Step 3 ----------
        private static int Step3_GpuInferenceIfDriverPresent(string baseDir, string cudaDir)
        {
            Console.WriteLine("\n--- Step 3 GPU 推理（复现 3009 的最终判定） ---");
            var sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (!File.Exists(Path.Combine(sys32, "nvcuda.dll")))
            {
                Console.WriteLine("  未检测到 NVIDIA 驱动(nvcuda.dll)：本机无 GPU，无法执行 CUDA 推理。");
                Console.WriteLine("  请把本目录整体拷贝到带 NVIDIA 驱动的干净机器上运行 CudaVerify.exe，");
                Console.WriteLine("  或在工位机部署修复后的发布包后，用真实主检测/本验证器确认无 3009。");
                return 2;
            }

            // 模型优先用最小 Conv 探针；若旁边有 MainAPP 的 Models\best.onnx 则用它做全模型验证
            var probe = Path.Combine(baseDir, "conv_verify.onnx");
            var best = Path.Combine(baseDir, "Models", "best.onnx");
            var modelPath = File.Exists(probe) ? probe : (File.Exists(best) ? best : null);
            if (modelPath is null)
            {
                Console.WriteLine("  缺少模型(conv_verify.onnx / Models\\best.onnx)，无法推理。");
                return 1;
            }
            Console.WriteLine($"  模型: {modelPath}（{(File.Exists(best) && modelPath != probe ? "best.onnx 全模型" : "最小 Conv 探针")}）");

            try
            {
                using var options = CreateCudaSessionOptions();
                using var session = new InferenceSession(modelPath, options);
                Console.WriteLine("  CUDA 会话创建成功。");

                // 探针输入形状固定 [1,3,1280,992]（与生产分割首层一致）；best.onnx 输入以实际元数据为准
                long[] dims;
                if (modelPath == probe)
                {
                    dims = new long[] { 1, 3, 1280, 992 };
                }
                else
                {
                    dims = session.InputMetadata.Values.First().Dimensions!.Select(d => (long)d).ToArray();
                }
                long count = 1;
                foreach (var d in dims) count *= d;
                var input = new float[count];
                var name = session.InputMetadata.Keys.First();
                using var tensor = OrtValue.CreateTensorValueFromMemory(input, dims);
                var inputs = new Dictionary<string, OrtValue> { [name] = tensor };

                using var output = session.Run(new RunOptions(), inputs, session.OutputNames);
                Console.WriteLine("  推理 Run 成功完成，无 CUDNN 3009 —— cuDNN 集合修复生效。");
                return 0;
            }
            catch (OnnxRuntimeException ex)
            {
                var msg = ex.Message;
                if (msg.Contains("3009") || msg.Contains("SUBLIBRARY") || msg.Contains("CUDNN_FE"))
                {
                    Fail($"推理抛 cuDNN 错误（3009/SUBLIBRARY/CUDNN_FE）：{FirstLine(msg)}");
                    return 1;
                }

                Console.WriteLine($"  推理异常（非 3009 类，按驱动/环境问题处理）：{FirstLine(msg)}");
                Console.WriteLine("  若信息为 CUDA driver 初始化失败/找不到 driver，说明本机驱动不可用，");
                Console.WriteLine("  请在带可用 NVIDIA 驱动的干净机器上复跑以获得最终判定。");
                return 2;
            }
        }

        private static SessionOptions CreateCudaSessionOptions()
        {
            var providerOptions = new Dictionary<string, string>
            {
                ["device_id"] = "0",
                ["gpu_mem_limit"] = (2048L * 1024 * 1024).ToString(),
                ["arena_extend_strategy"] = "kSameAsRequested",
                ["cudnn_conv_algo_search"] = "HEURISTIC",
                ["cudnn_conv_use_max_workspace"] = "0",
                ["do_copy_in_default_stream"] = "1",
            };
            var options = new SessionOptions();
            using var cudaOptions = new OrtCUDAProviderOptions();
            cudaOptions.UpdateOptions(providerOptions);
            options.AppendExecutionProvider_CUDA(cudaOptions);
            return options;
        }

        private static string FirstLine(string s)
        {
            var i = s.IndexOfAny(['\r', '\n']);
            return i >= 0 ? s[..i] : s;
        }

        private static void Fail(string msg)
        {
            _failCount++;
            Console.WriteLine($"  [FAIL] {msg}");
        }
    }
}
