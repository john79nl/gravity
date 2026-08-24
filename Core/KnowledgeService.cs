using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Gravity.Core
{
    public class KnowledgeItem
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();

        [JsonIgnore]
        public string RootPath { get; set; } = string.Empty;

        [JsonIgnore]
        public string ContentPath => Path.Combine(RootPath, "content.md");
    }

    public class KnowledgeService : IKnowledgeService
    {
        private readonly IProjectContext _projectContext;
        private List<KnowledgeItem> _items = new();

        public KnowledgeService(IProjectContext projectContext)
        {
            _projectContext = projectContext ?? throw new ArgumentNullException(nameof(projectContext));
        }

        public virtual async Task RefreshKnowledgeAsync()
        {
            var root = _projectContext.ProjectDirectory;
            if (string.IsNullOrEmpty(root)) return;

            var knowledgeDir = Path.Combine(root, "knowledge");
            if (!Directory.Exists(knowledgeDir)) Directory.CreateDirectory(knowledgeDir);

            await Task.Run(() =>
            {
                var newItems = new List<KnowledgeItem>();
                foreach (var dir in Directory.EnumerateDirectories(knowledgeDir))
                {
                    var metaFile = Path.Combine(dir, "metadata.json");
                    if (File.Exists(metaFile))
                    {
                        try
                        {
                            var json = File.ReadAllText(metaFile);
                            var item = JsonSerializer.Deserialize<KnowledgeItem>(json);
                            if (item != null)
                            {
                                item.RootPath = dir;
                                newItems.Add(item);
                            }
                        }
                        catch { /* Log error in real app */ }
                    }
                }
                _items = newItems;
            });
        }

        public virtual List<KnowledgeItem> MatchKnowledge(string intent, int limit = 5)
        {
            if (string.IsNullOrWhiteSpace(intent)) return new List<KnowledgeItem>();

            if (intent == "*") return _items.Take(limit).ToList();

            return _items
                .Select(item => new { Item = item, Score = CalculateMatchScore(intent, item) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Take(limit)
                .Select(x => x.Item)
                .ToList();
        }

        public async Task<string> GetContentAsync(KnowledgeItem item)
        {
            if (item == null || !File.Exists(item.ContentPath)) return string.Empty;
            return await File.ReadAllTextAsync(item.ContentPath);
        }

        private int CalculateMatchScore(string intent, KnowledgeItem item)
        {
            int score = 0;
            var intentLower = intent.ToLowerInvariant();
            var nameLower = item.Name.ToLowerInvariant();
            var descLower = item.Description.ToLowerInvariant();

            if (nameLower.Contains(intentLower) || intentLower.Contains(nameLower)) score += 100;

            var keywords = intent.Split(new[] { ' ', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var k in keywords)
            {
                if (k.Length < 3) continue;
                var kw = k.ToLowerInvariant();
                if (nameLower.Contains(kw)) score += 30;
                if (descLower.Contains(kw)) score += 15;
                if (item.Tags.Any(t => t.Contains(kw, StringComparison.OrdinalIgnoreCase))) score += 20;
            }
            return score;
        }

        public virtual async Task AddKnowledgeAsync(KnowledgeItem item, string content)
        {
            var root = _projectContext.ProjectDirectory;
            if (string.IsNullOrEmpty(root)) return;

            var safeName = Regex.Replace(item.Name, @"[^a-zA-Z0-9_\-]", "_").ToLowerInvariant();
            var itemDir = Path.Combine(root, "knowledge", safeName);
            if (!Directory.Exists(itemDir)) Directory.CreateDirectory(itemDir);

            item.RootPath = itemDir;
            var metaFile = Path.Combine(itemDir, "metadata.json");
            var contentFile = Path.Combine(itemDir, "content.md");

            await File.WriteAllTextAsync(metaFile, JsonSerializer.Serialize(item, new JsonSerializerOptions { WriteIndented = true }));
            await File.WriteAllTextAsync(contentFile, content);
            
            await RefreshKnowledgeAsync();
        }

        public List<KnowledgeItem> GetKnowledgeItems()
        {
            if (_items == null || !_items.Any())
            {
                RefreshKnowledgeAsync().GetAwaiter().GetResult();
            }
            return _items?.ToList() ?? new List<KnowledgeItem>();
        }
    }
}
