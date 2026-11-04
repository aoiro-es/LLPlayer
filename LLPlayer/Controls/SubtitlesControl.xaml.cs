using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using FlyleafLib;
using System.Windows.Controls;
using FlyleafLib.MediaPlayer;
using LLPlayer.Services;

namespace LLPlayer.Controls;

public partial class SubtitlesControl : UserControl
{
    public FlyleafManager FL { get; }

    public SubtitlesControl()
    {
        InitializeComponent();

        FL = ((App)Application.Current).Container.Resolve<FlyleafManager>();

        DataContext = this;
        _hoverTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Input,
            (_, _) => UpdateHover(), Dispatcher);
        _hoverTimer.Stop();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private readonly DispatcherTimer _hoverTimer;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FL.Config.Subs.PropertyChanged += OnLayoutChanged;
        UpdateLayoutPanels();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        FL.Config.Subs.PropertyChanged -= OnLayoutChanged;
        _hoverTimer.Stop();
    }

    private void OnLayoutChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppConfigSubs.PrimaryPlacement) or nameof(AppConfigSubs.SecondaryPlacement)
            or nameof(AppConfigSubs.PrimaryOnlyOnEdgeHover) or nameof(AppConfigSubs.SecondaryOnlyOnEdgeHover)
            or nameof(AppConfigSubs.EdgeMargin) or nameof(AppConfigSubs.HoverRegionHeight))
        {
            if (Dispatcher.CheckAccess()) UpdateLayoutPanels();
            else Dispatcher.BeginInvoke(UpdateLayoutPanels);
        }
    }

    private void UpdateLayoutPanels()
    {
        var config = FL.Config.Subs;
        SubtitleTrackLayout.Apply(SubtitlePanel, TopSubtitlePanel, BottomSubtitlePanel,
            PrimarySubtitleContent, SecondarySubtitleContent, SubtitleSeparator,
            config.PrimaryPlacement, config.SecondaryPlacement, config.EdgeMargin);
        if (config.PrimaryOnlyOnEdgeHover || config.SecondaryOnlyOnEdgeHover) _hoverTimer.Start();
        else _hoverTimer.Stop();
        UpdateHover();
    }

    private void UpdateHover()
    {
        if (!IsLoaded) return;
        Utils.NativeMethods.GetCursorPos(out var cursor);
        var point = PointFromScreen(new Point(cursor.X, cursor.Y));
        var config = FL.Config.Subs;
        SetReveal(PrimarySubtitleContent, 0, config.PrimaryPlacement, config.PrimaryOnlyOnEdgeHover);
        SetReveal(SecondarySubtitleContent, 1, config.SecondaryPlacement, config.SecondaryOnlyOnEdgeHover);
        SubtitleSeparator.SetCurrentValue(OpacityProperty,
            PrimarySubtitleContent.Opacity > 0 && SecondarySubtitleContent.Opacity > 0 ? config.SubsSeparatorOpacity : 0);

        void SetReveal(Grid content, int index, SubtitleTrackPlacement placement, bool hover)
        {
            bool visible = SubtitleLayoutPolicy.Reveal(placement, hover, point.X, point.Y, ActualWidth, ActualHeight, config.HoverRegionHeight)
                || (hover && content.IsMouseOver) || (WordPopupHost.IsOpen && WordPopupHost.PlacementTarget ==
                    content.Children.OfType<SelectableSubtitleText>().FirstOrDefault());
            content.Opacity = visible ? 1 : 0;
            content.IsHitTestVisible = visible;
            ((UIElement)SubtitleAbsoluteBitmap.Children[index]).Opacity = visible ? 1 : 0;
        }
    }

    private void SubtitlePanel_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.HeightChanged)
        {
            // Sometimes there is a very small difference in decimal points when the subtitles are switched, and this event fires.
            // If you update the margin of the Sub at this time, the window will go wrong, so do it only when the difference is above a certain level.
            double heightDiff = Math.Abs(e.NewSize.Height - e.PreviousSize.Height);
            if (heightDiff >= 1.0)
            {
                FL.Config.Subs.SubsPanelSize = e.NewSize;
            }
        }
    }

    private async void SelectableSubtitleText_OnWordClicked(object sender, WordClickedEventArgs e)
    {
        if (e.Sender is SelectableSubtitleText text)
        {
            WordPopupHost.PlacementTarget = text;
            WordPopupControl.PopupBelow = (text.SubIndex == 0 ? FL.Config.Subs.PrimaryPlacement : FL.Config.Subs.SecondaryPlacement) == SubtitleTrackPlacement.Top;
        }
        await WordPopupControl.OnWordClicked(e);
    }

    private void SelectableSubtitleText_OnWordClickedDown(object? sender, EventArgs e)
    {
        // Assume drag and stop playback.
        if (FL.Player.Status == Status.Playing)
        {
            FL.Player.Pause();
        }
    }
}
