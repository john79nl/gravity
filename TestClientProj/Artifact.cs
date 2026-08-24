using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Gravity.Core
{
    public enum ArtifactType
    {
        TaskPlan,
        ImplementationPlan,
        Walkthrough,
        Diff,
        General
    }

    public enum ArtifactStatus
    {
        Draft,
        InReview,
        Approved,
        Completed,
        Failed
    }

    public class Artifact
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public ArtifactType Type { get; set; } = ArtifactType.General;
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public ArtifactStatus Status { get; set; } = ArtifactStatus.Draft;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string AgentId { get; set; } = string.Empty;
        public Dictionary<string, string> Metadata { get; set; } = new();

        public override string ToString() => $"[{Type}] {Title} ({Status})";
    }

    public class TaskItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public bool IsInProgress { get; set; }
    }

    public class TaskArtifact : Artifact
    {
        public List<TaskItem> Tasks { get; set; } = new();
        public TaskArtifact() { Type = ArtifactType.TaskPlan; }
    }

    public class DiffArtifact : Artifact
    {
        public string FilePath { get; set; } = string.Empty;
        public string DiffContent { get; set; } = string.Empty;
        public DiffArtifact() { Type = ArtifactType.Diff; }
    }
}
