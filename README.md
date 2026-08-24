# 🌌 Gravity AI

> **An Open-Source Multi-Agent Platform for Autonomous Software Engineering**

[![License: AGPL v3](https://img.shields.io/badge/License-AGPL_v3-blue.svg)](LICENSE)
[![Framework](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Protocol](https://img.shields.io/badge/Protocol-MCP-orange.svg)](https://modelcontextprotocol.io/)
[![Status](https://img.shields.io/badge/Status-Open_Source-success.svg)](#-open-source-vision--call-for-collaboration)

Gravity AI is an autonomous, multi-agent software engineering environment built for local software development. Designed as a high-performance desktop application, Gravity bridges the gap between passive AI autocomplete suggestors and **true, closed-loop autonomous execution**.

---

## 🌟 Open Source Vision & Call for Collaboration

### Why We Are Releasing Gravity Open Source
Today, the landscape of AI coding tools is fragmented across dozens of single-purpose scripts, extensions, CLI wrappers, and isolated chat plugins. 

We are releasing **Gravity AI under an open-source license (AGPL-3.0)** to showcase a full, working autonomous product and to bring together developers, researchers, and creators who are passionate about the future of AI-driven software engineering.

### 🤝 Let's Build ONE Ultimate Tool Together!
Our primary goal in open-sourcing Gravity is to foster collaboration across the community. **Instead of building dozens of competing, separate tools, let's help each other build ONE comprehensive, open-source tool that incorporates all of them!**

Whether your expertise is in:
- **LLM Orchestration & Prompting**
- **Model Context Protocol (MCP) integrations**
- **Language Server Protocol (LSP) & Roslyn Compiler APIs**
- **Sandboxed Execution & Terminal Automation**
- **UI/UX for AI Agent Reasoning & Artifact Visualization**

We invite you to join the project, share ideas, open discussions, submit pull requests, and help shape the single definitive open-source platform for autonomous development.

---

## 🚀 Key Features

### 🧠 Autonomous Router-Worker Architecture
Gravity uses a multi-layered routing pipeline to evaluate intent and execute complex developer tasks safely:
- **Layer 1 (`IntentRouter`)**: Classifies user queries into `Conversational`, `CodeAnalysis`, `SingleStep`, or `ComplexPlan`.
- **Layer 2 (`TaskPlanner`)**: Dynamically breaks complex engineering tasks into actionable, multi-step execution plans.
- **Worker Execution (`Orchestrator` & `ReasoningRouter`)**: Executes tool calls in a structured JSON loop with step-by-step verification.

### 🤖 Built-in Specialized Agents
Gravity includes a rich pool of specialized agents out of the box:
- 📁 **FileAgent (`code_editor`)**: Safe file reading, line-range editing, differential patching (`apply_diff`), and workspace search.
- 💻 **ShellAgent (`terminal`)**: Sandboxed command execution for builds (`dotnet build`), tests, git commands, and system tasks.
- 🧠 **KnowledgeAgent (`knowledge`)**: Retrieves and manages Standard Operating Procedures (SOPs), team standards, and repository patterns.
- 🚀 **GravityAgent (`gravity`)**: Manages meta-context, interactive user prompts, and execution artifacts (Plans, Walkthroughs, Diffs).
- 🔍 **SearchAgent (`search`)**: Integrated real-time web research via DuckDuckGo.
- 🧩 **DynamicAgent**: Create and register custom domain agents on the fly using simple JSON schemas in the `agents/` directory.
- 🔌 **McpAgent**: Extends capabilities natively using the **Model Context Protocol (MCP)** via stdio JSON-RPC.

### 🛠️ Deep Codebase Intelligence
- **Roslyn Integration**: Native C# semantic analysis for real-time compilation diagnostics, symbol lookup, and AST inspection.
- **Workspace RAG**: Fast, in-memory chunk indexing for semantic and keyword code retrieval across large repositories.
- **Git Context**: Automatic git status and git diff monitoring to track agent modifications in real time.

### 🎨 Hybrid UI & Visual Feedback
- Built with **WinForms + WPF ElementHost** for low-latency desktop execution.
- Integrated **AvalonEdit** code viewer with custom Roslyn syntax highlighting.
- Visual **Artifact Panel** displaying step-by-step task plans, code diffs, implementation plans, and execution walkthroughs.
- Real-time **Reasoning Visualizer** showing agent thoughts, tool invocations, and live terminal outputs.

---

## 🏗️ High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                          UI Layer                               │
│  Form1 (WinForms)  ←→  WPF Controls (via ElementHost)         │
│  Chat Bubbles │ Artifacts │ Editor (AvalonEdit) │ Sidebar       │
└───────────────────────────┬─────────────────────────────────────┘
                            │
┌───────────────────────────┴─────────────────────────────────────┐
│                     Orchestration Layer                         │
│  Orchestrator  ←→  IntentRouter  ←→  TaskPlanner               │
│  (Agent Pool)     (Layer 1)         (Layer 2)                  │
└───────────────────────────┬─────────────────────────────────────┘
                            │
┌───────────────────────────┴─────────────────────────────────────┐
│                       Agent Layer                               │
│  ReasoningRouter (Command Resolution)                           │
│  ├─ FileAgent (code_editor)    ├─ ShellAgent (terminal)        │
│  ├─ KnowledgeAgent (knowledge)  ├─ GravityAgent (gravity)      │
│  ├─ SearchAgent (search)        ├─ DynamicAgent (JSON schema)   │
│  └─ McpAgent (MCP protocol)                                     │
└───────────────────────────┬─────────────────────────────────────┘
                            │
┌───────────────────────────┴─────────────────────────────────────┐
│                      Service Layer                              │
│  IModelClient │ RagService │ KnowledgeService │ RoslynService  │
│  GitService │ FileSearchService │ BuildService │ McpClient     │
└───────────────────────────┬─────────────────────────────────────┘
```

For full technical specifications, see [`ARCHITECTURE.txt`](file:///ARCHITECTURE.txt).

---

## ⚡ Getting Started

### Prerequisites
- **Operating System**: Windows 10 / 11
- **SDK**: [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or higher
- **LLM API Access**: OpenAI API Key (or any OpenAI-compatible API endpoint such as Azure OpenAI, Ollama, vLLM, or LM Studio)

### Installation & Execution

1. **Clone the repository**:
   ```bash
   git clone https://github.com/YourUsername/Gravity.git
   cd Gravity
   ```

2. **Build the project**:
   ```bash
   dotnet build -c Release
   ```

3. **Run Gravity**:
   ```bash
   dotnet run --project Gravity.csproj
   ```

4. **Configure LLM Credentials**:
   - On first launch, click **Settings** in the application interface.
   - Enter your **API Base URL** (default: `https://api.openai.com/v1`), **Model Name** (e.g. `gpt-4o` or `gemini-2.5-flash`), and **API Key**.
   - Select your target project folder and start interacting with Gravity!

---

## 📁 Repository Structure

```
Gravity/
├── Core/                    # Business logic, routing, agents & core services
│   ├── FileAgent.cs         # Code modification & line-range editing
│   ├── ShellAgent.cs        # Sandboxed shell command execution
│   ├── IntentRouter.cs      # Intent classification engine
│   ├── TaskPlanner.cs       # Multi-step task plan generator
│   ├── Orchestrator.cs      # Main agent execution loop
│   ├── RoslynService.cs     # Roslyn compilation & C# analysis
│   └── RagService.cs        # Workspace indexing & semantic search
├── UI/                      # Hybrid WinForms & WPF UI components
│   ├── ArtifactPanel.cs     # Artifact visualizer (Plans, Walkthroughs, Diffs)
│   ├── ReasoningVisualizer.cs  # Live agent execution steps
│   └── CollapsibleStepPanel.cs # Step-by-step tool output breakdown
├── agents/                  # JSON-defined dynamic agent definitions
├── knowledge/               # Knowledge base SOPs and markdown guides
├── Skills/                  # Agent skills & domain capabilities
├── appsettings.json         # Default configuration settings
├── ARCHITECTURE.txt         # Complete system architecture specification
├── business_plan.txt        # Vision, product roadmap & market positioning
└── LICENSE                  # GNU AGPL-3.0 Open Source License
```

---

## 🤝 How to Contribute & Get in Touch

We warmly welcome all contributions! Here are ways you can help build the future of Gravity:

1. **Submit Dynamic Agents**: Add custom JSON agent definitions under `agents/` for specialized tasks (e.g. Docker management, database migrations, security audits).
2. **Expand MCP Support**: Help build and integrate MCP servers to connect Gravity with third-party developer platforms (Jira, GitHub, Postgres, Figma, etc.).
3. **Enhance UI & Controls**: Contribute to the WPF/WinForms controls, code visualization, or dark theme styling.
4. **Improve Cross-Platform Engine**: Help abstract OS-dependent components to bring Gravity to Linux and macOS.

### Contact & Community
- **GitHub Issues**: For bug reports, feature proposals, and task tracking.
- **GitHub Discussions**: For sharing ideas, showcase implementations, and discussing agentic architectures.

Let's collaborate and make **Gravity** the single, open-source tool that unifies the developer community!

---

## 📄 License

Gravity is released under the [GNU Affero General Public License v3.0 (AGPL-3.0)](LICENSE). 

---

<p align="center">
  <b>Gravity AI</b> • Empowering Developers Through Autonomous Multi-Agent Engineering
</p>
