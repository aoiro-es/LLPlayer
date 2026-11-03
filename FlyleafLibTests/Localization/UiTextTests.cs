using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using FlyleafLib.Localization;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Translation;

namespace FlyleafLib.Tests.Localization;

public class UiTextTests
{
    private static readonly ResourceManager Resources = new("FlyleafLib.Localization.Strings", typeof(UiText).Assembly);

    [Theory]
    [InlineData("en-US", "Settings - LLPlayer", "Primary")]
    [InlineData("ja-JP", "設定 - LLPlayer", "第1字幕")]
    public void LanguageChangesLabelsAndFormattedMessages(string language, string title, string primary)
    {
        using var culture = new UiCulture(language);
        Assert.Equal(title, UiText.Format($"Settings - {"LLPlayer"}"));
        Assert.Equal(primary, UiText.Get("Primary"));
        Assert.Equal("user-provided title", UiText.Get("user-provided title"));
        Assert.Equal(UiText.ResourceKey("line1\nline2"), UiText.ResourceKey("line1\r\nline2"));
    }

    [Fact]
    public void JapaneseCatalogCoversEnglishCatalogAndPreservesFormatArguments()
    {
        var english = Resources.GetResourceSet(CultureInfo.InvariantCulture, true, true)!;
        var japanese = Resources.GetResourceSet(CultureInfo.GetCultureInfo("ja"), true, false)!;
        Assert.Equal(english.Cast<DictionaryEntry>().Count(), japanese.Cast<DictionaryEntry>().Count());
        foreach (DictionaryEntry entry in english)
        {
            string key = (string)entry.Key;
            string source = (string)entry.Value!;
            string? translation = japanese.GetString(key);
            Assert.False(string.IsNullOrWhiteSpace(translation), key);
            Assert.Equal(UiText.ResourceKey(source), key);
            var expected = Regex.Matches(source, @"\{\d+(?:[^{}]*)\}").Select(m => m.Value).Order().ToArray();
            var actual = Regex.Matches(translation!, @"\{\d+(?:[^{}]*)\}").Select(m => m.Value).Order().ToArray();
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void AllXamlResourceReferencesExistInBothLanguages()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "LLPlayer.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var japanese = Resources.GetResourceSet(CultureInfo.GetCultureInfo("ja"), true, false)!;
        foreach (var directory in new[] { "LLPlayer", "FlyleafLib/Controls/WPF" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root.FullName, directory), "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj")) continue;
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\{loc:Text Key=(Text_[A-F0-9]+)\}"))
            {
                string key = match.Groups[1].Value;
                Assert.NotNull(Resources.GetString(key, CultureInfo.InvariantCulture));
                Assert.NotNull(japanese.GetString(key));
            }
        }
    }

    [Fact]
    public void JapaneseDisplayDoesNotChangeActionIdentifiersOrTranslationPromptLanguage()
    {
        using var culture = new UiCulture("ja-JP");
        Assert.Equal("再生を停止", UiText.Describe(KeyBindingAction.Stop));
        Assert.Equal("Stop", KeyBindingAction.Stop.ToString());
        Assert.Equal("英語（アメリカ）", UiText.Describe(TargetLanguage.EnglishAmerican));
        Assert.Equal("English (American)", TargetLanguage.EnglishAmerican.DisplayName());
        Assert.Equal("英語", UiText.LanguageName("en", "English"));
    }

    private sealed class UiCulture : IDisposable
    {
        private readonly CultureInfo previous = CultureInfo.CurrentUICulture;
        public UiCulture(string name) => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        public void Dispose() => CultureInfo.CurrentUICulture = previous;
    }
}
