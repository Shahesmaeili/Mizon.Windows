using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mizon.Windows.Services;

internal sealed record AppDeepLinkResult
{
    [JsonRequired] public int Version { get; init; } = AppDeepLinkResultProtocol.Version;
    [JsonRequired] public Guid RequestId { get; init; }
    [JsonRequired] public bool Success { get; init; }
    [JsonRequired] public string Code { get; init; } = string.Empty;
    [JsonRequired] public string Message { get; init; } = string.Empty;
    public AppDeepLinkResultData? Data { get; init; }
}

internal sealed record AppDeepLinkResultData
{
    [JsonRequired] public string Destination { get; init; } = string.Empty;
    [JsonRequired] public string Uri { get; init; } = string.Empty;
    [JsonRequired] public string CompletionStage { get; init; } = "opened";
    public Guid? PublicId { get; init; }
    public Guid? CommentPublicId { get; init; }
    public string? SettingsSection { get; init; }
}

// Pure shared transport: compile-linked by the taskbar companion and headless
// checks. No WinRT, permissions, navigation or arbitrary result payloads live here.
internal static class AppDeepLinkResultProtocol
{
    public const string ResponseKey = "mizon.response";
    public const string RequestIdKey = "mizon.requestId";
    public const string VersionKey = "mizon.responseVersion";
    public const int Version = 1;
    public const int MaximumJsonBytes = 16 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Serialize(AppDeepLinkResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!IsValid(result)) throw new ArgumentException("Invalid deep-link result.", nameof(result));
        var json = JsonSerializer.Serialize(result, AppDeepLinkResultJsonContext.Default.AppDeepLinkResult);
        if (!Fits(json)) throw new ArgumentException("The deep-link result exceeds the transport limit.", nameof(result));
        return json;
    }

    public static bool TryParse(string? json, Guid expectedRequestId, out AppDeepLinkResult? result)
    {
        result = null;
        if (expectedRequestId == Guid.Empty || json is null) return false;
        try
        {
            if (!Fits(json)) return false;
            var parsed = JsonSerializer.Deserialize(json, AppDeepLinkResultJsonContext.Default.AppDeepLinkResult);
            if (parsed is null || parsed.RequestId != expectedRequestId || !IsValid(parsed)) return false;
            result = parsed;
            return true;
        }
        catch (JsonException) { return false; }
        catch (EncoderFallbackException) { return false; }
    }

    private static bool Fits(string json) => json.Length is > 0 and <= MaximumJsonBytes
        && StrictUtf8.GetByteCount(json) <= MaximumJsonBytes;

    public static bool IsValid(AppDeepLinkResult? result) => result is not null && result.Version == Version && result.RequestId != Guid.Empty
        && IsKey(result.Code, allowUnderscore: true) && IsText(result.Message, 512, allowEmpty: false)
        && (result.Data is null || IsValidData(result.Data));

    private static bool IsValidData(AppDeepLinkResultData data) => IsKey(data.Destination)
        && data.CompletionStage is "opened" or "completed"
        && data.PublicId != Guid.Empty && data.CommentPublicId != Guid.Empty
        && (data.SettingsSection is null || IsKey(data.SettingsSection))
        && IsText(data.Uri, 2048, allowEmpty: false)
        && System.Uri.TryCreate(data.Uri, UriKind.Absolute, out var uri)
        && uri.Scheme == "mizon" && uri.UserInfo.Length == 0 && uri.IsDefaultPort && uri.Fragment.Length == 0;

    private static bool IsKey(string? text, bool allowUnderscore = false)
    {
        if (text is null || text.Length is < 1 or > 64) return false;
        foreach (var character in text)
            if (!(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
                && !(allowUnderscore && character == '_')) return false;
        return true;
    }

    private static bool IsText(string? text, int maximumLength, bool allowEmpty)
    {
        if (text is null || text.Length > maximumLength || (!allowEmpty && string.IsNullOrWhiteSpace(text))) return false;
        foreach (var character in text)
            if (char.IsControl(character)) return false;
        try { StrictUtf8.GetByteCount(text); return true; }
        catch (EncoderFallbackException) { return false; }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, AllowDuplicateProperties = false, MaxDepth = 8)]
[JsonSerializable(typeof(AppDeepLinkResult))]
internal sealed partial class AppDeepLinkResultJsonContext : JsonSerializerContext;
