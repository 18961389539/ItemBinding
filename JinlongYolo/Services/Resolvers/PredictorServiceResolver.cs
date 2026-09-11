using System.Collections.Concurrent;

namespace JinlongYolo.YoloSharp.Services.Resolvers;

internal class PredictorServiceResolver : IDisposable
{
    private readonly YoloSession _yoloSession;
    private readonly YoloConfiguration _configuration;

    private readonly ServiceProvider _provider;

    // REVIEW-FIX: 改为并发安全字典；YoloConfiguration 实现了基于值的 Equals/GetHashCode，
    // 因此键天然按配置内容去重（等价配置共享同一 provider），可控制缓存无限增长。
    private readonly ConcurrentDictionary<YoloConfiguration, ServiceProvider> _providers = new();

    private bool _disposed;

    public PredictorServiceResolver(InferenceSession session, YoloConfiguration configuration)
    {
        _configuration = configuration;

        var metadata = YoloMetadata.Parse(session);
        var shapeInfo = new SessionIoShapeInfo(session, metadata);

        // Create default services
        var services = CreateDefaultServices(metadata);

        // Create yolo session
        _yoloSession = new YoloSession(metadata, session, shapeInfo);

        // Add yolo session
        services.AddSingleton(_yoloSession);
        services.AddSingleton(_configuration);

        // Build the service provider
        _provider = services.BuildServiceProvider();
    }

    public T Resolve<T>(YoloConfiguration? configuration = null) where T : notnull
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (configuration is null || _configuration.Equals(configuration))
        {
            return _provider.GetRequiredService<T>();
        }

        if (_providers.TryGetValue(configuration, out var p))
        {
            return p.GetRequiredService<T>();
        }
        else
        {
            var services = CreateDefaultServices(_yoloSession.Metadata);

            services.AddSingleton(_yoloSession);
            services.AddSingleton(configuration);

            var provider = services.BuildServiceProvider();

            // REVIEW-FIX: TryAdd 保证并发安全；若竞态失败则释放本次构建的 provider，
            // 使用已注册的那个，避免字典中缓存重复的 provider。
            if (_providers.TryAdd(configuration, provider))
            {
                return provider.GetRequiredService<T>();
            }

            provider.Dispose();
            return _providers[configuration].GetRequiredService<T>();
        }
    }

    private static ServiceCollection CreateDefaultServices(YoloMetadata metadata)
    {
        var services = new ServiceCollection();

        if (metadata is YoloPoseMetadata pose)
        {
            services.AddSingleton(pose);
        }

        services
            .AddSingleton(metadata)
            .AddSingleton<ISessionRunner, SessionRunner>()
            .AddSingleton<IMemoryAllocator, MemoryAllocator>()
            .AddSingleton<IPixelsNormalizer, PixelsNormalizer>()
            .AddSingleton<IBoundingBoxTransformer, BoundingBoxTransformer>();

        var task = metadata.Task;
        var version = metadata.Architecture;

        var obb = task == YoloTask.Obb;

        AddNonMaxSuppression(services, obb);
        AddRawBoundingBoxDecoder(services, version, obb);

        switch (task)
        {
            case YoloTask.Pose:
                services.AddSingleton<IDecoder<Pose>, PoseDecoder>();
                break;

            case YoloTask.Detect:
                services.AddSingleton<IDecoder<Detection>, DetectionDecoder>();
                break;

            case YoloTask.Obb:
                services.AddSingleton<IDecoder<ObbDetection>, ObbDetectionDecoder>();
                break;

            case YoloTask.Segment:
                services.AddSingleton<IDecoder<Segmentation>, SegmentationDecoder>();
                break;

            case YoloTask.Classify:
                services.AddSingleton<IDecoder<Classification>, ClassificationDecoder>();
                break;
        }

        return services;
    }

    private static void AddNonMaxSuppression(ServiceCollection services, bool obb)
    {
        if (obb)
        {
            services.AddSingleton<INonMaxSuppression, ObbNonMaxSuppression>();
        }
        else
        {
            services.AddSingleton<INonMaxSuppression, NonMaxSuppression>();
        }
    }

    private static void AddRawBoundingBoxDecoder(ServiceCollection services, YoloArchitecture architecture, bool obb)
    {
        if (architecture == YoloArchitecture.AnchorFree)
        {
            if (obb)
            {
                services.AddSingleton<IBoundingBoxDecoder, AnchorFreeOrientedBoxDecoder>();
            }
            else
            {

                services.AddSingleton<IBoundingBoxDecoder, AnchorFreeBoxDecoder>();
            }
        }
        // anchor based
        else
        {
            if (obb)
            {
                services.AddSingleton<IBoundingBoxDecoder, AnchorBasedOrientedBoundingBoxDecoder>();
            }
            else
            {
                services.AddSingleton<IBoundingBoxDecoder, AnchorBasedBoundingBoxDecoder>();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _provider.Dispose();

        foreach (var provider in _providers.Values)
        {
            provider.Dispose();
        }

        _disposed = true;
    }
}