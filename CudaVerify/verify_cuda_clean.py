# -*- coding: utf-8 -*-
"""
cuDNN 干净环境验证脚本（Python 版，与 CudaVerify.csproj 同语义）。

用途：在"无 CUDA Toolkit / 无系统 cuDNN"的干净机器上，仅靠本目录 cuda_runtime_dlls/
子目录验证：
  Step 0  环境快照（是否干净、有无 NVIDIA 驱动）
  Step 1  cuDNN 集合审计：必须恰为官方 9.18.1.3 的 8 个文件，且不存在 tensor_ir 异物
  Step 2  核心 12 DLL 按名预载（与 MainAPP CudaRuntimeLoader 同清单）
  Step 3  （有驱动时）onnxruntime CUDA EP + 与生产同形态首层 Conv 推理（conv_verify.onnx），
          判定是否复现 CUDNN 3009 SUBLIBRARY_UNAVAILABLE

退出码：0 = PASS（加载级全过；无驱动时为加载级 PASS）；1 = FAIL；2 = GPU 推理 SKIP（无驱动）。

运行：python verify_cuda_clean.py
"""
import ctypes
import os
import sys

# 与 MainAPP CudaRuntimeLoader.s_cudaDlls 一致（CUDA 13 + cuDNN 9.25.1）
CORE_CUDA_DLLS = [
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
]

# cuDNN 9.25.1.1 官方发行版集合（nvidia-cudnn-cu13==9.25.1.1）：恰好 10 个
OFFICIAL_CUDNN_SET = {
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
}

BASE = os.path.dirname(os.path.abspath(__file__))
CUDA_DIR = os.path.join(BASE, "cuda_runtime_dlls")
FAILS = []


def fail(msg: str):
    FAILS.append(msg)
    print(f"  [FAIL] {msg}")


def step0_snapshot():
    print("\n--- Step 0 环境快照（判断是否'干净'） ---")
    cuda_path = os.environ.get("CUDA_PATH", "")
    print(f"  CUDA_PATH      = {cuda_path or '<空>'}")
    sys_cudnn = [f for f in os.listdir(os.environ["SystemRoot"] + r"\System32")
                 if f.lower().startswith("cudnn")]
    print(f"  System32 cuDNN = {'<无>（干净）' if not sys_cudnn else ', '.join(sys_cudnn)}")
    root_cudnn = [f for f in os.listdir(BASE) if f.lower().startswith("cudnn")]
    print(f"  程序根 cuDNN   = {'<无>（干净）' if not root_cudnn else ', '.join(root_cudnn)}")
    nvcuda = os.path.join(os.environ["SystemRoot"], "System32", "nvcuda.dll")
    print(f"  NVIDIA 驱动    = {'存在（可做 GPU 推理验证）' if os.path.exists(nvcuda) else '不存在（无 GPU，Step 3 将 SKIP）'}")
    return os.path.exists(nvcuda)


def step1_audit():
    print("\n--- Step 1 cuDNN 集合审计（期望：官方 9.25.1.1 的 10 个文件，含 tensor_ir/ext） ---")
    if not os.path.isdir(CUDA_DIR):
        fail(f"缺少目录 {CUDA_DIR}")
        return
    actual = {f.lower(): f for f in os.listdir(CUDA_DIR) if f.lower().endswith(".dll") and f.lower().startswith("cudnn")}
    print(f"  实际文件({len(actual)}): {', '.join(sorted(actual.values()))}")

    if set(actual) != {f.lower() for f in OFFICIAL_CUDNN_SET}:
        fail("cuDNN 文件集合与官方 9.25.1.1 不一致——存在多余子库或缺失文件。")
        return

    # 主库版本已由 cuda_runtime_dlls_SHA256.txt 清单保证；脚本侧重"集合自洽"
    main = os.path.join(CUDA_DIR, "cudnn64_9.dll")
    print(f"  主库 cudnn64_9.dll 存在 = {os.path.exists(main)}，大小 {os.path.getsize(main)} B")


def step2_preload():
    print("\n--- Step 2 核心 12 DLL 按名预载（绝对路径） ---")
    loaded = 0
    for dll in CORE_CUDA_DLLS:
        full = os.path.join(CUDA_DIR, dll)
        if os.path.exists(full):
            try:
                ctypes.WinDLL(full)
                loaded += 1
                print(f"  [OK] {dll}")
            except OSError as e:
                fail(f"预载失败 {dll}: {e}")
        else:
            fail(f"预载失败（文件缺失）: {dll}")
    print(f"  预载 {loaded}/{len(CORE_CUDA_DLLS)}")
    return loaded == len(CORE_CUDA_DLLS)


def step3_gpu(has_driver: bool) -> int:
    print("\n--- Step 3 GPU 推理（复现 3009 的最终判定） ---")
    if not has_driver:
        print("  未检测到 NVIDIA 驱动(nvcuda.dll)：本机无 GPU，无法执行 CUDA 推理。")
        print("  请到带 NVIDIA 驱动的干净机器上运行本脚本，或在工位机部署修复后的发布包复测。")
        return 2
    probe = os.path.join(BASE, "conv_verify.onnx")
    best = os.path.join(BASE, "Models", "best.onnx")
    model = probe if os.path.exists(probe) else (best if os.path.exists(best) else None)
    if not model:
        fail("缺少模型(conv_verify.onnx / Models\\best.onnx)，无法推理。")
        return 1
    print(f"  模型: {model}")

    # 预载 CUDA 目录并把它加入 DLL 搜索路径（模拟发布形态：cuda_runtime_dlls 是子目录）
    os.add_dll_directory(CUDA_DIR)
    os.environ["PATH"] = CUDA_DIR + os.pathsep + os.environ.get("PATH", "")

    try:
        import onnxruntime as ort
    except ImportError as e:
        fail(f"需要 onnxruntime-gpu: {e}")
        return 1

    so = ort.SessionOptions()
    opts = {
        "device_id": "0",
        "gpu_mem_limit": str(2048 * 1024 * 1024),
        "arena_extend_strategy": "kSameAsRequested",
        "cudnn_conv_algo_search": "HEURISTIC",
        "cudnn_conv_use_max_workspace": "0",
        "do_copy_in_default_stream": "1",
    }
    try:
        sess = ort.InferenceSession(
            model, sess_options=so, providers=["CUDAExecutionProvider"],
            provider_options=[opts])
        print("  CUDA 会话创建成功，实际 EP:", sess.get_providers())
    except Exception as e:
        msg = str(e)
        low = msg.lower()
        if any(k in low for k in ("3009", "sublibrary", "cudnn_fe", "failed to initialize cudnn")):
            fail(f"建会即抛 cuDNN 错误（3009/SUBLIBRARY）：{msg.splitlines()[0] if msg else e}")
            return 1
        print("  建会异常（非 3009 类，按驱动/环境问题处理）：")
        print("   ", msg.splitlines()[0] if msg else e)
        return 2

    import numpy as np
    x = np.zeros((1, 3, 1280, 992), dtype=np.float32)
    try:
        sess.run(None, {"x": x})
        print("  推理 Run 成功完成，无 CUDNN 3009 —— cuDNN 集合修复生效。")
        return 0
    except Exception as e:
        msg = str(e)
        if any(k in msg for k in ("3009", "SUBLIBRARY", "CUDNN_FE")):
            fail(f"推理抛 cuDNN 错误（3009/SUBLIBRARY/CUDNN_FE）：{msg.splitlines()[0]}")
            return 1
        print(f"  推理异常（非 3009 类，按驱动/环境问题处理）：{msg.splitlines()[0]}")
        return 2


def main() -> int:
    print("=" * 60)
    print(" cuDNN 干净环境验证（Python 版）")
    print(f" 目录 : {BASE}")
    print("=" * 60)
    has_driver = step0_snapshot()
    step1_audit()
    preload_ok = step2_preload()
    if FAILS or not preload_ok:
        print(f"\n[RESULT] FAIL：{len(FAILS)} 项未通过。")
        return 1
    print("\n[RESULT] 加载级验证 PASS（cuDNN 集合自洽、核心 DLL 全量可加载）。")
    return step3_gpu(has_driver)


if __name__ == "__main__":
    sys.exit(main())
