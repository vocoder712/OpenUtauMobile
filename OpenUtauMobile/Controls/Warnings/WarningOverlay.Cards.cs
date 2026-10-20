using System;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using OpenUtauMobile.Services.Warnings;
using OpenUtauMobile.Themes.OpenUtauMobile.Controls;

namespace OpenUtauMobile.Controls.Warnings
{
    public partial class WarningOverlay
    {
        private IDisposable? _clearShadowSubscription;
        private Color _clearShadowColor;

        private void AttachCardAppearance()
        {
            _clearShadowSubscription = ClearButton.GetResourceObservable("Sem.Value.Shadow").Subscribe(value =>
            {
                if (value is Color color)
                {
                    _clearShadowColor = color;
                    UpdateClearElevation();
                }
            });
        }

        private void ClearFabStateChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == IsPointerOverProperty || e.Property == Button.IsPressedProperty || e.Property == IsEnabledProperty)
                UpdateClearElevation();
        }

        /// <summary>复用共享按钮的表面阴影接口；MD3 FAB 常态 6dp、悬停 8dp，按下/聚焦仍为 6dp。</summary>
        private void UpdateClearElevation()
        {
            BoxShadows shadows = !ClearButton.IsEffectivelyEnabled ? default
                : ClearButton.IsPointerOver && !ClearButton.IsPressed ? WarningElevation.Level4(_clearShadowColor)
                : WarningElevation.Level3(_clearShadowColor);
            ButtonChrome.SetBoxShadow(ClearButton, shadows);
        }

        private void CardSwipeStarted(object? sender, EventArgs e)
        {
            ResetElastic();
            CancelCardInteractions(sender as WarningCard);
        }

        private void CardDismissRequested(WarningCard card, WarningItem item)
        {
            if (!IsCenterOpen || _closing || _clearing) return;
            if (card.IsKeyboardFocusWithin) CloseButton.Focus();
            _inbox?.Dismiss(item);
        }

        private void CardDismissMenuClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { DataContext: WarningItem item })
                WarningItems.GetVisualDescendants().OfType<WarningCard>().FirstOrDefault(card => ReferenceEquals(card.DataContext, item))?.RequestDismiss();
            e.Handled = true;
        }

        private void CancelCardInteractions(WarningCard? except = null)
        {
            foreach (WarningCard card in WarningItems.GetVisualDescendants().OfType<WarningCard>())
                if (card != except) card.CancelSwipe();
        }
    }
}
