using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.Services;

namespace FlyleafLib.Tests;

public class CodexTranslationTests
{
    [Fact]
    public async Task RpcMatchesOutOfOrderResponsesAndSeparatesNotifications()
    {
        var input = new Lines();
        var output = new Messages();
        using var rpc = new CodexRpc(input, output);
        using var timeout = new CancellationTokenSource(5000);
        var first = rpc.CallAsync("first", new { }, timeout.Token);
        var second = rpc.CallAsync("second", new { }, timeout.Token);
        var a = await output.NextAsync(timeout.Token);
        var b = await output.NextAsync(timeout.Token);
        string? notification = null;
        rpc.Notification += (method, _) => notification = method;
        input.Send(new { method = "turn/started", @params = new { } });
        input.Send(new { id = b.GetProperty("id").GetInt64(), result = new { value = "second" } });
        input.Send(new { id = a.GetProperty("id").GetInt64(), result = new { value = "first" } });
        Assert.Equal("first", (await first).GetProperty("value").GetString());
        Assert.Equal("second", (await second).GetProperty("value").GetString());
        Assert.Equal("turn/started", notification);
    }

    [Fact]
    public async Task CancelingRequestDoesNotConsumeTheNextResponse()
    {
        var input = new Lines(); var output = new Messages();
        using var rpc = new CodexRpc(input, output);
        using var timeout = new CancellationTokenSource(5000);
        using var canceled = new CancellationTokenSource();
        var first = rpc.CallAsync("first", new { }, canceled.Token);
        var a = await output.NextAsync(timeout.Token);
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var second = rpc.CallAsync("second", new { }, timeout.Token);
        var b = await output.NextAsync(timeout.Token);
        input.Send(new { id = a.GetProperty("id").GetInt64(), result = new { value = "stale" } });
        input.Send(new { id = b.GetProperty("id").GetInt64(), result = new { value = "current" } });
        Assert.Equal("current", (await second).GetProperty("value").GetString());
    }

    [Fact]
    public async Task ToolAndPermissionRequestsAreRejected()
    {
        var input = new Lines(); var output = new Messages();
        using var rpc = new CodexRpc(input, output);
        using var timeout = new CancellationTokenSource(5000);
        input.Send(new { id = "permission-1", method = "item/commandExecution/requestApproval", @params = new { } });
        var response = await output.NextAsync(timeout.Token);
        Assert.Equal("permission-1", response.GetProperty("id").GetString());
        Assert.Equal(-32601, response.GetProperty("error").GetProperty("code").GetInt32());
        Assert.False(response.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task DisconnectFailsPendingRequests()
    {
        var input = new Lines(); var output = new Messages();
        using var rpc = new CodexRpc(input, output);
        using var timeout = new CancellationTokenSource(5000);
        var pending = rpc.CallAsync("waiting", new { }, timeout.Token);
        await output.NextAsync(timeout.Token);
        input.End();
        await Assert.ThrowsAsync<TranslationException>(() => pending);
    }

    [Fact]
    public async Task RpcErrorsDoNotExposeRawAccountOrSubtitlePayloads()
    {
        var input = new Lines(); var output = new Messages();
        using var rpc = new CodexRpc(input, output);
        using var timeout = new CancellationTokenSource(5000);
        var pending = rpc.CallAsync("failure", new { }, timeout.Token);
        var request = await output.NextAsync(timeout.Token);
        input.Send(new { id = request.GetProperty("id").GetInt64(), error = new { code = -32000, message = "sensitive account details" } });
        var error = await Assert.ThrowsAsync<TranslationException>(() => pending);
        Assert.DoesNotContain("sensitive account details", error.Message);
        Assert.Contains("-32000", error.Message);
    }

    [Fact]
    public void TranslationOnlyAcceptsTheStructuredFinalResult()
    {
        Assert.Equal("こんにちは", CodexAppServerClient.ParseTranslation("{\"translation\":\"こんにちは\"}"));
        Assert.Throws<TranslationException>(() => CodexAppServerClient.ParseTranslation("analysis: not a translation"));
        Assert.Throws<TranslationException>(() => CodexAppServerClient.ParseTranslation("{\"translation\":\"\"}"));
    }

    [Fact]
    public void ExistingMcpAndPluginIntegrationsAreDisabledForTranslation()
    {
        using var data = JsonDocument.Parse("{\"config\":{\"mcp_servers\":{\"sample\":{\"url\":\"unused\"}},\"plugins\":{\"sample-plugin\":{\"enabled\":true}}}}");
        var disabled = JsonSerializer.SerializeToElement(CodexAppServerClient.DisableConfiguredIntegrations(data.RootElement));
        Assert.False(disabled.GetProperty("mcp_servers").GetProperty("sample").GetProperty("enabled").GetBoolean());
        Assert.False(disabled.GetProperty("plugins").GetProperty("sample-plugin").GetProperty("enabled").GetBoolean());
        Assert.Contains("features.shell_tool=false", CodexAppServerClient.SafeOverrides);
        Assert.Contains("features.hooks=false", CodexAppServerClient.SafeOverrides);
    }

    [Fact]
    public void SettingsPersistWithoutAccountDataOrFixedModelDefaults()
    {
        var config = new AppConfig();
        var settings = new CodexTranslateSettings();
        Assert.Empty(settings.Model);
        Assert.Empty(settings.ReasoningEffort);
        Assert.True(TranslateServiceType.Codex.IsLLM());
        Assert.True(TranslateLanguage.Langs["en"].SupportedServices.HasFlag(TranslateServiceType.Codex));
        var serialized = JsonSerializer.Serialize<ITranslateSettings>(settings, AppConfig.GetJsonSerializerOptions());
        var restored = JsonSerializer.Deserialize<ITranslateSettings>(serialized, AppConfig.GetJsonSerializerOptions());
        Assert.IsType<CodexTranslateSettings>(restored);
        Assert.DoesNotContain("token", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ModelSelectionAcceptsFutureCatalogEntriesAndKeepsSavedChoices()
    {
        var settings = new CodexTranslateSettings { Model = "future-model", ReasoningEffort = "future-effort" };
        using var vm = new LLPlayer.Controls.Settings.TransControl.CodexTranslateControlVM(settings);
        Assert.Equal("future-model", settings.Model);
        Assert.Equal("future-effort", settings.ReasoningEffort);
        vm.SelectedModel = new CodexModel("another-future-model", "Catalog display name", false, ["catalog-effort"]);
        Assert.Equal("another-future-model", settings.Model);
        Assert.Contains(vm.Efforts, e => e.Value == "catalog-effort");
        Assert.Empty(settings.ReasoningEffort);
    }

    [Fact]
    public void TranslationDisplayChoicesSurviveRestart()
    {
        var config = new Config(true);
        config.Subtitles[0].EnabledTranslated = true;
        config.Subtitles[1].EnabledTranslated = false;
        var options = AppConfig.GetJsonSerializerOptions();
        var restored = JsonSerializer.Deserialize<Config.SubConfig[]>(JsonSerializer.Serialize(config.Subtitles.SubConfigs, options), options)!;
        Assert.True(restored[0].EnabledTranslated);
        Assert.False(restored[1].EnabledTranslated);
    }

    private sealed class Lines : TextReader
    {
        private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();
        public void Send(object message) => _lines.Writer.TryWrite(JsonSerializer.Serialize(message));
        public void End() => _lines.Writer.Complete();
        public override async ValueTask<string?> ReadLineAsync(CancellationToken token)
        {
            try { return await _lines.Reader.ReadAsync(token); }
            catch (ChannelClosedException) { return null; }
        }
    }
    private sealed class Messages : TextWriter
    {
        private readonly Channel<string> _messages = Channel.CreateUnbounded<string>();
        public override Encoding Encoding => Encoding.UTF8;
        public override Task WriteLineAsync(ReadOnlyMemory<char> value, CancellationToken token = default)
        {
            _messages.Writer.TryWrite(value.ToString());
            return Task.CompletedTask;
        }
        public async Task<JsonElement> NextAsync(CancellationToken token)
        {
            using var doc = JsonDocument.Parse(await _messages.Reader.ReadAsync(token));
            return doc.RootElement.Clone();
        }
    }
}
