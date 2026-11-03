using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using FlyleafLib.MediaPlayer.Translation.Services;
using Microsoft.Win32;
using LLPlayer.Extensions;

namespace LLPlayer.Controls.Settings.TransControl;

public partial class CodexTranslateControl : UserControl
{
    private CodexTranslateControlVM? _vm;
    public CodexTranslateControl()
    {
        InitializeComponent();
        Loaded += (_, _) => Bind();
        DataContextChanged += (_, _) => { if (IsLoaded) Bind(); };
        Unloaded += (_, _) => { _vm?.Dispose(); _vm = null; };
    }
    private void Bind()
    {
        if (DataContext is not CodexTranslateSettings settings || _vm?.Settings == settings) return;
        _vm?.Dispose();
        _vm = new CodexTranslateControlVM(settings);
        SettingsPanel.DataContext = _vm;
    }
}

public sealed record CodexEffort(string Value, string DisplayName);

public sealed class CodexTranslateControlVM : Bindable, IDisposable
{
    public CodexTranslateSettings Settings { get; }
    public ObservableCollection<CodexModel> Models { get; } = [];
    public ObservableCollection<CodexEffort> Efforts { get; } = [];
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private CodexAppServerClient? _client;
    private string? _connectedPath;
    public CodexTranslateControlVM(CodexTranslateSettings settings)
    {
        Settings = settings;
        Models.Add(new("", UiText.Get("Codex default"), true, []));
        if (!string.IsNullOrEmpty(settings.Model)) Models.Add(new(settings.Model, settings.Model, false, []));
        var effort = settings.ReasoningEffort;
        SelectedModel = Models.First(m => m.Id == settings.Model);
        settings.ReasoningEffort = effort;
        if (!string.IsNullOrEmpty(effort)) Efforts.Add(new(effort, UiText.Get(effort)));
    }

    public CodexModel? SelectedModel
    {
        get;
        set
        {
            if (!Set(ref field, value) || value == null) return;
            Settings.Model = value.Id;
            Efforts.Clear();
            Efforts.Add(new("", UiText.Get("Codex default")));
            foreach (var effort in value.ReasoningEfforts) Efforts.Add(new(effort, UiText.Get(effort)));
            if (!Efforts.Any(e => e.Value == Settings.ReasoningEffort)) Settings.ReasoningEffort = "";
        }
    }
    public string Status { get; set => Set(ref field, value); } = UiText.Get("Refresh to check Codex sign-in and available models.");
    public bool IsBusy
    {
        get;
        private set { if (Set(ref field, value)) OnPropertyChanged(nameof(CanInteract)); }
    }
    public bool CanInteract => !IsBusy;
    public AsyncDelegateCommand CmdRefresh => field ??= new AsyncDelegateCommand(() => RunAsync(RefreshAsync, 30000)).ObservesCanExecute(() => CanInteract);
    public AsyncDelegateCommand CmdSignIn => field ??= new AsyncDelegateCommand(() => RunAsync(async token =>
    {
        await ConnectAsync(token);
        Status = UiText.Get("Complete sign-in in your browser. Waiting for Codex...");
        await _client!.SignInAsync(url => Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }), token);
        await RefreshAsync(token);
    }, 300000)).ObservesCanExecute(() => CanInteract);
    public DelegateCommand CmdCancel => field ??= new(() => _operation?.Cancel());
    public DelegateCommand CmdBrowse => field ??= new DelegateCommand(() =>
    {
        OpenFileDialog dialog = new() { Filter = "Codex (codex.exe)|codex.exe|Executable (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog() == true) Settings.ExecutablePath = dialog.FileName;
    }).ObservesCanExecute(() => CanInteract);

    private async Task ConnectAsync(CancellationToken token)
    {
        if (_client is { IsConnected: true } && _connectedPath == Settings.ExecutablePath) return;
        _client?.Dispose();
        _client = new();
        await _client.ConnectAsync(Settings.ExecutablePath, token);
        _connectedPath = Settings.ExecutablePath;
    }
    private async Task RefreshAsync(CancellationToken token)
    {
        await ConnectAsync(token);
        await _client!.RequireChatGptAccountAsync(token);
        var models = await _client.GetModelsAsync(token);
        var selectedId = Settings.Model;
        var effort = Settings.ReasoningEffort;
        Models.Clear();
        Models.Add(new("", UiText.Get("Codex default"), true, models.FirstOrDefault(m => m.IsDefault)?.ReasoningEfforts ?? []));
        foreach (var model in models) Models.Add(model);
        if (!Models.Any(m => m.Id == selectedId)) Models.Add(new(selectedId, UiText.Format($"Unavailable model: {selectedId}"), false, []));
        SelectedModel = Models.First(m => m.Id == selectedId);
        Settings.ReasoningEffort = Efforts.Any(e => e.Value == effort) ? effort : "";
        Status = UiText.Format($"Signed in with ChatGPT. {models.Count} models available.");
    }
    private async Task RunAsync(Func<CancellationToken, Task> action, int timeoutMs)
    {
        if (IsBusy) return;
        IsBusy = true;
        _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation.CancelAfter(timeoutMs);
        Status = UiText.Get("Connecting to Codex...");
        try { await action(_operation.Token); }
        catch (OperationCanceledException) { Status = UiText.Get("Codex operation canceled or timed out."); _client?.Dispose(); _client = null; }
        catch (Exception ex) when (ex is TranslationException or TranslationConfigException) { Status = ex.Message; _client?.Dispose(); _client = null; }
        catch (Exception) { Status = UiText.Get("Could not connect to Codex. Check codex.exe and sign-in status."); _client?.Dispose(); _client = null; }
        finally { _operation.Dispose(); _operation = null; IsBusy = false; }
    }
    public void Dispose() { _lifetime.Cancel(); _operation?.Cancel(); _client?.Dispose(); }
}
