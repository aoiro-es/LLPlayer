using System.Windows.Input;
using FlyleafLib.MediaPlayer;
using KeyBinding = FlyleafLib.MediaPlayer.KeyBinding;

namespace LLPlayer.Services;

public static class KeyboardProfiles
{
    public static List<KeyBinding> Create(bool mpc)
    {
        KeysConfig keys = new();
        keys.LoadDefault();
        keys.Keys.AddRange(AppActions.DefaultCustomActionsMap());
        AddNewShortcuts(keys, false);
        if (mpc)
        {
            keys.Remove(Key.Left, ctrl: true);
            keys.Remove(Key.Right, ctrl: true);
            keys.Keys.Add(Custom(Key.Left, CustomKeyBindingAction.PreviousSubtitle, ctrl: true));
            keys.Keys.Add(Custom(Key.Right, CustomKeyBindingAction.NextSubtitle, ctrl: true));
            keys.Add(Key.Left, KeyBindingAction.ShowPrevFrame, ctrl: true, alt: true);
            keys.Add(Key.Right, KeyBindingAction.ShowNextFrame, ctrl: true, alt: true);
        }
        return keys.Keys;
    }

    public static void AddNewShortcuts(KeysConfig keys, bool bindActions = true)
    {
        AddIfFree(new KeyBinding { Key = Key.F5, Action = KeyBindingAction.ToggleFullScreen, IsKeyUp = true });
        AddIfFree(new KeyBinding { Key = Key.Enter, Alt = true, Action = KeyBindingAction.ToggleFullScreen, IsKeyUp = true });
        AddIfFree(Custom(Key.K, CustomKeyBindingAction.OpenShortcuts, ctrl: true));
        AddIfFree(Custom(Key.Left, CustomKeyBindingAction.PreviousSubtitle, ctrl: true, alt: true));
        AddIfFree(Custom(Key.Right, CustomKeyBindingAction.NextSubtitle, ctrl: true, alt: true));

        void AddIfFree(KeyBinding binding)
        {
            if (keys.Keys.Any(k => k.Key == binding.Key && k.Ctrl == binding.Ctrl && k.Alt == binding.Alt && k.Shift == binding.Shift)) return;
            if (bindActions && binding.Action != KeyBindingAction.Custom)
                binding.SetAction(keys.GetKeyBindingAction(binding.Action), binding.IsKeyUp);
            keys.Keys.Add(binding);
        }
    }

    private static KeyBinding Custom(Key key, CustomKeyBindingAction action, bool ctrl = false, bool alt = false) =>
        new() { Key = key, Ctrl = ctrl, Alt = alt, ActionName = action.ToString(), IsKeyUp = true };
}
