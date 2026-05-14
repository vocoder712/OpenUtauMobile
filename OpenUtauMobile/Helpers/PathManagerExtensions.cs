using System;
using System.Reflection;
using OpenUtau.Core;

namespace OpenUtauMobile.Helpers;

/// <summary>
/// PathManager extensions that allow platform projects to override paths
/// without modifying OpenUtau.Core.
/// </summary>
public static class PathManagerExtensions
{
    /// <summary>
    /// Configures selected PathManager paths. Null keeps the original value.
    /// </summary>
    public static void Configure(
        this PathManager manager,
        string? rootPath = null,
        string? dataPath = null,
        string? cachePath = null,
        bool? homePathIsAscii = null)
    {
        SetPrivate(manager, nameof(PathManager.RootPath), rootPath);
        SetPrivate(manager, nameof(PathManager.DataPath), dataPath);
        SetPrivate(manager, nameof(PathManager.CachePath), cachePath);
        if (homePathIsAscii.HasValue)
        {
            SetPrivate(manager, nameof(PathManager.HomePathIsAscii), homePathIsAscii.Value);
        }
    }

    private static void SetPrivate(PathManager manager, string propertyName, object? value)
    {
        if (value == null)
        {
            return;
        }

        PropertyInfo? prop = typeof(PathManager).GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance);
        if (prop == null)
        {
            throw new MissingMemberException(nameof(PathManager), propertyName);
        }

        // Prefer compiler-generated backing field for private setters.
        FieldInfo? backingField = typeof(PathManager).GetField(
            $"<{propertyName}>k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (backingField != null)
        {
            backingField.SetValue(manager, value);
        }
        else
        {
            // Fallback in case backing field naming is different.
            prop.SetValue(manager, value);
        }
    }
}