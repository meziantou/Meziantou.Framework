using System.Collections;
using System.Collections.Immutable;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>
/// A builder for creating diagnostic snapshots of the current execution context. Use this to gather system, runtime, and process information for troubleshooting.
/// </summary>
/// <example>
/// Capture comprehensive system diagnostics:
/// <code>
/// var builder = new ContextSnapshotBuilder();
/// builder.AddDefault();
/// var snapshot = builder.BuildSnapshot();
/// 
/// // Serialize for logging
/// var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
/// </code>
/// </example>
public sealed class ContextSnapshotBuilder
{
    private readonly Dictionary<string, object?> _contextSnapshot = new(StringComparer.Ordinal);

    /// <summary>Adds a custom key-value pair to the snapshot.</summary>
    public ContextSnapshotBuilder AddValue(string key, object? value)
    {
        _contextSnapshot[key] = value;
        return this;
    }

    /// <summary>Builds and returns the snapshot as a sorted dictionary of key-value pairs.</summary>
    public IReadOnlyDictionary<string, object?> BuildSnapshot()
    {
        return _contextSnapshot.OrderBy(kvp => kvp.Key, StringComparer.Ordinal).ToImmutableSortedDictionary();
    }

    /// <summary>Adds all available diagnostic information including environment variables, GC, drives, processes, OS details, and more.</summary>
    public ContextSnapshotBuilder AddDefault()
    {
        AddEnvironmentVariables(EnvironmentVariableTarget.Process);
        AddEnvironmentVariables(EnvironmentVariableTarget.User);
        AddEnvironmentVariables(EnvironmentVariableTarget.Machine);
        AddGarbageCollector();
        AddDrives();
        AddSpecialFolderPaths();
        AddMachineName();
        AddConsole();
        AddAssemblyLoadContexts();
        AddThreadPool();
        AddCulture();
        AddCurrentDirectory();
        AddNewLine();
        AddUser();
        AddOperatingSystem();
        AddLocalTimeZone();
        AddHypervisor();
        AddPowerManagement();
        AddSecurityProviders();
        AddProcessorCount();
        AddCpu();
        AddCurrentProcess();
        AddAppContextData();
        AddRuntimeFeatures();
        return this;
    }

    /// <summary>Adds information about the current process including command line, memory usage, and processor time.</summary>
    public ContextSnapshotBuilder AddCurrentProcess() => AddValue("Process", new CurrentProcessSnapshot());

    /// <summary>Adds CPU information including processor name, core count, and hardware intrinsics support.</summary>
    public ContextSnapshotBuilder AddCpu() => AddValue("CPU", CpuSnapshot.Get());

    /// <summary>Adds the number of logical processors available to the current process.</summary>
    public ContextSnapshotBuilder AddProcessorCount() => AddValue("ProcessorCount", Environment.ProcessorCount);

    /// <summary>Adds security provider information including antivirus, firewall, and anti-spyware status (Windows only).</summary>
    public ContextSnapshotBuilder AddSecurityProviders() => AddValue("Antivirus", new SecurityProvidersSnapshot());

    /// <summary>Adds the active power management scheme (Windows only).</summary>
    public ContextSnapshotBuilder AddPowerManagement() => AddValue("PowerManagement", PowerManagementSnapshot.Get());

    /// <summary>Adds hypervisor information when running in a virtualized environment (Windows only).</summary>
    public ContextSnapshotBuilder AddHypervisor() => AddValue("Hypervisor", HypervisorSnapshot.Get());

    /// <summary>Adds local time zone information including UTC offset and daylight saving time support.</summary>
    public ContextSnapshotBuilder AddLocalTimeZone() => AddValue("LocalTimeZone", TimeZoneSnapshot.Get());

    /// <summary>Adds operating system information including platform, version, and architecture.</summary>
    public ContextSnapshotBuilder AddOperatingSystem() => AddValue("OperatingSystem", new OperatingSystemSnapshot());

    /// <summary>Adds .NET runtime information including runtime identifier and framework description.</summary>
    public ContextSnapshotBuilder AddDotnetRuntime() => AddValue("Dotnet", new DotnetRuntimeSnapshot());

    /// <summary>Adds current user information including username and domain.</summary>
    public ContextSnapshotBuilder AddUser() => AddValue("User", new UserSnapshot());

    /// <summary>Adds the current working directory path.</summary>
    public ContextSnapshotBuilder AddCurrentDirectory() => AddValue("CurrentDirectory", Environment.CurrentDirectory);

    /// <summary>Adds the newline string for the current platform.</summary>
    public ContextSnapshotBuilder AddNewLine() => AddValue("NewLine", Environment.NewLine);

    /// <summary>Adds the machine name.</summary>
    public ContextSnapshotBuilder AddMachineName() => AddValue("MachineName", Environment.MachineName);

    /// <summary>Adds garbage collector information including generation count and memory usage.</summary>
    public ContextSnapshotBuilder AddGarbageCollector() => AddValue("GC", new GarbageCollectorSnapshot());

    /// <summary>Adds information about all available drives including free space and drive type.</summary>
    public ContextSnapshotBuilder AddDrives() => AddValue("Drives", DriveSnapshot.Get());

    /// <summary>Adds paths to special system folders.</summary>
    public ContextSnapshotBuilder AddSpecialFolderPaths() => AddValue("SpecialFolders", new SpecialFolderSnapshot());

    /// <summary>Adds console information including encoding, buffer size, and colors.</summary>
    public ContextSnapshotBuilder AddConsole() => AddValue("Console", new ConsoleSnapshot());

    /// <summary>Adds information about loaded assembly load contexts and their assemblies.</summary>
    public ContextSnapshotBuilder AddAssemblyLoadContexts() => AddValue("AssemblyLoadContexts", AssemblyLoadContextSnapshot.Get());

    /// <summary>Adds thread pool configuration including worker thread and completion port thread counts.</summary>
    public ContextSnapshotBuilder AddThreadPool() => AddValue("ThreadPool", new ThreadPoolSnapshot());

    /// <summary>Adds culture and globalization settings.</summary>
    public ContextSnapshotBuilder AddCulture() => AddValue("Culture", new CultureSnapshot());

    /// <summary>Adds supported .NET runtime features.</summary>
    public ContextSnapshotBuilder AddRuntimeFeatures() => AddValue("RuntimeFeatures", new RuntimeFeaturesSnapshot());

    /// <summary>Adds commonly used AppContext switches and data values related to runtime configuration.</summary>
    public ContextSnapshotBuilder AddCommonAppContext()
    {
        AddContextData("APP_CONTEXT_BASE_DIRECTORY");
        AddContextData("APP_CONTEXT_DEPS_FILES");
        AddContextData("FX_DEPS_FILE");
        AddContextData("GCHeapHardLimit");
        AddContextData("GCHeapHardLimitLOH");
        AddContextData("GCHeapHardLimitLOHPercent");
        AddContextData("GCHeapHardLimitPercent");
        AddContextData("GCHeapHardLimitPOH");
        AddContextData("GCHeapHardLimitPOHPercent");
        AddContextData("GCHeapHardLimitSOH");
        AddContextData("GCHeapHardLimitSOHPercent");
        AddContextData("Microsoft.AspNetCore.Server.Kestrel.Http2.MaxConnectionFlowControlQueueSize");
        AddContextData("Microsoft.AspNetCore.Server.Kestrel.Http2.MaxEnhanceYourCalmCount");
        AddContextData("PROBING_DIRECTORIES");
        AddContextData("REGEX_DEFAULT_MATCH_TIMEOUT");
        AddContextData("REGEX_NONBACKTRACKING_MAX_AUTOMATA_SIZE");
        AddContextData("RUNTIME_IDENTIFIER");
        AddContextData("STARTUP_HOOKS");
        AddContextData("System.Net.Security.TlsCacheSize");
        AddContextData("System.Security.Cryptography.Pkcs12UnspecifiedPasswordIterationLimit");
        AddContextData("System.Threading.DefaultStackSize");
        AddContextData("System.Threading.ThreadPool.HillClimbing.Bias");
        AddContextData("System.Threading.ThreadPool.HillClimbing.ErrorSmoothingFactor");
        AddContextData("System.Threading.ThreadPool.HillClimbing.GainExponent");
        AddContextData("System.Threading.ThreadPool.HillClimbing.MaxChangePerSample");
        AddContextData("System.Threading.ThreadPool.HillClimbing.MaxChangePerSecond");
        AddContextData("System.Threading.ThreadPool.HillClimbing.MaxSampleErrorPercent");
        AddContextData("System.Threading.ThreadPool.HillClimbing.MaxWaveMagnitude");
        AddContextData("System.Threading.ThreadPool.HillClimbing.SampleIntervalHigh");
        AddContextData("System.Threading.ThreadPool.HillClimbing.SampleIntervalLow");
        AddContextData("System.Threading.ThreadPool.HillClimbing.TargetSignalToNoiseRatio");
        AddContextData("System.Threading.ThreadPool.HillClimbing.WaveHistorySize");
        AddContextData("System.Threading.ThreadPool.HillClimbing.WaveMagnitudeMultiplier");
        AddContextData("System.Threading.ThreadPool.HillClimbing.WavePeriod");
        AddContextData("System.Threading.ThreadPool.UnfairSemaphoreSpinLimit");

        AddContextSwitch("Microsoft.AspNetCore.Authentication.SuppressAutoDefaultScheme");
        AddContextSwitch("Microsoft.AspNetCore.Authorization.SuppressUseHttpContextAsAuthorizationResource");
        AddContextSwitch("Microsoft.AspNetCore.Caching.StackExchangeRedis.UseForceReconnect");
        AddContextSwitch("Microsoft.AspNetCore.Identity.CheckPasswordSignInAlwaysResetLockoutOnSuccess");
        AddContextSwitch("Microsoft.AspNetCore.Server.IIS.Latin1RequestHeaders");
        AddContextSwitch("Microsoft.AspNetCore.Server.Kestrel.DisableCertificateFileWatching");
        AddContextSwitch("Microsoft.AspNetCore.Server.Kestrel.DisableHttp1LineFeedTerminators");
        AddContextSwitch("Microsoft.AspNetCore.Server.Kestrel.EnableWindows81Http2");
        AddContextSwitch("Microsoft.AspNetCore.Server.Kestrel.FinOnError");
        AddContextSwitch("Microsoft.Extensions.DependencyInjection.DisableDynamicEngine");
        AddContextSwitch("Microsoft.Extensions.DependencyInjection.VerifyOpenGenericServiceTrimmability");
        AddContextSwitch("Switch.Microsoft.AspNetCore.Mvc.UsePasswordValue");
        AddContextSwitch("Switch.System.Data.AllowArbitraryDataSetTypeInstantiation");
        AddContextSwitch("Switch.System.Data.AllowUnsafeSerializationFormatBinary");
        AddContextSwitch("Switch.System.Diagnostics.EventSource.PreserveEventListenerObjectIdentity");
        AddContextSwitch("Switch.System.Diagnostics.StackTrace.ShowILOffsets");
        AddContextSwitch("Switch.System.Drawing.DontSupportPngFramesInIcons");
        AddContextSwitch("Switch.System.Drawing.DontSupportPngFramesInIcons");
        AddContextSwitch("Switch.System.Drawing.Printing.OptimizePrintPreview");
        AddContextSwitch("Switch.System.Globalization.EnforceJapaneseEraYearRanges");
        AddContextSwitch("Switch.System.Globalization.EnforceLegacyJapaneseDateParsing");
        AddContextSwitch("Switch.System.Globalization.FormatJapaneseFirstYearAsANumber");
        AddContextSwitch("Switch.System.Reflection.ForceEmitInvoke");
        AddContextSwitch("Switch.System.Reflection.ForceInterpretedInvoke");
        AddContextSwitch("Switch.System.Runtime.Serialization.DataContracts.Auto_Import_KVP");
        AddContextSwitch("Switch.System.Runtime.Serialization.SerializationGuard.AllowAssembliesFromByteArrays");
        AddContextSwitch("Switch.System.Runtime.Serialization.SerializationGuard.AllowFileWrites");
        AddContextSwitch("Switch.System.Runtime.Serialization.SerializationGuard");
        AddContextSwitch("Switch.System.Runtime.Serialization.SerializationGuard");
        AddContextSwitch("Switch.System.ServiceModel.UseSha1InPipeConnectionGetHashAlgorithm");
        AddContextSwitch("Switch.System.Windows.AllowExternalProcessToBlockAccessToTemporaryFiles");
        AddContextSwitch("Switch.System.Windows.Diagnostics.AllowChangesDuringVisualTreeChanged");
        AddContextSwitch("Switch.System.Windows.Diagnostics.DisableDiagnostics");
        AddContextSwitch("Switch.System.Windows.DoNotScaleForDpiChanges");
        AddContextSwitch("Switch.System.Windows.DoNotUsePresentationDpiCapabilityTier2OrGreater");
        AddContextSwitch("Switch.System.Windows.DoNotUsePresentationDpiCapabilityTier3OrGreater");
        AddContextSwitch("Switch.System.Windows.Forms.AccessibleObject.NoClientNotifications");
        AddContextSwitch("Switch.System.Windows.Input.Stylus.DisableImplicitTouchKeyboardInvocation");
        AddContextSwitch("Switch.System.Windows.Input.Stylus.DisableStylusAndTouchSupport");
        AddContextSwitch("Switch.System.Windows.Input.Stylus.EnablePointerSupport");
        AddContextSwitch("Switch.System.Windows.Media.EnableHardwareAccelerationInRdp");
        AddContextSwitch("Switch.System.Windows.Media.ImageSourceConverter.OverrideExceptionWithNullReferenceException");
        AddContextSwitch("Switch.System.Windows.Media.MediaContext.DisableDirtyRectangles");
        AddContextSwitch("Switch.System.Windows.Media.MediaContext.EnableDynamicDirtyRectangles");
        AddContextSwitch("Switch.System.Windows.Media.ShouldNotRenderInNonInteractiveWindowStation");
        AddContextSwitch("Switch.System.Windows.Media.ShouldRenderEvenWhenNoDisplayDevicesAreAvailable");
        AddContextSwitch("Switch.System.Xml.AllowDefaultResolver");
        AddContextSwitch("Switch.System.Xml.DontThrowOnInvalidSurrogatePairs");
        AddContextSwitch("Switch.System.Xml.IgnoreEmptyKeySequences");
        AddContextSwitch("Switch.System.Xml.IgnoreKindInUtcTimeSerialization");
        AddContextSwitch("Switch.System.Xml.LimitXPathComplexity");
        AddContextSwitch("System.Diagnostics.Tracing.EventSource.IsSupported");
        AddContextSwitch("System.Net.DisableIPv6");
        AddContextSwitch("System.Net.Http.EnableActivityPropagation");
        AddContextSwitch("System.Net.Http.SocketsHttpHandler.Http2Support");
        AddContextSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport");
        AddContextSwitch("System.Net.Http.UsePortInSpn");
        AddContextSwitch("System.Net.Security.DisableTlsResume");
        AddContextSwitch("System.Net.Security.UseManagedNtlm");
        AddContextSwitch("System.Net.SocketsHttpHandler.Http2FlowControl.DisableDynamicWindowSizing");
        AddContextSwitch("System.Net.SocketsHttpHandler.Http3Support");
        AddContextSwitch("System.Net.SocketsHttpHandler.MaxConnectionsPerServer");
        AddContextSwitch("System.Net.SocketsHttpHandler.PendingConnectionTimeoutOnRequestCompletion");
        AddContextSwitch("System.Reflection.NullabilityInfoContext.IsSupported");
        AddContextSwitch("System.Resources.ResourceManager.AllowCustomResourceTypes");
        AddContextSwitch("System.Resources.UseSystemResourceKeys");
        AddContextSwitch("System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported");
        AddContextSwitch("System.Runtime.InteropServices.BuiltInComInterop.IsSupported");
        AddContextSwitch("System.Runtime.InteropServices.BuiltInComInterop.IsSupported");
        AddContextSwitch("System.Runtime.InteropServices.EnableConsumingManagedCodeFromNativeHosting");
        AddContextSwitch("System.Runtime.InteropServices.Marshalling.EnableGeneratedComInterfaceComImportInterop");
        AddContextSwitch("System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization");
        AddContextSwitch("System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization");
        AddContextSwitch("System.ServiceModel.Dispatcher.UseLegacyInvokeDelegate");
        AddContextSwitch("System.ServiceModel.OperationContext.DisableAsyncFlow");
        AddContextSwitch("System.ServiceModel.OperationContext.DisableAsyncFlow");
        AddContextSwitch("System.Text.Encoding.EnableUnsafeUTF7Encoding");
        AddContextSwitch("System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault");
        AddContextSwitch("System.Text.Json.Serialization.EnableSourceGenReflectionFallback");
        AddContextSwitch("System.Threading.ThreadPool.HillClimbing.Disable");
        AddContextSwitch("System.Windows.Forms.AnchorLayoutV2");
        AddContextSwitch("System.Windows.Forms.AnchorLayoutV2");
        AddContextSwitch("System.Windows.Forms.DoNotCatchUnhandledExceptions");
        AddContextSwitch("System.Windows.Forms.DoNotCatchUnhandledExceptions");
        AddContextSwitch("System.Windows.Forms.PictureBox.UseWebRequest");
        AddContextSwitch("System.Windows.Forms.ScaleTopLevelFormMinMaxSizeForDpi");
        AddContextSwitch("System.Windows.Forms.ScaleTopLevelFormMinMaxSizeForDpi");
        AddContextSwitch("System.Windows.Forms.ServicePointManagerCheckCrl");
        AddContextSwitch("System.Windows.Forms.ServicePointManagerCheckCrl");
        AddContextSwitch("System.Windows.Forms.TrackBarModernRendering");
        AddContextSwitch("System.Windows.Forms.TrackBarModernRendering");
        AddContextSwitch("System.Xml.XmlResolver.IsNetworkingEnabledByDefault");
        AddContextSwitch("System.Xml.XmlResolver.IsNetworkingEnabledByDefault");
        AddContextSwitch("TestSwitch.LocalAppContext.DisableCaching");
        AddContextSwitch("wcf:useBestMatchNamedPipeUri");

        return this;

        void AddContextSwitch(string name)
        {
            if (AppContext.TryGetSwitch(name, out var isEnabled))
            {
                AddValue("AppContext." + name, isEnabled);
            }
        }

        void AddContextData(string name)
        {
            var value = AppContext.GetData(name);
            if (value is not null)
            {
                AddValue("AppContext." + name, value);
            }
        }
    }

    /// <summary>Adds all AppContext data and switches using reflection to access internal stores.</summary>
    public ContextSnapshotBuilder AddAppContextData()
    {
        if (typeof(AppContext).GetField("s_dataStore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null) is Dictionary<string, object?> data)
        {
            lock (data)
            {
                foreach (var kvp in data)
                {
                    AddValue("AppContext." + kvp.Key, kvp.Value);
                }
            }
        }

        if (typeof(AppContext).GetField("s_switches", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null) is Dictionary<string, bool> switches)
        {
            lock (switches)
            {
                foreach (var kvp in switches)
                {
                    AddValue("AppContext." + kvp.Key, kvp.Value);
                }
            }
        }

        return this;
    }

    /// <summary>Adds environment variables for the specified target scope.</summary>
    public void AddEnvironmentVariables(EnvironmentVariableTarget target = EnvironmentVariableTarget.Process)
    {
        AddValue("EnvironmentVariables." + target, GetEnvironmentVariables(target));

        static ImmutableSortedDictionary<string, object> GetEnvironmentVariables(EnvironmentVariableTarget target)
        {
            return Environment.GetEnvironmentVariables(target).Cast<DictionaryEntry>().Select(item => Parse(item)).ToImmutableSortedDictionary();

            static KeyValuePair<string, object> Parse(DictionaryEntry entry)
            {
                var key = (string)entry.Key;
                if (string.Equals(key, "PATH", StringComparison.OrdinalIgnoreCase))
                    return KeyValuePair.Create<string, object>(key, ((string)entry.Value).Split(';').ToImmutableArray());

                return KeyValuePair.Create<string, object>(key, entry.Value);
            }
        }
    }
}
