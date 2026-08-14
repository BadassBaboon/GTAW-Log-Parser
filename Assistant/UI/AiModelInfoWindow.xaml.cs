using System.Windows;
using MahApps.Metro.Controls;

namespace Assistant.UI
{
    public partial class AiModelInfoWindow : MetroWindow
    {
        public AiModelInfoWindow()
        {
            InitializeComponent();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
