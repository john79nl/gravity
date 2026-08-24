using System;
using System.IO;

namespace Gravity.Core
{
    public interface IProjectContext
    {
        string? ProjectPath { get; set; }
        string? ProjectDirectory { get; }
        string? ActiveFilePath { get; set; }
    }

    public class ProjectContext : IProjectContext
    {
        public string? ProjectPath { get; set; }
        public string? ActiveFilePath { get; set; }

        public string? ProjectDirectory
        {
            get
            {
                if (string.IsNullOrEmpty(ProjectPath)) return null;
                try
                {
                    return Path.GetDirectoryName(ProjectPath);
                }
                catch { return null; }
            }
        }
    }
}
