using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LLPlayer.Controls;
using LLPlayer.Converters;
using LLPlayer.Services;

namespace FlyleafLib.Tests;

public class PlaybackRegressionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SingleClickChangesPlaybackImmediatelyAndDoubleClickRestoresIt(bool initiallyPlaying)
    {
        bool playing = initiallyPlaying;
        int calls = 0;
        var controller = new VideoClickPlaybackController(() => playing, () => { playing = true; calls++; }, () => { playing = false; calls++; });
        controller.SingleClick(1000);
        Assert.Equal(!initiallyPlaying, playing); // No timer or message loop needed.
        Assert.Equal(1, calls);
        controller.DoubleClick(1200, 500);
        Assert.Equal(initiallyPlaying, playing);
        controller.DoubleClick(1250, 500);
        Assert.Equal(2, calls); // The first click is restored only once.
    }

    [Fact]
    public void SeparateClicksEachTogglePlayback()
    {
        bool playing = true;
        var controller = new VideoClickPlaybackController(() => playing, () => playing = true, () => playing = false);
        controller.SingleClick(1000);
        Assert.False(playing);
        controller.SingleClick(2000);
        Assert.True(playing);
        controller.DoubleClick(3000, 500);
        Assert.True(playing);
    }

    [Fact]
    public void AllTrackPositionCombinationsArrangeAndUpdateIndependently() => OnSta(() =>
    {
        var root = new Grid();
        var legacy = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 350, 0, 0) };
        var top = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        root.Children.Add(legacy); root.Children.Add(top); root.Children.Add(bottom);
        var first = new Border { Height = 20 };
        var second = new Border { Height = 30 };
        var gap = new Border { Height = 5 };
        foreach (var a in Enum.GetValues<SubtitleTrackPlacement>())
            foreach (var b in Enum.GetValues<SubtitleTrackPlacement>())
            {
                SubtitleTrackLayout.Apply(legacy, top, bottom, first, second, gap, a, b, 24);
                root.Measure(new Size(1280, 720)); root.Arrange(new Rect(0, 0, 1280, 720)); root.UpdateLayout();
                double firstY = first.TranslatePoint(default, root).Y;
                double secondY = second.TranslatePoint(default, root).Y;
                double expectedFirst = a switch { SubtitleTrackPlacement.Top => 24, SubtitleTrackPlacement.Bottom => 720 - 24 - 20 - (a == b ? 35 : 0), _ => 350 };
                double expectedSecond = b switch { SubtitleTrackPlacement.Top => 24 + (a == b ? 25 : 0), SubtitleTrackPlacement.Bottom => 720 - 24 - 30, _ => 350 + (a == b ? 25 : 0) };
                Assert.Equal(expectedFirst, firstY, 2);
                Assert.Equal(expectedSecond, secondY, 2);
            }
    });

    [Fact]
    public void BottomTracksReservePlaybackBarHeightAndGap() => OnSta(() =>
    {
        var root = new Grid();
        var legacy = new StackPanel();
        var top = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        root.Children.Add(legacy); root.Children.Add(top); root.Children.Add(bottom);
        var first = new Border { Height = 40 };
        var second = new Border { Height = 30 };
        SubtitleTrackLayout.Apply(legacy, top, bottom, first, second, new Border { Height = 5 },
            SubtitleTrackPlacement.Bottom, SubtitleTrackPlacement.Bottom, 24, 80);
        root.Measure(new Size(1280, 720)); root.Arrange(new Rect(0, 0, 1280, 720));
        Assert.Equal(720 - 80 - 24, second.TranslatePoint(new Point(0, 30), root).Y);
        Assert.True(first.TranslatePoint(new Point(0, 40), root).Y < second.TranslatePoint(default, root).Y);
    });

    [Fact]
    public void ForcedBitmapPositionRemovesTransparentFullFramePadding() => OnSta(() =>
    {
        var pixels = new byte[100 * 100 * 4];
        for (int y = 70; y < 90; y++) for (int x = 20; x < 80; x++) pixels[(y * 100 + x) * 4 + 3] = 255;
        var source = BitmapSource.Create(100, 100, 96, 96, PixelFormats.Bgra32, null, pixels, 400);
        source.Freeze();
        var converter = new SubtitleBitmapContentConverter();
        Assert.Same(source, converter.Convert([source, SubtitleTrackPlacement.Legacy], typeof(BitmapSource), null!, System.Globalization.CultureInfo.InvariantCulture));
        foreach (var placement in new[] { SubtitleTrackPlacement.Top, SubtitleTrackPlacement.Bottom })
        {
            var cropped = Assert.IsType<CroppedBitmap>(converter.Convert([source, placement], typeof(BitmapSource), null!, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(new Int32Rect(20, 70, 60, 20), cropped.SourceRect);
        }
    });

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
