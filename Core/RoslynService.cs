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
// Microsoft.CodeAnalysis.Completion requires Microsoft.CodeAnalysis.Features package (not referenced)
using System.Text;

namespace Gravity.Core
{
    public class RoslynService : IDisposable, IAgent
    {
        private static bool _msbuildRegistered = false;
        private MSBuildWorkspace? _workspace;
        private Project? _currentProject;
        private string? _lastProjectPath;

        public AgentDescriptor Descriptor { get; } = new AgentDescriptor
        {
            Name = "roslyn",
            Description = "Provides C# code analysis, diagnostics, symbol lookup, and reference tracking using MSBuild and Roslyn.",
            CanWrite = false,
            SupportedVerbs = new[] { "analyze_project", "get_file_symbols", "get_blast_radius", "get_file_diagnostics" }
        };

        public Task<AgentResult> ExecuteAsync(AgentRequest request, CancellationToken ct)
        {
            return Task.FromResult(new AgentResult 
            { 
                Success = false, 
                Output = "RoslynService verbs are only available via direct service method invocation." 
            });
        }

        public void InvalidateProjectCache()
        {
            _currentProject = null;
        }

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
            if (string.IsNullOrEmpty(projectPath)) return null;
            projectPath = Path.GetFullPath(projectPath);

            if (_workspace != null && _lastProjectPath == projectPath && _currentProject != null)
            {
                return _currentProject;
            }

            _workspace?.Dispose();
            _workspace = MSBuildWorkspace.Create();
            
            _workspace.WorkspaceFailed += (s, e) => {
                Log($"Workspace Warning: {e.Diagnostic.Message}");
            };

            try
            {
                Log($"Opening project: {projectPath}");
                if (Path.GetExtension(projectPath).Equals(".sln", StringComparison.OrdinalIgnoreCase))
                {
                    var solution = await _workspace.OpenSolutionAsync(projectPath);
                    _currentProject = solution.Projects.FirstOrDefault();
                }
                else
                {
                    _currentProject = await _workspace.OpenProjectAsync(projectPath);
                }

                if (_currentProject != null)
                {
                    Log($"Project loaded: {_currentProject.Name}. Documents: {_currentProject.Documents.Count()}");
                }
                _lastProjectPath = projectPath;
                return _currentProject;
            }
            catch (Exception ex)
            {
                Log($"Failed to open project: {ex.Message}");
                return null;
            }
        }

        private static readonly List<CompletionItem> StandardKeywordsAndTypes = new List<string>
        {
            "abstract", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch", "char", "class", "const", "continue",
            "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
            "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long",
            "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
            "readonly", "record", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string",
            "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
            "using", "virtual", "void", "volatile", "while", "yield",
            "Console", "Math", "String", "Int32", "Boolean", "Task", "List", "Dictionary", "Enumerable", "IEnumerable",
            "DateTime", "TimeSpan", "Guid", "Exception", "File", "Path", "Directory", "Thread", "Action", "Func", "StringBuilder"
        }.Select(k => new CompletionItem { Text = k, Description = $"[Keyword/Type] {k}" }).ToList();

        public async Task<List<CompletionItem>> GetCompletionsAsync(string projectPath, string filePath, int position)
        {
            var results = new List<CompletionItem>(StandardKeywordsAndTypes);
            try
            {
                var project = await GetOrLoadProjectAsync(projectPath);
                if (project != null)
                {
                    filePath = Path.GetFullPath(filePath);
                    var document = project.Documents.FirstOrDefault(d => d.FilePath != null && Path.GetFullPath(d.FilePath).Equals(filePath, StringComparison.OrdinalIgnoreCase));
                    if (document != null)
                    {
                        var root = await document.GetSyntaxRootAsync();
                        if (root != null)
                        {
                            var symbols = root.DescendantNodes()
                                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax>()
                                .Select(m => m switch
                                {
                                    Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax c => c.Identifier.Text,
                                    Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax md => md.Identifier.Text,
                                    Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax p => p.Identifier.Text,
                                    Microsoft.CodeAnalysis.CSharp.Syntax.InterfaceDeclarationSyntax i => i.Identifier.Text,
                                    _ => null
                                })
                                .Where(name => !string.IsNullOrEmpty(name))
                                .Distinct();

                            foreach (var s in symbols)
                            {
                                if (!results.Any(r => r.Text == s))
                                {
                                    results.Add(new CompletionItem { Text = s!, Description = $"[Project Symbol] {s}" });
                                }
                            }
                        }
                    }
                }
            }
            catch { /* best-effort */ }

            return results;
        }

        public Task<List<CompletionItem>> GetDefaultCompletionsAsync(string code, int position)
        {
            var results = new List<CompletionItem>(StandardKeywordsAndTypes);
            try
            {
                var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code ?? string.Empty);
                var root = tree.GetRoot();
                var identifiers = root.DescendantTokens()
                    .Where(t => t.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.IdentifierToken))
                    .Select(t => t.ValueText)
                    .Where(t => !string.IsNullOrWhiteSpace(t) && t.Length > 1)
                    .Distinct();

                foreach (var id in identifiers)
                {
                    if (!results.Any(r => r.Text == id))
                    {
                        results.Add(new CompletionItem { Text = id, Description = $"[Local Symbol] {id}" });
                    }
                }
            }
            catch { /* best effort */ }

            return Task.FromResult(results);
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

        /// <summary>
        /// Compiles <paramref name="code"/> in-memory within the loaded project context (Roslyn Solution)
        /// so that cross-file and cross-project references are fully preserved during live typing.
        /// </summary>
        public async Task<List<Diagnostic>> GetLiveDiagnosticsAsync(string projectPath, string filePath, string code)
        {
            try
            {
                if (!string.IsNullOrEmpty(projectPath) && File.Exists(projectPath))
                {
                    var project = await GetOrLoadProjectAsync(projectPath);
                    if (project != null && !string.IsNullOrEmpty(filePath))
                    {
                        var fullPath = Path.GetFullPath(filePath);
                        // Search in current project and all projects in the solution
                        Document? doc = project.Documents.FirstOrDefault(d => d.FilePath != null && Path.GetFullPath(d.FilePath).Equals(fullPath, StringComparison.OrdinalIgnoreCase));
                        if (doc == null && project.Solution != null)
                        {
                            foreach (var p in project.Solution.Projects)
                            {
                                doc = p.Documents.FirstOrDefault(d => d.FilePath != null && Path.GetFullPath(d.FilePath).Equals(fullPath, StringComparison.OrdinalIgnoreCase));
                                if (doc != null) break;
                            }
                        }

                        if (doc != null)
                        {
                            var updatedSolution = doc.Project.Solution.WithDocumentText(doc.Id, SourceText.From(code));
                            var updatedProject = updatedSolution.GetProject(doc.Project.Id);
                            if (updatedProject != null)
                            {
                                _currentProject = updatedProject;
                                var compilation = await updatedProject.GetCompilationAsync();
                                if (compilation != null)
                                {
                                    var fileDiagnostics = compilation.GetDiagnostics().Where(d =>
                                    {
                                        if (d.Severity < DiagnosticSeverity.Warning || !d.Location.IsInSource) return false;
                                        var pathStr = d.Location.GetLineSpan().Path;
                                        return pathStr != null && Path.GetFullPath(pathStr).Equals(fullPath, StringComparison.OrdinalIgnoreCase);
                                    }).ToList();

                                    return fileDiagnostics;
                                }
                            }
                        }
                    }
                }

                // Fallback for standalone/isolated C# files outside project structure
                return await Task.Run(() =>
                {
                    try
                    {
                        var syntaxTree = CSharpSyntaxTree.ParseText(code);
                        IEnumerable<MetadataReference> references;
                        if (_currentProject != null && _currentProject.MetadataReferences.Any())
                        {
                            references = _currentProject.MetadataReferences;
                        }
                        else
                        {
                            references = AppDomain.CurrentDomain.GetAssemblies()
                                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                                .Select(a => MetadataReference.CreateFromFile(a.Location))
                                .Cast<MetadataReference>();
                        }

                        var compilation = CSharpCompilation.Create(
                            "LiveCheck",
                            new[] { syntaxTree },
                            references,
                            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, reportSuppressedDiagnostics: false));

                        return compilation.GetDiagnostics()
                            .Where(d => d.Severity >= DiagnosticSeverity.Warning && d.Location.IsInSource)
                            .ToList();
                    }
                    catch { return new List<Diagnostic>(); }
                });
            }
            catch (Exception ex)
            {
                Log($"Roslyn Live Diagnostics Error: {ex.Message}");
                return new List<Diagnostic>();
            }
        }

        public Task<List<Diagnostic>> GetLiveDiagnosticsAsync(string code) =>
            GetLiveDiagnosticsAsync(_lastProjectPath ?? "", "", code);


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

                var declarations = root.DescendantNodes().Where(n => n is MethodDeclarationSyntax || n is ClassDeclarationSyntax);
                
                foreach (var decl in declarations)
                {
                    var symbol = semanticModel.GetDeclaredSymbol(decl);
                    if (symbol == null) continue;

                    var references = await SymbolFinder.FindReferencesAsync(symbol, _workspace.CurrentSolution);
                    foreach (var reference in references)
                    {
                        foreach (var loc in reference.Locations)
                        {
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

    public class CompletionItem
    {
        public string Text { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
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