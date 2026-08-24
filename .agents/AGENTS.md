# Code Editing Workflow Rules

To ensure reliable, zero-ambiguity file modifications, all agents must adhere to the following workflow:

1. **Always Read Before Edit:** Never edit a file blindly. You must always use `view_file` to retrieve the current content and **exact line numbers** of the file before making any modification.
2. **Prefer `edit_lines` (Line-Range Edits):** The preferred surgical edit tool is `edit_lines` (parameters: `path`, `start_line`, `end_line`, `new_content`). Because line numbers come directly from `view_file` output, this approach is zero-ambiguity and immune to whitespace/indentation mismatches. Use `replace_file_content` / `apply_diff` only as a fallback when a line-range approach is impractical.
3. **Precise Line Ranges:** Provide exact `StartLine` and `EndLine` parameters based strictly on the `view_file` output to narrow the search scope.
4. **Minimal Replacements:** Scope your modifications to the smallest contiguous block possible. Avoid replacing entire functions or large files if only a few lines need changing.
