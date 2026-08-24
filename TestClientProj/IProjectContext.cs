namespace Gravity.Core
{
    public interface IProjectContext
    {
        string? ProjectPath { get; set; }
        string? ProjectDirectory { get; }
    }

    public class ProjectContext : IProjectContext
    {
        public string? ProjectPath { get; set; }

        public string? ProjectDirectory
        {
            get
            {
                if (string.IsNullOrEmpty(ProjectPath)) return null;
                try
                {
                    return System.IO.Path.GetDirectoryName(ProjectPath);
                }
                catch { return null; }
            }
        }
    }
}
