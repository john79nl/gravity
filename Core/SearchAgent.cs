using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Gravity.Core
{
    public class SearchAgent : IAgent
    {
        private static readonly HttpClient _client = new HttpClient();
        private readonly ISettingsService _settings;

        static SearchAgent()
        {
            _client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            _client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
            _client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
            _client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            _client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
            _client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
            _client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
        }

        public AgentDescriptor Descriptor { get; }

        public SearchAgent(ISettingsService settings)
        {
            _settings = settings;

            Descriptor = new AgentDescriptor
            {
                Name = "search",
                Description = "Search the web for EXTERNAL documentation and general technical info. DO NOT use for project-specific logic or codebase exploration.",
                CanWrite = false,
                SupportedVerbs = new[] { "web", "docs" },
                Actions = new List<ActionMetadata>
                {
                    new ActionMetadata { Name = "web", Description = "General web search.", Parameters = new Dictionary<string, string> { ["query"] = "Search query" } },
                    new ActionMetadata { Name = "docs", Description = "Technical/Documentation scoped search.", Parameters = new Dictionary<string, string> { ["query"] = "Search query" } }
                }
            };
        }

        public async Task<AgentResult> ExecuteAsync(AgentRequest request, CancellationToken ct)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Verb))
                return new AgentResult { Success = false, Output = "Invalid request." };

            var query = request.GetStringArgument("query", request.GetStringArgument("args"));
            if (string.IsNullOrWhiteSpace(query))
                return new AgentResult { Success = false, Output = "Missing 'query' argument." };

            switch (request.Verb.ToLowerInvariant())
            {
                case "web":
                    return await HandleSearchAsync(query, ct);
                case "docs":
                    return await HandleSearchAsync(query + " documentation OR stackoverflow", ct);
                default:
                    return new AgentResult { Success = false, Output = $"Unknown search verb '{request.Verb}'." };
            }
        }

        private async Task<AgentResult> HandleSearchAsync(string query, CancellationToken ct)
        {
            try
            {
                // Run Web search backend and Wikipedia search concurrently through the Gateway
                var webTask = SearchWebBackendAsync(query, ct);
                var wikiTask = SearchWikipediaAsync(query, ct);

                await Task.WhenAll(webTask, wikiTask);

                var webResult = await webTask;
                var wikiOutput = await wikiTask;

                var combinedOutput = new StringBuilder();

                if (!string.IsNullOrWhiteSpace(wikiOutput))
                {
                    combinedOutput.AppendLine("--- 🌐 WIKIPEDIA SUMMARY ---");
                    combinedOutput.AppendLine(wikiOutput);
                }

                if (webResult.Success && !string.IsNullOrWhiteSpace(webResult.Output))
                {
                    if (combinedOutput.Length > 0)
                        combinedOutput.AppendLine("--- 🔍 WEB SEARCH RESULTS ---");
                    combinedOutput.AppendLine(webResult.Output);
                }

                if (combinedOutput.Length == 0)
                {
                    return new AgentResult { Success = true, Output = $"No search results found for '{query}'." };
                }

                return new AgentResult
                {
                    Success = true,
                    Output = combinedOutput.ToString().TrimEnd()
                };
            }
            catch (Exception ex)
            {
                return new AgentResult { Success = false, Output = $"Search gateway failed: {ex.Message}" };
            }
        }

        private async Task<AgentResult> SearchWebBackendAsync(string query, CancellationToken ct)
        {
            var provider = _settings.Current.SearchProvider;
            return provider switch
            {
                SearchProvider.LangSearch => await SearchLangSearchAsync(query, ct),
                _ => await SearchDuckDuckGoAsync(query, ct)
            };
        }

        private async Task<string> SearchWikipediaAsync(string query, CancellationToken ct)
        {
            try
            {
                // 1. Search Wikipedia for matching article titles
                var searchUrl = $"https://en.wikipedia.org/w/api.php?action=query&list=search&srsearch={HttpUtility.UrlEncode(query)}&utf8=&format=json&srlimit=2";
                using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                using var response = await _client.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode) return string.Empty;

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("query", out var qObj) ||
                    !qObj.TryGetProperty("search", out var searchArray) ||
                    searchArray.GetArrayLength() == 0)
                {
                    return string.Empty;
                }

                var wikiResults = new List<string>();

                foreach (var item in searchArray.EnumerateArray())
                {
                    if (!item.TryGetProperty("title", out var titleProp)) continue;
                    var title = titleProp.GetString();
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    // 2. Fetch page summary extract
                    var summaryUrl = $"https://en.wikipedia.org/api/rest_v1/page/summary/{HttpUtility.UrlEncode(title.Replace(" ", "_"))}";
                    using var sumReq = new HttpRequestMessage(HttpMethod.Get, summaryUrl);
                    using var sumResp = await _client.SendAsync(sumReq, ct);

                    if (sumResp.IsSuccessStatusCode)
                    {
                        var sumJson = await sumResp.Content.ReadAsStringAsync(ct);
                        using var sumDoc = JsonDocument.Parse(sumJson);
                        var extract = sumDoc.RootElement.TryGetProperty("extract", out var extProp) ? extProp.GetString() : string.Empty;
                        var pageUrl = sumDoc.RootElement.TryGetProperty("content_urls", out var urlsObj) &&
                                      urlsObj.TryGetProperty("desktop", out var deskObj) &&
                                      deskObj.TryGetProperty("page", out var pageProp)
                                      ? pageProp.GetString()
                                      : $"https://en.wikipedia.org/wiki/{HttpUtility.UrlEncode(title.Replace(" ", "_"))}";

                        if (!string.IsNullOrWhiteSpace(extract))
                        {
                            wikiResults.Add($"### Wikipedia: {title}\nURL: {pageUrl}\n{extract}\n");
                        }
                    }
                }

                return string.Join("\n", wikiResults);
            }
            catch
            {
                // Graceful fallback if Wikipedia API is unreachable
                return string.Empty;
            }
        }

        private async Task<AgentResult> SearchLangSearchAsync(string query, CancellationToken ct)
        {
            var apiKey = _settings.Current.LangSearchApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
                return new AgentResult { Success = false, Output = "LangSearch API key is not configured. Set it in Settings > Search Provider." };

            var url = "https://api.langsearch.com/v1/web-search";
            var payload = new
            {
                query = query,
                freshness = "noLimit",
                summary = true,
                count = 5
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            request.Content = content;

            using var response = await _client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseBody);

            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("webPages", out var webPages) ||
                !webPages.TryGetProperty("value", out var pages))
            {
                return new AgentResult { Success = true, Output = "No results found." };
            }

            var results = new List<string>();
            foreach (var page in pages.EnumerateArray())
            {
                var title = page.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var link = page.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var snippet = page.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(snippet) && page.TryGetProperty("snippet", out var sn))
                    snippet = sn.GetString() ?? "";

                results.Add($"### {title}\nURL: {link}\n{snippet}\n");
            }

            if (results.Count == 0)
                return new AgentResult { Success = true, Output = "No results found." };

            return new AgentResult
            {
                Success = true,
                Output = $"Search Results for '{query}' (via LangSearch):\n\n" + string.Join("\n", results)
            };
        }

        private async Task<AgentResult> SearchDuckDuckGoAsync(string query, CancellationToken ct)
        {
            var url = $"https://lite.duckduckgo.com/lite/?q={HttpUtility.UrlEncode(query)}";
            var response = await _client.GetStringAsync(url, ct);

            var results = new List<string>();
            var matches = Regex.Matches(response, @"<a[^>]+class='result-link'[^>]*href=""([^""]+)""[^>]*>(.*?)</a>", RegexOptions.Singleline);
            var snippets = Regex.Matches(response, @"<td class='result-snippet'>(.*?)</td>", RegexOptions.Singleline);

            for (int i = 0; i < Math.Min(5, matches.Count); i++)
            {
                var title = StripHtml(matches[i].Groups[2].Value).Trim();
                var rawHref = HttpUtility.HtmlDecode(matches[i].Groups[1].Value);

                string link;
                var uddgMatch = Regex.Match(rawHref, @"uddg=([^&]+)");
                link = uddgMatch.Success
                    ? HttpUtility.UrlDecode(uddgMatch.Groups[1].Value)
                    : rawHref;
                if (link.StartsWith("//")) link = "https:" + link;

                var snippet = i < snippets.Count ? StripHtml(snippets[i].Groups[1].Value).Trim() : "";
                results.Add($"### {title}\nURL: {link}\n{snippet}\n");
            }

            if (results.Count == 0)
                return new AgentResult { Success = true, Output = "No results found." };

            return new AgentResult
            {
                Success = true,
                Output = $"Search Results for '{query}':\n\n" + string.Join("\n", results)
            };
        }

        private string StripHtml(string input)
        {
            return Regex.Replace(input, "<.*?>", string.Empty);
        }
    }
}
