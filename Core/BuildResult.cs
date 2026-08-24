namespace Gravity.Core
{
    public class BuildResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; } = string.Empty;
        public bool Success => ExitCode == 0;
    }
}
