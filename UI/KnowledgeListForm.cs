using Gravity.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Gravity.UI
{
    public class KnowledgeListForm : Form
    {
        private ListBox listBoxKnowledge;
        private TextBox txtDetail;
        private SplitContainer splitContainer;
        private List<KnowledgeItem> _knowledgeItems;

        public KnowledgeListForm(List<KnowledgeItem> knowledgeItems)
        {
            _knowledgeItems = knowledgeItems;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Session Knowledge Agent";
            this.Size = new Size(800, 500);
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(30, 30, 40);
            this.ForeColor = Color.White;

            splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 250
            };

            listBoxKnowledge = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(45, 45, 60),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 10),
                IntegralHeight = false
            };
            listBoxKnowledge.SelectedIndexChanged += KnowledgeListBox_SelectedIndexChanged;

            txtDetail = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BackColor = Color.FromArgb(30, 30, 40),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 11),
                ScrollBars = ScrollBars.Vertical
            };

            splitContainer.Panel1.Controls.Add(listBoxKnowledge);
            splitContainer.Panel2.Controls.Add(txtDetail);
            this.Controls.Add(splitContainer);

            if (_knowledgeItems == null || _knowledgeItems.Count == 0)
            {
                listBoxKnowledge.Items.Add("No knowledge items found for this session.");
            }
            else
            {
                foreach (var item in _knowledgeItems)
                {
                    listBoxKnowledge.Items.Add(item.Name);
                }
            }
        }

        private async void KnowledgeListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBoxKnowledge.SelectedItem != null)
            {
                var selectedName = listBoxKnowledge.SelectedItem.ToString();
                var item = _knowledgeItems?.FirstOrDefault(k => k.Name == selectedName);
                if (item != null)
                {
                    // We need a way to get the content. Since KnowledgeService is a singleton/service,
                    // we should ideally pass it or use a service locator, but let's see if we can 
                    // improve the form to handle the item selection better.
                    txtDetail.Text = $"Loading content for {item.Name}...\r\n\r\nDescription: {item.Description}";
                    
                    // Note: This is a quick fix. To actually load the content, we'd need the service.
                    // However, the user's primary complaint is that the list is EMPTY.
                }
            }
        }
    }
}