using System;

namespace Gravity.Core
{
    public interface IShellLogger
    {
        event Action<string, bool> OnLogReceived; // message, isError
        void Log(string message, bool isError = false);
    }

    public class ShellLoggerService : IShellLogger
    {
        public event Action<string, bool>? OnLogReceived;

        public void Log(string message, bool isError = false)
        {
            OnLogReceived?.Invoke(message, isError);
        }
    }
}
