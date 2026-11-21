using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Mizon.Windows.Services.Discovery;

internal sealed record NearbyDevice(Guid Id, string Platform, string AppVersion, string Name, string? Account, string? DisplayName);

internal enum NearbyDiscoveryStatus { Off, Starting, Searching, Unavailable }

internal sealed record NearbyDiscoveryState(NearbyDiscoveryStatus Status, IReadOnlyList<NearbyDevice> Devices)
{
    internal static NearbyDiscoveryState Empty(NearbyDiscoveryStatus status) => new(status, []);
}

internal static class NearbyDeviceProtocol
{
    internal const string ServiceType = "_mizon-peer._tcp";
    private const string DomainSuffix = "." + ServiceType + ".local";
    internal static string FullName(Guid id) => $"Mizon-{id:D}{DomainSuffix}.";

    internal static Dictionary<string, string> ToTxt(NearbyDevice device)
    {
        var txt = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["v"] = "1",
            ["id"] = device.Id.ToString("D"),
            ["platform"] = device.Platform,
            ["app"] = Clean(device.AppVersion, 32) ?? "0.0.0",
            ["device"] = Clean(device.Name, 96) ?? "Windows"
        };
        if (Clean(device.Account, 64) is { } account) txt.Add("account", account);
        if (Clean(device.DisplayName, 96) is { } displayName && txt.ContainsKey("account")) txt.Add("display", displayName);
        return txt;
    }

    internal static NearbyDevice? Parse(string fullName, IEnumerable<string> attributes, Guid localId)
    {
        var name = fullName.TrimEnd('.');
        if (name.Length != 42 + DomainSuffix.Length || !name.StartsWith("Mizon-", StringComparison.OrdinalIgnoreCase)
            || !name.EndsWith(DomainSuffix, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(name.AsSpan(6, 36), "D", out var id) || id == Guid.Empty || id == localId)
            return null;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var attribute in attributes)
        {
            if (++count > 24) return null;
            if (attribute is null || attribute.Length > 255) return null;
            var equals = attribute.IndexOf('=');
            if (equals <= 0) continue;
            var key = attribute[..equals];
            if (!values.TryAdd(key, attribute[(equals + 1)..])) return null;
        }

        if (values.GetValueOrDefault("v") != "1"
            || !Guid.TryParseExact(values.GetValueOrDefault("id"), "D", out var advertisedId) || advertisedId != id
            || Check(values.GetValueOrDefault("device"), 96) is not { } device
            || Check(values.GetValueOrDefault("app"), 32) is not { } app
            || Check(values.GetValueOrDefault("platform"), 16) is not { } platform)
            return null;

        var account = Check(values.GetValueOrDefault("account"), 64);
        return new(id, platform switch
        {
            "windows" or "macos" or "ios" or "android" or "watchos" or "wearos" => platform,
            _ => "unknown"
        }, app, device, account, account is null ? null : Check(values.GetValueOrDefault("display"), 96));
    }

    private static string? Check(string? value, int byteLimit) =>
        value is not null && Encoding.UTF8.GetByteCount(value) <= byteLimit ? Clean(value, byteLimit) : null;

    private static string? Clean(string? value, int byteLimit)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = new StringBuilder(Math.Min(value.Length, byteLimit));
        var bytes = 0;
        foreach (var rune in value.Trim().EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                || rune.Value is 0x061c or 0x200e or 0x200f or >= 0x202a and <= 0x202e or >= 0x2066 and <= 0x2069) continue;
            bytes += rune.Utf8SequenceLength;
            if (bytes > byteLimit) break;
            cleaned.Append(rune.ToString());
        }
        var result = cleaned.ToString().Trim();
        return result.Length == 0 ? null : result;
    }
}
