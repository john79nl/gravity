using System.Collections.Generic;
using System.Windows;

namespace Gravity.UI
{
    public partial class KnowledgeListWindow : Window
    {
        public KnowledgeListWindow(List<string> knowledgeItems)
        {
            InitializeComponent();
            
            if (knowledgeItems == null || knowledgeItems.Count == 0)
            {
                KnowledgeListBox.Items.Add("No knowledge items found for this session.");
            }
            else
            {
                foreach (var item in knowledgeItems)
                {
                    KnowledgeListBox.Items.Add(item);
                }
            }
        }

        private void KnowledgeListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (KnowledgeListBox.SelectedItem != null)
            {
                KnowledgeDetailText.Text = KnowledgeListBox.SelectedItem.ToString();
            }
        }
    }
}