using System.Text.Json;
using PlaylistBridge.Core;

namespace PlaylistBridge.Providers;

internal static class ProviderHttp
{
    public static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string providerName,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        string? errorCode = null;
        string? providerMessage = null;
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                    providerMessage = error.GetString();
                else if (error.ValueKind == JsonValueKind.Object)
                {
                    providerMessage = GetString(error, "message");
                    errorCode = GetString(error, "status") ?? GetString(error, "code");
                    if (error.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array &&
                        errors.GetArrayLength() > 0)
                    {
                        var first = errors[0];
                        errorCode = GetString(first, "reason") ?? GetString(first, "code") ?? errorCode;
                        providerMessage ??= GetString(first, "message");
                    }
                }
            }
            else if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array &&
                     errors.GetArrayLength() > 0)
            {
                var first = errors[0];
                errorCode = GetString(first, "code");
                providerMessage = GetString(first, "detail") ?? GetString(first, "title");
            }
        }
        catch (JsonException)
        {
            // Providers occasionally return an HTML or empty error response. Status and operation remain useful.
        }

        throw new ProviderApiException(
            providerName, operation, (int)response.StatusCode, errorCode, providerMessage);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)) return null;
        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }
}
