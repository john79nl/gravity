using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Gravity.Models;
using System.Reflection;
using System.IO;
using System.Diagnostics;

namespace Gravity.Core
{
    public class UpdateService
    {
        private readonly HttpClient _httpClient;
        private readonly string _manifestUrl;

        public UpdateService(HttpClient httpClient, string manifestUrl)
        {
            _httpClient = httpClient;
            _manifestUrl = manifestUrl;
        }

        public async Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            try
            {
                var manifest = await _httpClient.GetFromJsonAsync<UpdateManifest>(_manifestUrl);
                if (manifest == null) return new UpdateCheckResult { UpdateAvailable = false };

                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                if (Version.TryParse(manifest.Version, out var latestVersion))
                {
                    if (latestVersion > currentVersion)
                    {
                        return new UpdateCheckResult
                        {
                            UpdateAvailable = true,
                            LatestVersion = manifest.Version,
                            DownloadUrl = manifest.DownloadUrl,
                            ReleaseNotes = manifest.ReleaseNotes
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error here if logger is available
                Console.WriteLine($"Update check failed: {ex.Message}");
            }

            return new UpdateCheckResult { UpdateAvailable = false };
        }

        public async Task<bool> DownloadAndApplyUpdateAsync(string downloadUrl)
        {
            try
            {
                // 1. Download payload
                var zipPath = Path.Combine(Path.GetTempPath(), "GravityUpdate.zip");
                var response = await _httpClient.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();
                await using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs);
                await fs.DisposeAsync();

                // 2. Generate Bootstrapper Script
                var targetDir = AppDomain.CurrentDomain.BaseDirectory;
                var currentPid = Environment.ProcessId;
                var scriptPath = Path.Combine(Path.GetTempPath(), "update_gravity.ps1");

                var scriptContent = $@"
$ErrorActionPreference = 'Stop'
$pidToWait = {currentPid}
$zipPath = '{zipPath}'
$targetDir = '{targetDir}'
$exePath = Join-Path $targetDir 'Gravity.exe'

Write-Host 'Waiting for Gravity to exit...'
Wait-Process -Id $pidToWait -ErrorAction SilentlyContinue

Write-Host 'Extracting update...'
Expand-Archive -Path $zipPath -DestinationPath $targetDir -Force

Write-Host 'Cleaning up...'
Remove-Item -Path $zipPath -Force

Write-Host 'Restarting Gravity...'
Start-Process -FilePath $exePath
";
                await File.WriteAllTextAsync(scriptPath, scriptContent);

                // 3. Launch script and exit
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);

                // Assuming Windows Forms Application.Exit or Environment.Exit
                Environment.Exit(0);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Update failed: {ex.Message}");
                return false;
            }
        }
    }
}