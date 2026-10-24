namespace Meziantou.Framework.JavaScriptEngine.NpmPackages;

/// <summary>
/// Runs the workloads of workloads/*.mjs, which use npm packages, with harness.js, and compares their results with the
/// results of Node.js (expected/*.txt, written by generate.js). See readme.md.
/// </summary>
public sealed class NpmPackagesTests
{
    private static readonly string Root = FullPath.FromPath(AppContext.BaseDirectory).FindRequiredGitRepositoryRoot();

    private static readonly string ProjectDirectory = Path.Combine(Root, "tests", "Meziantou.Framework.JavaScriptEngine.NpmPackages");

    /// <summary>The npm packages of package-lock.json. Run 'dotnet run eng/javascriptengine/fetch-tests.cs' to install them.</summary>
    private static readonly string PackagesDirectory = Path.Combine(Root, "tests", "Meziantou.Framework.JavaScriptEngine.External", "npm-packages");

    public static TheoryData<string> Workloads
    {
        get
        {
            var data = new TheoryData<string>();
            if (!Directory.Exists(Path.Combine(PackagesDirectory, "node_modules")))
                return data;

            foreach (var path in Directory.GetFiles(Path.Combine(ProjectDirectory, "workloads"), "*.mjs").Order(StringComparer.Ordinal))
            {
                data.Add(Path.GetFileNameWithoutExtension(path));
            }

            return data;
        }
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Workloads), DisableDiscoveryEnumeration = true)]
    public void Run(string workload)
    {
        var expected = File.ReadAllText(Path.Combine(ProjectDirectory, "expected", workload + ".txt"));
        var result = RunWorkload(workload);
        if (result != expected)
        {
            var resultPath = Path.Combine(Path.GetTempPath(), "npm-packages", workload + ".txt");
            Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
            File.WriteAllText(resultPath, result);
            Assert.Fail($"The result of '{workload}' differs from the result of Node.js. The result is in '{resultPath}'.");
        }
    }

    private static string RunWorkload(string workload)
    {
        var loader = new HarnessModuleLoader();
        using var engine = new JsEngine(new JsEngineOptions
        {
            Timeout = TimeSpan.FromMinutes(5),
            TimeZone = "UTC",
            DefaultLocale = "en-US",
            ModuleLoader = loader,
        });
        var realm = engine.CreateRealm();
        var harness = realm.Execute(File.ReadAllText(Path.Combine(ProjectDirectory, "harness.js")), "/harness.js");

        JsObject host;
        using (engine.Enter())
        {
            host = realm.CreateObject();
            host.SetProperty("readFile", realm.CreateFunction("readFile", 1, (thisValue, arguments) => ReadFile(arguments[0].AsString()) ?? JsValue.Undefined));
            host.SetProperty("requireModule", realm.CreateFunction("requireModule", 1, (thisValue, arguments) => loader.Require(arguments[0].AsString())));
        }

        var api = engine.Call(harness, JsValue.Undefined, host).AsObject();
        loader.Initialize(realm, api);

        JsValue run;
        JsValue runTimer;
        using (engine.Enter())
        {
            run = api.GetProperty("run");
            runTimer = api.GetProperty("runTimer");
        }

        var state = engine.Call(run, api, workload).AsObject();
        while (true)
        {
            engine.RunJobs();
            using (engine.Enter())
            {
                if (state.GetProperty("done").AsBoolean())
                    return state.GetProperty("result").AsString();
            }

            if (!engine.Call(runTimer, api).AsBoolean())
                return "Error: the workload did not complete";
        }
    }

    // '/workloads' is the directory of the workloads, the other paths are in the installed packages
    private static string? ReadFile(string path)
    {
        var fullPath = path.StartsWith("/workloads/", StringComparison.Ordinal)
            ? Path.Combine(ProjectDirectory, path.TrimStart('/'))
            : Path.Combine(PackagesDirectory, path.TrimStart('/'));
        return File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
    }

    /// <summary>Creates the modules that harness.js resolves, as generate.js does with the modules of Node.js.</summary>
    private sealed class HarnessModuleLoader : JsModuleLoader
    {
        private readonly Dictionary<string, JsModule> _modules = new(StringComparer.Ordinal);
        private JsRealm? _realm;
        private JsObject? _api;

        public void Initialize(JsRealm realm, JsObject api)
        {
            _realm = realm;
            _api = api;
        }

        /// <summary>The first error of the loading of the modules that <see cref="Require"/> loads.</summary>
        private JsException? _requireError;

        protected override void LoadImportedModule(JsModuleLoadRequest request)
        {
            try
            {
                request.Complete(GetModule(request.Specifier, request.ReferrerUrl));
            }
            catch (JsException exception)
            {
                _requireError ??= exception;
                request.Fail(exception);
            }
        }

        /// <summary>
        /// require() of an ES module: the module is loaded, linked and evaluated synchronously (the loader completes the
        /// requests synchronously), and the result is its namespace.
        /// </summary>
        public JsValue Require(string path)
        {
            var module = GetModule(path, referrer: null);
            var previousError = _requireError;
            _requireError = null;
            try
            {
                module.LoadRequestedModules();
                if (_requireError is not null)
                    throw _requireError;

                module.Link();
                module.Evaluate();
                if (module.HasEvaluationError)
                    throw new JsException(module.EvaluationError);

                if (module.Status is not JsModuleStatus.Evaluated)
                    throw _realm!.CreateException(JsErrorType.Error, "require() of the ES module '" + path + "' that uses top-level await is not supported");

                return module.GetNamespace();
            }
            finally
            {
                _requireError = previousError;
            }
        }

        protected override void InitializeImportMeta(JsSourceTextModule module, JsObject importMeta)
        {
            importMeta.SetProperty("url", "file://" + module.Url);
        }

        private JsModule GetModule(string specifier, string? referrer)
        {
            var realm = _realm!;
            var engine = realm.Engine;
            var api = _api!;

            string path;
            string format;
            using (engine.Enter())
            {
                var resolved = engine.Call(api.GetProperty("resolve"), api, specifier, referrer is null ? JsValue.Undefined : referrer).AsObject();
                path = resolved.GetProperty("path").AsString();
                format = resolved.GetProperty("format").AsString();
            }

            if (_modules.TryGetValue(path, out var module))
                return module;

            if (format is "module")
            {
                module = realm.ParseModule(ReadFile(path)!, path);
            }
            else
            {
                // A CommonJS module, a JSON file or a Node.js module
                var exportNames = new List<string>();
                using (engine.Enter())
                {
                    var names = engine.Call(api.GetProperty("getExportNames"), api, path).AsObject();
                    var length = (int)names.GetProperty("length").AsNumber();
                    for (var i = 0; i < length; i++)
                    {
                        exportNames.Add(names.GetProperty(i.ToString(CultureInfo.InvariantCulture)).AsString());
                    }
                }

                module = realm.CreateSyntheticModule(exportNames, synthetic =>
                {
                    var exports = engine.Call(api.GetProperty("require"), api, path);
                    foreach (var name in exportNames)
                    {
                        synthetic.SetExport(name, name is "default" ? exports : exports.AsObject().GetProperty(name));
                    }
                }, path);
            }

            _modules.Add(path, module);
            return module;
        }
    }
}
