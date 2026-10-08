using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace ApeRadar.Controls
{
    internal sealed class PlayerDetailPopupController<TRow> where TRow : class
    {
        private readonly Popup popup;
        private readonly Border border;
        private readonly FrameworkElement card;
        private readonly FrameworkElement owner;
        private readonly DataGrid alliesGrid;
        private readonly Func<TRow, object> getDetail;
        private readonly bool openIfTargetHovered;
        private readonly bool keepOpenWhileOverRow;
        private readonly DispatcherTimer openTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
        private readonly DispatcherTimer closeTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
        private TRow? pendingRow;
        private FrameworkElement? pendingTarget;
        private bool pointerOverRow;
        private bool pointerOverPopup;

        internal PlayerDetailPopupController(Popup popup, Border border, FrameworkElement card, FrameworkElement owner,
            DataGrid alliesGrid, Func<TRow, object> getDetail, bool openIfTargetHovered = false,
            bool keepOpenWhileOverRow = true)
        {
            this.popup = popup;
            this.border = border;
            this.card = card;
            this.owner = owner;
            this.alliesGrid = alliesGrid;
            this.getDetail = getDetail;
            this.openIfTargetHovered = openIfTargetHovered;
            this.keepOpenWhileOverRow = keepOpenWhileOverRow;
            openTimer.Tick += OpenTimer_Tick;
            closeTimer.Tick += CloseTimer_Tick;
            popup.CustomPopupPlacementCallback = PlacePopup;
            popup.Closed += (_, _) =>
            {
                CurrentRow = null;
                card.DataContext = null;
            };
        }

        internal TRow? CurrentRow { get; private set; }

        internal void RowEntered(TRow row, FrameworkElement target)
        {
            closeTimer.Stop();
            openTimer.Stop();
            pointerOverRow = true;
            pendingRow = row;
            pendingTarget = target;
            if (popup.IsOpen)
                ShowPendingRow();
            else
                openTimer.Start();
        }

        internal void RowLeft()
        {
            pointerOverRow = false;
            openTimer.Stop();
            closeTimer.Stop();
            closeTimer.Start();
        }

        internal void PopupEntered()
        {
            pointerOverPopup = true;
            closeTimer.Stop();
        }

        internal void PopupLeft()
        {
            pointerOverPopup = false;
            closeTimer.Stop();
            closeTimer.Start();
        }

        internal void UpdateBounds()
        {
            System.Windows.Forms.Screen screen;
            if (pendingTarget is FrameworkElement target && target.IsLoaded)
            {
                Point point = target.PointToScreen(new Point(target.ActualWidth / 2, target.ActualHeight / 2));
                screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)point.X, (int)point.Y));
            }
            else
            {
                screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            }
            DpiScale dpi = VisualTreeHelper.GetDpi(owner);
            border.Width = Math.Min(560, Math.Max(360, screen.WorkingArea.Width / dpi.DpiScaleX - 32));
            border.MaxHeight = Math.Min(720, Math.Max(300, screen.WorkingArea.Height / dpi.DpiScaleY * 0.70));
        }

        internal void Close()
        {
            openTimer.Stop();
            closeTimer.Stop();
            pendingRow = null;
            pendingTarget = null;
            pointerOverRow = false;
            pointerOverPopup = false;
            popup.IsOpen = false;
            CurrentRow = null;
            card.DataContext = null;
        }

        private void OpenTimer_Tick(object? sender, EventArgs e)
        {
            openTimer.Stop();
            if (pendingRow == null || pendingTarget == null ||
                (!pointerOverRow && !(openIfTargetHovered && pendingTarget.IsMouseOver))) return;
            ShowPendingRow();
        }

        private void ShowPendingRow()
        {
            if (pendingRow == null || pendingTarget == null) return;
            closeTimer.Stop();
            bool reanchor = popup.IsOpen && !ReferenceEquals(popup.PlacementTarget, pendingTarget);
            PopupAnimation animation = popup.PopupAnimation;
            try
            {
                if (reanchor)
                {
                    // WPF does not reposition an open popup when only PlacementTarget changes.
                    // Reopen without a fade so a different row gets fresh screen coordinates.
                    popup.PopupAnimation = PopupAnimation.None;
                    popup.IsOpen = false;
                }
                CurrentRow = pendingRow;
                card.DataContext = getDetail(pendingRow);
                popup.PlacementTarget = pendingTarget;
                popup.Placement = PlacementMode.Custom;
                popup.StaysOpen = true;
                UpdateBounds();
                popup.IsOpen = true;
            }
            finally { popup.PopupAnimation = animation; }
            closeTimer.Start();
        }

        private void CloseTimer_Tick(object? sender, EventArgs e)
        {
            closeTimer.Stop();
            if ((keepOpenWhileOverRow && pointerOverRow) || pointerOverPopup || border.IsMouseOver || pendingTarget?.IsMouseOver == true)
                closeTimer.Start();
            else
                Close();
        }

        private CustomPopupPlacement[] PlacePopup(Size popupSize, Size targetSize, Point offset)
        {
            double y = targetSize.Height / 2 - popupSize.Height / 2;
            bool ally = FindVisualParent<DataGrid>(pendingTarget) == alliesGrid;
            double preferredX = ally ? targetSize.Width + 8 : -popupSize.Width - 8;
            double fallbackX = ally ? -popupSize.Width - 8 : targetSize.Width + 8;
            return new[]
            {
                new CustomPopupPlacement(new Point(preferredX, y), PopupPrimaryAxis.Horizontal),
                new CustomPopupPlacement(new Point(fallbackX, y), PopupPrimaryAxis.Horizontal),
                new CustomPopupPlacement(new Point(preferredX, targetSize.Height + 6), PopupPrimaryAxis.Vertical),
                new CustomPopupPlacement(new Point(preferredX, -popupSize.Height - 6), PopupPrimaryAxis.Vertical)
            };
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T match) return match;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }
    }
}
