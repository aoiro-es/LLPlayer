using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using FlyleafLib.Localization;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer.Translation;

#nullable enable

public class SubTranslator
{
    private readonly SubManager _subManager;
    private readonly Config.SubtitlesConfig _config;
    private readonly int _subIndex;
    private readonly Func<ITranslateService> _createTranslationService;
    private readonly LogHandler Log;

    private readonly object _scheduleLock = new();
    private ITranslateService? _translateService;
    private ITranslateSettings? _observedSettings;
    private readonly HashSet<SubtitleData> _observedCaptions = [];
    private Task? _worker;
    private CancellationTokenSource? _activeCancellation;
    private bool _pending;
    private bool _resetService;
    private bool _clearTranslations;
    private int _generation;
    private int _activeIndex = -1;

    private bool IsEnabled => _config[_subIndex].EnabledTranslated;
    private bool PretranslateAll => _config.TranslateServiceType == TranslateServiceType.Codex && _config.CodexTranslateAllOnLoad;

    public SubTranslator(SubManager subManager, Config.SubtitlesConfig config, int subIndex)
        : this(subManager, config, subIndex, () => new TranslateServiceFactory(config).GetService(config.TranslateServiceType, false))
    {
    }

    internal SubTranslator(SubManager subManager, Config.SubtitlesConfig config, int subIndex, Func<ITranslateService> createTranslationService)
    {
        _subManager = subManager;
        _config = config;
        _subIndex = subIndex;
        _createTranslationService = createTranslationService;
        Log = new LogHandler(("[#1]").PadRight(8, ' ') + $" [Translator{subIndex + 1}   ] ");

        _subManager.PropertyChanged += SubManager_OnPropertyChanged;
        _subManager.Subs.CollectionChanged += Subtitles_OnCollectionChanged;
        _config.PropertyChanged += SubtitlesConfig_OnPropertyChanged;
        _config.SubConfigs[subIndex].PropertyChanged += SubConfig_OnPropertyChanged;
        _config.TranslateChatConfig.PropertyChanged += TranslateChatConfig_OnPropertyChanged;
        ObserveServiceSettings();
    }

    private void SubManager_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SubManager.CurrentIndex) when !PretranslateAll:
                QueueTranslation();
                break;
            case nameof(SubManager.Language):
                QueueTranslation(resetService: true, clearTranslations: true);
                break;
            case nameof(SubManager.IsLoading) when !_subManager.IsLoading:
                QueueTranslation();
                break;
        }
    }

    private void Subtitles_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // OCR can fill in text after bitmap cues have already been loaded.
        var captions = _subManager.GetSnapshot().ToHashSet();
        foreach (var removed in _observedCaptions.Where(c => !captions.Contains(c)).ToArray())
        {
            removed.PropertyChanged -= Caption_OnPropertyChanged;
            _observedCaptions.Remove(removed);
        }
        foreach (var added in captions)
            if (_observedCaptions.Add(added)) added.PropertyChanged += Caption_OnPropertyChanged;
        // AddRange uses Reset too. Only an empty collection means the old stream was cleared.
        QueueTranslation(resetService: captions.Count == 0);
    }

    private void Caption_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (PretranslateAll && IsEnabled && e.PropertyName == nameof(SubtitleData.Text)) QueueTranslation();
    }

    private void SubtitlesConfig_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        bool fallbackChanged = _subIndex == 0
            ? e.PropertyName == nameof(Config.SubtitlesConfig.LanguageFallbackPrimary)
            : e.PropertyName == nameof(Config.SubtitlesConfig.LanguageFallbackSecondary)
                || e.PropertyName == nameof(Config.SubtitlesConfig.LanguageFallbackSecondarySame)
                || (_config.LanguageFallbackSecondarySame && e.PropertyName == nameof(Config.SubtitlesConfig.LanguageFallbackPrimary));

        if (e.PropertyName is nameof(Config.SubtitlesConfig.TranslateServiceType) or nameof(Config.SubtitlesConfig.TranslateServiceSettings))
            ObserveServiceSettings();
        if (e.PropertyName == nameof(Config.SubtitlesConfig.CodexTranslateAllOnLoad))
        {
            // Change the work range immediately, retaining translations that already finished.
            QueueTranslation(resetService: true);
            return;
        }
        if (fallbackChanged || e.PropertyName is nameof(Config.SubtitlesConfig.TranslateServiceType)
                or nameof(Config.SubtitlesConfig.TranslateTargetLanguage) or nameof(Config.SubtitlesConfig.TranslateServiceSettings))
            QueueTranslation(resetService: true, clearTranslations: true);
    }

    private void SubConfig_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Config.SubConfig.EnabledTranslated)) return;

        foreach (var sub in _subManager.GetSnapshot())
            sub.EnabledTranslated = IsEnabled;
        if (_subManager.GetCurrent() is { } current)
            _subManager.RefreshTranslatedSubtitle(current);

        if (IsEnabled) _subManager.TranslationError = null;
        _subManager.SetTranslationStatus(UiText.Get(IsEnabled ? "Waiting for translation..." : "Translation off"));
        // Enabling translation must work at a fixed playback position as well.
        QueueTranslation(resetService: !IsEnabled);
    }

    private void ObserveServiceSettings()
    {
        var settings = _config.TranslateServiceSettings.GetValueOrDefault(_config.TranslateServiceType);
        if (ReferenceEquals(settings, _observedSettings)) return;
        if (_observedSettings != null) _observedSettings.PropertyChanged -= ServiceSettings_OnPropertyChanged;
        _observedSettings = settings;
        if (_observedSettings != null) _observedSettings.PropertyChanged += ServiceSettings_OnPropertyChanged;
    }

    private void ServiceSettings_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _subManager.TranslationError = null;
        QueueTranslation(resetService: true, clearTranslations: true);
    }

    private void TranslateChatConfig_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => QueueTranslation(resetService: true, clearTranslations: true);

    private void QueueTranslation(bool resetService = false, bool clearTranslations = false)
    {
        CancellationTokenSource? cancel = null;
        lock (_scheduleLock)
        {
            int index = Math.Max(0, _subManager.CurrentIndex);
            bool largeSeek = !PretranslateAll && _activeIndex >= 0 && (index - _activeIndex > _config.TranslateCountForward
                || _activeIndex - index > _config.TranslateCountBackward);

            if (resetService || largeSeek)
            {
                _generation++;
                cancel = _activeCancellation;
            }
            _resetService |= resetService;
            _clearTranslations |= clearTranslations;
            _pending = true;

            // The worker takes the same lock before running, so a synchronous provider cannot
            // finish before this assignment and leave a completed task blocking future requests.
            _worker ??= Task.Run(ProcessRequestsAsync);
        }

        try { cancel?.Cancel(); }
        catch (ObjectDisposedException) { } // The previous batch finished before cancellation.
    }

    private async Task ProcessRequestsAsync()
    {
        while (true)
        {
            bool resetService;
            bool clearTranslations;
            int generation;
            CancellationTokenSource cancellation;
            lock (_scheduleLock)
            {
                if (!_pending)
                {
                    _worker = null;
                    return;
                }
                _pending = false;
                resetService = _resetService;
                clearTranslations = _clearTranslations;
                _resetService = false;
                _clearTranslations = false;
                generation = _generation;
                cancellation = new CancellationTokenSource();
                _activeCancellation = cancellation;
                _activeIndex = Math.Max(0, _subManager.CurrentIndex);
            }

            var token = cancellation.Token;
            try
            {
                if (resetService)
                {
                    _translateService?.Dispose();
                    _translateService = null;
                }

                var subtitles = _subManager.GetSnapshot();
                foreach (var sub in subtitles)
                {
                    sub.EnabledTranslated = IsEnabled;
                    if (clearTranslations) sub.TranslatedText = null;
                }
                if (clearTranslations && _subManager.GetCurrent() is { } current)
                    _subManager.RefreshTranslatedSubtitle(current);

                if (!IsEnabled)
                {
                    _subManager.SetTranslationStatus(UiText.Get("Translation off"));
                    continue;
                }
                if (subtitles.Length == 0)
                {
                    _subManager.SetTranslationStatus(UiText.Get("Waiting for subtitles..."));
                    continue;
                }
                if (_subManager.Language is not { } sourceLanguage || sourceLanguage == Language.Unknown)
                {
                    _subManager.SetTranslationStatus(UiText.Get("Select the source subtitle language in subtitle settings."));
                    continue;
                }

                // Coalesce subtitle loading and rapid seeks, including while playback is paused.
                await Task.Delay(300, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                subtitles = _subManager.GetSnapshot();
                if (subtitles.Length == 0) continue;

                int index = Math.Clamp(_subManager.CurrentIndex, 0, subtitles.Length - 1);
                lock (_scheduleLock)
                {
                    if (generation != _generation) continue;
                    _activeIndex = index;
                }
                bool pretranslateAll = PretranslateAll;
                int start = pretranslateAll ? 0 : Math.Max(0, index - Math.Max(0, _config.TranslateCountBackward));
                int end = pretranslateAll ? subtitles.Length - 1
                    : (int)Math.Min((long)index + Math.Max(0, _config.TranslateCountForward), subtitles.Length - 1);
                // Whole-track pretranslation runs in chronological order, independent of playback.
                // Windowed translation prioritizes the current cue over look-behind cues.
                var pending = Enumerable.Range(start, end - start + 1)
                    .Where(i => !subtitles[i].IsTranslated && !string.IsNullOrWhiteSpace(subtitles[i].Text))
                    .OrderBy(i => !pretranslateAll && i < index ? 1 : 0).ThenBy(i => i).Select(i => subtitles[i]).ToArray();
                int total = subtitles.Count(s => !string.IsNullOrWhiteSpace(s.Text));
                int completed = subtitles.Count(s => !string.IsNullOrWhiteSpace(s.Text) && s.IsTranslated);
                if (pending.Length == 0)
                {
                    _subManager.SetTranslationStatus(pretranslateAll
                        ? UiText.Format($"Subtitle pretranslation complete ({completed}/{total})") : UiText.Get("Translation ready"));
                    continue;
                }
                _subManager.TranslationError = null;
                _subManager.SetTranslationStatus(pretranslateAll
                    ? UiText.Format($"Pretranslating all subtitles ({completed}/{total})") : UiText.Get("Translating subtitles..."), true);
                ObserveServiceSettings();

                if (_translateService == null)
                {
                    var service = _createTranslationService();
                    try { service.Initialize(sourceLanguage, _config.TranslateTargetLanguage); }
                    catch { service.Dispose(); throw; }
                    _translateService = service;
                }

                int concurrency = pretranslateAll ? 1 : _config.TranslateMaxConcurrency;
                if (_config.TranslateServiceType.IsLLM() && _config.TranslateChatConfig.TranslateMethod == ChatTranslateMethod.KeepContext)
                    concurrency = 1;

                if (concurrency <= 1)
                {
                    foreach (var sub in pending)
                    {
                        await TranslateSubAsync(sub, generation, token).ConfigureAwait(false);
                        if (pretranslateAll && !token.IsCancellationRequested)
                            _subManager.SetTranslationStatus(UiText.Format($"Pretranslating all subtitles ({++completed}/{total})"), true);
                    }
                }
                else
                {
                    await Parallel.ForEachAsync(pending, new ParallelOptions
                    {
                        CancellationToken = token,
                        MaxDegreeOfParallelism = concurrency
                    }, async (sub, ct) => await TranslateSubAsync(sub, generation, ct).ConfigureAwait(false)).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                if (pretranslateAll)
                    _subManager.SetTranslationStatus(UiText.Format($"Subtitle pretranslation complete ({completed}/{total})"));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Disabled translation, changed settings/stream, or sought outside the current batch.
            }
            catch (Exception ex)
            {
                bool isCurrent;
                lock (_scheduleLock)
                    isCurrent = generation == _generation && !token.IsCancellationRequested;
                if (isCurrent)
                {
                    if (Engine.Config != null) Log.Error($"Translation failed: {ex.Message}");
                    _subManager.TranslationError = ex.Message;
                    _config[_subIndex].EnabledTranslated = false;
                    if (ex is TranslationConfigException)
                        _config.player?.RaiseKnownErrorOccurred($"Translation Failed: {ex.Message}", KnownErrorType.Configuration);
                    else
                        _config.player?.RaiseUnknownErrorOccurred($"Translation Failed: {ex.Message}", UnknownErrorType.Translation, ex);
                }
            }
            finally
            {
                _subManager.IsTranslating = false;
                if (!token.IsCancellationRequested && IsEnabled && _subManager.TranslationError == null && _subManager.TranslationStatus == UiText.Get("Translating subtitles..."))
                    _subManager.TranslationStatus = UiText.Get("Translation ready");
                lock (_scheduleLock)
                {
                    if (ReferenceEquals(_activeCancellation, cancellation))
                        _activeCancellation = null;
                }
                cancellation.Dispose();
            }
        }
    }

    private async Task TranslateSubAsync(SubtitleData sub, int generation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        long start = Stopwatch.GetTimestamp();
        string text = SubtitleTextUtil.FlattenText(sub.Text!);
        string translated = await _translateService!.TranslateAsync(text, token).ConfigureAwait(false);

        lock (_scheduleLock)
        {
            // Some providers finish even after cancellation. Never publish results for an old
            // subtitle file, source/target language, or a translation that has been disabled.
            if (generation != _generation || !IsEnabled || token.IsCancellationRequested) return;
            sub.EnabledTranslated = true;
            sub.TranslatedText = translated;
        }
        _subManager.RefreshTranslatedSubtitle(sub);

        if (Engine.Config != null && CanDebug)
            Log.Debug($"Translation End {sub.Index} in {Stopwatch.GetElapsedTime(start).TotalMilliseconds} ms");
    }
}
