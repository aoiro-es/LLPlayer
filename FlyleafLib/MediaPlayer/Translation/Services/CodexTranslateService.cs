using System.Linq;
using System.Text.Json;
using FlyleafLib.Localization;

#nullable enable
namespace FlyleafLib.MediaPlayer.Translation.Services;

public sealed class CodexTranslateService(CodexTranslateSettings settings, TranslateChatConfig chatConfig, bool wordMode) : ITranslateService
{
    private readonly SemaphoreSlim _gate = new(1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Queue<(string Text, string Translation)> _context = new();
    private CodexAppServerClient? _client;
    private string? _instructions;
    private string _model = "";
    private string _effort = "";
    private bool _disposed;
    public TranslateServiceType ServiceType => TranslateServiceType.Codex;

    public void Initialize(Language src, TargetLanguage target)
    {
        var (source, destination) = this.TryGetLanguage(src, target);
        var prompt = !wordMode && chatConfig.TranslateMethod == ChatTranslateMethod.KeepContext ? chatConfig.PromptKeepContext : chatConfig.PromptOneByOne;
        _instructions = prompt.Replace("{source_lang}", source.Name)
            .Replace("{target_lang}", chatConfig.IncludeTargetLangRegion ? target.DisplayName() : destination.Name)
            .Replace("{source_text}", "The subtitle is in the input JSON's subtitle property.") +
            "\nThe input is JSON data. Translate only its subtitle property, using previousSubtitles only as context. Output JSON with a single translation string.";
        _context.Clear();
    }

    public async Task<string> TranslateAsync(string text, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_instructions == null) throw new InvalidOperationException("Initialize first.");
        if (string.IsNullOrWhiteSpace(text)) return text;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            linked.CancelAfter(settings.TimeoutMs);
            if (_client is not { IsConnected: true })
            {
                _client?.Dispose();
                _client = new CodexAppServerClient();
                await _client.ConnectAsync(settings.ExecutablePath, linked.Token).ConfigureAwait(false);
                await _client.RequireChatGptAccountAsync(linked.Token).ConfigureAwait(false);
                var models = await _client.GetModelsAsync(linked.Token).ConfigureAwait(false);
                var selected = string.IsNullOrWhiteSpace(settings.Model)
                    ? models.FirstOrDefault(m => m.IsDefault) : models.FirstOrDefault(m => m.Id == settings.Model);
                if (selected == null) throw new TranslationConfigException(UiText.Get("The selected Codex model is unavailable. Refresh the model list in translation settings."));
                _model = selected.Id;
                _effort = string.IsNullOrWhiteSpace(settings.ReasoningEffort) ? selected.DefaultReasoningEffort ?? "" : settings.ReasoningEffort;
                if (!string.IsNullOrWhiteSpace(settings.ReasoningEffort) && !selected.ReasoningEfforts.Contains(settings.ReasoningEffort))
                    throw new TranslationConfigException(UiText.Get("The selected reasoning effort is unavailable for this Codex model. Refresh translation settings."));
            }
            var keepContext = !wordMode && chatConfig.TranslateMethod == ChatTranslateMethod.KeepContext;
            var count = Math.Clamp(chatConfig.SubtitleContextCount, 0, 50);
            while (_context.Count > count)
            {
                if (chatConfig.ContextRetainPolicy == ChatContextRetainPolicy.Reset) _context.Clear();
                else _context.Dequeue();
            }
            var input = JsonSerializer.Serialize(new
            {
                previousSubtitles = keepContext ? _context.Select(c => new { subtitle = c.Text, translation = c.Translation }).ToArray() : [],
                subtitle = text
            });
            var translated = await _client.TranslateAsync(_instructions, input, _model, _effort, linked.Token).ConfigureAwait(false);
            if (keepContext && count > 0) _context.Enqueue((text, translated));
            return translated;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            ResetClient();
            throw new TranslationException(UiText.Get("Codex translation timed out. Increase the timeout or select a faster model or reasoning effort."));
        }
        catch
        {
            ResetClient();
            throw;
        }
        finally { _gate.Release(); }
    }

    private void ResetClient() => Interlocked.Exchange(ref _client, null)?.Dispose();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        ResetClient();
    }
}
