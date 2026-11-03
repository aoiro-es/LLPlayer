using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace WpfColorFontDialog
{
    internal static class I18NUtil
    {
        private const string DefaultLanguage = @"en-US";

        public static string CurrentLanguage;

        private static readonly Dictionary<string, ResourceDictionary> Dictionaries = new Dictionary<string, ResourceDictionary>();

        internal static string GetString(string key, string fallback)
        {
            string language = GetLanguage();
            if (!Dictionaries.TryGetValue(language, out ResourceDictionary dictionary))
            {
                dictionary = new ResourceDictionary
                {
                    Source = new Uri($"/WpfColorFontDialog;component/I18n/{language}.xaml", UriKind.Relative)
                };
                Dictionaries.Add(language, dictionary);
            }
            return dictionary.Contains(key) ? dictionary[key] as string ?? fallback : fallback;
        }

        public static readonly Dictionary<string, string> SupportLanguage = new Dictionary<string, string>
        {
            {@"简体中文", @"zh-CN"},
            {@"English (United States)", @"en-US"},
            {@"日本語", @"ja-JP"},
        };

        public static string GetCurrentLanguage()
        {
            return System.Globalization.CultureInfo.CurrentUICulture.Name;
        }

        public static string GetLanguage()
        {
            var name = GetCurrentLanguage();
            return SupportLanguage.Any(s => name == s.Value) ? name : DefaultLanguage;
        }

        public static string GetWindowStringValue(Window window, string key)
        {
            if (window.Resources.MergedDictionaries[0][key] is string str)
            {
                return str;
            }
            return null;
        }

        public static void SetLanguage(ResourceDictionary resources, string langName = @"")
        {
            if (string.IsNullOrEmpty(langName))
            {
                langName = GetLanguage();
            }
            CurrentLanguage = langName;
            if (resources.MergedDictionaries.Count > 0)
            {
                resources.MergedDictionaries[0].Source = new Uri($@"I18n/{langName}.xaml", UriKind.Relative);
            }
        }
    }
}
