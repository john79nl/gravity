### WinForms Tab-Based Navigation Pattern

This pattern describes a lightweight implementation of a tabbed interface using standard Windows Forms controls instead of the heavy `TabControl`.

#### Technical Implementation
1.  **Tab Headers**: Use a `ToolStrip` (referred to as `tabStrip` in the session) to hold `ToolStripButton` objects.
2.  **Content Containers**: Use `Panel` objects (referred to as `contentPan`) to hold the actual page content.
3.  **Association**: The `Tag` property of the `ToolStripButton` is used to store a reference to its corresponding `Panel`.
    - `btn.Tag = contentPan;`
4.  **Management**:
    - **Creation**: A helper method (e.g., `CreateTabButton`) encapsulates the creation of the button, assigning the content panel to the tag and setting docking properties (`DockStyle.Fill`).
    - **Switching**: When a button is clicked, the corresponding panel (retrieved from `btn.Tag`) is brought to the front or added to the main document area.
    - **Closing**: Closing a tab involves removing the `ToolStripButton` from the `ToolStrip.Items` collection and disposing of the associated `Panel` stored in the `Tag`.

#### Theme Integration
To implement dynamic theme switching (Dark/Light mode) for this system:
- **Tab Strip**: Update `tabStrip.BackColor` and `tabStrip.ForeColor` based on the active theme.
- **Content Panels**: Iterate through a list of active panels (`_allPanels`) to update the `BackColor` of the panel and any nested containers (e.g., `outerPanel`).
- **Invalidation**: Call `tabStrip.Invalidate()` after updating theme properties to force the UI to redraw the custom colors.