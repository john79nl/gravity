using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Gravity.Core
{
    public interface IArtifactService
    {
        event Action<Artifact> OnArtifactCreated;
        event Action<Artifact> OnArtifactUpdated;

        Artifact CreateArtifact(ArtifactType type, string title, string content, string agentId = "");
        void UpdateArtifact(Artifact artifact);
        IEnumerable<Artifact> GetArtifacts();
        Artifact? GetArtifact(string id);
        void Clear();
    }

    public class ArtifactService : IArtifactService
    {
        private readonly ConcurrentDictionary<string, Artifact> _artifacts = new();

        public event Action<Artifact>? OnArtifactCreated;
        public event Action<Artifact>? OnArtifactUpdated;

        public Artifact CreateArtifact(ArtifactType type, string title, string content, string agentId = "")
        {
            var artifact = type switch
            {
                ArtifactType.TaskPlan => new TaskArtifact { Title = title, Content = content },
                ArtifactType.Diff => new DiffArtifact { Title = title, Content = content },
                _ => new Artifact { Type = type, Title = title, Content = content }
            };
            artifact.AgentId = agentId;

            _artifacts[artifact.Id] = artifact;
            OnArtifactCreated?.Invoke(artifact);
            return artifact;
        }

        public void UpdateArtifact(Artifact artifact)
        {
            if (artifact == null) return;
            artifact.UpdatedAt = DateTime.UtcNow;
            _artifacts[artifact.Id] = artifact;
            OnArtifactUpdated?.Invoke(artifact);
        }

        public IEnumerable<Artifact> GetArtifacts() => _artifacts.Values.OrderByDescending(a => a.CreatedAt);

        public Artifact? GetArtifact(string id) => _artifacts.TryGetValue(id, out var a) ? a : null;

        public void Clear()
        {
            _artifacts.Clear();
        }
    }
}
