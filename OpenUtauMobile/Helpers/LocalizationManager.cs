using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using Serilog;

namespace OpenUtauMobile.Helpers;

/// <summary>
/// Manages loading and switching UI language strings from RESX asset files.
/// Language files are stored in Assets/Lang/Strings.{langCode}.resx as AvaloniaResource embeds.
/// </summary>
public static class LocalizationManager
{
    public const string FollowSystemLanguageCode = "system";

    private const string ExplicitLanguageFallbackCode = "en";
    private const string SystemLanguageFallbackCode = "zh-Hans";

    /// <summary>Supported languages (language code => display name).</summary>
    public static readonly IReadOnlyList<(string Code, string DisplayName)> AvailableLanguages =
        new List<(string, string)>
        {
            ("zh-Hans", "简体中文"),
            ("en", "English"),
            ("ja", "日本語"),
            ("ru", "Русский"),
            ("uk", "Українська"),
        };

    // Reference to the currently loaded language dictionary, used to remove it on the next switch.
    private static IResourceDictionary? _currentLangDict;

    /// <summary>
    /// Loads the resource dictionary for the given language code, replacing the current one.
    /// Supports explicit language code or "system" preference.
    /// Must be called on the UI thread.
    /// </summary>
    public static void LoadLanguage(string langCode)
    {
        string resolvedCode = ResolveLanguagePreference(langCode);
        Uri uri = new Uri($"avares://OpenUtauMobile/Assets/Lang/Strings.{resolvedCode}.resx");

        Dictionary<string, string> strings;
        try
        {
            using Stream stream = AssetLoader.Open(uri);
            strings = ParseResx(stream);
            Log.Information($"[LocalizationManager] Loaded language: {resolvedCode}");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"[LocalizationManager] Failed to load language '{resolvedCode}', falling back to en");
            using Stream fallback = AssetLoader.Open(new Uri("avares://OpenUtauMobile/Assets/Lang/Strings.en.resx"));
            strings = ParseResx(fallback);
        }

        if (Application.Current == null) return;

        IList<IResourceProvider> merged = Application.Current.Resources.MergedDictionaries;
        if (_currentLangDict != null)
            merged.Remove(_currentLangDict);

        var newDict = new ResourceDictionary();
        foreach (var (key, value) in strings)
            newDict[key] = value;

        merged.Add(newDict);
        _currentLangDict = newDict;
    }

    /// <summary>
    /// Resolves language preference to a supported resource language code.
    /// Empty or "system" preference uses system UI language and falls back to zh-Hans.
    /// Explicit codes fall back to en when unsupported.
    /// </summary>
    public static string ResolveLanguagePreference(string? preferenceCode)
    {
        if (string.IsNullOrWhiteSpace(preferenceCode) ||
            string.Equals(preferenceCode, FollowSystemLanguageCode, StringComparison.OrdinalIgnoreCase))
        {
            return ResolveSystemLanguage();
        }

        return Resolve(preferenceCode, ExplicitLanguageFallbackCode);
    }

    /// <summary>
    /// Resolves current system language to a supported resource language code.
    /// Falls back to zh-Hans when system language is not in the supported list.
    /// </summary>
    public static string ResolveSystemLanguage()
    {
        string systemCode = CultureInfo.CurrentUICulture.Name;
        if (string.IsNullOrWhiteSpace(systemCode))
        {
            systemCode = CultureInfo.CurrentCulture.Name;
        }

        return Resolve(systemCode, SystemLanguageFallbackCode);
    }

    /// <summary>
    /// Returns the localized string for the given key.
    /// Returns the key itself if not found, which makes missing translations visible during development.
    /// </summary>
    public static string Get(string key)
    {
#if DEBUG
        string result = ThemeResources.GetString(key);
        if (result == key)
            Log.Warning($"[L10n] Missing translation key: {key}");
        return result;
#else
        return ThemeResources.GetString(key);
#endif
    }

    /// <summary>
    /// Resolves any language code to a supported file name
    /// (exact match → two-letter/prefix match → fallback).
    /// </summary>
    private static string Resolve(string code, string fallbackCode)
    {
        if (string.IsNullOrWhiteSpace(code)) return fallbackCode;

        (string Code, string DisplayName) exact =
            AvailableLanguages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(exact.Code))
        {
            return exact.Code;
        }

        string twoLetter = code.Length >= 2 ? code[..2] : code;
        (string Code, string DisplayName) prefixMatch = AvailableLanguages.FirstOrDefault(l =>
            string.Equals(l.Code, twoLetter, StringComparison.OrdinalIgnoreCase) ||
            l.Code.StartsWith($"{twoLetter}-", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(prefixMatch.Code))
        {
            return prefixMatch.Code;
        }

        return fallbackCode;
    }

    /// <summary>
    /// Parses a RESX XML stream and returns a dictionary of all string entries.
    /// Only processes &lt;data&gt; elements that contain a &lt;value&gt; child element.
    /// </summary>
    private static Dictionary<string, string> ParseResx(Stream stream)
    {
        var dict = new Dictionary<string, string>();
        using var reader = XmlReader.Create(stream);

        string? currentName = null;

        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element when reader.Name == "data":
                    currentName = reader.GetAttribute("name");
                    break;

                case XmlNodeType.Element when reader.Name == "value" && currentName != null:
                    dict[currentName] = reader.ReadElementContentAsString();
                    currentName = null;
                    // ReadElementContentAsString advances past </value>; the while loop
                    // continues from the next node naturally.
                    break;

                case XmlNodeType.EndElement when reader.Name == "data":
                    currentName = null;
                    break;
            }
        }

        return dict;
    }
}

/// <summary>
/// Shorthand static class for convenient localized string access in C# code.
/// Usage: L.S("Settings.Title")
/// </summary>
public static class L
{
    /// <summary>Returns the localized string for the given key, or the key itself if not found.</summary>
    public static string S(string key) => LocalizationManager.Get(key);
}