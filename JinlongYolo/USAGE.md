使用示例 (中文)

以下示例展示如何使用 JinlongYolo 库加载模型并对图片执行检测。

示例 1：从模型文件创建预测器并检测图片

```csharp
using JinlongYolo.YoloSharp;
using SixLabors.ImageSharp;

// 创建预测器（自动使用默认配置）
using var predictor = new YoloPredictor("model.onnx");

// 可选：自定义配置
var config = new YoloConfiguration
{
    Confidence = 0.25f, // 最小置信度
    IoU = 0.45f,
    KeepAspectRatio = true,
    ApplyAutoOrient = true,
};

// 运行检测（基于文件路径）
var result = predictor.Detect("test.jpg", config);

Console.WriteLine($"Image size: {result.ImageSize}");
Console.WriteLine($"Speed: {result.Speed}");

// 遍历检测结果
foreach (var det in result)
{
    Console.WriteLine($"{det.Name.Name} {det.Confidence:P1} Bounds: {det.Bounds}");
}
```

示例 2：从内存模型或字节流创建预测器

```csharp
// 假设 modelBytes 是从文件或网络读取的字节数组
using var predictor = new YoloPredictor(modelBytes);
var detectResult = predictor.Detect(imageBuffer);
```

Notes (English)

- Use `YoloPredictor` to load ONNX models and run inference.
- Methods like `Detect`, `Pose`, `Segment`, `Classify` are provided as extension methods and accept file path, stream, buffer or `Image<Rgb24>`.
- Configure behavior using `YoloConfiguration`.
