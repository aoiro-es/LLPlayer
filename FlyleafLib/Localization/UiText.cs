using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Markup;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.Localization;

/// <summary>Presentation text only; persisted names, media text and protocol values stay unchanged.</summary>
public static class UiText
{
    private static readonly ResourceManager Resources = new("FlyleafLib.Localization.Strings", typeof(UiText).Assembly);
    private static readonly Lazy<Dictionary<string, string>> LanguageCodes = new(() => CultureInfo.GetCultures(CultureTypes.NeutralCultures)
        .Where(c => !string.IsNullOrEmpty(c.Name)).GroupBy(c => c.EnglishName, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase));

    public static string ResourceKey(string english) => "Text_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(english.ReplaceLineEndings("\n"))))[..16];

    public static string GetResource(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Get(string english) => Resources.GetString(ResourceKey(english), CultureInfo.CurrentUICulture) ?? english;

    public static string Format(FormattableString message) => string.Format(CultureInfo.CurrentCulture, Get(message.Format), message.GetArguments());

    public static string Describe(object value)
    {
        if (value is Language language)
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja"
                ? LanguageName(language.CultureName, language.TopEnglishName) : language.DisplayName;
        if (value is WhisperLanguage whisperLanguage)
            return LanguageName(whisperLanguage.Code, whisperLanguage.EnglishName);
        if (value is TranslateLanguage translateLanguage)
            return LanguageName(translateLanguage.ISO6391, translateLanguage.Name);
        if (value is LanguageRegions regions)
            return LanguageName(regions.ISO6391, regions.Name);
        if (value is LanguageRegionMember region)
        {
            string translated = Get(region.Name);
            return translated == region.Name ? LanguageName(region.Code, region.Name) : translated;
        }
        if (value is TargetLanguage targetLanguage)
        {
            string english = targetLanguage.DisplayName();
            string translated = Get(english);
            return translated == english ? LanguageName(targetLanguage.ToISO6391(), english) : translated;
        }
        if (value is TesseractOCR.Enums.Language ocrLanguage && TesseractModel.TesseractLangToISO6391.TryGetValue(ocrLanguage, out string code))
        {
            string translated = Get(ocrLanguage.ToString());
            return translated == ocrLanguage.ToString() ? LanguageName(code, translated) : translated;
        }
        if (value is Enum e)
        {
            var name = e.ToString();
            var description = e.GetType().GetField(name)?.GetCustomAttribute<DescriptionAttribute>()?.Description;
            return Get(description ?? name);
        }
        string text = value?.ToString() ?? string.Empty;
        string localizedText = Get(text);
        return text == localizedText && LanguageCodes.Value.TryGetValue(text, out string languageCode)
            ? LanguageName(languageCode, text) : localizedText;
    }

    public static string LanguageName(string code, string english)
    {
        if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "ja") return english;
        if (string.IsNullOrEmpty(code)) return Get(english);
        try
        {
            var localized = CultureInfo.GetCultureInfo(code).DisplayName;
            return localized == code ? Get(english) : localized;
        }
        catch (CultureNotFoundException)
        {
            return Get(english);
        }
    }

    public static string ActionName(Enum action) => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? Describe(action) : action.ToString();
}

/// <summary>Resolves XAML labels from the same catalog used by view models.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TextExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => UiText.GetResource(Key);
}
