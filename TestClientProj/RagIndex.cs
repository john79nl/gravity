using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Gravity.Core
{
    public class RagChunk
    {
        public string FilePath { get; set; } = string.Empty;
        public int StartLine { get; set; }
        public string Content { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;  // e.g. "class ReasoningRouter"
    }

    /// <summary>
    /// Builds and queries a local keyword-based index of workspace source files.
    /// Uses TF-IDF inspired scoring — no embedding model or external API required.
    /// </summary>
    public class RagIndex
    {
        private readonly List<RagChunk> _chunks = new();
        private static readonly Regex _chunkBoundary = new Regex(
            @"^\s*(public|private|protected|internal|static|async|override|virtual|abstract).*?(class|interface|enum|record|void|Task|string|bool|int|List|IEnumerable|async Task)\s+\w+",
            RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly HashSet<string> _stopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "the","a","an","is","in","on","of","to","and","or","for","with","this","that","it",
            "are","be","as","at","by","from","var","new","return","using","public","private",
            "protected","static","void","string","bool","int","class","namespace","override"
        };

        public void IndexFile(string filePath, string content)
        {
            var lines = content.Split('\n');
            var chunks = SplitIntoChunks(filePath, lines);
            lock (_chunks)
            {
                _chunks.RemoveAll(c => c.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
                _chunks.AddRange(chunks);
            }
        }

        public void RemoveFile(string filePath)
        {
            lock (_chunks)
                _chunks.RemoveAll(c => c.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        }

        public void Clear()
        {
            lock (_chunks) _chunks.Clear();
        }

        public IReadOnlyList<RagChunk> RetrieveTopK(string query, int k = 5)
        {
            var queryTerms = Tokenize(query);
            if (!queryTerms.Any()) return Array.Empty<RagChunk>();

            List<RagChunk> snapshot;
            lock (_chunks) snapshot = _chunks.ToList();

            // Score each chunk: count of matching query terms weighted by position/symbol match
            var scored = snapshot.Select(chunk =>
            {
                var chunkTerms = Tokenize(chunk.Content + " " + chunk.Symbol);
                var termFreq = chunkTerms.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());

                double score = 0;
                foreach (var term in queryTerms)
                {
                    if (termFreq.TryGetValue(term, out var freq))
                        score += 1.0 + Math.Log(1 + freq);

                    // Bonus if term appears in the symbol/method name
                    if (chunk.Symbol.Contains(term, StringComparison.OrdinalIgnoreCase))
                        score += 2.0;
                }

                // Prefer shorter chunks (more focused)
                if (chunkTerms.Count > 0)
                    score /= Math.Log(2 + chunkTerms.Count);

                return (chunk, score);
            })
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(k)
            .Select(x => x.chunk)
            .ToList();

            return scored;
        }

        private List<RagChunk> SplitIntoChunks(string filePath, string[] lines)
        {
            var chunks = new List<RagChunk>();
            var currentLines = new List<string>();
            int chunkStart = 0;
            string currentSymbol = Path.GetFileNameWithoutExtension(filePath);

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                bool isBoundary = _chunkBoundary.IsMatch(line);

                if (isBoundary && currentLines.Count > 5)
                {
                    // Flush current chunk
                    chunks.Add(new RagChunk
                    {
                        FilePath = filePath,
                        StartLine = chunkStart + 1,
                        Content = string.Join("\n", currentLines),
                        Symbol = currentSymbol
                    });
                    currentLines.Clear();
                    chunkStart = i;
                    currentSymbol = ExtractSymbolName(line);
                }

                currentLines.Add(line);

                // Hard max chunk size to avoid huge classes flooding context
                if (currentLines.Count >= 80)
                {
                    chunks.Add(new RagChunk
                    {
                        FilePath = filePath,
                        StartLine = chunkStart + 1,
                        Content = string.Join("\n", currentLines),
                        Symbol = currentSymbol
                    });
                    currentLines.Clear();
                    chunkStart = i + 1;
                }
            }

            if (currentLines.Count > 3)
            {
                chunks.Add(new RagChunk
                {
                    FilePath = filePath,
                    StartLine = chunkStart + 1,
                    Content = string.Join("\n", currentLines),
                    Symbol = currentSymbol
                });
            }

            return chunks;
        }

        private static string ExtractSymbolName(string line)
        {
            var match = Regex.Match(line, @"\b(\w+)\s*[({<]");
            return match.Success ? match.Groups[1].Value : line.Trim().Substring(0, Math.Min(line.Trim().Length, 40));
        }

        private static List<string> Tokenize(string text)
        {
            return Regex.Matches(text, @"[a-zA-Z][a-zA-Z0-9]*")
                .Cast<Match>()
                .Select(m => m.Value.ToLowerInvariant())
                .Where(t => t.Length > 2 && !_stopWords.Contains(t))
                .ToList();
        }
    }
}
