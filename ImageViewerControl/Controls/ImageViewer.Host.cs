using System;
using ImageViewer.Abstractions;
using ImageViewer.Dialogs;
using ImageViewer.Plugins;
using ImageViewer.Services;
using ImageViewer.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ImageViewer.Controls
{
    public sealed class ImageViewerHost
    {
        public ImageViewerHost(ImageViewerDependencies dependencies)
        {
            Dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        }

        public ImageViewerDependencies Dependencies { get; }

        public ImageViewer CreateViewer()
        {
            return new ImageViewer(Dependencies);
        }

        public static ImageViewerHost CreateDefault(RoiPluginRegistry? pluginRegistry = null)
        {
            return new ImageViewerHostBuilder()
                .UsePluginRegistry(ImageViewerPluginRegistryBootstrap.Resolve(pluginRegistry))
                .Build();
        }
    }

    public sealed class ImageViewerHostBuilder
    {
        private RoiPluginRegistry? _pluginRegistry;
        private ImageViewerRuntimeServices? _runtimeServices;
        private ImageViewerHostServices? _hostServices;
        private ImageViewerCompositionHooks? _compositionHooks;

        public ImageViewerHostBuilder UsePluginRegistry(RoiPluginRegistry pluginRegistry)
        {
            _pluginRegistry = pluginRegistry ?? throw new ArgumentNullException(nameof(pluginRegistry));
            return this;
        }

        public ImageViewerHostBuilder UseRuntimeServices(ImageViewerRuntimeServices runtimeServices)
        {
            _runtimeServices = runtimeServices ?? throw new ArgumentNullException(nameof(runtimeServices));
            return this;
        }

        public ImageViewerHostBuilder UseHostServices(ImageViewerHostServices hostServices)
        {
            _hostServices = hostServices ?? throw new ArgumentNullException(nameof(hostServices));
            return this;
        }

        public ImageViewerHostBuilder UseServiceProvider(IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider);

            _pluginRegistry = ImageViewerPluginRegistryBootstrap.Resolve(serviceProvider);
            _runtimeServices = serviceProvider.GetService<ImageViewerRuntimeServices>()
                ?? ImageViewerHostDefaults.CreateRuntimeServices(serviceProvider);
            _hostServices = serviceProvider.GetService<ImageViewerHostServices>()
                ?? ImageViewerHostDefaults.CreateHostServices(serviceProvider);
            return this;
        }

        internal ImageViewerHostBuilder UseCompositionHooks(ImageViewerCompositionHooks compositionHooks)
        {
            _compositionHooks = compositionHooks ?? throw new ArgumentNullException(nameof(compositionHooks));
            return this;
        }

        public ImageViewerHost Build()
        {
            RoiPluginRegistry pluginRegistry = ImageViewerPluginRegistryBootstrap.Resolve(_pluginRegistry);
            ImageViewerHostServices hostServices = _hostServices ?? ImageViewerHostDefaults.CreateHostServices();
            ImageViewerRuntimeServices runtimeServices = _runtimeServices ?? ImageViewerHostDefaults.CreateRuntimeServices(hostServices.SessionStoragePolicy);
            ImageViewerDependencies dependencies = _compositionHooks is null
                ? new ImageViewerDependencies(pluginRegistry, runtimeServices, hostServices)
                : new ImageViewerDependencies(pluginRegistry, runtimeServices, hostServices, _compositionHooks);

            return new ImageViewerHost(dependencies);
        }
    }

    internal sealed class ImageViewerHostState
    {
        private readonly ImageViewerPluginRuntimeState _pluginRuntimeState;

        public ImageViewerHostState(
            RoiPluginRegistry pluginRegistry,
            ImageViewerRuntimeServices runtimeServices,
            ImageViewerHostServices hostServices)
        {
            ArgumentNullException.ThrowIfNull(pluginRegistry);
            RuntimeServices = runtimeServices ?? throw new ArgumentNullException(nameof(runtimeServices));
            HostServices = hostServices ?? throw new ArgumentNullException(nameof(hostServices));
            _pluginRuntimeState = new ImageViewerPluginRuntimeState(pluginRegistry, RuntimeServices.SelectedRoiDetectionService);
        }

        public RoiPluginRegistry PluginRegistry
        {
            get => _pluginRuntimeState.PluginRegistry;
            set => _pluginRuntimeState.PluginRegistry = value;
        }

        public ImageViewerRuntimeServices RuntimeServices { get; }

        public ImageViewerHostServices HostServices { get; }

        public ImageViewerViewModel ViewModel => _pluginRuntimeState.ViewModel;

        public RoiInteractionService RoiInteraction => _pluginRuntimeState.RoiInteraction;

        public Rendering.RoiRenderService RoiRenderer => _pluginRuntimeState.RoiRenderer;
    }

    internal static class ImageViewerHostDefaults
    {
        public static ImageViewerRuntimeServices CreateRuntimeServices(IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider);

            return new ImageViewerRuntimeServices(
                serviceProvider.GetRequiredService<IImageViewerDialogService>(),
                serviceProvider.GetRequiredService<IImageViewerFileDialogService>(),
                serviceProvider.GetRequiredService<IImageViewerLogger>(),
                serviceProvider.GetRequiredService<IImageViewerViewportService>(),
                serviceProvider.GetRequiredService<IImageViewerSessionService>(),
                serviceProvider.GetRequiredService<IImageViewerRecentProjectService>(),
                serviceProvider.GetRequiredService<IImageViewerProjectPackageService>(),
                serviceProvider.GetRequiredService<IImageViewerRenderService>(),
                serviceProvider.GetRequiredService<ISelectedRoiDetectionService>());
        }

        public static ImageViewerRuntimeServices CreateRuntimeServices(IImageViewerSessionStoragePolicy sessionStoragePolicy)
        {
            ArgumentNullException.ThrowIfNull(sessionStoragePolicy);

            var sessionService = new ImageViewerSessionService();
            return new ImageViewerRuntimeServices(
                new ImageViewerDialogService(),
                new ImageViewerFileDialogService(),
                new TraceImageViewerLogger(),
                new ImageViewerViewportService(),
                sessionService,
                new ImageViewerRecentProjectService(),
                new ImageViewerProjectPackageService(sessionService, sessionStoragePolicy),
                new ImageViewerRenderService(),
                SelectedRoiDetectionService.Default);
        }

        public static ImageViewerHostServices CreateHostServices(IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider);

            return new ImageViewerHostServices(
                serviceProvider.GetRequiredService<IImageViewerDispatcherTimerFactory>(),
                serviceProvider.GetRequiredService<IImageViewerRefreshSchedulerFactory>(),
                serviceProvider.GetRequiredService<IImageViewerLatestTaskSchedulerFactory>(),
                serviceProvider.GetRequiredService<IImageViewerPeriodicTaskSchedulerFactory>(),
                serviceProvider.GetRequiredService<IImageViewerAnalysisDiagnostics>(),
                serviceProvider.GetService<IImageViewerSessionStoragePolicy>() ?? new LocalAppDataImageViewerSessionStoragePolicy());
        }

        public static ImageViewerHostServices CreateHostServices()
        {
            var timerFactory = new WpfImageViewerDispatcherTimerFactory();
            return new ImageViewerHostServices(
                timerFactory,
                new DispatcherImageViewerRefreshSchedulerFactory(timerFactory),
                new LatestImageViewerTaskSchedulerFactory(),
                new DispatcherImageViewerPeriodicTaskSchedulerFactory(timerFactory),
                new LoggerImageViewerAnalysisDiagnostics());
        }
    }
}