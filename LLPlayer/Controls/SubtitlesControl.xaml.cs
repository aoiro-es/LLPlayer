using System.ComponentModel;
using FlyleafLib.Localization;
using FlyleafLib.MediaPlayer.Translation.Services;
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

    public static readonly DependencyProperty PlaybackBarHeightProperty = DependencyProperty.Register(
        nameof(PlaybackBarHeight), typeof(double), typeof(SubtitlesControl),
        new PropertyMetadata(80.0, (d, _) => { if (((SubtitlesControl)d).IsLoaded) ((SubtitlesControl)d).UpdateLayoutPanels(); }));
    public double PlaybackBarHeight { get => (double)GetValue(PlaybackBarHeightProperty); set => SetValue(PlaybackBarHeightProperty, value); }

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
        HideExplanation();
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
        HideExplanation();
        WordPopupControl.IsOpen = false;
        var config = FL.Config.Subs;
        SubtitleTrackLayout.Apply(SubtitlePanel, TopSubtitlePanel, BottomSubtitlePanel,
            PrimarySubtitleContent, SecondarySubtitleContent, SubtitleSeparator,
            config.PrimaryPlacement, config.SecondaryPlacement, config.EdgeMargin, PlaybackBarHeight);
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
            visible |= ExplanationHost.IsOpen && _explanationAnchor?.SubIndex == index;
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

    private CancellationTokenSource? _explanationCancellation;
    private SelectableSubtitleText? _explanationAnchor;

    private void HideExplanation()
    {
        ExplanationHost.IsOpen = false;
        _explanationAnchor?.HoldSentenceHighlight(false);
        _explanationAnchor = null;
        _explanationCancellation?.Cancel();
    }

    private void CloseExplanation(object sender, RoutedEventArgs e) => HideExplanation();
    private void OnSentenceChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _explanationAnchor)) HideExplanation();
    }

    private async void OnSentenceHovered(object? sender, EventArgs e)
    {
        if (sender is not SelectableSubtitleText anchor || string.IsNullOrWhiteSpace(anchor.Text)) return;
        var manager = FL.Player.SubtitlesManager[anchor.SubIndex];
        var caption = manager.GetCurrent();
        if (caption == null) return;
        if (ExplanationHost.IsOpen && ReferenceEquals(_explanationAnchor, anchor)) return;
        HideExplanation();
        WordPopupControl.IsOpen = false;
        _explanationAnchor = anchor;
        anchor.HoldSentenceHighlight(true);
        var cancellation = new CancellationTokenSource();
        _explanationCancellation = cancellation;
        var token = cancellation.Token;
        ExplanationHost.PlacementTarget = anchor;
        bool below = anchor.TranslatePoint(new Point(), this).Y < ActualHeight / 2;
        ExplanationHost.CustomPopupPlacementCallback = (popup, target, _) => SubtitlePopupPlacement.Candidates(popup, target, below);
        ExplanationCard.MaxHeight = Math.Max(100, Math.Min(420, ActualHeight - anchor.ActualHeight - 30));
        ExplanationText.Text = caption.Text + "\n\n" + (caption.Explanation ?? UiText.Get("Generating explanation..."));
        ExplanationHost.IsOpen = true;
        if (!string.IsNullOrWhiteSpace(caption.Explanation)) { cancellation.Dispose(); _explanationCancellation = null; return; }
        try
        {
            // On-demand fallback when pre-generation is disabled or has not reached this cue yet.
            var config = FL.PlayerConfig.Subtitles;
            var settings = config.TranslateServiceSettings.GetValueOrDefault(TranslateServiceType.Codex) as CodexTranslateSettings ?? new();
            while (settings.PreGenerateExplanations && manager.IsTranslating && caption.Explanation == null)
                await Task.Delay(100, token);
            token.ThrowIfCancellationRequested();
            if (caption.Explanation != null) { ExplanationText.Text = caption.Text + "\n\n" + caption.Explanation; return; }
            using var service = new CodexTranslateService(settings, config.TranslateChatConfig, false);
            service.Initialize(manager.Language ?? FlyleafLib.Language.Unknown, config.TranslateTargetLanguage);
            var all = manager.GetSnapshot();
            int index = System.Array.IndexOf(all, caption);
            int context = settings.ContextCues;
            var result = await service.GenerateBatchAsync([caption], all.Skip(Math.Max(0, index - context)).Take(Math.Min(index, context)).ToArray(),
                all.Skip(index + 1).Take(context).ToArray(), true, token);
            if (!token.IsCancellationRequested && ReferenceEquals(manager.GetCurrent(), caption))
            {
                caption.Explanation = result[0].Explanation;
                ExplanationText.Text = caption.Text + "\n\n" + caption.Explanation;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!token.IsCancellationRequested) ExplanationText.Text = UiText.Get("Explanation unavailable. Check Codex sign-in and translation settings."); }
        finally
        {
            if (ReferenceEquals(_explanationCancellation, cancellation)) _explanationCancellation = null;
            cancellation.Dispose();
        }
    }

    private async void SelectableSubtitleText_OnWordClicked(object sender, WordClickedEventArgs e)
    {
        HideExplanation();
        if ((e.Sender ?? e.OriginalSource) is SelectableSubtitleText text)
        {
            WordPopupHost.PlacementTarget = text;
            WordPopupControl.PopupBelow = text.TranslatePoint(new Point(), this).Y < ActualHeight / 2;
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
