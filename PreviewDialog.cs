using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Gravity
{
    public partial class PreviewDialog : MaterialSkin.Controls.MaterialForm
    {
        public class PreviewItem
        {
            public string Title { get; set; }
            public string Preview { get; set; }
            public bool Selected { get; set; }
            public int Index { get; set; }
        }

        public List<PreviewItem> Items { get; } = new List<PreviewItem>();

        public PreviewDialog()
        {
            InitializeComponent();
            MaterialSkin.MaterialSkinManager.Instance.AddFormToManage(this);
        }

        public void SetItems(IEnumerable<PreviewItem> items)
        {
            Items.Clear();
            Items.AddRange(items);
            listView1.Items.Clear();
            foreach (var it in Items)
            {
                var lvi = new ListViewItem(new[] { it.Selected ? "X" : "", it.Title, it.Preview });
                lvi.Tag = it;
                listView1.Items.Add(lvi);
            }
        }

        public List<int> GetSelectedIndexes()
        {
            return Items.Where(i => i.Selected).Select(i => i.Index).ToList();
        }

        private void btnApply_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem lvi in listView1.Items)
            {
                var it = (PreviewItem)lvi.Tag!;
                it.Selected = lvi.Checked;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
