using System.Windows;

namespace TimeRenderer
{
    public partial class SimpleTextInputDialog : Window
    {
        public string InputText
        {
            get => InputTextBox.Text;
            set => InputTextBox.Text = value;
        }

        public SimpleTextInputDialog()
        {
            InitializeComponent();
            Loaded += (s, e) => InputTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
