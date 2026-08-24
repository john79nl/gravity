using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FindSymbols;
using System.Text;

namespace Gravity.Core
{
    public class RoslynService : IDisposable
    {
        private static bool _msbuildRegistered = false;
        private MSBuildWorkspace? _workspace;
        private Project? _currentProject;
        private string? _lastProjectPath;

        public event Action<string>? OnDiagnosticMessage;

        private void Log(string msg) => OnDiagnosticMessage?.Invoke(msg);

        private void EnsureMSBuild()
        {
            if (!_msbuildRegistered)
            {
                try
                {
                    if (!MSBuildLocator.IsRegistered)
                    {
                        var instances = MSBuildLocator.QueryVisualStudioInstances().ToList();
                        Log($"Found {instances.Count} MSBuild instances.");
                        foreach(var inst in instances) Log($"- {inst.Name} ({inst.Version}) at {inst.MSBuildPath}");

                        if (instances.Any())
                        {
                            var best = instances.OrderByDescending(i => i.Version).First();
                            Log($"Registering {best.Name}...");
                            MSBuildLocator.RegisterInstance(best);
                        }
                        else
                        {
                            Log("No VS instances found, trying RegisterDefaults...");
                            MSBuildLocator.RegisterDefaults();
                        }
                    }
                    _msbuildRegistered = true;
                }
                catch (Exception ex) { Log($"MSBuild Registration Error: {ex.Message}"); }
            }
        }

        public async Task<Project?> GetOrLoadProjectAsync(string projectPath)
        {
            EnsureMSBuild();
            projectPath = Path.GetFullPath(projectPath);

            if (_workspace != null && _lastProjectPath == projectPath && _currentProject != null)
            {
                return _currentProject;
            }

            // Cleanup old
            _workspace?.Dispose();
            _workspace = MSBuildWorkspace.Create();
            
            _workspace.WorkspaceFailed += (s, e) => {
                Log($"Workspace Warning: {e.Diagnostic.Message}");
            };

            try
            {
                Log($"Opening project: {projectPath}");
                _currentProject = await _workspace.OpenProjectAsync(projectPath);
                Log($"Project loaded. Documents: {_currentProject.Documents.Count()}");
                _lastProjectPath = projectPath;
                return _currentProject;
            }
            catch (Exception ex)
            {
                Log($"Failed to open project: {ex.Message}");
                return null;
            }
        }

        public async Task<(bool Success, List<string>? Diagnostics, string? Error)> AnalyzeProjectAsync(string projectPath)
        {
            try
            {
                var project = await GetOrLoadProjectAsync(projectPath);
                if (project == null) return (false, null, "Failed to load project workspace.");

                var compilation = await project.GetCompilationAsync();
                if (compilation == null) return (false, null, "Failed to get compilation.");

                var diagnostics = compilation.GetDiagnostics();
                var results = diagnostics.Select(d => d.ToString()).ToList();
                return (true, results, null);
            }
            catch (Exception ex)
            {
                return (false, null, "Roslyn analysis failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Highly compressed overview of the project structure for AI reasoning.
        /// Returns a map of [Namespace] -> [Classes/Interfaces].
        /// </summary>
        public async Task<string> GetArchitectureOverviewAsync(string projectPath)
        {
            try
            {
                var project = await GetOrLoadProjectAsync(projectPath);
                if (project == null) return "Project not loaded.";

                var sb = new StringBuilder();
                sb.AppendLine("[PROJECT ARCHITECTURE OVERVIEW]");
                
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) return "Failed to compile for architecture map.";

                var namespaces = compilation.GlobalNamespace.GetNamespaceMembers();
                foreach (var ns in namespaces.Where(n => !n.Name.StartsWith("System") && !n.Name.StartsWith("Microsoft")))
                {
                    sb.AppendLine($"Namespace: {ns.ToDisplayString()}");
                    foreach (var type in ns.GetTypeMembers())
                    {
                        if (type.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Interface)
                        {
                            var kind = type.TypeKind == TypeKind.Interface ? "interface" : "class";
                            sb.AppendLine($"  - [{kind}] {type.Name}");
                            // Omitted member iteration to prevent context window saturation
                        }
                    }
                }
                return sb.ToString();
            }
            catch (Exception ex) { return $"Error mapping architecture: {ex.Message}"; }
        }

        public async Task<List<ClassifiedSpan>> GetClassificationSpansAsync(string projectPath, string filePath)
        {
            try
            {
                var project = await GetOrLoadProjectAsync(projectPath);
                if (project == null) return new List<ClassifiedSpan>();

                filePath = Path.GetFullPath(filePath);
                var document = project.Documents.FirstOrDefault(d => d.FilePath != null && Path.GetFullPath(d.FilePath).Equals(filePath, StringComparison.OrdinalIgnoreCase));
                
                if (document == null)
                {
                    Log($"Roslyn: Document not found: {filePath}");
                    return new List<ClassifiedSpan>();
                }

                var text = await document.GetTextAsync();
                var spans = await Classifier.GetClassifiedSpansAsync(document, new TextSpan(0, text.Length));
                return spans.ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Roslyn Classification Error: {ex.Message}");
                return new List<ClassifiedSpan>();
            }
        }

        public async Task<List<ClassifiedSpan>> GetSyntacticClassificationsAsync(string code)
        {
            try
            {
                using var workspace = new AdhocWorkspace();
                var solution = workspace.CurrentSolution;
                var projectId = ProjectId.CreateNewId();
                var documentId = DocumentId.CreateNewId(projectId);

                solution = solution.AddProject(projectId, "LiteProject", "LiteProject", LanguageNames.CSharp)
                                   .AddDocument(documentId, "LiteFile.cs", code);

                var document = solution.GetDocument(documentId);
                if (document == null) return new List<ClassifiedSpan>();

                var spans = await Classifier.GetClassifiedSpansAsync(document, new TextSpan(0, code.Length));
                return spans.ToList();
            }
            catch { return new List<ClassifiedSpan>(); }
        }

        public async Task<List<Diagnostic>> GetFileDiagnosticsAsync(string projectPath, string filePath)
        {
            try
            {
                var project = await GetOrLoadProjectAsync(projectPath);
                if (project == null) return new List<Diagnostic>();

                var compilation = await project.GetCompilationAsync();
                if (compilation == null) return new List<Diagnostic>();

                filePath = Path.GetFullPath(filePath);
                var diagnostics = compilation.GetDiagnostics();
                var fileDiagnostics = diagnostics.Where(d => {
                    var path = d.Location.GetLineSpan().Path;
                    return path != null && Path.GetFullPath(path).Equals(filePath, StringComparison.OrdinalIgnoreCase);
                }).ToList();

                Log($"Roslyn: File Diagnostics={fileDiagnostics.Count}, Total Errors={diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error)}");
                foreach (var d in fileDiagnostics.Take(10))
                {
                    Log($"- [{d.Severity}] Line {d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.GetMessage()}");
                }

                return fileDiagnostics;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Roslyn Diagnostics Error: {ex.Message}");
                return new List<Diagnostic>();
            }
        }

        public async Task<(bool Success, List<SymbolInfo>? Symbols, string? Error)> GetFileSymbolsAsync(string projectPath, string relativeFilePath)
        {
            try
            {
                var project = await GetOrLoadProjectAsync(projectPath);
                if (project == null) return (false, null, "Failed to load project.");

                var filePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath) ?? "", relativeFilePath));
                var document = project.Documents.FirstOrDefault(d => d.FilePath != null && Path.GetFullPath(d.FilePath).Equals(filePath, StringComparison.OrdinalIgnoreCase));
                
                if (document == null) return (false, null, $"File '{relativeFilePath}' not found.");

                var root = await document.GetSyntaxRootAsync();
                if (root == null) return (false, null, "Failed to get syntax root.");

                var symbols = new List<SymbolInfo>();
                foreach (var node in root.DescendantNodes())
                {
                    if (node is ClassDeclarationSyntax cds) symbols.Add(new SymbolInfo { Name = cds.Identifier.Text, Type = "Class", Line = GetLine(cds) });
                    else if (node is MethodDeclarationSyntax mds) symbols.Add(new SymbolInfo { Name = mds.Identifier.Text, Type = "Method", Line = GetLine(mds) });
                    else if (node is PropertyDeclarationSyntax pds) symbols.Add(new SymbolInfo { Name = pds.Identifier.Text, Type = "Property", Line = GetLine(pds) });
                    else if (node is InterfaceDeclarationSyntax ids) symbols.Add(new SymbolInfo { Name = ids.Identifier.Text, Type = "Interface", Line = GetLine(ids) });
                }

                return (true, symbols, null);
            }
            catch (Exception ex) { return (false, null, ex.Message); }
        }

        public async Task<List<ImpactInfo>> GetBlastRadiusAsync(string projectPath, string relativeFilePath)
        {
            var impacts = new List<ImpactInfo>();
            try
            {
                var project = await GetOrLoadProjectAsync(projectPath);
                if (project == null || _workspace == null) return impacts;

                var filePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath) ?? "", relativeFilePath));
                var document = project.Documents.FirstOrDefault(d => d.FilePath != null && Path.GetFullPath(d.FilePath).Equals(filePath, StringComparison.OrdinalIgnoreCase));
                if (document == null) return impacts;

                var semanticModel = await document.GetSemanticModelAsync();
                if (semanticModel == null) return impacts;

                var root = await document.GetSyntaxRootAsync();
                if (root == null) return impacts;

                // Find all class and method declarations in the file
                var declarations = root.DescendantNodes().Where(n => n is MethodDeclarationSyntax || n is ClassDeclarationSyntax);
                
                foreach (var decl in declarations)
                {
                    var symbol = semanticModel.GetDeclaredSymbol(decl);
                    if (symbol == null) continue;

                    // Find all references in the solution
                    var references = await SymbolFinder.FindReferencesAsync(symbol, _workspace.CurrentSolution);
                    foreach (var reference in references)
                    {
                        foreach (var loc in reference.Locations)
                        {
                            // Ignore references in the same file
                            if (loc.Document.Id == document.Id) continue;

                            impacts.Add(new ImpactInfo
                            {
                                SymbolName = symbol.Name,
                                DependentFile = loc.Document.Name,
                                DependentPath = loc.Document.FilePath ?? "unknown",
                                Line = loc.Location.GetLineSpan().StartLinePosition.Line + 1
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Blast Radius Error: {ex.Message}");
            }
            return impacts.OrderBy(i => i.DependentFile).ToList();
        }

        private int GetLine(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

        public void Dispose()
        {
            _workspace?.Dispose();
        }
    }

    public class SymbolInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int Line { get; set; }
        public override string ToString() => $"[{Type}] {Name} (Line {Line})";
    }

    public class ImpactInfo
    {
        public string SymbolName { get; set; } = string.Empty;
        public string DependentFile { get; set; } = string.Empty;
        public string DependentPath { get; set; } = string.Empty;
        public int Line { get; set; }
    }
}
