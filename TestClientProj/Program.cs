using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Gravity.Core;

namespace GravityTest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                var settings = new SettingsService();
                Console.WriteLine($"Testing with URL: {settings.Current.BaseUrl}, Provider: {settings.Current.Provider}, Model: {settings.Current.ModelName}");
                
                var client = new GenericOpenAIClient(settings);
                var messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "hello how are you" } };
                
                Console.WriteLine("Sending request...");
                var tokenTracker = new Progress<string>(t => Console.Write(t));
                var res = await client.StreamResponseAsync(messages, tokenTracker, CancellationToken.None);
                
                Console.WriteLine("\n\nDone! Final output:");
                Console.WriteLine(res.Content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nERROR: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"INNER: {ex.InnerException.Message}");
            }
        }
    }
}
