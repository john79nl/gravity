using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace Gravity.Core
{
    public class BuildService
    {
        public async Task<BuildResult> RunDotnetBuildAsync(string projectOrFolder, int timeoutMs = 120000)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{projectOrFolder}\" --no-restore",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var sb = new StringBuilder();
            using var proc = new Process { StartInfo = psi };
            proc.OutputDataReceived += (s, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) sb.AppendLine(e.Data); };

            var tcs = new TaskCompletionSource<int>();
            proc.EnableRaisingEvents = true;
            proc.Exited += (s, e) => tcs.TrySetResult(proc.ExitCode);

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            if (completedTask != tcs.Task)
            {
                try { proc.Kill(); } catch { }
                return new BuildResult { ExitCode = -1, Output = sb.ToString() + "\n[Timed out]" };
            }

            var exit = await tcs.Task;
            return new BuildResult { ExitCode = exit, Output = sb.ToString() };
        }
    }
}
