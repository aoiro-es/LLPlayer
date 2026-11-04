using FlyleafLib.Localization;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.Tests;

public class SubtitlePreparationProgressTests
{
    [Fact]
    public void TranslationAndExplanationHaveSeparateProgress()
    {
        var progress = new SubtitlePreparationProgress(1, 8, 6, 2, 0, true, true, true, true, false, "", null);
        Assert.Equal(75, progress.TranslationPercent);
        Assert.Equal(25, progress.ExplanationPercent);
        Assert.False(progress.IsComplete);
        Assert.Contains("6/8", progress.TranslationText);
        Assert.Contains("2/8", progress.ExplanationText);
        Assert.Equal(UiText.Get("Second subtitle"), progress.TrackText);
        Assert.Equal(UiText.Get("Pre-generating subtitles..."), progress.StateText);
    }

    [Fact]
    public void BothRequestedResultsMustBeReadyForCompletion()
    {
        var progress = new SubtitlePreparationProgress(0, 10, 10, 9, 0, true, true, true, false, false, "", null);
        Assert.False(progress.IsComplete);
        Assert.True((progress with { Explained = 10 }).IsComplete);
        Assert.True((progress with { ExplainEnabled = false }).IsComplete);
        Assert.False((progress with { Explained = 10, IsLoading = true }).IsComplete);
        Assert.False((progress with { Explained = 10, AwaitingText = 1 }).IsComplete);
        Assert.False((progress with { Explained = 10, Error = "test failure" }).IsComplete);
    }

    [Fact]
    public void LoadingStoppedErrorAndWindowedStatesAreDistinct()
    {
        var progress = new SubtitlePreparationProgress(0, 4, 2, 0, 0, true, false, true, false, false, "", null);
        Assert.Equal(UiText.Get("Reading subtitles..."), (progress with { IsLoading = true }).StateText);
        Assert.Equal(UiText.Get("Automatic preparation stopped"), (progress with { TranslateEnabled = false }).StateText);
        Assert.Equal(UiText.Get("Nearby subtitles ready"), (progress with { WholeTrack = false }).StateText);
        var failed = progress with { Error = "test failure" };
        Assert.True(failed.HasError);
        Assert.Equal("test failure", failed.DetailText);
        Assert.Equal(UiText.Get("Preparation failed"), failed.StateText);
        Assert.False(SubtitlePreparationProgress.Empty(0).IsVisible);
        Assert.Equal(0, SubtitlePreparationProgress.Empty(0).TranslationPercent);
    }

    [Fact]
    public void OcrCuesStayInDenominatorAndNewFilesResetCountersAndErrors()
    {
        Utils.IsTesting = true;
        var config = new Config(true);
        config.Subtitles.TranslateServiceType = TranslateServiceType.Codex;
        config.Subtitles.TranslateServiceSettings[TranslateServiceType.Codex] = new CodexTranslateSettings();
        var manager = new SubManager(config, 0, () => new UnusedTranslator());
        manager.Load([
            new SubtitleData { Text = "Known", TranslatedText = "訳", Explanation = "解説" },
            new SubtitleData { IsBitmap = true },
            new SubtitleData { Text = "" }
        ]);
        var progress = manager.PreparationProgress;
        Assert.Equal(2, progress.Total);
        Assert.Equal(1, progress.Translated);
        Assert.Equal(1, progress.Explained);
        Assert.Equal(1, progress.AwaitingText);
        Assert.False(progress.IsComplete);
        manager.TranslationError = "old file failure";
        manager.Clear();
        Assert.False(manager.PreparationProgress.IsVisible);
        Assert.Null(manager.PreparationProgress.Error);
        manager.Load([new SubtitleData { Text = "New file" }]);
        Assert.Equal(1, manager.PreparationProgress.Total);
        Assert.Equal(0, manager.PreparationProgress.Translated);
        Assert.Equal(0, manager.PreparationProgress.Explained);
    }

    [Fact]
    public async Task WorkerPublishesCompletedCountsAfterEachBatch()
    {
        Utils.IsTesting = true;
        var config = new Config(true);
        config.Subtitles.TranslateServiceType = TranslateServiceType.Codex;
        config.Subtitles.TranslateServiceSettings[TranslateServiceType.Codex] = new CodexTranslateSettings { BatchSize = 1 };
        var service = new GatedTranslator();
        var manager = new SubManager(config, 0, () => service) { LanguageSource = Language.Get("en") };
        manager.Load([new SubtitleData { Text = "One" }, new SubtitleData { Text = "Two" }]);
        await Wait(() => manager.PreparationProgress.Translated == 1);
        Assert.Equal(1, manager.PreparationProgress.Explained);
        Assert.Equal(50, manager.PreparationProgress.TranslationPercent);
        Assert.False(manager.PreparationProgress.IsComplete);
        service.Finish.TrySetResult();
        await Wait(() => manager.PreparationProgress.IsComplete);
        Assert.Equal(100, manager.PreparationProgress.ExplanationPercent);
        ((CodexTranslateSettings)config.Subtitles.TranslateServiceSettings[TranslateServiceType.Codex]).PreGenerateExplanations = false;
    }

    [Fact]
    public async Task ClearingCachedResultsUpdatesProgressEvenWhenTranslationIsStopped()
    {
        Utils.IsTesting = true;
        var config = new Config(true);
        var manager = new SubManager(config, 0, () => new UnusedTranslator());
        manager.Load([new SubtitleData { Text = "Cached", TranslatedText = "訳", Explanation = "解説" }]);
        Assert.True(manager.PreparationProgress.IsVisible);
        config.Subtitles.TranslateTargetLanguage = TargetLanguage.French;
        await Wait(() => manager.PreparationProgress.Translated == 0 && manager.PreparationProgress.Explained == 0);
        Assert.False(manager.PreparationProgress.IsVisible);
    }

    private static async Task Wait(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(5000);
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private class UnusedTranslator : ITranslateService
    {
        public TranslateServiceType ServiceType => TranslateServiceType.Codex;
        public void Initialize(Language source, TargetLanguage target) { }
        public void Dispose() { }
        public Task<string> TranslateAsync(string text, CancellationToken token) => throw new InvalidOperationException("Not needed.");
    }

    private sealed class GatedTranslator : UnusedTranslator, ISubtitleBatchTranslator
    {
        public TaskCompletionSource Finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        public async Task<IReadOnlyList<SubtitleBatchResult>> TranslateBatchAsync(IReadOnlyList<SubtitleData> captions,
            IReadOnlyList<SubtitleData> before, IReadOnlyList<SubtitleData> after, CancellationToken token)
        {
            if (Interlocked.Increment(ref _calls) > 1) await Finish.Task.WaitAsync(token);
            return captions.Select(c => new SubtitleBatchResult(c.Index, "訳:" + c.Text, "解説:" + c.Text)).ToArray();
        }
    }
}
