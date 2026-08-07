using System.Windows;

namespace ApeRadar
{
    partial class NoteEditWindow : Window
    {
        public string NoteText
        {
            get { return TxtNote.Text; }
            set { TxtNote.Text = value; }
        }

        public NoteEditWindow(string playerName)
        {
            InitializeComponent();
            TxtNotePlayerName.Text = $"{Application.Current.FindResource("NoteEditWindowPlayerName")}{playerName}";
            TxtNote.Focus();
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
