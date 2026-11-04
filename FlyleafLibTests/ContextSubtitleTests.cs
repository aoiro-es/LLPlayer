using System.Collections.Concurrent;
using System.Text.Json;
using System.Windows;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.Controls;
using LLPlayer.Services;

namespace FlyleafLib.Tests;

public class ContextSubtitleTests
{
    [Fact]
    public void PendingTranslationNeverFallsBackToOriginal()
    {
        var cue = new SubtitleData { Text = "Original", EnabledTranslated = true };
        Assert.Null(cue.DisplayText);
        cue.TranslatedText = "訳文";
        Assert.Equal("訳文", cue.DisplayText);
        cue.EnabledTranslated = false;
        Assert.Equal("Original", cue.DisplayText);
        cue.EnabledTranslated = true;
        cue.TranslatedText = null;
        Assert.Null(cue.DisplayText);
        Assert.Equal("Original", cue.Text);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LoadingEstablishesDisplayModeAndStableIdsBeforePublishing(bool translate)
    {
        Utils.IsTesting = true;
        var config = new Config(true);
        config.Subtitles[0].EnabledTranslated = translate;
        var manager = new SubManager(config, 0, () => new BatchTranslator());
        var cues = Enumerable.Range(0, 4).Select(i => Cue($"Line {i}", i)).ToArray();
        manager.Subs.CollectionChanged += (_, _) =>
        {
            foreach (var cue in manager.Subs) Assert.Equal(translate ? null : cue.Text, cue.DisplayText);
        };
        manager.Load(cues[..2]);
        manager.AddRange(cues[2..3]);
        manager.Add(cues[3]);
        Assert.Equal(new[] { 0, 1, 2, 3 }, manager.GetSnapshot().Select(c => c.Index));
        config.Subtitles[0].EnabledTranslated = false;
    }

    [Fact]
    public void SentenceExplanationsAllowSourceAndTargetToMatch()
    {
        using var service = new CodexTranslateService(new(), new(), false);
        service.Initialize(Language.Get("ja"), TargetLanguage.Japanese);
    }

    [Fact]
    public void BatchResponsesAreMatchedByIdRatherThanOutputOrder()
    {
        var json = """{"continuity":"Alice is a doctor.","items":[{"id":5,"translation":"二","explanation":"二の解説"},{"id":2,"translation":"一","explanation":"一の解説"}]}""";
        var result = CodexTranslateService.ParseBatch(json, [2, 5], true, out var continuity);
        Assert.Equal("一", result[0].Translation);
        Assert.Equal("二の解説", result[1].Explanation);
        Assert.Equal("Alice is a doctor.", continuity);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"id\":2,\"translation\":\"a\",\"explanation\":\"b\"}]")]
    [InlineData("[{\"id\":2,\"translation\":\"a\",\"explanation\":\"b\"},{\"id\":2,\"translation\":\"a\",\"explanation\":\"b\"}]")]
    [InlineData("[{\"id\":2,\"translation\":\"a\",\"explanation\":\"b\"},{\"id\":9,\"translation\":\"a\",\"explanation\":\"b\"}]")]
    [InlineData("[{\"id\":2,\"translation\":\"a\",\"explanation\":\"\"},{\"id\":5,\"translation\":\"a\",\"explanation\":\"b\"}]")]
    public void MissingDuplicateUnknownAndEmptyResultsAreRejected(string items)
        => Assert.Throws<SubtitleBatchValidationException>(() => CodexTranslateService.ParseBatch("{\"continuity\":\"\",\"items\":" + items + "}", [2, 5], true, out _));

    [Fact]
    public void BatchLimitsBoundCountAndCharactersWithoutDroppingCues()
    {
        var cues = Enumerable.Range(0, 7).Select(i => Cue(new string('x', i == 3 ? 9000 : 100), i)).ToArray();
        var batches = SubtitleBatch.Split(cues, 3, 250).ToArray();
        Assert.Equal(cues, batches.SelectMany(c => c));
        Assert.All(batches, b => Assert.True(b.Length == 1 || b.Sum(c => c.Text!.Length) <= 250));
        Assert.All(batches, b => Assert.InRange(b.Length, 1, 3));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BatchesUseFutureContextAndPreloadExplanationsEvenWhenDisplayingOriginal(bool displayTranslation)
    {
        Utils.IsTesting = true;
        var config = new Config(true);
        var settings = new CodexTranslateSettings { BatchSize = 2, ContextCues = 1 };
        config.Subtitles.TranslateServiceSettings[TranslateServiceType.Codex] = settings;
        config.Subtitles.TranslateServiceType = TranslateServiceType.Codex;
        config.Subtitles[0].EnabledTranslated = displayTranslation;
        var service = new BatchTranslator();
        var manager = new SubManager(config, 0, () => service) { LanguageSource = Language.Get("en") };
        var cues = Enumerable.Range(0, 5).Select(i => Cue($"Context {i}", i)).ToArray();
        manager.Load(cues);
        await Wait(() => cues.All(c => c.Explanation != null) && !manager.IsTranslating);
        Assert.Equal(3, service.Batches.Count);
        var requests = service.Batches.ToArray();
        Assert.Equal(new[] { 0, 1 }, requests[0].Requested);
        Assert.Equal(new[] { 2 }, requests[0].After);
        Assert.Equal(new[] { 1 }, requests[1].Before);
        Assert.Equal(new[] { 4 }, requests[1].After);
        Assert.All(cues, c => Assert.Equal(displayTranslation ? "訳:" + c.Text : c.Text, c.DisplayText));
        settings.PreGenerateExplanations = false;
        config.Subtitles[0].EnabledTranslated = false;
    }

    [Fact]
    public async Task InvalidBatchSplitsAndRetriesWithoutShiftingCues()
    {
        Utils.IsTesting = true;
        var config = new Config(true);
        config.Subtitles.TranslateServiceType = TranslateServiceType.Codex;
        config.Subtitles[0].EnabledTranslated = true;
        var service = new BatchTranslator { RejectMultiple = true };
        var manager = new SubManager(config, 0, () => service) { LanguageSource = Language.Get("en") };
        var cues = Enumerable.Range(0, 3).Select(i => Cue($"Retry {i}", i)).ToArray();
        manager.Load(cues);
        await Wait(() => cues.All(c => c.IsTranslated));
        Assert.All(cues, c => Assert.Equal("訳:" + c.Text, c.TranslatedText));
        Assert.Equal(5, service.Batches.Count); // 3 -> 1 + (2 -> 1 + 1).
        config.Subtitles[0].EnabledTranslated = false;
    }

    [Fact]
    public async Task ChangingPreparationSettingsPreservesCachedResultsAndEnablingDisplayReusesThem()
    {
        Utils.IsTesting = true;
        var config = new Config(true);
        var settings = new CodexTranslateSettings();
        config.Subtitles.TranslateServiceSettings[TranslateServiceType.Codex] = settings;
        config.Subtitles.TranslateServiceType = TranslateServiceType.Codex;
        var service = new BatchTranslator();
        var manager = new SubManager(config, 0, () => service) { LanguageSource = Language.Get("en") };
        var cue = Cue("Cached sentence", 0);
        manager.Load([cue]);
        await Wait(() => cue.Explanation != null && !manager.IsTranslating);
        string? translation = cue.TranslatedText, explanation = cue.Explanation;
        settings.PreGenerateExplanations = false;
        settings.BatchSize = 4;
        settings.TimeoutMs = 150000;
        config.Subtitles[0].EnabledTranslated = true;
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.Equal(translation, cue.DisplayText);
        Assert.Equal(explanation, cue.Explanation);
        Assert.Single(service.Batches);
        config.Subtitles[0].EnabledTranslated = false;
    }

    [Fact]
    public void ChapterNavigationUsesChapterMarkersAndHandlesEdges()
    {
        var chapters = new[] { new Demuxer.Chapter { StartTime = 0 }, new Demuxer.Chapter { StartTime = 10 * TimeSpan.TicksPerSecond }, new Demuxer.Chapter { StartTime = 20 * TimeSpan.TicksPerSecond } };
        Assert.Same(chapters[1], ChapterNavigation.Find(chapters, 0, true));
        Assert.Same(chapters[2], ChapterNavigation.Find(chapters, 10 * TimeSpan.TicksPerSecond, true));
        Assert.Same(chapters[0], ChapterNavigation.Find(chapters, 10 * TimeSpan.TicksPerSecond, false));
        Assert.Null(ChapterNavigation.Find(chapters, 0, false));
        Assert.Null(ChapterNavigation.Find(chapters, 25 * TimeSpan.TicksPerSecond, true));
        Assert.Null(ChapterNavigation.Find([], 0, true));
    }

    [Fact]
    public void PopupCandidatesStayAdjacentToTheClickedTrack()
    {
        var upper = SubtitlePopupPlacement.Candidates(new Size(300, 200), new Size(500, 40), true);
        Assert.Equal(new Point(100, 46), upper[0].Point);
        var lower = SubtitlePopupPlacement.Candidates(new Size(300, 200), new Size(500, 40), false);
        Assert.Equal(new Point(100, -206), lower[0].Point);
    }

    [Fact]
    public void DefaultsPreloadExplanationsAndPlaceBothTracksAbovePlaybackBar()
    {
        var settings = JsonSerializer.Deserialize<CodexTranslateSettings>("{}")!;
        Assert.True(settings.PreGenerateExplanations);
        Assert.Equal(12, settings.BatchSize);
        Assert.Equal(4, settings.ContextCues);
        var config = JsonSerializer.Deserialize<AppConfigSubs>("{}")!;
        Assert.Equal(SubtitleTrackPlacement.Bottom, config.PrimaryPlacement);
        Assert.Equal(SubtitleTrackPlacement.Bottom, config.SecondaryPlacement);
    }

    private static SubtitleData Cue(string text, int id) => new() { Index = id, Text = text, StartTime = TimeSpan.FromSeconds(id * 5), EndTime = TimeSpan.FromSeconds(id * 5 + 4) };
    private static async Task Wait(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(5000);
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class BatchTranslator : ITranslateService, ISubtitleBatchTranslator
    {
        public bool RejectMultiple;
        public ConcurrentQueue<(int[] Requested, int[] Before, int[] After)> Batches = new();
        public TranslateServiceType ServiceType => TranslateServiceType.Codex;
        public void Initialize(Language src, TargetLanguage target) { }
        public void Dispose() { }
        public Task<string> TranslateAsync(string text, CancellationToken token) => throw new InvalidOperationException("Must use a batch.");
        public Task<IReadOnlyList<SubtitleBatchResult>> TranslateBatchAsync(IReadOnlyList<SubtitleData> captions, IReadOnlyList<SubtitleData> before, IReadOnlyList<SubtitleData> after, CancellationToken token)
        {
            Batches.Enqueue((captions.Select(c => c.Index).ToArray(), before.Select(c => c.Index).ToArray(), after.Select(c => c.Index).ToArray()));
            if (RejectMultiple && captions.Count > 1) throw new SubtitleBatchValidationException();
            return Task.FromResult<IReadOnlyList<SubtitleBatchResult>>(captions.Select(c => new SubtitleBatchResult(c.Index, "訳:" + c.Text, "意味とニュアンス:" + c.Text)).Reverse().ToArray());
        }
    }
}
