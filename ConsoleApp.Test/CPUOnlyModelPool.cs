//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading;
//using System.Threading.Tasks;
//using JinlongYolo.YoloSharp;

//namespace ConsoleApp.Test
//{
//    /// <summary>
//    /// 线程安全的 CPU 专用 YOLO 模型池。
//    /// 使用 JinlongYolo.YoloSharp.YoloPredictor 进行推理。
//    /// 支持阻塞获取、异步初始化。
//    /// </summary>
//    public class CPUOnlyModelPool : IDisposable
//    {
//        // 使用 BlockingCollection 支持阻塞等待和超时
//        private readonly BlockingCollection<YoloPredictor> _models;

//        // 追踪借出的模型，防止重复归还或归还非本池创建的对象。
//        private readonly ConcurrentDictionary<YoloPredictor, byte> _leasedModels = new ConcurrentDictionary<YoloPredictor, byte>();

//        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
//        private bool _disposed = false;
//        private readonly object _disposeLock = new object();

//        // 私有构造函数，强制使用 CreateAsync
//        private CPUOnlyModelPool(int cpuCount)
//        {
//            _models = new BlockingCollection<YoloPredictor>(new ConcurrentQueue<YoloPredictor>(), cpuCount);
//        }

//        /// <summary>
//        /// 异步创建并初始化模型池。
//        /// 使用并发加载来显著减少启动时间。
//        /// </summary>
//        public static async Task<CPUOnlyModelPool> CreateAsync(string modelPath, int cpuCount = 2)
//        {
//            if (string.IsNullOrEmpty(modelPath)) throw new ArgumentNullException(nameof(modelPath));
//            if (cpuCount <= 0) throw new ArgumentOutOfRangeException(nameof(cpuCount), "Count must be positive");

//            var pool = new CPUOnlyModelPool(cpuCount);

//            try
//            {
//                // 并发加载所有模型，比串行循环更快
//                var tasks = new List<Task>();

//                for (int i = 0; i < cpuCount; i++)
//                {
//                    tasks.Add(Task.Run(() => 
//                    {
//                        var predictor = new YoloPredictor(modelPath);
//                        pool._models.Add(predictor);
//                    }));
//                }

//                await Task.WhenAll(tasks);
//                return pool;
//            }
//            catch (Exception)
//            {
//                // 如果初始化失败，清理已创建的资源
//                pool.Dispose();
//                throw;
//            }
//        }

//        /// <summary>
//        /// 从池中获取模型。如果暂时无可用模型，将阻塞等待直到超时。
//        /// </summary>
//        /// <param name="timeoutMs">超时时间（毫秒），-1表示无限等待</param>
//        /// <returns>模型实例</returns>
//        /// <exception cref="TimeoutException">超时抛出</exception>
//        /// <exception cref="ObjectDisposedException">池已销毁</exception>
//        public YoloPredictor AcquireModel(int timeoutMs = -1)
//        {
//            if (_disposed) throw new ObjectDisposedException(nameof(CPUOnlyModelPool));

//            YoloPredictor? model = null;

//            try
//            {
//                // TryTake 会阻塞直到有可用项或超时
//                if (_models.TryTake(out model, timeoutMs, _cts.Token))
//                {
//                    // 记录借出状态
//                    if (!_leasedModels.TryAdd(model, 0))
//                    {
//                        // 理论上不应发生，除非内部逻辑错误
//                        // 如果添加失败，说明模型虽然刚拿出来但已在借出列表（不可能）
//                        // 为安全起见，归还模型并抛错
//                        _models.Add(model);
//                        throw new InvalidOperationException("Internal state error: Model already leased.");
//                    }
//                    return model;
//                }
//                else
//                {
//                    throw new TimeoutException($"Timed out waiting for CPU model after {timeoutMs}ms");
//                }
//            }
//            catch (OperationCanceledException)
//            {
//                throw new ObjectDisposedException(nameof(CPUOnlyModelPool));
//            }
//        }

//        /// <summary>
//        /// 将模型归还回池中。
//        /// </summary>
//        /// <param name="model">要归还的模型</param>
//        public void ReleaseModel(YoloPredictor model)
//        {
//            if (_disposed) return; // 如果池已销毁，直接忽略（Dispose方法会负责清理）
//            if (model == null) throw new ArgumentNullException(nameof(model));

//            // 1. 验证模型是否属于该池
//            if (_leasedModels.TryRemove(model, out _))
//            {
//                // 2. 归还到对应的队列
//                try
//                {
//                    if (!_models.IsAddingCompleted)
//                    {
//                        _models.Add(model);
//                    }
//                    else
//                    {
//                        // 正在 Dispose 中，直接销毁模型
//                        model.Dispose();
//                    }
//                }
//                catch (InvalidOperationException)
//                {
//                    // 极少数情况下，AddingCompleted 在检查后被设置
//                    model.Dispose();
//                }
//            }
//            else
//            {
//                // 模型不在借出列表中：可能是重复归还，或者归还了不属于此池的对象
//                throw new InvalidOperationException("Attempted to release a model that is not currently leased by this pool. Check for double-return.");
//            }
//        }

//        /// <summary>
//        /// 获取池统计信息。
//        /// </summary>
//        /// <returns>可用模型数量，借出模型数量</returns>
//        public (int available, int leased) GetPoolStats()
//        {
//            return (_models.Count, _leasedModels.Count);
//        }

//        public void Dispose()
//        {
//            if (_disposed) return;

//            lock (_disposeLock)
//            {
//                if (_disposed) return;
//                _disposed = true;
//                _cts.Cancel(); // 取消正在阻塞等待的 Acquire 请求
//            }

//            // 1. 标记集合停止添加
//            _models.CompleteAdding();

//            // 2. 清理池中剩余的模型
//            foreach (var model in _models) model.Dispose();

//            // 3. 清理正在被借出的模型
//            foreach (var kvp in _leasedModels)
//            {
//                kvp.Key.Dispose();
//            }
//            _leasedModels.Clear();

//            _models.Dispose();
//            _cts.Dispose();
//        }
//    }
//}
