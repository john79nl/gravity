---
name: context-driven-development
description: Deep context-aware development with recursive file creation. When user says "improve", automatically create all referenced files up to 3 levels deep.
---

## Use this skill when

- User says "improve" or "enhance" with file creation requests
- Creating pages, components, or files that reference other files
- Building interconnected file structures with dependencies

## Do not use this skill when

- User explicitly asks for shallow/simple implementation
- Working with existing files that don't need new references
- Tasks unrelated to file creation or context expansion

## Instructions

### Core Behavior: 3-Level Deep Context Expansion

When the user says "improve" (or equivalent: enhance, expand, build out), automatically:

**Level 1**: Create the main requested file(s)
- Example: "Create a page with 2 links" → Create the page file

**Level 2**: Create physical files for all links/references in Level 1 files
- If the page contains links to `about.html`, `contact.html` → Create those files
- If the page references components like `Header.vue`, `Footer.vue` → Create those files

**Level 3**: Create physical files for all links/references in Level 2 files
- If `about.html` links to `team.html`, `history.html` → Create those files
- If `Header.vue` imports `Logo.vue`, `Navigation.vue` → Create those files

### Implementation Strategy

1. **Parse Initial Request**
   - Identify the main file(s) to create
   - Extract all references (links, imports, includes) from the request

2. **Level 1 Creation**
   - Create the primary file(s) with full content
   - Include all referenced file paths as comments or placeholders

3. **Level 2 Expansion**
   - For each reference in Level 1 files:
     - Create the referenced file with appropriate content
     - Mark any new references discovered

4. **Level 3 Expansion**
   - For each reference in Level 2 files:
     - Create the referenced file with appropriate content
     - Stop here (do not go beyond Level 3)

### Example Workflow

**User Request**: "Improve: Create a dashboard page with links to Settings, Profile, and Analytics"

**Level 1**:
- Create `Dashboard.html` with links to `settings.html`, `profile.html`, `analytics.html`

**Level 2**:
- Create `settings.html` (with links to `account.html`, `notifications.html`)
- Create `profile.html` (with links to `edit-profile.html`, `avatar.html`)
- Create `analytics.html` (with links to `reports.html`, `charts.html`)

**Level 3**:
- Create `account.html`, `notifications.html`
- Create `edit-profile.html`, `avatar.html`
- Create `reports.html`, `charts.html`

### File Content Guidelines

- **Level 1 files**: Full implementation with proper structure
- **Level 2 files**: Functional stubs with basic structure and navigation
- **Level 3 files**: Minimal stubs with placeholder content

### Reference Detection Patterns

Look for these patterns to identify references:
- HTML: `<a href="...">`, `<link href="...">`, `<script src="...">`
- JavaScript/TypeScript: `import ... from '...'`, `require('...')`
- CSS: `@import url(...)`, `url(...)`
- Markdown: `[text](path)`, `![image](path)`
- Configuration: `"file": "..."`, `"path": "..."`

### Safety

- Always ask for confirmation before creating files outside the project root
- Never create files that might overwrite existing important files
- Log all file creations for audit trail
- Respect .gitignore and other exclusion patterns

## Verification

After implementation, verify:
1. All Level 1 files created with full content
2. All Level 2 files created with functional stubs
3. All Level 3 files created with minimal stubs
4. No circular dependencies created
5. All file references are valid paths

## Integration with Gravity

This skill integrates with Gravity's IntentRouter and TaskPlanner:

1. **IntentRouter**: Detects "improve" keywords and sets IntentType.Improve with PlanShape.DeepContextExpansion
2. **TaskPlanner**: Creates a hierarchical plan with sub_steps for each level
3. **Orchestrator**: Executes the plan using the FileAgent to create files

The system automatically handles the recursive file creation without requiring manual intervention.