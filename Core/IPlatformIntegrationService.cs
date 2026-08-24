using System;

namespace Gravity.Core
{
    /// <summary>
    /// Contract for OS-specific shell integration, desktop shortcuts, and single-instance IPC.
    /// Implemented by WindowsIntegrationService on Windows and LinuxIntegrationService on Linux.
    /// </summary>
    public interface IPlatformIntegrationService
    {
        /// <summary>Event fired when a path is passed to the running instance via single-instance IPC.</summary>
        event Action<string>? OnFileReceived;

        /// <summary>
        /// Checks if an instance of Gravity is already running on the current OS.
        /// Returns true if an instance is active and targetPath was sent to it.
        /// </summary>
        bool CheckAndSendToRunningInstance(string? targetPath);

        /// <summary>Registers OS desktop context menu and file associations.</summary>
        void RegisterShellIntegration();
    }
}
