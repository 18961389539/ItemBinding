using System;
using ImageViewer.Abstractions;
using ImageViewer.Dialogs;
using ImageViewer.Plugins;
using ImageViewer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImageViewer.Controls
{
    public static class ImageViewerServiceCollectionExtensions
    {
        public static IServiceCollection AddImageViewerRuntimeServices(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAdd(ServiceDescriptor.Singleton<RoiPluginRegistry>(static _ => ImageViewerPluginRegistryBootstrap.CreateDefault()));
            services.TryAdd(ServiceDescriptor.Singleton<IImageViewerSessionStoragePolicy>(static _ => new LocalAppDataImageViewerSessionStoragePolicy()));
            services.TryAdd(ServiceDescriptor.Singleton<IImageViewerDialogService, ImageViewerDialogService>());
            services.TryAdd(ServiceDescriptor.Singleton<IImageViewerFileDialogService, ImageViewerFileDialogService>());
            services.TryAdd(ServiceDescriptor.Singleton<IImageViewerLogger, TraceImageViewerLogger>());
            services.TryAdd(ServiceDescriptor.Singleton<IImageViewerViewportService, ImageViewerViewportService>());
            services.TryAdd(ServiceDescriptor.Singleton<IImageViewerSessionService, ImageViewerSessionService>());
            services.TryAdd(ServiceDescriptor.Singleton<IImageViewerRecentProjectService, ImageViewerRecentProjectService>());
            services.TryAdd(ServiceDescriptor.Singleton<IImageViewerProjectPackageService>(static serviceProvider =>
                new ImageViewerProjectPackageService(
                    serviceProvider.GetRequiredService<IImageViewerSessionService>(),
                    serviceProvider.GetRequiredService<IImageViewerSessionStoragePolicy>())));
            services.TryAdd(ServiceDescriptor.Singleton<IImageViewerRenderService, ImageViewerRenderService>());
            services.TryAdd(ServiceDescriptor.Singleton<ISelectedRoiDetectionService>(static _ => SelectedRoiDetectionService.Default));
            services.TryAdd(ServiceDescriptor.Singleton<ImageViewerRuntimeServices>(static serviceProvider =>
                ImageViewerHostDefaults.CreateRuntimeServices(serviceProvider)));

            return services;
        }

        public static IServiceCollection AddImageViewerHostServices(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAdd(ServiceDescriptor.Singleton<IImageViewerDispatcherTimerFactory, WpfImageViewerDispatcherTimerFactory>());
            services.TryAdd(ServiceDescriptor.Singleton<IImageViewerRefreshSchedulerFactory>(static serviceProvider =>
                new DispatcherImageViewerRefreshSchedulerFactory(serviceProvider.GetRequiredService<IImageViewerDispatcherTimerFactory>())));
            services.TryAdd(ServiceDescriptor.Singleton<IImageViewerLatestTaskSchedulerFactory, LatestImageViewerTaskSchedulerFactory>());
            services.TryAdd(ServiceDescriptor.Singleton<IImageViewerPeriodicTaskSchedulerFactory>(static serviceProvider =>
                new DispatcherImageViewerPeriodicTaskSchedulerFactory(serviceProvider.GetRequiredService<IImageViewerDispatcherTimerFactory>())));
            services.TryAdd(ServiceDescriptor.Singleton<IImageViewerAnalysisDiagnostics, LoggerImageViewerAnalysisDiagnostics>());
            services.TryAdd(ServiceDescriptor.Singleton<ImageViewerHostServices>(static serviceProvider =>
                ImageViewerHostDefaults.CreateHostServices(serviceProvider)));

            return services;
        }

        public static IServiceCollection AddImageViewerHost(this IServiceCollection services, Action<ImageViewerHostBuilder>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddImageViewerRuntimeServices();
            services.AddImageViewerHostServices();
            services.TryAdd(ServiceDescriptor.Transient<ImageViewerHost>(serviceProvider =>
            {
                var builder = new ImageViewerHostBuilder().UseServiceProvider(serviceProvider);
                configure?.Invoke(builder);
                return builder.Build();
            }));
            services.TryAdd(ServiceDescriptor.Transient<ImageViewer>(static serviceProvider =>
                serviceProvider.GetRequiredService<ImageViewerHost>().CreateViewer()));

            return services;
        }
    }
}