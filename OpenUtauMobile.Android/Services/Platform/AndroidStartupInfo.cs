using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Systems;
using OpenUtauMobile.Services;
using Serilog;

namespace OpenUtauMobile.Android.Services.Platform;

/// <summary>读取已安装的 APK 和 Android 系统信息。</summary>
internal static class AndroidStartupInfo
{
    private const string Unavailable = "不可用";

    /// <summary>将 Android 平台信息写入启动日志。</summary>
    internal static void Write(ILogger logger)
    {
        Context context = Application.Context;
        string packageName = context.PackageName!;
        using PackageInfo? package = GetPackageInfo(context.PackageManager!, packageName, logger);
        long? versionCode = package == null ? null : OperatingSystem.IsAndroidVersionAtLeast(28)
            ? package.LongVersionCode
            : package.VersionCode;

        logger.Information("APK 包名：{ApkPackageName:l}", packageName);
        logger.Information("APK 版本名：{ApkVersionName:l}", package?.VersionName ?? Unavailable);
        logger.Information("APK 整数版本号：{ApkVersionCode}", (object?)versionCode ?? Unavailable);
        WriteSigningOwners(logger, package);

        using StructUtsname? uname = Os.Uname();
        ulong? totalPhysicalMemory = null;
        if (context.GetSystemService(Context.ActivityService) is ActivityManager activityManager)
        {
            using ActivityManager.MemoryInfo memory = new();
            activityManager.GetMemoryInfo(memory);
            totalPhysicalMemory = (ulong)memory.TotalMem;
        }
        AppLogging.LogOperatingSystem(logger, "Android", RuntimeInformation.OSArchitecture,
            Build.VERSION.Release, uname?.Release, totalPhysicalMemory);
    }

    private static PackageInfo? GetPackageInfo(PackageManager manager, string packageName, ILogger logger)
    {
        try
        {
            PackageInfoFlags flags = OperatingSystem.IsAndroidVersionAtLeast(28)
                ? PackageInfoFlags.SigningCertificates
                : PackageInfoFlags.Signatures;
            return manager.GetPackageInfo(packageName, flags);
        }
        catch (PackageManager.NameNotFoundException exception)
        {
            logger.Warning(exception, "无法读取 APK 包信息");
            return null;
        }
    }

    private static void WriteSigningOwners(ILogger logger, PackageInfo? package)
    {
        List<string> owners = [];
        if (package != null)
        {
            foreach (Signature signature in GetCurrentSigners(package))
            {
                try
                {
                    using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(signature.ToByteArray()!);
                    string subject = certificate.Subject;
                    if (!string.IsNullOrEmpty(subject))
                    {
                        owners.Add(subject);
                    }
                }
                catch (CryptographicException exception)
                {
                    logger.Warning(exception, "无法解析 APK 签名证书");
                }
            }
        }

        logger.Information("APK 签名所有者：{ApkSigningOwner:l}",
            owners.Count > 0 ? string.Join("；", owners) : Unavailable);
    }

    private static IEnumerable<Signature> GetCurrentSigners(PackageInfo package)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            return package.Signatures ?? [];
        }

        using SigningInfo? signing = package.SigningInfo;
        if (signing?.HasMultipleSigners == true)
        {
            return signing.GetApkContentsSigners() ?? [];
        }

        // 单签名轮换历史的末尾是当前签名。
        Signature[]? history = signing?.GetSigningCertificateHistory();
        return history is { Length: > 0 } ? [history[^1]] : [];
    }
}
