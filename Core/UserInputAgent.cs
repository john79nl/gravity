using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Gravity.Core
{
    /// <summary>
    /// Provides the LLM with structured ways to ask the user for input during an autonomous task.
    /// The UI layer injects a callback that renders the appropriate widget in the chat and awaits
    /// the user's response before returning control to the agent loop.
    ///
    /// Supported verbs:
    ///   ask            — Free-text question. Returns the user's typed answer.
    ///   confirm        — Yes / No question. Returns "yes" or "no".
    ///   choose         — Multiple-choice. Returns the selected option text.
    ///   approve_command — Shows the compact ⚡ approval bar. Returns "approved" or "denied".
    /// </summary>
    public class UserInputAgent : IAgent
    {
        // Injected by Form1 at startup — renders the widget and awaits user response.
        private readonly Func<UserInputRequest, Task<string>> _uiHandler;

        public AgentDescriptor Descriptor { get; }

        public UserInputAgent(Func<UserInputRequest, Task<string>> uiHandler)
        {
            _uiHandler = uiHandler ?? throw new ArgumentNullException(nameof(uiHandler));

            Descriptor = new AgentDescriptor
            {
                Name        = "user_input",
                Description = "Ask the user for information or approval during task execution. Use this whenever you need a decision, clarification, or missing value before you can continue.",
                CanWrite    = false,
                SupportedVerbs = new[] { "ask", "confirm", "choose", "approve_command" },
                Actions = new List<ActionMetadata>
                {
                    new ActionMetadata
                    {
                        Name        = "ask",
                        Description = "Ask the user an open-ended question and wait for a typed answer. REQUIRED: question",
                        IsMutation  = false,
                        Parameters  = new Dictionary<string, string>
                        {
                            ["question"] = "The question to display to the user"
                        },
                        OptionalParameters = new List<string> { "placeholder" }
                    },
                    new ActionMetadata
                    {
                        Name        = "confirm",
                        Description = "Ask the user a yes/no question. Returns 'yes' or 'no'. REQUIRED: question",
                        IsMutation  = false,
                        Parameters  = new Dictionary<string, string>
                        {
                            ["question"] = "The yes/no question to display"
                        }
                    },
                    new ActionMetadata
                    {
                        Name        = "choose",
                        Description = "Present the user with a multiple-choice selection. Returns the chosen option text. REQUIRED: question, options (comma-separated list)",
                        IsMutation  = false,
                        Parameters  = new Dictionary<string, string>
                        {
                            ["question"] = "The question or prompt shown above the options",
                            ["options"]  = "Comma-separated list of choices, e.g. 'Option A,Option B,Option C'"
                        }
                    },
                    new ActionMetadata
                    {
                        Name        = "approve_command",
                        Description = "Show the user a shell command and ask them to approve or deny its execution. Returns 'approved' or 'denied'. REQUIRED: verb, command",
                        IsMutation  = true,
                        Parameters  = new Dictionary<string, string>
                        {
                            ["verb"]    = "Short action verb shown in the bar, e.g. 'run_command', 'delete_file'",
                            ["command"] = "The full command string the user will review"
                        },
                        OptionalParameters = new List<string> { "description" }
                    }
                }
            };
        }

        public async Task<AgentResult> ExecuteAsync(AgentRequest request, CancellationToken ct)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Verb))
                return new AgentResult { Success = false, Output = "user_input requires a verb: ask | confirm | choose | approve_command" };

            var verb = request.Verb.ToLowerInvariant().Trim();

            switch (verb)
            {
                case "ask":
                {
                    var question    = request.GetStringArgument("question");
                    var placeholder = request.GetStringArgument("placeholder") ?? "Type your answer…";
                    if (string.IsNullOrWhiteSpace(question))
                        return new AgentResult { Success = false, Output = "Missing required argument: question" };

                    var uiReq = new UserInputRequest
                    {
                        Kind        = UserInputKind.Ask,
                        Question    = question,
                        Placeholder = placeholder
                    };

                    var answer = await _uiHandler(uiReq);
                    return new AgentResult
                    {
                        Success = true,
                        Output  = string.IsNullOrWhiteSpace(answer)
                            ? "[User did not provide an answer]"
                            : $"User answered: {answer}"
                    };
                }

                case "confirm":
                {
                    var question = request.GetStringArgument("question");
                    if (string.IsNullOrWhiteSpace(question))
                        return new AgentResult { Success = false, Output = "Missing required argument: question" };

                    var uiReq = new UserInputRequest
                    {
                        Kind     = UserInputKind.Confirm,
                        Question = question
                    };

                    var answer = await _uiHandler(uiReq);
                    return new AgentResult { Success = true, Output = answer }; // "yes" or "no"
                }

                case "choose":
                {
                    var question    = request.GetStringArgument("question");
                    var optionsRaw  = request.GetStringArgument("options");
                    if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(optionsRaw))
                        return new AgentResult { Success = false, Output = "Missing required arguments: question, options" };

                    var options = optionsRaw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (options.Length < 2)
                        return new AgentResult { Success = false, Output = "At least 2 options required in the 'options' argument." };

                    var uiReq = new UserInputRequest
                    {
                        Kind     = UserInputKind.Choose,
                        Question = question,
                        Options  = options
                    };

                    var chosen = await _uiHandler(uiReq);
                    return new AgentResult
                    {
                        Success = true,
                        Output  = string.IsNullOrWhiteSpace(chosen)
                            ? "[User did not select an option]"
                            : $"User selected: {chosen}"
                    };
                }

                case "approve_command":
                {
                    var cmdVerb   = request.GetStringArgument("verb");
                    var command   = request.GetStringArgument("command");
                    var desc      = request.GetStringArgument("description") ?? "";
                    if (string.IsNullOrWhiteSpace(cmdVerb) || string.IsNullOrWhiteSpace(command))
                        return new AgentResult { Success = false, Output = "Missing required arguments: verb, command" };

                    var uiReq = new UserInputRequest
                    {
                        Kind        = UserInputKind.ApproveCommand,
                        Question    = desc,
                        CommandVerb = cmdVerb,
                        Command     = command
                    };

                    var result = await _uiHandler(uiReq);
                    return new AgentResult { Success = true, Output = result }; // "approved" or "denied"
                }

                default:
                    return new AgentResult
                    {
                        Success = false,
                        Output  = $"Unknown verb '{verb}'. Available: ask | confirm | choose | approve_command"
                    };
            }
        }
    }

    // ── Request model ──────────────────────────────────────────────────────────

    public enum UserInputKind
    {
        Ask,
        Confirm,
        Choose,
        ApproveCommand
    }

    public class UserInputRequest
    {
        public UserInputKind Kind        { get; set; }
        public string        Question    { get; set; } = "";
        public string        Placeholder { get; set; } = "Type your answer…";
        public string[]?     Options     { get; set; }
        public string        CommandVerb { get; set; } = "";
        public string        Command     { get; set; } = "";
    }
}
