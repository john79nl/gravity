using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Gravity.Core
{
    public class GitService
    {
        private readonly IProjectContext _projectContext;

        public GitService(IProjectContext projectContext)
        {
            _projectContext = projectContext ?? throw new ArgumentNullException(nameof(projectContext));
        }

        private string? RepoDir => _projectContext.ProjectDirectory;

        public bool IsGitRepo()
        {
            var dir = RepoDir;
            if (string.IsNullOrEmpty(dir)) return false;
            return Directory.Exists(Path.Combine(dir, ".git"));
        }

        private async Task<string?> RunGitAsync(string args)
        {
            var dir = RepoDir;
            if (string.IsNullOrEmpty(dir)) return null;
            try
            {
                var psi = new ProcessStartInfo("git", $"-C \"{dir}\" {args}")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return null;
                var output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();
                return proc.ExitCode == 0 ? output : null;
            }
            catch { return null; }
        }

        public async Task<Dictionary<string, string>> GetStatusAsync()
        {
            var result = new Dictionary<string, string>();
            if (!IsGitRepo()) return result;

            var output = await RunGitAsync("status --porcelain");
            if (string.IsNullOrEmpty(output)) return result;

            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length < 3) continue;
                var statusChars = line.Substring(0, 2);
                var filePath = line.Substring(3).Trim();

                string status;
                if (statusChars == "??")
                    status = "?";
                else if (statusChars.Contains('U'))
                    status = "U";
                else if (statusChars[0] != ' ' && statusChars[1] != ' ')
                    status = "MM";
                else if (statusChars[0] != ' ')
                    status = statusChars[0].ToString();
                else if (statusChars[1] != ' ')
                    status = statusChars[1].ToString();
                else
                    continue;

                result[filePath] = status;
            }

            return result;
        }

        public async Task<string?> GetFileDiffAsync(string filePath)
        {
            if (!IsGitRepo()) return null;

            var relPath = GetRelativePath(filePath);
            if (string.IsNullOrEmpty(relPath)) return null;

            var sb = new System.Text.StringBuilder();

            var unstaged = await RunGitAsync($"diff \"{relPath}\"");
            if (!string.IsNullOrEmpty(unstaged?.Trim()))
            {
                sb.AppendLine("[git diff - unstaged changes]");
                sb.AppendLine(unstaged);
            }

            var staged = await RunGitAsync($"diff --cached \"{relPath}\"");
            if (!string.IsNullOrEmpty(staged?.Trim()))
            {
                sb.AppendLine("[git diff --staged]");
                sb.AppendLine(staged);
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }

        private string? GetRelativePath(string fullPath)
        {
            var dir = RepoDir;
            if (string.IsNullOrEmpty(dir)) return null;
            if (Path.IsPathRooted(fullPath))
            {
                try { return Path.GetRelativePath(dir, fullPath).Replace('\\', '/'); }
                catch { return null; }
            }
            return fullPath.Replace('\\', '/');
        }
    }
}
