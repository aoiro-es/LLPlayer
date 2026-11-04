using System.Windows;
using System.Windows.Controls;
using LLPlayer.Services;
using LLPlayer.ViewModels;

namespace LLPlayer.Controls.Settings;

public partial class SettingsSubtitlesPS : UserControl
{
    private void BothBottom_OnClick(object sender, RoutedEventArgs e) => SetPositions(SubtitleTrackPlacement.Bottom);
    private void Split_OnClick(object sender, RoutedEventArgs e) => SetPositions(SubtitleTrackPlacement.Top);
    private void SetPositions(SubtitleTrackPlacement second)
    {
        if (DataContext is not SettingsDialogVM vm) return;
        vm.FL.Config.Subs.PrimaryPlacement = SubtitleTrackPlacement.Bottom;
        vm.FL.Config.Subs.SecondaryPlacement = second;
    }

    public SettingsSubtitlesPS()
    {
        InitializeComponent();
    }
}
