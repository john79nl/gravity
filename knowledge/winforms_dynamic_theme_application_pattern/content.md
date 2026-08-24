### WinForms Dynamic Theme Application Pattern

This pattern is used to implement comprehensive theme switching (e.g., Light Mode vs. Dark Mode) in a Windows Forms application where controls are nested within various containers (Panels, ElementHosts, etc.).

#### Technical Implementation Details

1.  **Theme Management Logic**:
    The application utilizes a central method (e.g., `ApplyTheme` or similar) that accepts theme-specific colors (`bgHeader`, `bgDeep`, `fgMain`, `accent`) and a boolean flag (`isDark`).

2.  **Recursive UI Traversal**:
    To ensure all nested components are themed, the system iterates through a collection of open panels and their children:
    - **Top-level Panels**: Updates `BackColor` for primary content containers.
    - **Nested Controls**: Checks for specific types (e.g., `Panel`, `ElementHost`) to apply deep-level styling.
    - **Custom Component Integration**: Identifies specialized controls (like a `MinimapPanel` or `TextEditor`) and calls their specific color-setting methods (e.g., `mp.SetColors(c.Background, isDark)`).

3.  **Control-Specific Styling**:
    - **ToolStrip/TabStrips**: The `BackColor` and `ForeColor` are updated based on the `isDark` flag to maintain contrast between the navigation bar and the content area.
    - **Visual Refresh**: Calling `.Invalidate()` on the parent container (e.g., `tabStrip.Invalidate()`) is necessary to force the UI to redraw with the new color palette.

#### Code Example Pattern
```csharp
foreach (var contentPan in _allPanels)
{
    contentPan.BackColor = bgDeep;
    foreach (Control ctrl in contentPan.Controls)
    {
        if (ctrl is Panel outerPanel)
        {
            outerPanel.BackColor = bgDeep;
            foreach (Control inner in outerPanel.Controls)
            {
                if (inner is CustomUIComponent customCtrl)
                {
                    customCtrl.SetColors(themeColor, isDark);
                }
            }
        }
    }
}
tabStrip.BackColor = isDark ? bgHeader : Color.White;
tabStrip.ForeColor = fgMain;
tabStrip.Invalidate();
```