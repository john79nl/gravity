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
        
        static SearchAgent()
        {
            _client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            _client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
            _client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
            _client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            _client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
            _client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
            _client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
        }

        public AgentDescriptor Descriptor { get; }

        public SearchAgent()
        {
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
                var webTask = SearchDuckDuckGoAsync(query, ct);
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

        private async Task<string> SearchWikipediaAsync(string query, CancellationToken ct)
        {
            try
            {
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
                return string.Empty;
            }
        }

        private async Task<AgentResult> SearchDuckDuckGoAsync(string query, CancellationToken ct)
        {
            try
            {
                var url = $"https://duckduckgo.com/html/?q={HttpUtility.UrlEncode(query)}";
                var response = await _client.GetStringAsync(url, ct);
                
                var results = new List<string>();
                var matches = Regex.Matches(response, @"<a class=""result__a""[^>]+href=""([^""]+)""[^>]*>(.*?)</a>", RegexOptions.Singleline);
                var snippets = Regex.Matches(response, @"<a class=""result__snippet""[^>]*>(.*?)</a>", RegexOptions.Singleline);

                for (int i = 0; i < Math.Min(5, matches.Count); i++)
                {
                    var title = StripHtml(matches[i].Groups[2].Value);
                    var link = HttpUtility.UrlDecode(matches[i].Groups[1].Value);
                    
                    if (link.StartsWith("//duckduckgo.com/l/?uddg="))
                    {
                        var match = Regex.Match(link, @"uddg=([^&]+)");
                        if (match.Success) link = HttpUtility.UrlDecode(match.Groups[1].Value);
                    }

                    var snippet = i < snippets.Count ? StripHtml(snippets[i].Groups[1].Value) : "";
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
            catch (Exception ex)
            {
                return new AgentResult { Success = false, Output = $"Search failed: {ex.Message}" };
            }
        }

        private string StripHtml(string input)
        {
            return Regex.Replace(input, "<.*?>", string.Empty);
        }
    }
}
