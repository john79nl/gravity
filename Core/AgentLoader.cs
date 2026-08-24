using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Gravity.Core
{
    public class AgentLoader
    {
        public static List<DynamicAgentDefinition> LoadFromDirectory(string directoryPath)
        {
            var results = new List<DynamicAgentDefinition>();
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
                return results;
            }

            foreach (var file in Directory.GetFiles(directoryPath, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var def = JsonSerializer.Deserialize<DynamicAgentDefinition>(json);
                    if (def != null && !string.IsNullOrWhiteSpace(def.Name))
                    {
                        results.Add(def);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AgentLoader: Failed to load {file}: {ex.Message}");
                }
            }

            return results;
        }
    }
}
