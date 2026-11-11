using Microsoft.Windows.AppLifecycle;
using System;
using Windows.ApplicationModel.Activation;
using Windows.Foundation.Collections;

namespace Mizon.Windows.Services;

// Extract the agile native operation synchronously inside AppInstance.Activated.
// The redirecting process may exit as soon as that callback returns.
internal sealed record AppActivationEnvelope(AppActivationRequest Request, AppDeepLinkResponseSession? Response = null)
{
    internal static AppActivationEnvelope FromActivation(AppActivationArguments activation)
    {
        if (activation.Data is not ProtocolForResultsActivatedEventArgs arguments)
            return new(AppActivationRequest.FromActivation(activation));

        var operation = arguments.ProtocolForResultsOperation;
        var request = AppActivationRequest.FromUri(arguments.Uri);
        var requestId = Guid.NewGuid();
        string? errorCode = null;
        if (arguments.Data.TryGetValue(AppDeepLinkResultProtocol.RequestIdKey, out var correlation))
        {
            if (correlation is string text && Guid.TryParse(text, out var parsed) && parsed != Guid.Empty)
                requestId = parsed;
            else errorCode = "invalid_request";
        }
        if (arguments.Data.TryGetValue(AppDeepLinkResultProtocol.VersionKey, out var version)
            && (version is not int requestedVersion || requestedVersion != AppDeepLinkResultProtocol.Version))
            errorCode = "unsupported_version";

        var response = new AppDeepLinkResponseSession(requestId, result =>
        {
            try
            {
                operation.ReportCompleted(new ValueSet
                {
                    [AppDeepLinkResultProtocol.ResponseKey] = AppDeepLinkResultProtocol.Serialize(result)
                });
            }
            catch (Exception error)
            {
                // A caller can close or cancel its native request. Never log its URI,
                // search text or result payload, and never retry ReportCompleted.
                AppLog.Information($"Deep-link response transport closed (HRESULT=0x{error.HResult:X8}).");
            }
        }, request.MusicAction == MusicActivationAction.Download ? TimeSpan.FromMinutes(5) : null);
        if (errorCode is not null)
            response.Fail(errorCode, "The deep-link request metadata is invalid or its response version is unsupported.");
        return new(request, response);
    }

    internal void Close(string code, string message)
    {
        Response?.Cancel(code, message);
        Response?.Dispose();
    }
}
