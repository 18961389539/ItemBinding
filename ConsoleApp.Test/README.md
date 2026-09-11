# ConsoleApp.Test - YOLO 测试控制台应用

本项目为基于 YoloSharp 的 YOLO 对象检测与分割模型测试控制台程序。用于加载 ONNX 模型、处理目录内图片并统计推理性能，也可独立评估 NMS 热路径。

## 项目概述

该应用用于批量测试 YOLO 模型，支持从指定目录读取图片，对每张图片执行预处理和推理，并记录每张图片的推理耗时、平均耗时与总耗时，便于性能评估与调试。

## 功能

- 使用 YoloSharp 执行 YOLO 模型推理
- 支持图片加载、可选的 EXIF 自动旋转和预处理
- 记录并输出单张、平均与总推理耗时
- 批量处理目录内所有图片
- 支持从文件路径、流或内存缓冲区加载模型与图片
- 支持独立运行 `nms` benchmark，比对旧版与优化版 NMS 的耗时

## 先决条件

- .NET 8 SDK（项目目标为 .NET 8）
- 引用 YoloSharp 库（源码中已包含项目引用）
- ONNX 模型文件（例如 `best.onnx`）
- 用于测试的图片目录

## 项目结构（示例）

```
ConsoleApp.Test/
├── Program.cs                # 主程序入口
├── ConsoleApp.Test.csproj    # 项目配置
├── best.onnx                 # 示例 ONNX 模型（请替换为你的模型）
└── README.md                 # 本说明文档
```

## 依赖项

- YoloSharp（项目内或通过 NuGet 引用）
- Microsoft.ML.OnnxRuntime
- 可选：OpenCvSharp（若在程序中使用）

## 使用说明

1. 将你的 ONNX 模型文件放到项目可访问位置（例如项目根目录）并命名为 `best.onnx` 或修改程序中使用的路径。
2. 在 `Program.cs` 中配置图片目录路径，例如：

```csharp
var directory = @"D:\你的\图片\目录";
```

3. 运行程序：

```bash
dotnet run --project ConsoleApp.Test\ConsoleApp.Test.csproj
```

常用 benchmark 命令：

```bash
dotnet run --project ConsoleApp.Test\ConsoleApp.Test.csproj -- benchmark detect --iterations 20 --warmup 5 --count 10
dotnet run --project ConsoleApp.Test\ConsoleApp.Test.csproj -- benchmark segment --iterations 20 --warmup 5 --count 10
dotnet run --project ConsoleApp.Test\ConsoleApp.Test.csproj -- benchmark nms --iterations 100 --warmup 20 --nms-count 1024 --nms-labels 8
```

`detect` / `segment` 模式执行流程：

1. 加载 ONNX 模型
2. 读取指定目录下的图片文件
3. 对图片进行必要的预处理（如调整大小、通道归一化、EXIF 方向校正等）
4. 调用 YoloSharp 进行推理
5. 输出每张图片的推理时间，最后输出平均和总耗时

`nms` 模式不依赖模型和图片目录，会生成稳定的候选框数据，对比旧实现和当前实现的 NMS 性能。

## 输出示例

每张图片的推理时间（毫秒），及最终平均与总耗时，例如：

```
45
52
38
...
Average: 47.5
Total: 950
```

## 模型与图片要求

- 模型格式：ONNX
- 模型需兼容 YoloSharp（任务类型：检测/分割/姿态/分类）
- 图片格式：常见格式（JPEG、PNG 等）
- 程序中可能对图片进行统一缩放，具体尺寸可在代码中调整

## 性能注意事项

- 推理耗时受硬件（CPU/GPU）、模型复杂度以及输入图片大小影响
- 若需 GPU 加速，请确保 ONNX Runtime 已启用相应提供器（例如 CUDA）
- 预处理时间包含在总耗时内

## 常见问题与排查

1. 模型未找到：确认 `best.onnx` 文件存在且路径正确
2. 图片目录未找到：检查 `Program.cs` 中的目录路径是否正确
3. 依赖错误：运行 `dotnet restore` 以恢复 NuGet 包
4. `nms` benchmark 结果异常：检查传入的 `--nms-count` 和 `--nms-labels` 是否过小，过小会让 NMS 本身几乎没有工作量

### 构建与清理

构建：

```bash
dotnet build
```

清理：

```bash
dotnet clean
```

## 示例代码片段（快速开始）

```csharp
using JinlongYolo.YoloSharp;
using SixLabors.ImageSharp;

using var predictor = new YoloPredictor("best.onnx");

var config = new YoloConfiguration
{
    Confidence = 0.25f,
    IoU = 0.45f,
    ApplyAutoOrient = true,
};

var result = predictor.Detect("test.jpg", config);
Console.WriteLine($"Image size: {result.ImageSize}, Time: {result.Speed}");
foreach (var d in result)
{
    Console.WriteLine($"{d.Name.Name} {d.Confidence:P1} Bounds: {d.Bounds}");
}
```

## 贡献与许可

此示例项目为更大系统的一部分。如需贡献或报告问题，请联系维护者或在上游仓库提交 PR/Issue。

---

*最后更新：2026-03-17*
