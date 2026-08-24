using System;
#if MAUI
namespace Gravity.Models
{
    public class ChatMessageModel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Sender { get; set; } = "assistant";
        public string Time { get; set; } = DateTime.Now.ToString("HH:mm:ss");
        public string Content { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public bool IsUser { get; set; }
        public bool IsStreaming { get; set; }
    }
}
#endif
