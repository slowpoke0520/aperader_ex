using ApeRadar.Utils;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace ApeRadar
{
    partial class NoteEditWindow : Window
    {
        private IReadOnlyList<string> quickOptions = Array.Empty<string>();

        public string NoteText
        {
            get { return TxtNote.Text; }
            set { TxtNote.Text = value; }
        }

        public NoteEditWindow(string playerName)
        {
            InitializeComponent();
            TxtNotePlayerName.Text = $"{Application.Current.FindResource("NoteEditWindowPlayerName")}{playerName}";
            LoadQuickOptions();
            TxtNote.Focus();
        }

        private void LoadQuickOptions()
        {
            quickOptions = NoteQuickOptionUtils.GetOptions(Properties.Settings.Default.NoteQuickOptions);
            QuickOptionsPanel.Children.Clear();
            foreach (string option in quickOptions)
            {
                Button button = new()
                {
                    Content = option,
                    Tag = option,
                    MinHeight = 28,
                    Padding = new Thickness(9, 3, 9, 3),
                    Margin = new Thickness(3)
                };
                button.Click += QuickOption_Click;
                QuickOptionsPanel.Children.Add(button);
            }
        }

        private void QuickOption_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string option }) return;
            string existing = TxtNote.Text.TrimEnd();
            TxtNote.Text = existing.Length == 0 ? option : $"{existing} {option}";
            TxtNote.CaretIndex = TxtNote.Text.Length;
            TxtNote.Focus();
        }

        private void BtnManageQuickOptions_Click(object sender, RoutedEventArgs e)
        {
            bool show = QuickOptionsEditor.Visibility != Visibility.Visible;
            QuickOptionsEditor.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show)
            {
                TxtQuickOptions.Text = string.Join(Environment.NewLine, quickOptions);
                TxtQuickOptions.Focus();
            }
        }

        private void BtnSaveQuickOptions_Click(object sender, RoutedEventArgs e)
        {
            IReadOnlyList<string> options = NoteQuickOptionUtils.Normalize(
                TxtQuickOptions.Text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None));
            Properties.Settings.Default.NoteQuickOptions = NoteQuickOptionUtils.Serialize(options);
            Properties.Settings.Default.Save();
            LoadQuickOptions();
            QuickOptionsEditor.Visibility = Visibility.Collapsed;
        }

        private void BtnResetQuickOptions_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.NoteQuickOptions = "";
            Properties.Settings.Default.Save();
            LoadQuickOptions();
            TxtQuickOptions.Text = string.Join(Environment.NewLine, quickOptions);
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
