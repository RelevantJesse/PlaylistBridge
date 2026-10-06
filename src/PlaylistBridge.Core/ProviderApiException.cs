namespace PlaylistBridge.Core;

public sealed class ProviderApiException(
    string providerName,
    string operation,
    int statusCode,
    string? errorCode,
    string? providerMessage)
    : Exception(BuildMessage(providerName, operation, statusCode, errorCode, providerMessage))
{
    public string ProviderName { get; } = providerName;
    public string Operation { get; } = operation;
    public int StatusCode { get; } = statusCode;
    public string? ErrorCode { get; } = errorCode;
    public string? ProviderMessage { get; } = providerMessage;

    private static string BuildMessage(
        string providerName, string operation, int statusCode, string? errorCode, string? providerMessage)
    {
        var detail = string.Join(": ", new[] { errorCode, providerMessage }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        return $"{providerName} could not {operation} (HTTP {statusCode})" +
               (detail.Length == 0 ? "." : $": {detail}");
    }
}

public sealed class TrackWriteException(
    string providerName,
    int addedTrackCount,
    int requestedTrackCount,
    Exception innerException)
    : Exception(
        $"{providerName} added {addedTrackCount} of {requestedTrackCount} requested tracks before the operation failed.",
        innerException)
{
    public string ProviderName { get; } = providerName;
    public int AddedTrackCount { get; } = addedTrackCount;
    public int RequestedTrackCount { get; } = requestedTrackCount;
}

public sealed class PlaylistTransferException(
    TransferResult partialResult,
    Exception innerException)
    : Exception(BuildMessage(partialResult, innerException), innerException)
{
    public TransferResult PartialResult { get; } = partialResult;

    private static string BuildMessage(TransferResult result, Exception innerException) =>
        $"The destination playlist was created and {result.AddedTrackCount} of {result.MatchedCount} tracks were added. " +
        $"You can retry to resume. {innerException.Message}";
}
