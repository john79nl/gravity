using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Gravity.Core
{
    /// <summary>
    /// Integrates Gravity into Windows 10/11:
    ///   1. "Send to" Explorer context menu shortcut (%APPDATA%\Microsoft\Windows\SendTo)
    ///   2. Right-click context menu "Open with Gravity" for files and folders (HKCU Registry)
    ///   3. Single-Instance Named Pipe IPC server/client
    /// </summary>
    public class WindowsIntegrationService : IPlatformIntegrationService
    {
        private const string MutexName = "Gravity_SingleInstance_Mutex_987A";
        private const string PipeName  = "Gravity_IPC_Pipe_987A";

        /// <summary>Raised when a file or folder path is received via IPC from another Gravity invocation.</summary>
        public event Action<string>? OnFileReceived;
        public static event Action<string>? OnFileReceivedStatic;

        private static Mutex? _mutex;
        private static CancellationTokenSource? _pipeCts;

        /// <summary>
        /// Checks if another instance of Gravity is already running.
        /// If running, sends targetPath to the running instance and returns true.
        /// If not running, initializes single instance mutex & pipe server, returns false.
        /// </summary>
        public bool CheckAndSendToRunningInstance(string? targetPath)
        {
            _mutex = new Mutex(true, MutexName, out bool isNewInstance);
            if (!isNewInstance)
            {
                // Another instance is already running — send targetPath over IPC pipe
                if (!string.IsNullOrWhiteSpace(targetPath))
                {
                    try
                    {
                        using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                        client.Connect(1500); // 1.5s timeout
                        var bytes = Encoding.UTF8.GetBytes(targetPath);
                        client.Write(bytes, 0, bytes.Length);
                        client.Flush();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[IPC] Send to instance failed: {ex.Message}");
                    }
                }
                return true; // Is running — calling process should exit
            }

            // Primary instance — start background pipe server
            StartPipeServer();
            return false;
        }

        private void StartPipeServer()
        {
            _pipeCts = new CancellationTokenSource();
            var token = _pipeCts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                        await server.WaitForConnectionAsync(token);

                        using var ms = new MemoryStream();
                        await server.CopyToAsync(ms, token);
                        var path = Encoding.UTF8.GetString(ms.ToArray()).Trim();

                        if (!string.IsNullOrEmpty(path))
                        {
                            OnFileReceived?.Invoke(path);
                            OnFileReceivedStatic?.Invoke(path);
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[IPC Server] Error: {ex.Message}");
                        await Task.Delay(500, token);
                    }
                }
            }, token);
        }

        /// <summary>
        /// Registers Gravity in Windows 10/11 Shell:
        ///   - HKCU\Software\Classes\*\shell\Gravity  ("Open with Gravity")
        ///   - HKCU\Software\Classes\Directory\shell\Gravity
        ///   - %APPDATA%\Microsoft\Windows\SendTo\Gravity.cmd
        /// </summary>
        public void RegisterShellIntegration() => EnsureRegistered();

        public static void EnsureRegistered()
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

                // ── 1. Right-Click Context Menu for Files ───────────────────────
                RegisterShellContextMenu(@"Software\Classes\*\shell\Gravity", "Open with Gravity", exePath);

                // ── 2. Right-Click Context Menu for Folders ─────────────────────
                RegisterShellContextMenu(@"Software\Classes\Directory\shell\Gravity", "Open with Gravity", exePath);

                // ── 3. Windows "Send To" Menu Shortcut ──────────────────────────
                EnsureSendToShortcut(exePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WindowsIntegration] Registration error: {ex.Message}");
            }
        }

        private static void RegisterShellContextMenu(string subKeyPath, string menuText, string exePath)
        {
            using var key = Registry.CurrentUser.CreateSubKey(subKeyPath);
            if (key != null)
            {
                key.SetValue("", menuText);
                key.SetValue("Icon", $"\"{exePath}\"");

                using var commandKey = key.CreateSubKey("command");
                commandKey?.SetValue("", $"\"{exePath}\" \"%1\"");
            }
        }

        private static void EnsureSendToShortcut(string exePath)
        {
            try
            {
                var sendToFolder = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
                if (string.IsNullOrEmpty(sendToFolder) || !Directory.Exists(sendToFolder)) return;

                var cmdPath = Path.Combine(sendToFolder, "Gravity.cmd");
                var cmdContent = $"@echo off\r\nstart \"\" \"{exePath}\" %1\r\n";
                File.WriteAllText(cmdPath, cmdContent);
            }
            catch { }
        }
    }
}
