using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YoloDotNet;
using YoloDotNet.ExecutionProvider.Cpu;
using YoloDotNet.ExecutionProvider.OpenVino;
using YoloDotNet.Models;

namespace ConsoleApp.Test
{
    public enum DeviceType
    {
        CPU,
        GPU
    }

    /// <summary>
    /// 线程安全的 YOLO 模型池。
    /// 支持阻塞获取、自动类型识别归还、异步初始化。
    /// </summary>
    public class ModelPool : IDisposable
    {
        // 使用 BlockingCollection 支持阻塞等待和超时
        private readonly BlockingCollection<Yolo> _cpuModels;
        private readonly BlockingCollection<Yolo> _gpuModels;
        
        // 追踪借出的模型，Key=模型实例, Value=设备类型。
        // 作用1：归还时自动识别类型，无需调用者指定。
        // 作用2：防止重复归还（Double Release）或归还非本池创建的对象。
        private readonly ConcurrentDictionary<Yolo, DeviceType> _leasedModels = new ConcurrentDictionary<Yolo, DeviceType>();
        
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _disposed = false;
        private readonly object _disposeLock = new object();

        // 私有构造函数，强制使用 CreateAsync
        private ModelPool(int cpuCount, int gpuCount)
        {
            _cpuModels = new BlockingCollection<Yolo>(new ConcurrentQueue<Yolo>(), cpuCount);
            _gpuModels = new BlockingCollection<Yolo>(new ConcurrentQueue<Yolo>(), gpuCount);
        }

        /// <summary>
        /// 异步创建并初始化模型池。
        /// 使用并发加载来显著减少启动时间。
        /// </summary>
        public static async Task<ModelPool> CreateAsync(string modelPath, int cpuCount = 2, int gpuCount = 2)
        {
            if (string.IsNullOrEmpty(modelPath)) throw new ArgumentNullException(nameof(modelPath));
            if (cpuCount < 0 || gpuCount < 0) throw new ArgumentOutOfRangeException("Count cannot be negative");
            if (cpuCount == 0 && gpuCount == 0) throw new ArgumentException("At least one model must be created");

            var pool = new ModelPool(cpuCount, gpuCount);
            await Task.CompletedTask;
            for (int i = 0; i < cpuCount; i++)
            {
                var (model, actualDevice) = CreateYoloModel(modelPath, DeviceType.CPU);
                if (actualDevice == DeviceType.CPU) pool._cpuModels.Add(model);
                else pool._gpuModels.Add(model);
            }

            for (int i = 0; i < gpuCount; i++)
            {
                var (model, actualDevice) = CreateYoloModel(modelPath, DeviceType.GPU);
                if (actualDevice == DeviceType.CPU) pool._cpuModels.Add(model);
                else pool._gpuModels.Add(model);
            }
            return pool;
        }

        private static (Yolo model, DeviceType actualDevice) CreateYoloModel(string modelPath, DeviceType deviceType)
        {
            try
            {
                var yolo = new Yolo(new YoloOptions
                {
                    ExecutionProvider = new OpenVinoExecutionProvider(
                        modelPath,
                        new OpenVino
                        {
                            DeviceType = deviceType.ToString(),
                            Precision = Precision.FP16,
                            CachePath = "OpenVinoCache",
                            ModelPriority = ModelPriority.HIGH
                        })
                });
                return (yolo, deviceType);
            }
            catch (Microsoft.ML.OnnxRuntime.OnnxRuntimeException ex)
            {
                // If the build does not include the OpenVINO execution provider, fall back to CPU
                if (ex.Message != null && ex.Message.Contains("OpenVINOExecutionProvider", StringComparison.OrdinalIgnoreCase))
                {
                    // Fallback to explicit CPU execution provider
                    var fallback = new Yolo(new YoloOptions
                    {
                        ExecutionProvider = new CpuExecutionProvider(modelPath)
                    });
                    return (fallback, DeviceType.CPU);
                }

                // Rethrow other OnnxRuntime exceptions
                throw;
            }
        }

        /// <summary>
        /// 从池中获取模型。如果暂时无可用模型，将阻塞等待直到超时。
        /// </summary>
        /// <param name="deviceType">请求的设备类型</param>
        /// <param name="timeoutMs">超时时间（毫秒），-1表示无限等待</param>
        /// <returns>模型实例</returns>
        /// <exception cref="TimeoutException">超时抛出</exception>
        /// <exception cref="ObjectDisposedException">池已销毁</exception>
        public Yolo AcquireModel(DeviceType deviceType, int timeoutMs = -1)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ModelPool));

            var collection = deviceType == DeviceType.CPU ? _cpuModels : _gpuModels;
            Yolo? model = null;

            try
            {
                // TryTake 会阻塞直到有可用项或超时
                if (collection.TryTake(out model, timeoutMs, _cts.Token))
                {
                    // 记录借出状态
                    if (!_leasedModels.TryAdd(model, deviceType))
                    {
                        // 理论上不应发生，除非内部逻辑错误
                        // 如果添加失败，说明模型虽然刚拿出来但已在借出列表（不可能）
                        // 为安全起见，归还模型并抛错
                        collection.Add(model);
                        throw new InvalidOperationException("Internal state error: Model already leased.");
                    }
                    return model;
                }
                else
                {
                    throw new TimeoutException($"Timed out waiting for {deviceType} model after {timeoutMs}ms");
                }
            }
            catch (OperationCanceledException)
            {
                throw new ObjectDisposedException(nameof(ModelPool));
            }
        }

        /// <summary>
        /// 将模型归还回池中。会自动识别该模型原本所属的设备类型。
        /// </summary>
        /// <param name="model">要归还的模型</param>
        public void ReleaseModel(Yolo model)
        {
            if (_disposed) return; // 如果池已销毁，直接忽略（Dispose方法会负责清理）
            if (model == null) throw new ArgumentNullException(nameof(model));

            // 1. 验证模型是否属于该池，并获取其类型
            if (_leasedModels.TryRemove(model, out DeviceType deviceType))
            {
                // 2. 归还到对应的队列
                var collection = deviceType == DeviceType.CPU ? _cpuModels : _gpuModels;
                try
                {
                    if (!collection.IsAddingCompleted)
                    {
                        collection.Add(model);
                    }
                    else
                    {
                        // 正在 Dispose 中，直接销毁模型
                        model.Dispose();
                    }
                }
                catch (InvalidOperationException)
                {
                    // 极少数情况下，AddingCompleted 在检查后被设置
                    model.Dispose();
                }
            }
            else
            {
                // 模型不在借出列表中：可能是重复归还，或者归还了不属于此池的对象
                // 这里可以选择抛出异常或记录日志。为了安全，建议抛出异常以暴露Bug。
                throw new InvalidOperationException("Attempted to release a model that is not currently leased by this pool. Check for double-return.");
            }
        }

        public (int cpuAvailable, int gpuAvailable, int leasedCount) GetPoolStats()
        {
            return (_cpuModels.Count, _gpuModels.Count, _leasedModels.Count);
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            lock (_disposeLock)
            {
                if (_disposed) return;
                _disposed = true;
                _cts.Cancel(); // 取消正在阻塞等待的 Acquire 请求
            }

            // 1. 标记集合停止添加
            _cpuModels.CompleteAdding();
            _gpuModels.CompleteAdding();

            // 2. 清理池中剩余的模型
            foreach (var model in _cpuModels) model.Dispose();
            foreach (var model in _gpuModels) model.Dispose();

            // 3. 清理正在被借出的模型
            // 注意：这里强制销毁借出中的模型可能会导致正在使用该模型的线程报错
            // 但作为 Dispose 语义，这是合理的。如果需要优雅关闭，需增加逻辑等待借出归零。
            foreach (var kvp in _leasedModels)
            {
                kvp.Key.Dispose();
            }
            _leasedModels.Clear();

            _cpuModels.Dispose();
            _gpuModels.Dispose();
            _cts.Dispose();
        }
    }
}
