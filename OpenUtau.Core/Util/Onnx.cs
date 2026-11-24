using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core {
    public class GpuInfo {
        public int deviceId;
        public string description = "";

        override public string ToString() {
            return $"[{deviceId}] {description}";
        }
    }

    public enum OnnxRunnerChoice {
        Default,
        CPU,
        CPUForCoreML,
    }

    public class Onnx {

        private static bool cudaAvailable = OS.IsLinux() && CudaGpuDetector.IsCudaAvailable() && CudaGpuDetector.IsCuDnnAvailable();

        private static readonly Dictionary<int, OrtEpDevice> devices = initializeDevices();

        private static Dictionary<int, OrtEpDevice> initializeDevices() {
            var env = OrtEnv.Instance();
            var ortDevices = env.GetEpDevices();

            return ortDevices
                .Where(device => device.EpName.Contains("dml", StringComparison.OrdinalIgnoreCase))
                .Select((device, index) => new { index, device })
                .Where(x => !WindowsGpuDetector.IsIndirectDisplayAdapter(x.device.HardwareDevice.Metadata.Entries))
                .ToDictionary(x => x.index, x => x.device);
        }

        // ---- WebGPU plugin execution provider ----------------------------------
        // The native WebGPU EP ships as a plugin library that has to be registered
        // with OrtEnv before its devices show up in GetEpDevices(). It is only
        // packaged for macOS here, so every other platform reports it unavailable.

        /// <summary>EP name reported by the WebGPU plugin library.</summary>
        private const string WebGpuEpName = "WebGpuExecutionProvider";
        /// <summary>Registration name is arbitrary, only used to unregister.</summary>
        private const string WebGpuRegistrationName = "openutau_webgpu";
        /// <summary>Name of the plugin library inside Microsoft.ML.OnnxRuntime.EP.WebGpu.</summary>
        private const string WebGpuLibraryName = "libonnxruntime_providers_webgpu.dylib";

        /// <summary>Minimum ONNX Runtime the plugin library actually works with. The
        /// package advertises "1.24.4 or later", but its binary is built against ORT
        /// API 30: 1.24.x kills the process natively while an InferenceSession is
        /// created, and 1.26.x rejects the library outright, so both are unusable.</summary>
        private const int WebGpuMinMajor = 1;
        private const int WebGpuMinMinor = 30;

        private static readonly object webGpuLock = new object();
        private static bool webGpuInitialized;
        private static bool webGpuReady;
        private static List<OrtEpDevice> webGpuDevices = new List<OrtEpDevice>();

        private static void EnsureWebGpuInitialized() {
            lock (webGpuLock) {
                if (webGpuInitialized) {
                    return;
                }
                webGpuInitialized = true;
                webGpuReady = RegisterWebGpu();
            }
        }

        private static bool RegisterWebGpu() {
            if (!OS.IsMacOS() || RuntimeInformation.ProcessArchitecture != Architecture.Arm64) {
                return false;
            }
            try {
                var env = OrtEnv.Instance();
                string version = env.GetVersionString();
                if (!OnnxRuntimeVersion.IsAtLeast(version, WebGpuMinMajor, WebGpuMinMinor)) {
                    Log.Warning("WebGPU runner needs ONNX Runtime {Major}.{Minor} or newer, but {Version} is loaded.",
                        WebGpuMinMajor, WebGpuMinMinor, version);
                    return false;
                }
                string? libraryPath = findWebGpuLibrary();
                if (libraryPath == null) {
                    Log.Information("WebGPU runner is not available: {Library} was not found next to the executable.",
                        WebGpuLibraryName);
                    return false;
                }
                env.RegisterExecutionProviderLibrary(WebGpuRegistrationName, libraryPath);
                var found = env.GetEpDevices()
                    .Where(device => string.Equals(device.EpName, WebGpuEpName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (found.Count == 0) {
                    Log.Warning("WebGPU runner loaded {Library} but it reported no devices.", libraryPath);
                    return false;
                }
                webGpuDevices = found;
                Log.Information("WebGPU runner is ready with {Count} device(s).", found.Count);
                for (int i = 0; i < found.Count; i++) {
                    // Logged in full because a second entry can be the same physical GPU
                    // enumerated twice, and picking it aborts the process (see getGpuInfo).
                    Log.Information("WebGPU device {Index}: {Description} [{Metadata}]", i,
                        describe(found[i]),
                        string.Join(", ", found[i].HardwareDevice.Metadata.Entries.Select(entry => $"{entry.Key}={entry.Value}")));
                }
                return true;
            } catch (Exception e) {
                // Registration validates the plugin ABI, so a mismatched core fails
                // here with a managed exception rather than at session creation.
                Log.Error(e, "Failed to register the WebGPU execution provider.");
                return false;
            }
        }

        /// <summary>Locates the plugin library in the places a framework dependent
        /// build, an app bundle or a flat output directory put it.</summary>
        private static string? findWebGpuLibrary() {
            string baseDir = AppContext.BaseDirectory;
            // The package ships the library with a "lib" prefix on macOS; keep the
            // unprefixed name as a fallback so a packaging change does not silently
            // disable the runner.
            string[] names = { WebGpuLibraryName, "onnxruntime_providers_webgpu.dylib" };
            var candidates = names
                .SelectMany(name => new[] {
                    Path.Combine(baseDir, name),
                    Path.Combine(baseDir, "runtimes", "osx-arm64", "native", name),
                    Path.Combine(baseDir, "..", "Resources", name),
                });
            foreach (var candidate in candidates) {
                try {
                    string full = Path.GetFullPath(candidate);
                    if (File.Exists(full)) {
                        return full;
                    }
                } catch (Exception) {
                    // A malformed path is simply not a candidate.
                }
            }
            return null;
        }

        /// <summary>Whether the WebGPU plugin EP is registered and reported a device.</summary>
        public static bool IsWebGpuAvailable() {
            EnsureWebGpuInitialized();
            return webGpuReady;
        }

        public static List<string> getRunnerOptions() {
            if (OS.IsWindows()) {
                var runners = new List<string> { "CPU" };
                if (devices.Count > 0) {
                    runners.Add("DirectML");
                }
                return runners;
            } else if (OS.IsMacOS()) {
                var options = new List<string> { "CPU" };
                if (IsWebGpuAvailable()) {
                    options.Add("WebGPU");
                }
                return options;
            } else if (OS.IsIOS()) {
                return ["CPU", "CoreML"];
            } else if (cudaAvailable) {
                return new List<string> {
                "CPU",
                "CUDA"
                };
            } else if (OS.IsAndroid()) {
                return new List<string> {
                "CPU",
                "NNAPI"
                };
            }
            return new List<string> {
                "CPU"
            };
        }

        private static string describe(OrtEpDevice device) {
            foreach (var item in device.HardwareDevice.Metadata.Entries) {
                if (item.Key.ToLower() == "description") {
                    return $"{item.Value} ({device.HardwareDevice.Type})";
                }
            }
            return $"{device.EpName} {device.HardwareDevice.Vendor} ({device.HardwareDevice.Type})";
        }

        public static List<GpuInfo> getGpuInfo() {
            if (cudaAvailable) {
                return CudaGpuDetector.GetCudaDevices();
            }

            if (OS.IsAndroid()) {
                return new List<GpuInfo>{new GpuInfo {
                    deviceId = 0, // eliminate exception of taking OnnxGpuOptions[0]
                }};
            }

            if (OS.IsMacOS()) {
                if (!IsWebGpuAvailable()) {
                    return new List<GpuInfo>();
                }
                // Every enumerated device is offered. Which index actually works is not
                // stable across machines (there are reports of the second entry rendering
                // while the first does not), so the picker must not assume index 0 is the
                // good one. A device that cannot be used aborts the process inside the
                // plugin (ep::adapter::CreateTensorFromApiValue -> std::terminate,
                // microsoft/onnxruntime#33557), which no managed fallback can catch, so
                // the list is logged in full - see RegisterWebGpu - to make the next
                // report diagnosable.
                return webGpuDevices
                    .Select((device, index) => new GpuInfo { deviceId = index, description = describe(device) })
                    .ToList();
            }

            List<GpuInfo> gpuList = new List<GpuInfo>();
            // devices already excludes virtual display adapters (upstream #2549), so a
            // key is the original enumeration index and the numbering may have gaps.
            foreach (var entry in devices) {
                gpuList.Add(new GpuInfo {
                    deviceId = entry.Key,
                    description = describe(entry.Value),
                });
            }
            return gpuList;
        }

        /// <summary>The runner the preference resolves to, always one of <see cref="getRunnerOptions"/>.</summary>
        private static string getRunner() {
            List<string> runnerOptions = getRunnerOptions();
            string runner = Preferences.Default.OnnxRunner;
            if (String.IsNullOrEmpty(runner)) {
                runner = runnerOptions[0];
            }
            if (!runnerOptions.Contains(runner)) {
                // Happens after a runner is dropped, e.g. CoreML on macOS.
                Log.Information("Machine learning runner \"{Runner}\" is no longer available, using CPU.", runner);
                runner = "CPU";
            }
            return runner;
        }

        /// <summary>Whether a session created now runs on CPU, and so allows concurrent inference calls.</summary>
        public static bool IsCpuRunner() {
            return getRunner() == "CPU";
        }

        /// <summary>Serializes DirectML session creation, inference and disposal. The Windows
        /// DirectML build of ONNX Runtime kills the process natively when those overlap, and the
        /// package is frozen at 1.24.4, so the ORT-side fixes will never ship as an upgrade.</summary>
        public static readonly object DmlLock = new object();

        /// <summary>Whether sessions created now run on the DirectML execution provider.</summary>
        public static bool IsDmlRunner() {
            return getRunner() == "DirectML";
        }

        /// <summary>Whether sessions created now run on the WebGPU plugin execution provider.</summary>
        public static bool IsWebGpuRunner() {
            return getRunner() == "WebGPU";
        }

        private readonly struct DmlScope : IDisposable {
            private readonly bool engaged;
            public DmlScope(bool engaged) { this.engaged = engaged; }
            public void Dispose() {
                if (engaged) {
                    System.Threading.Monitor.Exit(DmlLock);
                }
            }
        }

        private static readonly DmlScope NoDmlScope = new DmlScope(false);

        /// <summary>Set when DirectML is or was in use in this process. Sessions are cached and
        /// stay on DirectML even after the preference moves to CPU, so the lock has to stay
        /// engaged for them; a process that never used DirectML keeps the no-op scope.</summary>
        private static volatile bool dmlInUse;

        /// <summary>Takes <see cref="DmlLock"/> while DirectML session creation, Run or disposal
        /// can happen, which is while the DirectML runner is selected or once a DirectML session
        /// exists. DirectML session creation, Run and disposal must stay inside it.
        /// This deliberately does not cover the WebGPU runner: its reported failure mode is
        /// concurrent Run across sessions (microsoft/onnxruntime#32561), which needs a lock over
        /// every GPU session in the process rather than one provider's lock.</summary>
        public static IDisposable EnterDmlScope() {
            // Lock first, then re-check the runner: a CPU->DirectML switch can land while this
            // thread is waiting on the lock, and a scope handed out as a no-op cannot be upgraded
            // afterwards, which would leave the following DirectML work outside the lock.
            System.Threading.Monitor.Enter(DmlLock);
            try {
                if (!IsDmlRunner() && !dmlInUse) {
                    System.Threading.Monitor.Exit(DmlLock);
                    return NoDmlScope;
                }
                dmlInUse = true;
                return new DmlScope(true);
            } catch {
                System.Threading.Monitor.Exit(DmlLock);
                throw;
            }
        }

        /// <summary>Creates a session with the selected execution provider. A GPU provider
        /// failure falls back to CPU: the DirectML graph compiler rejects some models it cannot
        /// run, and the WebGPU EP can fail to build a pipeline for a model or a device.</summary>
        private static InferenceSession createSession(Func<SessionOptions, InferenceSession> create, Func<InferenceSession> cpu) {
            InferenceSession withProvider(bool coremlEnableOnSubgraphs = false) {
                using var options = getOnnxSessionOptions(coremlEnableOnSubgraphs);
                return create(options);
            }
            if (OS.IsIOS() && Preferences.Default.OnnxRunner == "CoreML") {
                try {
                    return withProvider(coremlEnableOnSubgraphs: true);
                } catch (Exception e) {
                    Log.Warning(e, "Failed to create session with CoreML subgraphs enabled, falling back to default settings");
                }
            }
            if (IsDmlRunner()) {
                // Before creating: whatever later runs on that session must keep the lock even if the
                // preference switches to CPU while the session is cached.
                dmlInUse = true;
                lock (DmlLock) {
                    try {
                        return withProvider();
                    } catch (Exception e) {
                        Log.Warning(e, "Failed to create a DirectML inference session, falling back to CPU.");
                        return cpu();
                    }
                }
            }
            if (IsWebGpuRunner()) {
                // The WebGPU EP has no thread safety contract yet (concurrent Run across
                // sessions crashes natively, microsoft/onnxruntime#32561), so only failures
                // that surface as managed exceptions can be caught and degraded here.
                try {
                    return withProvider();
                } catch (Exception e) {
                    Log.Warning(e, "Failed to create a WebGPU inference session, falling back to CPU.");
                    return cpu();
                }
            }
            return withProvider();
        }

        private static OrtEpDevice getWebGpuDevice() {
            EnsureWebGpuInitialized();
            if (webGpuDevices.Count == 0) {
                throw new InvalidOperationException("The WebGPU execution provider has no device.");
            }
            int index = Preferences.Default.OnnxGpu;
            if (index < 0 || index >= webGpuDevices.Count) {
                index = 0;
            }
            return webGpuDevices[index];
        }

        private static SessionOptions getOnnxSessionOptions(bool coremlEnableOnSubgraphs = false) {
            SessionOptions options = new SessionOptions();
            string runner = getRunner();
            switch (runner) {
                case "DirectML":
                    options.EnableMemoryPattern = false;
                    options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                    var deviceId = devices.ContainsKey(Preferences.Default.OnnxGpu)
                        ? Preferences.Default.OnnxGpu
                        : devices.Keys.OrderBy(id => id).First();
                    var d = devices[deviceId];
                    options.AppendExecutionProvider(
                        OrtEnv.Instance(),
                        new List<OrtEpDevice> { d },
                        new Dictionary<string, string> { }
                     );
                    break;
                case "WebGPU":
                    options.AppendExecutionProvider(
                        OrtEnv.Instance(),
                        new List<OrtEpDevice> { getWebGpuDevice() },
                        new Dictionary<string, string> { }
                     );
                    break;
                case "CUDA":
                    options.AppendExecutionProvider_CUDA(Preferences.Default.OnnxGpu);
                    break;
                case "NNAPI":
                    options.AppendExecutionProvider_Nnapi();
                    break;
                case "CoreML":
                    // 保留 iOS 对复杂 DiffSinger 模型使用的 NeuralNetwork 格式。
                    options.AppendExecutionProvider("CoreML", new Dictionary<string, string> {
                        { "MLComputeUnits", "ALL" },
                        { "RequireStaticInputShapes", "1" },
                        { "ModelFormat", "NeuralNetwork" },
                        { "EnableOnSubgraphs", coremlEnableOnSubgraphs ? "1" : "0" }
                    });
                    break;
            }
            return options;
        }

        private static bool forceCpu(OnnxRunnerChoice runnerChoice) {
            return runnerChoice == OnnxRunnerChoice.CPU ||
                (runnerChoice == OnnxRunnerChoice.CPUForCoreML && OS.IsIOS() && Preferences.Default.OnnxRunner == "CoreML");
        }

        public static InferenceSession getInferenceSession(byte[] model, OnnxRunnerChoice runnerChoice = OnnxRunnerChoice.Default) {
            return forceCpu(runnerChoice)
                ? new InferenceSession(model)
                : createSession(options => new InferenceSession(model, options), () => new InferenceSession(model));
        }

        public static InferenceSession getInferenceSession(string modelPath, OnnxRunnerChoice runnerChoice = OnnxRunnerChoice.Default) {
            return forceCpu(runnerChoice)
                ? new InferenceSession(modelPath)
                : createSession(options => new InferenceSession(modelPath, options), () => new InferenceSession(modelPath));
        }

        public static void VerifyInputNames(InferenceSession session, IEnumerable<NamedOnnxValue> inputs) {
            var sessionInputNames = session.InputNames.ToHashSet();
            var givenInputNames = inputs.Select(v => v.Name).ToHashSet();
            var missing = sessionInputNames
                .Except(givenInputNames)
                .OrderBy(s => s, StringComparer.InvariantCulture)
                .ToArray();
            if (missing.Length > 0) {
                throw new ArgumentException("Missing input(s) for the inference session: " + string.Join(", ", missing));
            }
            var unexpected = givenInputNames
                .Except(sessionInputNames)
                .OrderBy(s => s, StringComparer.InvariantCulture)
                .ToArray();
            if (unexpected.Length > 0) {
                throw new ArgumentException("Unexpected input(s) for the inference session: " + string.Join(", ", unexpected));
            }
        }
    }
}
