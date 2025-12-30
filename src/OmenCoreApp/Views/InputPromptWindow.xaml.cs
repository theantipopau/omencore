using System.Windows;

namespace OmenCore.Views
{
    public partial class InputPromptWindow : Window
    {
        public string Input { get; private set; } = string.Empty;
        public bool DialogResultOk { get; private set; }

        public InputPromptWindow(string title, string message)
        {
            InitializeComponent();
            Title = title;
            MessageText.Text = message;
            InputTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Input = InputTextBox.Text ?? string.Empty;
            DialogResult = true;
            DialogResultOk = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            DialogResultOk = false;
            Close();
        }
    }
}





















