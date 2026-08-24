using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Gravity.Core
{
    public class FileSearchService
    {
        private readonly IProjectContext _projectContext;
        private readonly string? _overrideRoot;
        private readonly string[] _excludeDirs = { "bin", "obj", ".vs", ".git", ".gemini", "node_modules" };

        public string RootPath => _overrideRoot ?? _projectContext.ProjectDirectory ?? "";

        public event Action<string>? OnFileReading;
        public event Action<string>? OnFileRead;
        public event Action<string>? OnFileWriting;
        public event Action<string>? OnFileWritten;

        public FileSearchService(IProjectContext projectContext)
        {
            _projectContext = projectContext ?? throw new ArgumentNullException(nameof(projectContext));
        }

        // Back-compat constructor: allow passing a root path directly (used by tests)
        public FileSearchService(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath)) throw new ArgumentNullException(nameof(rootPath));
            _overrideRoot = Path.GetFullPath(rootPath);
            // create a default project context to satisfy other APIs
            _projectContext = new ProjectContext();
        }

        public Task<List<string>> EnumerateProjectFilesAsync(string projectFilePath, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                var results = new List<string>();
                bool isFolder = !string.IsNullOrWhiteSpace(projectFilePath) && Directory.Exists(projectFilePath);
                bool isFile = !string.IsNullOrWhiteSpace(projectFilePath) && File.Exists(projectFilePath);

                if (!isFile && !isFolder)
                    return results;

                var root = RootPath;
                var projectDir = isFolder ? projectFilePath : (Path.GetDirectoryName(projectFilePath) ?? root);
                try
                {
                    if (isFolder)
                    {
                        return Directory.EnumerateFiles(projectDir, "*.*", SearchOption.AllDirectories)
                            .Where(f => !_excludeDirs.Any(d => f.Contains(Path.DirectorySeparatorChar + d + Path.DirectorySeparatorChar) || f.Contains(Path.AltDirectorySeparatorChar + d + Path.AltDirectorySeparatorChar)))
                            .Select(Path.GetFullPath)
                            .ToList();
                    }

                    var doc = XDocument.Load(projectFilePath);
                    var isSdk = doc.Root?.Attribute("Sdk") != null;

                    if (isSdk)
                    {
                        // Modern SDK-style project: everything in the folder is included by default
                        return Directory.EnumerateFiles(projectDir, "*.*", SearchOption.AllDirectories)
                            .Where(f => !_excludeDirs.Any(d => f.Contains(Path.DirectorySeparatorChar + d + Path.DirectorySeparatorChar) || f.Contains(Path.AltDirectorySeparatorChar + d + Path.AltDirectorySeparatorChar)))
                            .Select(Path.GetFullPath)
                            .ToList();
                    }

                    // Legacy style: parse XML Includes
                    var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
                    var includes = new List<string>();
                    foreach (var elemName in new[] { "Compile", "None", "Content" })
                    {
                        foreach (var el in doc.Descendants(ns + elemName))
                        {
                            var inc = el.Attribute("Include")?.Value;
                            if (!string.IsNullOrEmpty(inc)) includes.Add(inc);
                        }
                    }

                    foreach (var inc in includes)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (inc.IndexOfAny(new[] { '*', '?' }) >= 0)
                        {
                            var fullPattern = Path.Combine(projectDir, inc);
                            var dir = Path.GetDirectoryName(fullPattern) ?? projectDir;
                            var pattern = Path.GetFileName(fullPattern);
                            if (Directory.Exists(dir))
                            {
                                var found = Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly);
                                foreach (var f in found) results.Add(Path.GetFullPath(f));
                            }
                        }
                        else
                        {
                            var abs = Path.IsPathRooted(inc) ? inc : Path.Combine(projectDir, inc);
                            if (File.Exists(abs)) results.Add(Path.GetFullPath(abs));
                        }
                    }

                    return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }
                catch
                {
                    return results;
                }
            }, ct);
        }

        public Task<List<string>> ListDirectoryAsync(string relativePath, bool recursive, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                var root = RootPath;
                var targetDir = ResolvePath(relativePath);
                if (!Directory.Exists(targetDir)) return new List<string>();

                var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var results = new List<string>();

                // Get Subdirectories
                var dirs = Directory.EnumerateDirectories(targetDir, "*", option)
                    .Where(d => !_excludeDirs.Any(x => d.EndsWith(Path.DirectorySeparatorChar + x) || d.Contains(Path.DirectorySeparatorChar + x + Path.DirectorySeparatorChar)))
                    .Select(d => "[DIR] " + Path.GetRelativePath(root, d));
                
                results.AddRange(dirs);

                // Get Files
                var files = Directory.EnumerateFiles(targetDir, "*.*", option)
                    .Where(f => !_excludeDirs.Any(x => f.Contains(Path.DirectorySeparatorChar + x + Path.DirectorySeparatorChar)))
                    .Select(f => "[FILE] " + Path.GetRelativePath(root, f));

                results.AddRange(files);

                return results.ToList();
            }, ct);
        }

        public Task<List<string>> SearchFilesAsync(string pattern, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                var root = RootPath;
                var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                    .Where(f => !_excludeDirs.Any(d => f.Contains(Path.DirectorySeparatorChar + d + Path.DirectorySeparatorChar)))
                    .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                             || p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var results = new List<string>();

                // Special case: '*' means list all files (no line matches needed)
                if (pattern == "*")
                {
                    return files.Select(Path.GetFullPath).ToList();
                }

                // Clean pattern: strip leading/trailing * for simpler matching
                var cleanPattern = pattern.Trim('*');
                if (string.IsNullOrWhiteSpace(cleanPattern)) return results;

                Regex? rx = null;
                try { rx = new Regex(cleanPattern, RegexOptions.IgnoreCase); } catch { }

                const int maxMatches = 200;

                foreach (var f in files)
                {
                    ct.ThrowIfCancellationRequested();
                    if (results.Count >= maxMatches) break;

                    var relativePath = Path.GetRelativePath(root, f);
                    string[] lines;
                    try { lines = File.ReadAllLines(f); }
                    catch { continue; }

                    // Also match on filename itself
                    var fileName = Path.GetFileName(f);
                    bool fileNameMatch = (rx != null && rx.IsMatch(fileName))
                                     || fileName.IndexOf(cleanPattern, StringComparison.OrdinalIgnoreCase) >= 0;

                    bool anyLineMatched = false;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (results.Count >= maxMatches) break;

                        var line = lines[i];
                        bool lineMatch = (rx != null && rx.IsMatch(line))
                                      || line.IndexOf(cleanPattern, StringComparison.OrdinalIgnoreCase) >= 0;

                        if (lineMatch)
                        {
                            results.Add($"{relativePath}:{i + 1}: {line.Trim()}");
                            anyLineMatched = true;
                        }
                    }

                    // If the filename matched but no lines did (e.g. binary or empty file), add the file itself
                    if (fileNameMatch && !anyLineMatched)
                    {
                        results.Add($"{relativePath}:1: [filename match]");
                    }
                }

                return results;
            }, ct);
        }


        public string ResolvePath(string relativePath)
        {
            var root = RootPath;
            if (string.IsNullOrWhiteSpace(relativePath)) return root;

            relativePath = relativePath.Trim();

            // Fix leading separator before drive letter (e.g. "\C:\..." -> "C:\...")
            if (relativePath.Length >= 3 && 
                (relativePath[0] == '/' || relativePath[0] == '\\') && 
                char.IsLetter(relativePath[1]) && 
                relativePath[2] == ':')
            {
                relativePath = relativePath.Substring(1);
            }

            // Fix double drive letter prefix (e.g. "C:\C:\..." -> "C:\...")
            if (relativePath.Length >= 6 && 
                char.IsLetter(relativePath[0]) && relativePath[1] == ':' && (relativePath[2] == '\\' || relativePath[2] == '/') &&
                char.IsLetter(relativePath[3]) && relativePath[4] == ':' && (relativePath[5] == '\\' || relativePath[5] == '/'))
            {
                relativePath = relativePath.Substring(3);
            }

            if (Path.IsPathRooted(relativePath)) return Path.GetFullPath(relativePath);

            relativePath = relativePath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            
            var dirName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrEmpty(dirName))
            {
                var normalizedRel = relativePath.Replace('\\', '/');
                while (normalizedRel.StartsWith(dirName + "/", StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = relativePath.Substring(dirName.Length + 1).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    normalizedRel = relativePath.Replace('\\', '/');
                }
            }

            return Path.GetFullPath(Path.Combine(root, relativePath));
        }

        public Task<string> ReadFileAsync(string relativePath, CancellationToken ct)
        {
            var abs = ResolvePath(relativePath);
            return Task.Run(() =>
            {
                // If the resolved path is a directory, surface a clear error instead of letting File.ReadAllText throw
                if (Directory.Exists(abs))
                    return $"Path '{abs}' is a directory and cannot be read as a file.";

                if (!File.Exists(abs))
                    return $"File not found: {abs}";

                try
                {
                    OnFileReading?.Invoke(abs);
                    
                    int retries = 3;
                    while (true)
                    {
                        try
                        {
                            using var fs = new FileStream(abs, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            using var sr = new StreamReader(fs, Encoding.UTF8);
                            return sr.ReadToEnd();
                        }
                        catch (IOException) when (retries > 1)
                        {
                            retries--;
                            Thread.Sleep(50);
                        }
                    }
                }
                catch (IOException ioEx)
                {
                    return $"File is currently locked or in use by another process: {ioEx.Message}";
                }
                catch (Exception ex)
                {
                    return $"Error reading file {abs}: {ex.Message}";
                }
                finally
                {
                    OnFileRead?.Invoke(abs);
                }
            }, ct);
        }

        public Task WriteFileAsync(string relativePath, string content, CancellationToken ct)
        {
            var abs = ResolvePath(relativePath);
            return Task.Run(() =>
            {
                var dir = Path.GetDirectoryName(abs);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(abs))
                {
                    var bak = abs + ".bak." + DateTime.UtcNow.Ticks;
                    File.Copy(abs, bak);
                }
                
                try
                {
                    OnFileWriting?.Invoke(abs);
                    File.WriteAllText(abs, content);
                }
                finally
                {
                    OnFileWritten?.Invoke(abs);
                }
            }, ct);
        }

        public async Task<List<string>> GetFileChunksAsync(string relativePath, int chunkSize = 1000, CancellationToken ct = default)
        {
            var content = await ReadFileAsync(relativePath, ct);
            if (string.IsNullOrEmpty(content)) return new List<string>();

            var chunks = new List<string>();
            for (int i = 0; i < content.Length; i += chunkSize)
            {
                var length = Math.Min(chunkSize, content.Length - i);
                chunks.Add(content.Substring(i, length));
            }
            return chunks;
        }
    }
}
