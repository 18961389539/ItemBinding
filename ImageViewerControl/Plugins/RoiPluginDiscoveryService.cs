using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using ImageViewer.Abstractions;

namespace ImageViewer.Plugins
{
    public static class RoiPluginDiscoveryService
    {
        public static void DiscoverAndRegister(RoiPluginRegistry? registry = null, string? directoryPath = null, IImageViewerLogger? logger = null)
        {
            ArgumentNullException.ThrowIfNull(registry);
            var options = new RoiPluginDiscoveryOptions { PluginDirectoryPath = directoryPath };
            DiscoverAndRegister(options, registry, logger);
        }

        public static void DiscoverAndRegister(RoiPluginDiscoveryOptions? options, RoiPluginRegistry? registry = null, IImageViewerLogger? logger = null)
        {
            var targetRegistry = registry ?? throw new ArgumentNullException(nameof(registry));
            var effectiveOptions = options ?? new RoiPluginDiscoveryOptions();
            string scanDirectory = effectiveOptions.ResolvePluginDirectory(AppContext.BaseDirectory);
            RegisterFromAssemblies(LoadAssemblies(scanDirectory, effectiveOptions, logger), targetRegistry, effectiveOptions, logger);
        }

        public static void RegisterFromAssemblies(IEnumerable<Assembly> assemblies, RoiPluginRegistry? registry = null, RoiPluginDiscoveryOptions? options = null, IImageViewerLogger? logger = null)
        {
            ArgumentNullException.ThrowIfNull(assemblies);

            var targetRegistry = registry ?? throw new ArgumentNullException(nameof(registry));
            var effectiveOptions = options ?? new RoiPluginDiscoveryOptions();
            var disabledModuleTypeNames = new HashSet<string>(effectiveOptions.DisabledModuleTypeNames, StringComparer.OrdinalIgnoreCase);

            var moduleTypes = assemblies
                .Distinct()
                .SelectMany(GetLoadableTypes)
                .Where(type =>
                    type is { IsAbstract: false, IsInterface: false } &&
                    typeof(IRoiPluginModule).IsAssignableFrom(type) &&
                    type.GetConstructor(Type.EmptyTypes) != null &&
                    !disabledModuleTypeNames.Contains(type.FullName ?? type.Name))
                .OrderBy(type => type.FullName, StringComparer.Ordinal);

            foreach (var moduleType in moduleTypes)
            {
                var beforeKeys = new HashSet<string>(targetRegistry.RegisteredTypeKeys, StringComparer.OrdinalIgnoreCase);
                var module = (IRoiPluginModule)Activator.CreateInstance(moduleType)!;
                logger?.LogInfo($"Registering ROI plugin module '{moduleType.FullName}'.");
                module.Register(targetRegistry);
                ApplyPluginFilters(targetRegistry, effectiveOptions, beforeKeys);
            }

            ApplyPluginFilters(targetRegistry, effectiveOptions, null);
        }

        private static void ApplyPluginFilters(RoiPluginRegistry registry, RoiPluginDiscoveryOptions options, HashSet<string>? registeredBeforeModule)
        {
            var disabledKeys = new HashSet<string>(options.DisabledPluginTypeKeys, StringComparer.OrdinalIgnoreCase);
            disabledKeys.UnionWith(options.UnloadedPluginTypeKeys);

            if (disabledKeys.Count == 0)
            {
                return;
            }

            string[] keysToInspect = registeredBeforeModule == null
                ? registry.RegisteredTypeKeys.ToArray()
                : registry.RegisteredTypeKeys.Where(key => !registeredBeforeModule.Contains(key)).ToArray();

            foreach (var typeKey in keysToInspect)
            {
                if (disabledKeys.Contains(typeKey))
                {
                    registry.Unregister(typeKey);
                }
            }
        }

        private static IEnumerable<Assembly> LoadAssemblies(string directoryPath, RoiPluginDiscoveryOptions options, IImageViewerLogger? logger)
        {
            var disabledAssemblyNames = new HashSet<string>(options.DisabledAssemblyNames, StringComparer.OrdinalIgnoreCase);
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic)
                .ToDictionary(assembly => assembly.FullName ?? assembly.GetName().Name ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            foreach (var assembly in loadedAssemblies.Values)
            {
                yield return assembly;
            }

            if (!Directory.Exists(directoryPath))
            {
                yield break;
            }

            foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*.dll", SearchOption.TopDirectoryOnly))
            {
                AssemblyName assemblyName;
                try
                {
                    assemblyName = AssemblyName.GetAssemblyName(filePath);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning($"Skipping non-.NET assembly '{filePath}': {ex.Message}");
                    continue;
                }

                string simpleName = assemblyName.Name ?? Path.GetFileNameWithoutExtension(filePath);
                if (disabledAssemblyNames.Contains(simpleName))
                {
                    continue;
                }

                if (loadedAssemblies.Values.Any(assembly => AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), assemblyName)))
                {
                    continue;
                }

                Assembly? assembly = null;
                try
                {
                    assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(filePath);
                }
                catch (Exception ex)
                {
                    logger?.LogError($"Failed to load plugin assembly '{filePath}'.", ex);
                }

                if (assembly != null)
                {
                    loadedAssemblies[assembly.FullName ?? assembly.GetName().Name ?? filePath] = assembly;
                    yield return assembly;
                }
            }
        }

        private static Type[] GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.OfType<Type>().ToArray();
            }
        }
    }
}
