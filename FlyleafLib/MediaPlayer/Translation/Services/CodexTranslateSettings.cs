using System.Text.Json.Serialization;

#nullable enable
namespace FlyleafLib.MediaPlayer.Translation.Services;

public class CodexTranslateSettings : NotifyPropertyChanged, ITranslateSettings
{
    public string ExecutablePath { get; set => Set(ref field, value); } = "";
    // Empty values follow the defaults supplied by Codex, rather than a model baked into LLPlayer.
    public string Model { get; set => Set(ref field, value); } = "";
    public string ReasoningEffort { get; set => Set(ref field, value); } = "";
    public int TimeoutMs { get; set => Set(ref field, Math.Clamp(value, 1000, 600000)); } = 120000;
}
