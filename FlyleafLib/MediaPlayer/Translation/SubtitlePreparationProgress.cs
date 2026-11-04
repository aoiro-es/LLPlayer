using FlyleafLib.Localization;

#nullable enable
namespace FlyleafLib.MediaPlayer.Translation;

// One immutable snapshot keeps the counters, denominator and state consistent in WPF bindings.
public sealed record SubtitlePreparationProgress(int SubIndex, int Total, int Translated, int Explained,
    int AwaitingText, bool TranslateEnabled, bool ExplainEnabled, bool WholeTrack,
    bool IsBusy, bool IsLoading, string WaitingReason, string? Error)
{
    public static SubtitlePreparationProgress Empty(int index) => new(index, 0, 0, 0, 0, false, false, false, false, false, "", null);
    public bool IsVisible => (Total > 0 || IsLoading) && (TranslateEnabled || ExplainEnabled || Translated > 0 || Explained > 0 || HasError);
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public bool ShowTranslation => TranslateEnabled || Translated > 0;
    public bool ShowExplanation => ExplainEnabled || Explained > 0;
    public bool IsIndeterminate => IsLoading && Total == 0;
    public bool IsComplete => !HasError && Total > 0 && !IsLoading && AwaitingText == 0 && (TranslateEnabled || ExplainEnabled)
        && (!TranslateEnabled || Translated >= Total) && (!ExplainEnabled || Explained >= Total);
    public double TranslationPercent => Percent(Translated);
    public double ExplanationPercent => Percent(Explained);
    public string TrackText => UiText.Get(SubIndex == 0 ? "First subtitle" : "Second subtitle");
    public string TranslationText => UiText.Format($"Translation {Translated}/{Total} ({TranslationPercent:0}%)");
    public string ExplanationText => UiText.Format($"AI explanations {Explained}/{Total} ({ExplanationPercent:0}%)");
    public string StateText => HasError ? UiText.Get("Preparation failed")
        : IsLoading ? UiText.Get("Reading subtitles...")
        : IsComplete ? UiText.Get("Preparation complete")
        : IsBusy ? UiText.Get(WholeTrack ? "Pre-generating subtitles..." : "Translating nearby subtitles...")
        : !TranslateEnabled && !ExplainEnabled ? UiText.Get("Automatic preparation stopped")
        : !string.IsNullOrEmpty(WaitingReason) ? WaitingReason
        : AwaitingText > 0 ? UiText.Get("Waiting for OCR text...")
        : !WholeTrack ? UiText.Get("Nearby subtitles ready") : UiText.Get("Waiting for preparation...");
    public string DetailText => HasError ? Error! : AwaitingText > 0 ? UiText.Format($"Waiting for OCR: {AwaitingText} subtitles") : "";
    public bool ShowDetail => DetailText.Length > 0;
    private double Percent(int completed) => Total > 0 ? Math.Clamp(100.0 * completed / Total, 0, 100) : 0;
}
