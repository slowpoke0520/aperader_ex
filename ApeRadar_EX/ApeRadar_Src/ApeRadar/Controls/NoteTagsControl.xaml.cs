using ApeRadar.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ApeRadar.Controls
{
    internal partial class NoteTagsControl : UserControl
    {
        public static readonly DependencyProperty NoteProperty = DependencyProperty.Register(
            nameof(Note), typeof(string), typeof(NoteTagsControl), new PropertyMetadata("", TagsChanged));
        public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
            nameof(Compact), typeof(bool), typeof(NoteTagsControl), new PropertyMetadata(false, TagsChanged));
        public static readonly DependencyProperty MaximumVisibleTagsProperty = DependencyProperty.Register(
            nameof(MaximumVisibleTags), typeof(int), typeof(NoteTagsControl), new PropertyMetadata(int.MaxValue, TagsChanged),
            value => value is int count && count > 0);

        private IReadOnlyList<NoteTag> tags = Array.Empty<NoteTag>();
        private int visibleCount = -1;

        public string Note { get => (string)GetValue(NoteProperty); set => SetValue(NoteProperty, value); }
        public bool Compact { get => (bool)GetValue(CompactProperty); set => SetValue(CompactProperty, value); }
        public int MaximumVisibleTags { get => (int)GetValue(MaximumVisibleTagsProperty); set => SetValue(MaximumVisibleTagsProperty, value); }

        public NoteTagsControl()
        {
            InitializeComponent();
            UpdateTags();
        }

        private static void TagsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
            ((NoteTagsControl)sender).UpdateTags();

        private void UpdateTags()
        {
            if (TagItems == null) return;
            tags = NoteTagUtils.CreateTags(Note);
            ToolTip = Note;
            Visibility = tags.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            visibleCount = -1;
            ShowTags(Math.Min(tags.Count, MaximumVisibleTags));
            InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size constraint)
        {
            int count = Math.Min(tags.Count, MaximumVisibleTags);
            if (Compact && !double.IsInfinity(constraint.Width))
            {
                double width = tags.Take(count).Sum(tag => Math.Min(74, TextWidth(tag.CompactText)) + 14);
                while (count > 0 && width + (tags.Count > count ? TextWidth($"+{tags.Count - count}") + 2 : 0) > constraint.Width)
                {
                    count--;
                    width -= Math.Min(74, TextWidth(tags[count].CompactText)) + 14;
                }
            }
            ShowTags(count);
            return base.MeasureOverride(constraint);
        }

        private double TextWidth(string text) => new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection,
            new Typeface(FontFamily, FontStyle, FontWeights.SemiBold, FontStretch), FontSize, Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;

        private void ShowTags(int count)
        {
            if (visibleCount == count) return;
            visibleCount = count;
            TagItems.ItemsSource = tags.Take(count).ToArray();
            int remaining = tags.Count - count;
            OverflowText.Text = remaining > 0 ? $"+{remaining}" : "";
            OverflowText.Visibility = remaining > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
