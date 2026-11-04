using System.Windows;
using System.Windows.Controls;
using LLPlayer.Services;

namespace LLPlayer.Controls;

public static class SubtitleTrackLayout
{
    public static void Apply(StackPanel legacy, StackPanel top, StackPanel bottom,
        UIElement primary, UIElement secondary, UIElement separator,
        SubtitleTrackPlacement primaryPlacement, SubtitleTrackPlacement secondaryPlacement, double edgeMargin, double playbackBarHeight = 0)
    {
        foreach (var panel in new[] { legacy, top, bottom }) panel.Children.Clear();
        var first = Select(primaryPlacement);
        var second = Select(secondaryPlacement);
        first.Children.Add(primary);
        if (first == second) first.Children.Add(separator);
        second.Children.Add(secondary);
        top.Margin = new Thickness(0, edgeMargin, 0, 0);
        bottom.Margin = new Thickness(0, 0, 0, edgeMargin + playbackBarHeight);

        StackPanel Select(SubtitleTrackPlacement placement) => placement switch
        {
            SubtitleTrackPlacement.Top => top,
            SubtitleTrackPlacement.Bottom => bottom,
            _ => legacy
        };
    }
}
