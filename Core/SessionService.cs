using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Gravity.Core
{
    public class Session
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "New Session";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<ChatMessage> History { get; set; } = new List<ChatMessage>();
    }

    public interface ISessionService
    {
        Session CurrentSession { get; set; }
        List<Session> GetAllSessions();
        void SaveCurrentSession(List<ChatMessage> history, string firstPrompt = null);
        void SetCurrentSession(string id);
        Session CreateNewSession();
        void DeleteSession(string id);
    }

    public class SessionService : ISessionService
    {
        private readonly string _dirPath;

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public Session CurrentSession { get; set; } = new Session();

        public SessionService()
        {
            _dirPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Gravity", "Sessions");
            Directory.CreateDirectory(_dirPath);
        }

        public List<Session> GetAllSessions()
        {
            var sessions = new List<Session>();
            try
            {
                var files = Directory.GetFiles(_dirPath, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var session = JsonSerializer.Deserialize<Session>(json, _jsonOptions);
                        if (session != null) sessions.Add(session);
                    }
                    catch { }
                }
            }
            catch { }
            return sessions.OrderByDescending(s => s.CreatedAt).ToList();
        }

        public void SaveCurrentSession(List<ChatMessage> history, string firstPrompt = null)
        {
            try
            {
                CurrentSession.History = history;

                // Auto-name based on the first prompt if it's the default name
                if (CurrentSession.Name == "New Session" && !string.IsNullOrWhiteSpace(firstPrompt))
                {
                    // Truncate prompt for name
                    CurrentSession.Name = firstPrompt.Length > 30 ? firstPrompt.Substring(0, 30) + "..." : firstPrompt;
                }

                var filePath = Path.Combine(_dirPath, $"{CurrentSession.Id}.json");

                // If history is empty and it's an unnamed default session, we might not want to save it, but let's just save it.
                var json = JsonSerializer.Serialize(CurrentSession, _jsonOptions);
                File.WriteAllText(filePath, json);
            }
            catch { /* best-effort */ }
        }

        public void SetCurrentSession(string id)
        {
            var sessions = GetAllSessions();
            var target = sessions.FirstOrDefault(s => s.Id == id);
            if (target != null)
            {
                CurrentSession = target;
            }
        }

        public Session CreateNewSession()
        {
            CurrentSession = new Session();
            return CurrentSession;
        }

        public void DeleteSession(string id)
        {
            try
            {
                var filePath = Path.Combine(_dirPath, $"{id}.json");
                if (File.Exists(filePath)) File.Delete(filePath);
            }
            catch { }
        }
    }
}
