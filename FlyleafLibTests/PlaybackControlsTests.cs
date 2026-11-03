using System.Windows.Input;
using FlyleafLib.MediaPlayer;
using LLPlayer.Services;

namespace FlyleafLib.Tests;

public class PlaybackControlsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PresetsHaveUniqueEnabledShortcuts(bool mpc)
    {
        var keys = KeyboardProfiles.Create(mpc);
        Assert.All(keys.Where(k => k.IsEnabled).GroupBy(k => (k.Key, k.Ctrl, k.Alt, k.Shift)), g => Assert.Single(g));
        Assert.Contains(keys, k => k.Key == Key.F5 && k.Action == KeyBindingAction.ToggleFullScreen);
        Assert.Contains(keys, k => k.ActionName == nameof(CustomKeyBindingAction.OpenShortcuts));
    }

    [Fact]
    public void AddingNewControlsPreservesCustomizedBindingsAndIsIdempotent()
    {
        KeysConfig keys = new() { Keys = [] };
        keys.Add(Key.F5, KeyBindingAction.ToggleMute);
        KeyboardProfiles.AddNewShortcuts(keys, false);
        var count = keys.Keys.Count;
        KeyboardProfiles.AddNewShortcuts(keys, false);
        Assert.Equal(count, keys.Keys.Count);
        Assert.Equal(KeyBindingAction.ToggleMute, keys.Keys.Single(k => k.Key == Key.F5).Action);
    }

    [Fact]
    public void MpcPresetUsesControlArrowsForSubtitles()
    {
        var keys = KeyboardProfiles.Create(true);
        Assert.Equal(nameof(CustomKeyBindingAction.PreviousSubtitle), keys.Single(k => k.Key == Key.Left && k.Ctrl && !k.Alt && !k.Shift).ActionName);
        Assert.Equal(nameof(CustomKeyBindingAction.NextSubtitle), keys.Single(k => k.Key == Key.Right && k.Ctrl && !k.Alt && !k.Shift).ActionName);
    }

    [Theory]
    [InlineData(SubtitleTrackPlacement.Top, 10, true)]
    [InlineData(SubtitleTrackPlacement.Top, 200, false)]
    [InlineData(SubtitleTrackPlacement.Bottom, 700, true)]
    [InlineData(SubtitleTrackPlacement.Bottom, 10, false)]
    [InlineData(SubtitleTrackPlacement.Legacy, 700, true)]
    [InlineData(SubtitleTrackPlacement.Top, -1, false)]
    public void HoverRevealUsesChosenScreenEdge(SubtitleTrackPlacement placement, double y, bool expected)
    {
        Assert.Equal(expected, SubtitleLayoutPolicy.Reveal(placement, true, 100, y, 1280, 720, 96));
        Assert.True(SubtitleLayoutPolicy.Reveal(placement, false, 100, y, 1280, 720, 96));
        Assert.False(SubtitleLayoutPolicy.Reveal(placement, true, 1500, y, 1280, 720, 96));
    }

    [Theory]
    [InlineData(SubtitleTrackPlacement.Top)]
    [InlineData(SubtitleTrackPlacement.Bottom)]
    public void ForcedPositionsOverrideEmbeddedBitmapCoordinates(SubtitleTrackPlacement placement)
    {
        Assert.False(SubtitleLayoutPolicy.UseAbsoluteBitmap(true, placement));
        Assert.True(SubtitleLayoutPolicy.UseAbsoluteBitmap(true, SubtitleTrackPlacement.Legacy));
    }
}
