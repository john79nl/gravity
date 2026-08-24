using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Gravity.UI
{
    public class MultiSelectTreeView : TreeView
    {
        private readonly List<TreeNode> _selectedNodes = new();
        private TreeNode? _lastSelectedNode;

        public IReadOnlyList<TreeNode> SelectedNodes => _selectedNodes;

        public MultiSelectTreeView()
        {
            this.DrawMode = TreeViewDrawMode.OwnerDrawText;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        public void SelectNode(TreeNode node, bool clearPrevious = true)
        {
            if (clearPrevious)
            {
                ClearSelectedNodes();
            }

            if (node != null && !_selectedNodes.Contains(node))
            {
                _selectedNodes.Add(node);
                _lastSelectedNode = node;
                this.SelectedNode = node;
                this.Invalidate();
            }
        }

        public void ToggleNodeSelection(TreeNode node)
        {
            if (node == null) return;

            if (_selectedNodes.Contains(node))
            {
                _selectedNodes.Remove(node);
            }
            else
            {
                _selectedNodes.Add(node);
            }
            _lastSelectedNode = node;
            this.SelectedNode = node;
            this.Invalidate();
        }

        public void SelectRange(TreeNode startNode, TreeNode endNode)
        {
            if (startNode == null || endNode == null) return;

            var allNodes = GetAllNodes(this.Nodes);
            int idx1 = allNodes.IndexOf(startNode);
            int idx2 = allNodes.IndexOf(endNode);

            if (idx1 < 0 || idx2 < 0) return;

            ClearSelectedNodes();
            int min = Math.Min(idx1, idx2);
            int max = Math.Max(idx1, idx2);

            for (int i = min; i <= max; i++)
            {
                _selectedNodes.Add(allNodes[i]);
            }
            this.SelectedNode = endNode;
            this.Invalidate();
        }

        public void ClearSelectedNodes()
        {
            _selectedNodes.Clear();
            this.Invalidate();
        }

        private static List<TreeNode> GetAllNodes(TreeNodeCollection nodes)
        {
            var list = new List<TreeNode>();
            foreach (TreeNode n in nodes)
            {
                list.Add(n);
                if (n.IsExpanded && n.Nodes.Count > 0)
                {
                    list.AddRange(GetAllNodes(n.Nodes));
                }
            }
            return list;
        }

        protected override void OnNodeMouseClick(TreeNodeMouseClickEventArgs e)
        {
            if (e.Node == null)
            {
                base.OnNodeMouseClick(e);
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                if (Control.ModifierKeys == Keys.Control)
                {
                    ToggleNodeSelection(e.Node);
                }
                else if (Control.ModifierKeys == Keys.Shift && _lastSelectedNode != null)
                {
                    SelectRange(_lastSelectedNode, e.Node);
                }
                else
                {
                    SelectNode(e.Node, clearPrevious: true);
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                // If right-clicking an unselected node, select only that node.
                // If right-clicking a node already in multi-selection, preserve the multi-selection.
                if (!_selectedNodes.Contains(e.Node))
                {
                    SelectNode(e.Node, clearPrevious: true);
                }
            }

            base.OnNodeMouseClick(e);
        }

        protected override void OnDrawNode(DrawTreeNodeEventArgs e)
        {
            if (e.Node == null) return;

            bool isSelected = _selectedNodes.Contains(e.Node) || e.Node == this.SelectedNode;
            bool isDark = MaterialSkin.MaterialSkinManager.Instance?.Theme == MaterialSkin.MaterialSkinManager.Themes.DARK;

            Color bg = isSelected
                ? (isDark ? Color.FromArgb(65, 80, 120) : Color.FromArgb(190, 210, 240))
                : (this.BackColor);

            Color fg = isSelected
                ? (isDark ? Color.White : Color.Black)
                : (e.Node.ForeColor != Color.Empty ? e.Node.ForeColor : this.ForeColor);

            Rectangle rect = e.Bounds;
            rect.X = Math.Max(0, rect.X - 2);
            rect.Width += 6;

            using (var brush = new SolidBrush(bg))
            {
                e.Graphics.FillRectangle(brush, rect);
            }

            Font font = e.Node.NodeFont ?? this.Font;
            TextRenderer.DrawText(e.Graphics, e.Node.Text, font, e.Bounds, fg, TextFormatFlags.GlyphOverhangPadding | TextFormatFlags.VerticalCenter);
        }
    }
}
