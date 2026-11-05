using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FlyleafLib;
using LLPlayer.Services;
using LLPlayer.ViewModels;

namespace LLPlayer.Views;

public partial class FlyleafOverlay : UserControl
{
    private FlyleafOverlayVM VM => (FlyleafOverlayVM)DataContext;

    public FlyleafOverlay()
    {
        InitializeComponent();

        DataContext = ((App)Application.Current).Container.Resolve<FlyleafOverlayVM>();
        _preparationHover = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Input,
            (_, _) => UpdatePreparationHover(), Dispatcher);
        _preparationHover.Stop();
        Loaded += (_, _) => { _preparationHover.Start(); UpdatePreparationHover(); };
        Unloaded += (_, _) => { _preparationHover.Stop(); PreparationReveal.Opacity = 0; };
    }

    private readonly DispatcherTimer _preparationHover;

    private void UpdatePreparationHover()
    {
        Utils.NativeMethods.GetCursorPos(out var cursor);
        var point = PointFromScreen(new Point(cursor.X, cursor.Y));
        // Opacity keeps the reserved subtitle area stable when the progress panel is hidden.
        PreparationReveal.Opacity = PreparationHoverPolicy.Reveal(point, new Size(ActualWidth, ActualHeight),
            new Size(PreparationReveal.ActualWidth, PreparationReveal.ActualHeight), PlaybackBar.ActualHeight) ? 1 : 0;
    }

    private void FlyleafOverlay_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.HeightChanged)
        {
            // The height of MainWindow cannot be used because it includes the title bar,
            // so the height is obtained here and passed on.
            double heightDiff = Math.Abs(e.NewSize.Height - e.PreviousSize.Height);

            if (heightDiff >= 1.0)
            {
                VM.FL.Config.ScreenWidth = e.NewSize.Width;
                VM.FL.Config.ScreenHeight = e.NewSize.Height;
            }
        }
    }
}
