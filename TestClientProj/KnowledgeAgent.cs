using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Gravity.Core
{
    public class KnowledgeAgent : IAgent
    {
        private readonly KnowledgeService _knowledgeService;
        private readonly IProjectContext _projectContext;

        public AgentDescriptor Descriptor { get; }

        public KnowledgeAgent(KnowledgeService knowledgeService, IProjectContext projectContext)
        {
            _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
            _projectContext = projectContext ?? throw new ArgumentNullException(nameof(projectContext));

            Descriptor = new AgentDescriptor
            {
                Name = "knowledge",
                Description = "Manage Gravity's 'Knowledge Base' (Standard Operating Procedures and learned patterns).",
                CanWrite = true,
                SupportedVerbs = new[] { "list", "read", "add" },
                Actions = new List<ActionMetadata>
                {
                    new ActionMetadata { Name = "list", Description = "List all available Knowledge Items (KIs).", IsMutation = false },
                    new ActionMetadata { Name = "read", Description = "Read a specific Knowledge Item. REQUIRED: name", IsMutation = false, Parameters = new Dictionary<string, string> { ["name"] = "Unique name of the Knowledge Item" } },
                    new ActionMetadata { Name = "add", Description = "Add a new Knowledge Item to the permanent base. REQUIRED: name, description, content.", IsMutation = true, Parameters = new Dictionary<string, string> { ["name"] = "KI name", ["description"] = "Short summary", ["content"] = "Markdown body" }, OptionalParameters = new List<string> { "tags" } }
                }
            };
        }

        public async Task<AgentResult> ExecuteAsync(AgentRequest request, CancellationToken ct)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Verb))
                return new AgentResult { Success = false, Output = "Invalid request." };

            switch (request.Verb.ToLowerInvariant())
            {
                case "list":
                    await _knowledgeService.RefreshKnowledgeAsync();
                    var items = _knowledgeService.MatchKnowledge("*", 100);
                    if (!items.Any()) return new AgentResult { Success = true, Output = "No Knowledge Items found." };
                    return new AgentResult { Success = true, Output = string.Join("\n", items.Select(i => $"- {i.Name}: {i.Description}")) };

                case "read":
                    return await HandleReadKnowledgeAsync(request, ct);

                case "add":
                    return await HandleAddKnowledgeAsync(request, ct);

                default:
                    return new AgentResult { Success = false, Output = $"Unknown verb '{request.Verb}'." };
            }
        }

        private async Task<AgentResult> HandleReadKnowledgeAsync(AgentRequest request, CancellationToken ct)
        {
            var name = request.GetStringArgument("name");
            var item = _knowledgeService.MatchKnowledge(name, 1).FirstOrDefault();
            if (item == null) return new AgentResult { Success = false, Output = $"Knowledge Item '{name}' not found." };
            
            var content = await _knowledgeService.GetContentAsync(item);
            return new AgentResult { Success = true, Output = content };
        }

        private async Task<AgentResult> HandleAddKnowledgeAsync(AgentRequest request, CancellationToken ct)
        {
            var name = request.GetStringArgument("name");
            var desc = request.GetStringArgument("description");
            var content = request.GetStringArgument("content");
            var tagsRaw = request.GetStringArgument("tags");

            if (string.IsNullOrWhiteSpace(name)) return new AgentResult { Success = false, Output = "Missing 'name'." };
            if (string.IsNullOrWhiteSpace(content)) return new AgentResult { Success = false, Output = "Missing 'content'." };

            var item = new KnowledgeItem
            {
                Name = name,
                Description = desc,
                Tags = tagsRaw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToList()
            };

            await _knowledgeService.AddKnowledgeAsync(item, content);

            return new AgentResult { Success = true, Output = $"Knowledge Item '{name}' added successfully to the base." };
        }
    }
}
