using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;
using FlyleafLib.MediaPlayer;
using LLPlayer.Services;
using System.Windows;

namespace FlyleafLib.Tests;

public class SharedTranslationRequestsTests
{
    [Fact]
    public async Task ConcurrentAndLaterConsumersUseOneRequest()
    {
        var cache = new SharedTranslationRequests<string>();
        var finish = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Task<string> Produce(CancellationToken _) { Interlocked.Increment(ref calls); return finish.Task; }
        var first = cache.GetAsync("same input", Produce, CancellationToken.None);
        var second = cache.GetAsync("same input", Produce, CancellationToken.None);
        finish.SetResult("translation and explanation");
        Assert.Equal(await first, await second);
        Assert.Equal("translation and explanation", await cache.GetAsync("same input", Produce, CancellationToken.None));
        Assert.Equal(1, calls);
        Assert.Equal(1, cache.StartedRequests);
    }

    [Fact]
    public async Task CancelingOneTrackDoesNotCancelTheOther()
    {
        var cache = new SharedTranslationRequests<string>();
        using var firstCancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string> Produce(CancellationToken token) { started.TrySetResult(token); return finish.Task.WaitAsync(token); }
        var first = cache.GetAsync("same", Produce, firstCancellation.Token);
        var second = cache.GetAsync("same", Produce, CancellationToken.None);
        var producerToken = await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        firstCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(producerToken.IsCancellationRequested);
        finish.SetResult("still needed");
        Assert.Equal("still needed", await second);
        Assert.Equal(1, cache.StartedRequests);
    }

    [Fact]
    public async Task LastConsumerLeavingCancelsAndEvictsTheRequest()
    {
        var cache = new SharedTranslationRequests<string>();
        using var cancellation = new CancellationTokenSource();
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<string> Produce(CancellationToken token)
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); return "unexpected"; }
            finally { if (token.IsCancellationRequested) canceled.TrySetResult(); }
        }
        var pending = cache.GetAsync("same", Produce, cancellation.Token);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await canceled.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal("retry", await cache.GetAsync("same", _ => Task.FromResult("retry"), CancellationToken.None));
        Assert.Equal(2, cache.StartedRequests);
    }

    [Fact]
    public async Task FailedRequestsAreNotCached()
    {
        var cache = new SharedTranslationRequests<string>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAsync("retry", _ => Task.FromException<string>(new InvalidOperationException("temporary")), CancellationToken.None));
        Assert.Equal("ok", await cache.GetAsync("retry", _ => Task.FromResult("ok"), CancellationToken.None));
        Assert.Equal(2, cache.StartedRequests);
    }

    [Fact]
    public async Task CompletedCacheIsBoundedAndKeepsRecentResults()
    {
        var cache = new SharedTranslationRequests<string>(2);
        foreach (string key in new[] { "old", "recent", "new", "recent", "old" })
            Assert.Equal(key, await cache.GetAsync(key, _ => Task.FromResult(key), CancellationToken.None));
        Assert.Equal(4, cache.StartedRequests);
    }

    [Fact]
    public void FactorySharesOnlyWithinTheSamePlayer()
    {
        var config = new Config(true);
        var factory = new TranslateServiceFactory(config.Subtitles);
        using var first = (CodexTranslateService)factory.GetService(TranslateServiceType.Codex, false);
        using var second = (CodexTranslateService)factory.GetService(TranslateServiceType.Codex, false);
        using var independent = (CodexTranslateService)new TranslateServiceFactory(new Config(true).Subtitles).GetService(TranslateServiceType.Codex, false);
        Assert.Same(first.SharedRequests, second.SharedRequests);
        Assert.NotSame(first.SharedRequests, independent.SharedRequests);
    }

    [Fact]
    public void FingerprintsIncludeContextPromptAndModelSettings()
    {
        string Key(string instructions = "Translate to Japanese", string input = "Hello", string model = "", string effort = "")
            => CodexTranslateService.RequestKey(instructions, input, new() { Model = model, ReasoningEffort = effort });
        Assert.Equal(Key(), Key());
        Assert.NotEqual(Key(), Key(input: "Hello; different context or continuity memo"));
        Assert.NotEqual(Key(), Key(instructions: "Translate to French"));
        Assert.NotEqual(Key(), Key(model: "another-model"));
        Assert.NotEqual(Key(), Key(effort: "another-effort"));
        Assert.DoesNotContain("Hello", Key());
    }

    [Fact]
    public async Task TurningOffTranslatedDisplayKeepsRequestedPreGenerationRunning()
    {
        Utils.IsTesting = true;
        var config = new Config(true);
        config.Subtitles.TranslateServiceType = TranslateServiceType.Codex;
        config.Subtitles.TranslateServiceSettings[TranslateServiceType.Codex] = new CodexTranslateSettings();
        config.Subtitles[0].EnabledTranslated = true;
        var service = new PendingBatch();
        var manager = new SubManager(config, 0, () => service) { LanguageSource = Language.Get("en") };
        manager.Load([new SubtitleData { Text = "Shared subtitle" }]);
        var active = await service.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        config.Subtitles[0].EnabledTranslated = false;
        Assert.False(active.IsCancellationRequested);
        service.Finish.SetResult();
        using var timeout = new CancellationTokenSource(5000);
        while (!manager.PreparationProgress.IsComplete) await Task.Delay(10, timeout.Token);
        Assert.Equal("Shared subtitle", manager.Subs[0].DisplayText);
        Assert.Equal(1, service.Calls);
        ((CodexTranslateSettings)config.Subtitles.TranslateServiceSettings[TranslateServiceType.Codex]).PreGenerateExplanations = false;
    }

    private sealed class PendingBatch : ITranslateService, ISubtitleBatchTranslator
    {
        public int Calls;
        public TaskCompletionSource<CancellationToken> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TranslateServiceType ServiceType => TranslateServiceType.Codex;
        public void Initialize(Language source, TargetLanguage target) { }
        public void Dispose() { }
        public Task<string> TranslateAsync(string text, CancellationToken token) => throw new InvalidOperationException();
        public async Task<IReadOnlyList<SubtitleBatchResult>> TranslateBatchAsync(IReadOnlyList<SubtitleData> cues, IReadOnlyList<SubtitleData> before, IReadOnlyList<SubtitleData> after, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            Started.TrySetResult(token);
            await Finish.Task.WaitAsync(token);
            return cues.Select(c => new SubtitleBatchResult(c.Index, "訳", "解説")).ToArray();
        }
    }

    [Theory]
    [InlineData(1279, 719, true)]
    [InlineData(1000, 670, true)]
    [InlineData(1000, 570, true)]
    [InlineData(400, 670, false)]
    [InlineData(1000, 200, false)]
    [InlineData(-1, 719, false)]
    [InlineData(1281, 719, false)]
    [InlineData(1000, 721, false)]
    public void ProgressOnlyAppearsInTheBottomRightRegion(double x, double y, bool expected)
        => Assert.Equal(expected, PreparationHoverPolicy.Reveal(new Point(x, y), new Size(1280, 720), new Size(400, 110), 64));
}
