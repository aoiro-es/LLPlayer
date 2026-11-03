using System.Collections.Concurrent;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer;

public class SubtitleTranslationTests
{
    private static (Config config, SubManager manager, FakeTranslator service) Create(int subIndex = 0)
    {
        Utils.IsTesting = true;
        var config = new Config(true);
        config.Subtitles.TranslateTargetLanguage = TargetLanguage.Japanese;
        config.Subtitles.TranslateMaxConcurrency = 1;
        var service = new FakeTranslator();
        var manager = new SubManager(config, subIndex, () => service);
        return (config, manager, service);
    }

    private static SubtitleData Caption(string text, int seconds = 10) => new()
    {
        Text = text,
        StartTime = TimeSpan.FromSeconds(seconds),
        EndTime = TimeSpan.FromSeconds(seconds + 5)
    };

    private static async Task WaitFor(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task LoadedTextSubtitlesTranslateBeforePlaybackStarts(int subIndex)
    {
        var (config, manager, service) = Create(subIndex);
        config.Subtitles[subIndex].EnabledTranslated = true;
        manager.LanguageSource = Language.Get("en");
        var subtitle = Caption("Hello from a subtitle file.");
        manager.Load([subtitle]);

        await WaitFor(() => subtitle.IsTranslated);

        Assert.Equal("Japanese:Hello from a subtitle file.", subtitle.DisplayText);
        Assert.Equal("en", service.Source?.ISO6391);
        Assert.Equal(-1, manager.CurrentIndex);
        config.Subtitles[subIndex].EnabledTranslated = false;
    }

    [Fact]
    public async Task EnablingTranslationProcessesAlreadyLoadedSubtitleWhilePaused()
    {
        var (config, manager, _) = Create();
        manager.LanguageSource = Language.Get("en");
        var subtitle = Caption("Already loaded.");
        manager.Load([subtitle]);
        manager.SetCurrentTime(TimeSpan.FromSeconds(11));
        config.Subtitles[0].EnabledTranslated = true;

        await WaitFor(() => subtitle.IsTranslated);

        Assert.Equal("Japanese:Already loaded.", subtitle.DisplayText);
        config.Subtitles[0].EnabledTranslated = false;
    }

    [Fact]
    public async Task LanguageArrivingAfterPlaybackPositionStartsTranslation()
    {
        var (config, manager, _) = Create();
        config.Subtitles[0].EnabledTranslated = true;
        var subtitle = Caption("Language detected later.");
        manager.Load([subtitle]);
        manager.SetCurrentTime(TimeSpan.FromSeconds(11));
        manager.LanguageSource = Language.Get("en");

        await WaitFor(() => subtitle.IsTranslated);

        Assert.Equal("Japanese:Language detected later.", subtitle.DisplayText);
        config.Subtitles[0].EnabledTranslated = false;
    }

    [Fact]
    public async Task ChangingTargetLanguageRetranslatesTheCurrentSubtitle()
    {
        var (config, manager, _) = Create();
        manager.LanguageSource = Language.Get("en");
        var subtitle = Caption("Change the target.");
        manager.Load([subtitle]);
        manager.SetCurrentTime(TimeSpan.FromSeconds(11));
        config.Subtitles[0].EnabledTranslated = true;
        await WaitFor(() => subtitle.TranslatedText == "Japanese:Change the target.");

        config.Subtitles.TranslateTargetLanguage = TargetLanguage.French;

        await WaitFor(() => subtitle.TranslatedText == "French:Change the target.");
        Assert.Equal("Change the target.", subtitle.Text);
        config.Subtitles[0].EnabledTranslated = false;
    }

    [Fact]
    public async Task CurrentSubtitleIsIncludedWhenForwardCountIsZero()
    {
        var (config, manager, _) = Create();
        config.Subtitles.TranslateCountBackward = 2;
        config.Subtitles.TranslateCountForward = 0;
        manager.LanguageSource = Language.Get("en");
        var captions = Enumerable.Range(1, 4).Select(i => Caption($"Line {i}", i * 10)).ToArray();
        manager.Load(captions);
        manager.SetCurrentTime(TimeSpan.FromSeconds(41));
        config.Subtitles[0].EnabledTranslated = true;

        await WaitFor(() => captions[3].IsTranslated);

        Assert.Equal("Japanese:Line 4", captions[3].DisplayText);
        Assert.Null(captions[0].TranslatedText);
        config.Subtitles[0].EnabledTranslated = false;
    }

    [Fact]
    public async Task NewlyAddedCaptionsTranslateWithoutMovingThePlaybackPosition()
    {
        var (config, manager, _) = Create();
        config.Subtitles[0].EnabledTranslated = true;
        manager.LanguageSource = Language.Get("en");
        var first = Caption("First chunk.");
        manager.Add(first);
        await WaitFor(() => first.IsTranslated);

        var second = Caption("Second chunk.", 20);
        manager.AddRange([second]);

        await WaitFor(() => second.IsTranslated);
        Assert.Equal("Japanese:Second chunk.", second.DisplayText);
        config.Subtitles[0].EnabledTranslated = false;
    }

    [Fact]
    public async Task DisabledTranslationDoesNotSendLoadedText()
    {
        var (_, manager, service) = Create();
        manager.LanguageSource = Language.Get("en");
        var caption = Caption("Do not send this.");
        manager.Load([caption]);
        manager.SetCurrentTime(TimeSpan.FromSeconds(11));

        await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.Empty(service.Requests);
        Assert.Null(caption.TranslatedText);
    }

    [Fact]
    public async Task LateResponseAfterReplacingTheSubtitleFileIsDiscarded()
    {
        var (config, manager, service) = Create();
        var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Translate = (_, _) => response.Task; // Intentionally ignores cancellation.
        manager.LanguageSource = Language.Get("en");
        var oldCaption = Caption("Old file.");
        manager.Load([oldCaption]);
        config.Subtitles[0].EnabledTranslated = true;
        await WaitFor(() => !service.Requests.IsEmpty);

        manager.Clear();
        var newCaption = Caption("New file.");
        manager.LanguageSource = Language.Get("en");
        manager.Load([newCaption]);
        service.Translate = null;
        response.SetResult("An obsolete response.");

        await WaitFor(() => newCaption.IsTranslated);

        Assert.Null(oldCaption.TranslatedText);
        Assert.Equal("Japanese:New file.", newCaption.DisplayText);
        config.Subtitles[0].EnabledTranslated = false;
    }

    [Fact]
    public async Task LatestPositionIsProcessedAfterAnActiveBatchCompletes()
    {
        var (config, manager, service) = Create();
        config.Subtitles.TranslateCountBackward = 0;
        config.Subtitles.TranslateCountForward = 0;
        var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Translate = (_, _) => response.Task;
        manager.LanguageSource = Language.Get("en");
        var first = Caption("Before seek.");
        var last = Caption("After seek.", 30);
        manager.Load([first, last]);
        manager.SetCurrentTime(TimeSpan.FromSeconds(11));
        config.Subtitles[0].EnabledTranslated = true;
        await WaitFor(() => !service.Requests.IsEmpty);

        manager.SetCurrentTime(TimeSpan.FromSeconds(31));
        service.Translate = null;
        response.SetResult("Obsolete before seek.");

        await WaitFor(() => last.IsTranslated);

        Assert.Null(first.TranslatedText);
        Assert.Equal("Japanese:After seek.", last.DisplayText);
        config.Subtitles[0].EnabledTranslated = false;
    }

    [Fact]
    public async Task DisablingTranslationDiscardsAnInFlightResponse()
    {
        var (config, manager, service) = Create();
        var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Translate = (_, _) => response.Task;
        manager.LanguageSource = Language.Get("en");
        var caption = Caption("Remain in the original language.");
        manager.Load([caption]);
        config.Subtitles[0].EnabledTranslated = true;
        await WaitFor(() => !service.Requests.IsEmpty);

        config.Subtitles[0].EnabledTranslated = false;
        response.SetResult("A result that should be discarded.");

        await WaitFor(() => service.DisposeCount > 0);
        Assert.Null(caption.TranslatedText);
        Assert.Equal(caption.Text, caption.DisplayText);
    }

    [Fact]
    public async Task ChangingTargetDiscardsAnInFlightResponseAndStartsTheNewLanguage()
    {
        var (config, manager, service) = Create();
        var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Translate = (_, _) => response.Task;
        manager.LanguageSource = Language.Get("en");
        var caption = Caption("New target.");
        manager.Load([caption]);
        config.Subtitles[0].EnabledTranslated = true;
        await WaitFor(() => !service.Requests.IsEmpty);

        config.Subtitles.TranslateTargetLanguage = TargetLanguage.French;
        service.Translate = null;
        response.SetResult("Obsolete Japanese translation.");

        await WaitFor(() => caption.TranslatedText == "French:New target.");
        Assert.Equal("French:New target.", caption.DisplayText);
        config.Subtitles[0].EnabledTranslated = false;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task SettingFallbackLanguageStartsTranslationOfUnknownLanguageFiles(int subIndex)
    {
        var (config, manager, service) = Create(subIndex);
        config.Subtitles.Languages = [];
        manager.LanguageSource = Language.Unknown;
        var caption = Caption("A file without language metadata.");
        manager.Load([caption]);
        config.Subtitles[subIndex].EnabledTranslated = true;

        config.Subtitles.LanguageFallbackPrimary = Language.Get("en");

        await WaitFor(() => caption.IsTranslated);
        Assert.Equal("en", service.Source?.ISO6391);
        config.Subtitles[subIndex].EnabledTranslated = false;
    }

    [Theory]
    [InlineData("lesson.en.srt", "en")]
    [InlineData("lesson.eng.ass", "en")]
    [InlineData("lesson.pt-BR.forced.ass", "pt")]
    [InlineData("lesson.zh-Hans.sdh.vtt", "zh")]
    [InlineData("lesson.srt", null)]
    [InlineData("lesson.1080p.srt", null)]
    public void ExplicitFilenameLanguageTagsAreUsedForManuallyOpenedFiles(string filename, string? code)
    {
        var language = Plugins.OpenSubtitles.GetLanguageFromFileName(filename);
        Assert.Equal(code, language.ISO6391);
    }

    private sealed class FakeTranslator : ITranslateService
    {
        public TranslateServiceType ServiceType => TranslateServiceType.GoogleV1;
        public Language? Source { get; private set; }
        public TargetLanguage Target { get; private set; }
        public ConcurrentQueue<string> Requests { get; } = new();
        public Func<string, CancellationToken, Task<string>>? Translate { get; set; }
        private int _disposeCount;
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public void Initialize(Language src, TargetLanguage target) { Source = src; Target = target; }
        public Task<string> TranslateAsync(string text, CancellationToken token)
        {
            var translate = Translate;
            Requests.Enqueue(text);
            return translate?.Invoke(text, token) ?? Task.FromResult($"{Target}:{text}");
        }
        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }
}
