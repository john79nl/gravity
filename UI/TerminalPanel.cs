using System;
using System.Diagnostics;
using System.Text;
#if MAUI
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Gravity.UI
{
    public class TerminalPanel : ContentView
    {
        private readonly Entry _cmdEntry;
        private readonly Button _runBtn;
        private readonly Label _outputLabel;
        private readonly ScrollView _scroll;

        public TerminalPanel()
        {
            BackgroundColor = Colors.FromArgb("FF0F1115");
            Padding = new Thickness(8);

            _cmdEntry = new Entry { Placeholder = "PowerShell command...", HorizontalOptions = LayoutOptions.FillAndExpand, TextColor = Colors.White };
            _runBtn = new Button { Text = "Run", HorizontalOptions = LayoutOptions.End };
            _outputLabel = new Label { LineBreakMode = LineBreakMode.WordWrap, FontFamily = "Consolas", TextColor = Colors.FromArgb("FFF1F1F1") };
            _scroll = new ScrollView { Content = _outputLabel, VerticalOptions = LayoutOptions.FillAndExpand };

            _runBtn.Clicked += async (s, e) => await RunCommandAsync(_cmdEntry.Text ?? "");

            var top = new HorizontalStackLayout { Spacing = 6 };
            top.Add(_cmdEntry);
            top.Add(_runBtn);

            var main = new VerticalStackLayout { Spacing = 6 };
            main.Add(top);
            main.Add(_scroll);

            Content = main;
        }

        public async System.Threading.Tasks.Task RunCommandAsync(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -Command \"{cmd}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var proc = Process.Start(psi);
                if (proc == null) return;

                var sb = new StringBuilder();

                while (!proc.StandardOutput.EndOfStream)
                {
                    var line = await proc.StandardOutput.ReadLineAsync();
                    sb.AppendLine(line);
                    UpdateOutput(sb.ToString());
                }

                while (!proc.StandardError.EndOfStream)
                {
                    var err = await proc.StandardError.ReadLineAsync();
                    sb.AppendLine(err);
                    UpdateOutput(sb.ToString());
                }

                proc.WaitForExit();
            }
            catch (Exception ex)
            {
                UpdateOutput("Error: " + ex.Message);
            }
        }

        private void UpdateOutput(string text)
        {
            if (Dispatcher == null) { _outputLabel.Text = text; return; }
            Dispatcher.Dispatch(() => {
                _outputLabel.Text = text;
                _scroll.ScrollToAsync(_outputLabel, ScrollToPosition.End, true);
            });
        }
    }
}
#endif
