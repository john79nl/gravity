using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Gravity.Core;
using AgentSwarmSimulation;
using Gravity.UI;

namespace Gravity
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            string? targetPath = args != null && args.Length > 0 ? args[0] : null;

            var isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            IPlatformIntegrationService platformIntegration = isWindows
                ? new WindowsIntegrationService()
                : new LinuxIntegrationService();

            // ── Single Instance IPC Check ─────────────────────────────────────
            // If another instance of Gravity is already running, send targetPath over IPC socket/pipe and exit
            if (platformIntegration.CheckAndSendToRunningInstance(targetPath))
            {
                return;
            }

            // Register Shell integrations (Send To / Desktop Entry, Context Menu)
            platformIntegration.RegisterShellIntegration();

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                var msg = $"FATAL: {(ex?.Message ?? "Unknown")}\n{ex?.StackTrace}";
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}");
                MessageBox.Show(msg, "Gravity Crash", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.ThreadException += (s, e) =>
            {
                var msg = $"UI Thread Exception: {e.Exception.Message}\n{e.Exception.StackTrace}";
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}");
                MessageBox.Show(msg, "Gravity Crash", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            ApplicationConfiguration.Initialize();

            // ── Build DI ──────────────────────────────────────────────────────
            var services = new ServiceCollection();
            ConfigureServices(services);
            using var serviceProvider = services.BuildServiceProvider();

            // Post-build agent registration (dynamic + MCP)
            var router = serviceProvider.GetRequiredService<ReasoningRouter>();
            RegisterDynamicAgents(router, serviceProvider);
            RegisterMcpAgents(router, serviceProvider).GetAwaiter().GetResult();

            // Start llama.cpp server asynchronously if LocalGguf provider is active
            var llamaManager = serviceProvider.GetRequiredService<LlamaCppServerManager>();
            llamaManager.OnLog += msg => System.Diagnostics.Debug.WriteLine(msg);
            _ = Task.Run(async () =>
            {
                try { await llamaManager.StartAsync(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LlamaCpp startup error: {ex.Message}"); }
            });

            // Stop the llama.cpp server when the application exits
            Application.ApplicationExit += (_, _) => llamaManager.Stop();

            // ── Open main form ────────────────────────────────────────────────
            var mainForm = serviceProvider.GetRequiredService<Form1>();

            // If a file path was passed on startup, pass it to mainForm
            if (!string.IsNullOrEmpty(targetPath))
            {
                mainForm.InitialFilePath = targetPath;
            }

            // Route files received via IPC while running to mainForm
            platformIntegration.OnFileReceived += (receivedPath) =>
            {
                mainForm.BeginInvoke(new Action(() =>
                {
                    mainForm.HandleExternalFileReceived(receivedPath);
                }));
            };

            Application.Run(mainForm);
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<ProjectContext>();
            services.AddSingleton<IProjectContext>(sp => sp.GetRequiredService<ProjectContext>());
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<IShellLogger, ShellLoggerService>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<System.Net.Http.HttpClient>();
            services.AddSingleton<IModelClient, GenericOpenAIClient>();
            services.AddSingleton<LlamaCppServerManager>();
            services.AddSingleton<IArtifactService, ArtifactService>();

            services.AddSingleton<GitService>();
            services.AddSingleton<FileSearchService>();
            services.AddSingleton<DocxPreviewService>();
            services.AddSingleton<BuildService>();
            services.AddSingleton<DebugService>();
            services.AddSingleton<RoslynService>();
            services.AddSingleton<IAgent>(sp => sp.GetRequiredService<RoslynService>());
            services.AddSingleton<RagService>();
            services.AddSingleton<IRagService>(sp => sp.GetRequiredService<RagService>());
            services.AddSingleton(sp => new UpdateService(sp.GetRequiredService<System.Net.Http.HttpClient>(), "https://github.com/john79nl/oxy2/blob/main/update.json"));

            services.AddSingleton<IAgent, FileAgent>();
            services.AddSingleton<IAgent, ShellAgent>();
            services.AddSingleton<IAgent, KnowledgeAgent>();
            services.AddSingleton<IAgent, GravityAgent>();
            services.AddSingleton<IAgent, SearchAgent>();

            services.AddSingleton<ReasoningRouter>();
            services.AddSingleton<IRouterService>(sp => sp.GetRequiredService<ReasoningRouter>());
            services.AddSingleton<KnowledgeService>();
            services.AddSingleton<IKnowledgeService>(sp => sp.GetRequiredService<KnowledgeService>());
            services.AddSingleton<IntentRouter>();
            services.AddSingleton<TaskPlanner>();
            services.AddSingleton<Orchestrator>();
            services.AddSingleton<IAgentService>(sp => sp.GetRequiredService<Orchestrator>().AsService());

            services.AddTransient<Form1>();
            services.AddTransient<Form3>();
            services.AddTransient<Form5>();
        }

        private static void RegisterDynamicAgents(ReasoningRouter router, ServiceProvider sp)
        {
        var agentsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "agents");
            var definitions = AgentLoader.LoadFromDirectory(agentsDir);

            var model = sp.GetRequiredService<IModelClient>();
            var settings = sp.GetRequiredService<ISettingsService>();

            foreach (var def in definitions)
            {
                if (router.GetAgent(def.Name) != null) continue;
                var dynamicAgent = new DynamicAgent(def, model, router, settings);
                router.RegisterAgent(def.Name, dynamicAgent);
            }
        }

        private static async Task RegisterMcpAgents(ReasoningRouter router, ServiceProvider sp)
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            var mcpServers = settings.Current.McpServers;
            if (mcpServers == null || mcpServers.Count == 0) return;

            foreach (var kv in mcpServers)
            {
                var config = kv.Value;
                config.Name = kv.Key;
                if (string.IsNullOrWhiteSpace(config.Command)) continue;
                if (router.GetAgent(config.Name) != null) continue;

                try
                {
                    var mcpAgent = new McpAgent(config);
                    await mcpAgent.InitializeAsync(CancellationToken.None);
                    router.RegisterAgent(config.Name, mcpAgent);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"MCP agent '{config.Name}' failed: {ex.Message}");
                }
            }
        }
    }
}

