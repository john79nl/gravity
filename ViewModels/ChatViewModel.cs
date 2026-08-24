using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
#if MAUI
using Gravity.Models;

namespace Gravity.ViewModels
{
    public class ChatViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<ChatMessageModel> Messages { get; } = new();

        private string _inputText = string.Empty;
        public string InputText { get => _inputText; set { _inputText = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public void AddMessage(ChatMessageModel msg)
        {
            Messages.Add(msg);
        }

        public ChatMessageModel AddAssistantMessage(string content)
        {
            var m = new ChatMessageModel { Sender = "assistant", Content = content, IsUser = false };
            Messages.Add(m);
            return m;
        }

        public ChatMessageModel AddUserMessage(string content)
        {
            var m = new ChatMessageModel { Sender = "you", Content = content, IsUser = true };
            Messages.Add(m);
            return m;
        }

        public void AppendToMessage(ChatMessageModel msg, string chunk)
        {
            msg.Content += chunk;
            // Notify collection changed by replacing item (simple approach)
            var idx = Messages.IndexOf(msg);
            if (idx >= 0) Messages[idx] = msg;
        }
    }
}
#endif
