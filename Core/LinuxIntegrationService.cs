using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Gravity.Core
{
    /// <summary>
    /// Integrates Gravity into Linux (X11 / Wayland desktop environments):
    ///   1. Unix Domain Socket IPC (/tmp/gravity_ipc.sock) for single instance detection.
    ///   2. XDG Desktop Entry (~/.local/share/applications/gravity.desktop) for Linux file manager "Open with" context menus.
    /// </summary>
    public class LinuxIntegrationService : IPlatformIntegrationService
    {
        private const string SocketPath = "/tmp/gravity_ipc.sock";
        public event Action<string>? OnFileReceived;

        private CancellationTokenSource? _cts;

        public bool CheckAndSendToRunningInstance(string? targetPath)
        {
            if (File.Exists(SocketPath))
            {
                try
                {
                    using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    var endpoint = new UnixDomainSocketEndPoint(SocketPath);
                    socket.Connect(endpoint);

                    if (!string.IsNullOrWhiteSpace(targetPath))
                    {
                        var bytes = Encoding.UTF8.GetBytes(targetPath);
                        socket.Send(bytes);
                    }
                    return true; // Running instance received argument — exit calling process
                }
                catch
                {
                    // Stale socket file — delete and start new server
                    try { File.Delete(SocketPath); } catch { }
                }
            }

            StartUnixSocketServer();
            return false;
        }

        private void StartUnixSocketServer()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Task.Run(async () =>
            {
                try
                {
                    if (File.Exists(SocketPath)) File.Delete(SocketPath);

                    using var listenSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    var endpoint = new UnixDomainSocketEndPoint(SocketPath);
                    listenSocket.Bind(endpoint);
                    listenSocket.Listen(5);

                    while (!token.IsCancellationRequested)
                    {
                        var handler = await listenSocket.AcceptAsync(token);
                        using var ms = new MemoryStream();
                        var buffer = new byte[1024];
                        int bytesRead;

                        while ((bytesRead = await handler.ReceiveAsync(buffer, SocketFlags.None, token)) > 0)
                        {
                            ms.Write(buffer, 0, bytesRead);
                        }

                        var path = Encoding.UTF8.GetString(ms.ToArray()).Trim();
                        if (!string.IsNullOrEmpty(path))
                        {
                            OnFileReceived?.Invoke(path);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Linux IPC Server] Error: {ex.Message}");
                }
            }, token);
        }

        public void RegisterShellIntegration()
        {
            try
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var appsDir = Path.Combine(home, ".local", "share", "applications");
                Directory.CreateDirectory(appsDir);

                var desktopFile = Path.Combine(appsDir, "gravity.desktop");
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "gravity";

                var content = $@"Custom Desktop Entry]
Type=Application
Name=Gravity AI
Comment=Multi-Agent AI Desktop Workspace
Exec={exePath} %f
Icon=utilities-terminal
Terminal=false
Categories=Development;IDE;
MimeType=text/plain;application/x-docx;
";
                File.WriteAllText(desktopFile, content);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Linux Integration] Desktop entry error: {ex.Message}");
            }
        }
    }
}
