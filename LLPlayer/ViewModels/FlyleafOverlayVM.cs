using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Runtime.InteropServices;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using LLPlayer.Extensions;
using LLPlayer.Services;
using MaterialDesignThemes.Wpf;
using static FlyleafLib.Utils.NativeMethods;

namespace LLPlayer.ViewModels;

public class FlyleafOverlayVM : Bindable
{
    public FlyleafManager FL { get; }

    public FlyleafOverlayVM(FlyleafManager fl)
    {
        FL = fl;
        _clickPlayback = new VideoClickPlaybackController(() => FL.Player.IsPlaying, FL.Player.Play, FL.Player.Pause);
    }

    public DelegateCommand CmdOnLoaded => field ??= new(() =>
    {
        SetupOSDStatus();
        SetupFlyleafContextMenu();
        SetupFlyleafKeybindings();

        FL.Config.FlyleafHostLoaded();
    });

    private void SetupFlyleafContextMenu()
    {
        // When a context menu is opened using the Overlay.MouseRightButtonUp event,
        // it is a bubble event and therefore has priority over the ContextMenu of the child controls.
        // so set ContextMenu directly to each of the controls in order to give priority to the child contextmenu.
        ContextMenu menu = (ContextMenu)Application.Current.FindResource("PopUpMenu")!;
        menu.DataContext = this;
        menu.PlacementTarget = FL.FlyleafHost!.Overlay;

        FL.FlyleafHost!.Overlay.ContextMenu = menu;
        FL.FlyleafHost!.Surface.ContextMenu = menu;
    }

    #region Flyleaf Keybindings
    // TODO: L: Make it fully customizable like PotPlayer
    private void SetupFlyleafKeybindings()
    {
        // Subscribe to the same event to the following two
        // Surface: On Video Screen
        // Overlay: Content of FlyleafHost = FlyleafOverlay
        foreach (var window in (Window[])[FL.FlyleafHost!.Surface, FL.FlyleafHost!.Overlay])
        {
            window.MouseWheel += FlyleafOnMouseWheel;
        }
        FL.FlyleafHost.Surface.MouseUp += SurfaceOnMouseUp;
        FL.FlyleafHost.Surface.MouseDoubleClick += SurfaceOnMouseDoubleClick;
        FL.FlyleafHost.Surface.MouseLeftButtonDown += SurfaceOnMouseLeftButtonDown;
        FL.FlyleafHost.Surface.MouseLeftButtonUp += SurfaceOnMouseLeftButtonUp;
        FL.FlyleafHost.Surface.MouseMove += SurfaceOnMouseMove;
        FL.FlyleafHost.Surface.LostMouseCapture += SurfaceOnLostMouseCapture;
    }

    private void SurfaceOnMouseUp(object sender, MouseButtonEventArgs e)
    {
        switch (e.ChangedButton)
        {
            // Middle click: seek to current subtitle
            case MouseButton.Middle:
                bool ctrlDown = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
                if (ctrlDown)
                {
                    // CTRL + Middle click: Reset zoom
                    FL.Player.ResetZoom();
                    e.Handled = true;
                    return;
                }

                if (FL.Player.Subtitles[0].Enabled)
                {
                    FL.Player.Subtitles.CurSeek();
                }
                e.Handled = true;
                break;

            // X1 click: subtitles prev seek with fallback
            case MouseButton.XButton1:
                FL.Player.Subtitles.PrevSeekFallback();
                e.Handled = true;
                break;

            // X2 click: subtitles next seek with fallback
            case MouseButton.XButton2:
                FL.Player.Subtitles.NextSeekFallback();
                e.Handled = true;
                break;
        }
    }

    private void SurfaceOnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FL.Config.MouseDoubleClickToFullScreen && e.ChangedButton == MouseButton.Left)
        {
            FL.Player.ToggleFullScreen();

            _clickPlayback.DoubleClick(Environment.TickCount64, GetDoubleClickTime());
            _ignoreDoubleClickMouseUp = true;
            _isLeftDragging = false;

            e.Handled = true;
        }
    }

    private bool _isLeftDragging;
    private Point _downPoint;
    private Point _downCursorPoint;
    private long _downTick;

    private void SurfaceOnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) _ignoreDoubleClickMouseUp = false;
        if (e.ClickCount == 2 && FL.Config.MouseDoubleClickToFullScreen) return;
        // Support window dragging & play / pause toggle
        if (Keyboard.Modifiers != ModifierKeys.None)
            return;

        if (FL.FlyleafHost is not { IsResizing: false, IsPanMoving: false, IsDragMoving: false })
            return;

        Point downPoint = FL.FlyleafHost!.Surface.PointToScreen(default);
        long downTick = Stopwatch.GetTimestamp();

        if (!FL.FlyleafHost.IsFullScreen && FL.FlyleafHost.Owner.WindowState == WindowState.Normal)
        {
            // normal window: window dragging
            DragMoveOwner();

            MouseLeftButtonUpAction(downPoint, downTick);
        }
        else
        {
            // fullscreen or maximized window: do action in KeyUp event
            _isLeftDragging = true;
            _downPoint = downPoint;
            _downTick = downTick;

            _downCursorPoint = e.GetPosition((IInputElement)sender);
        }
    }

    // When left dragging while maximized, move window back to normal and move window
    private void SurfaceOnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isLeftDragging || e.LeftButton != MouseButtonState.Pressed)
            return;

        if (FL.FlyleafHost!.IsFullScreen || FL.FlyleafHost.Owner.WindowState != WindowState.Maximized)
            return;

        Point curPoint = e.GetPosition((IInputElement)sender);

        // distinguish between mouse click and dragging (to prevent misfire)
        if (Math.Abs(curPoint.X - _downCursorPoint.X) >= 60 ||
            Math.Abs(curPoint.Y - _downCursorPoint.Y) >= 60)
        {
            _isLeftDragging = false;

            // change to normal window
            FL.FlyleafHost.Owner.WindowState = WindowState.Normal;

            // start dragging
            DragMoveOwner();
        }
    }

    private void DragMoveOwner()
    {
        // (!SeekBarShowOnlyMouseOver) always show cursor when moving
        // (SeekBarShowOnlyMouseOver)  prevent to activate seek bar
        if (!FL.Config.SeekBarShowOnlyMouseOver)
        {
            FL.Player.Activity.IsEnabled = false;
        }

        FL.FlyleafHost!.Owner.DragMove();

        if (!FL.Config.SeekBarShowOnlyMouseOver)
        {
            FL.Player.Activity.IsEnabled = true;
        }
    }

    private void SurfaceOnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_ignoreDoubleClickMouseUp)
        {
            _ignoreDoubleClickMouseUp = false;
            return;
        }
        if (!_isLeftDragging)
            return;

        _isLeftDragging = false;

        MouseLeftButtonUpAction(_downPoint, _downTick);
    }

    private void SurfaceOnLostMouseCapture(object sender, MouseEventArgs e)
    {
        _isLeftDragging = false;
    }

    private void MouseLeftButtonUpAction(Point downPoint, long downTick)
    {
        Point upPoint = FL.FlyleafHost!.Surface.PointToScreen(default);

        if (downPoint == upPoint // if not moved at all
            ||
            Stopwatch.GetElapsedTime(downTick) <= TimeSpan.FromMilliseconds(200)
            &&                   // if click within 200ms and not so much moved
            Math.Abs(upPoint.X - downPoint.X) < 60 && Math.Abs(upPoint.Y - downPoint.Y) < 60)
        {
            SingleClickAction();
        }
    }

    private readonly VideoClickPlaybackController _clickPlayback;
    private bool _ignoreDoubleClickMouseUp;
    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    private void SingleClickAction()
    {
        if (FL.Config.MouseSingleClickToPlay && FL.Player.CanPlay)
            _clickPlayback.SingleClick(Environment.TickCount64);
    }

    private void FlyleafOnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // CTRL + Wheel  : Zoom in / out
        // SHIFT + Wheel : Subtitles Up / Down
        // CTRL + SHIFT + Wheel : Subtitles Size Increase / Decrease
        if (e.Delta == 0 || e.Handled)
        {
            return;
        }

        Window surface = FL.FlyleafHost!.Surface;

        bool ctrlDown = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
        bool shiftDown = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

        bool actionDone = false;

        if (ctrlDown && shiftDown)
        {
            if (e.Delta > 0)
            {
                FL.Action.CmdSubsSizeIncrease.Execute();
            }
            else
            {
                FL.Action.CmdSubsSizeDecrease.Execute();
            }
            actionDone = true;
        }
        else if (ctrlDown)
        {
            Point cur = e.GetPosition(surface);
            Point curDpi = new(cur.X * DpiX, cur.Y * DpiY);
            if (e.Delta > 0)
            {
                FL.PlayerConfig.Video.ZoomIn(curDpi);
            }
            else
            {
                FL.PlayerConfig.Video.ZoomOut(curDpi);
            }
            actionDone = true;
        }
        else if (shiftDown)
        {
            if (e.Delta > 0)
            {
                FL.Action.CmdSubsPositionUp.Execute();
            }
            else
            {
                FL.Action.CmdSubsPositionDown.Execute();
            }
            actionDone = true;
        }
        else
        {
            if (FL.Config.MouseWheelToVolumeUpDown)
            {
                if (e.Delta > 0)
                {
                    FL.PlayerConfig.Audio.VolumeUp();
                }
                else
                {
                    FL.PlayerConfig.Audio.VolumeDown();
                }
                actionDone = true;
            }
        }

        if (actionDone)
        {
            e.Handled = true;
        }
    }
    #endregion

    #region OSD
    private void SetupOSDStatus()
    {
        var player = FL.Player;

        player.PropertyChanged += (o, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(player.OSDMessage):
                    OSDMessage = player.OSDMessage;
                    break;
                case nameof(player.ReversePlayback):
                    OSDMessage = UiText.Format($"Reverse Playback {UiText.Get(player.ReversePlayback ? "On" : "Off")}");
                    break;
                case nameof(player.LoopPlayback):
                    OSDMessage = UiText.Format($"Loop Playback {UiText.Get(player.LoopPlayback ? "On" : "Off")}");
                    break;
                case nameof(player.Speed):
                    OSDMessage = UiText.Format($"Speed x{player.Speed}");
                    break;
                case nameof(player.Status):
                    // Change only Play and Pause to icon
                    switch (player.Status)
                    {
                        case Status.Paused:
                            OSDIcon = PackIconKind.Pause;
                            return;
                        case Status.Playing:
                            OSDIcon = PackIconKind.Play;
                            return;
                    }
                    OSDMessage = UiText.Describe(player.Status);
                    break;
            }
        };

        player.Audio.PropertyChanged += (o, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(player.Audio.Volume):
                    OSDMessage = UiText.Format($"Volume {player.Audio.Volume}%");
                    break;
                case nameof(player.Audio.Mute):
                    OSDIcon = player.Audio.Mute ? PackIconKind.VolumeOff : PackIconKind.VolumeHigh;
                    break;
            }
        };

        player.Config.Player.PropertyChanged += (o, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(player.Config.Player.SeekAccurate):
                    OSDMessage = UiText.Format($"Always Seek Accurate {UiText.Get(player.Config.Player.SeekAccurate ? "On" : "Off")}");
                    break;
            }
        };

        player.Config.Audio.PropertyChanged += (o, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(player.Config.Audio.Enabled):
                    OSDMessage = UiText.Format($"Audio {UiText.Get(player.Config.Audio.Enabled ? "Enabled" : "Disabled")}");
                    break;
                case nameof(player.Config.Audio.Delay):
                    OSDMessage = UiText.Format($"Audio Delay {player.Config.Audio.Delay / 10000}ms");
                    break;
            }
        };

        player.Config.Video.PropertyChanged += (o, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(player.Config.Video.Rotation):
                    OSDMessage = UiText.Format($"Rotation {player.Config.Video.Rotation}°");
                    break;
                case nameof(player.Config.Video.Zoom):
                    OSDMessage = UiText.Format($"Zoom {player.Config.Video.Zoom}%");
                    break;
                case nameof(player.Config.Video.Enabled):
                    OSDMessage = UiText.Format($"Video {UiText.Get(player.Config.Video.Enabled ? "Enabled" : "Disabled")}");
                    break;
                case nameof(player.Config.Video.AspectRatio):
                    OSDMessage = UiText.Format($"Aspect Ratio {player.Config.Video.AspectRatio.ToString()}");
                    break;
                case nameof(player.Config.Video.VideoAcceleration):
                    OSDMessage = UiText.Format($"Video Acceleration {UiText.Get(player.Config.Video.VideoAcceleration ? "On" : "Off")}");
                    break;
            }
        };

        foreach (var (i, config) in player.Config.Subtitles.SubConfigs.Index())
        {
            config.PropertyChanged += (o, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(config.Delay):
                        OSDMessage = UiText.Format($"{UiText.Get(i == 0 ? "Primary" : "Secondary")} Subs Delay {config.Delay / 10000}ms");
                        break;
                    case nameof(config.Visible):
                        var primary = player.Config.Subtitles[0].Visible ? UiText.Get("visible") : UiText.Get("hidden");
                        var secondary = player.Config.Subtitles[1].Visible ? UiText.Get("visible") : UiText.Get("hidden");

                        if (player.Subtitles[1].Enabled)
                        {
                            OSDMessage = UiText.Format($"Subs {primary} / {secondary}");
                        }
                        else
                        {
                            OSDMessage = UiText.Format($"Subs {primary}");
                        }
                        break;
                }
            };
        }

        // app config
        FL.Config.PropertyChanged += (o, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(FL.Config.AlwaysOnTop):
                    OSDMessage = UiText.Format($"Always on Top {UiText.Get(FL.Config.AlwaysOnTop ? "On" : "Off")}");
                    break;
            }
        };

        FL.Config.Subs.PropertyChanged += (o, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(FL.Config.Subs.SubsAutoTextCopy):
                    OSDMessage = UiText.Format($"Subs Auto Text Copy {UiText.Get(FL.Config.Subs.SubsAutoTextCopy ? "On" : "Off")}");
                    break;
            }
        };
    }

    private CancellationTokenSource? _cancelMsgToken;

    public string OSDMessage
    {
        get => _osdMessage;
        set
        {
            if (Set(ref _osdMessage, value))
            {
                if (_cancelMsgToken != null)
                {
                    _cancelMsgToken.Cancel();
                    _cancelMsgToken.Dispose();
                    _cancelMsgToken = null;
                }

                if (Set(ref _osdIcon, null, nameof(OSDIcon)))
                {
                    OnPropertyChanged(nameof(IsOSDIcon));
                }

                _cancelMsgToken = new();

                var token = _cancelMsgToken.Token;
                _ = Task.Run(() => ClearOSD(3000, token));
            }
        }
    }
    private string _osdMessage = "";

    public PackIconKind? OSDIcon
    {
        get => _osdIcon;
        set
        {
            if (Set(ref _osdIcon, value))
            {
                OnPropertyChanged(nameof(IsOSDIcon));

                if (_cancelMsgToken != null)
                {
                    _cancelMsgToken.Cancel();
                    _cancelMsgToken.Dispose();
                    _cancelMsgToken = null;
                }

                Set(ref _osdMessage, "", nameof(OSDMessage));

                _cancelMsgToken = new();

                var token = _cancelMsgToken.Token;
                _ = Task.Run(() => ClearOSD(500, token));
            }
        }
    }
    private PackIconKind? _osdIcon;

    public bool IsOSDIcon => OSDIcon != null;

    private async Task ClearOSD(int timeoutMs, CancellationToken token)
    {
        await Task.Delay(timeoutMs, token);

        if (token.IsCancellationRequested)
            return;

        Utils.UI(() =>
        {
            Set(ref _osdMessage, "", nameof(OSDMessage));
            if (Set(ref _osdIcon, null, nameof(OSDIcon)))
            {
                OnPropertyChanged(nameof(IsOSDIcon));
            }
        });
    }
    #endregion
}
