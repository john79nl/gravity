### WinForms ToolStrip Tab Implementation Pattern

This pattern implements a lightweight tabbed interface in Windows Forms without using the standard `TabControl`, allowing for greater customization of the tab headers (via `ToolStrip`).

#### Architectural Pattern
- **Header**: A `ToolStrip` (referred to as `tabStrip` in the implementation) serves as the tab bar.
- **Tab Handle**: `ToolStripButton` objects are used as the clickable tabs.
- **Content Mapping**: The `Tag` property of the `ToolStripButton` stores a reference to the corresponding `Control` (typically a `Panel` or `ElementHost`) that represents the tab's content.
- **Content Area**: A main container (e.g., `mainDocumentArea`) manages the visibility and docking of the content panels.

#### Key Implementation Details

**1. Creating a Tab**
The `CreateTabButton` method encapsulates the association between the header and the content:
```csharp
private ToolStripButton CreateTabButton(string text, Control contentPan, string? path = null)
{
    contentPan.Tag = path;
    contentPan.Dock = DockStyle.Fill;
    
    var btn = new ToolStripButton(text) { 
        Tag = contentPan, 
        CheckOnClick = false 
    };
    
    tabStrip.Items.Add(btn);
    return btn;
}
```

**2. Closing a Tab**
To remove a tab, the reference to the content panel must be retrieved from the button's `Tag` to ensure proper memory management:
```csharp
private void CloseTab(ToolStripButton btn)
{
    var contentPan = btn.Tag as Control;
    tabStrip.Items.Remove(btn);
    if (contentPan != null)
    {
        mainDocumentArea.Controls.Remove(contentPan);
        contentPan.Dispose();
    }
}
```

**3. Theme Application**
Since `ToolStrip` does not always inherit form colors automatically, the `BackColor` and `ForeColor` should be explicitly set during theme transitions to ensure consistency between Dark and Light modes.