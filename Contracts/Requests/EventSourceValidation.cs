namespace EventLauscherApi.Contracts.Requests;

internal static class EventSourceValidation
{
    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Validate locally only; never resolve or fetch the source URL.
    public static string? Validate(string? sourceUrl, string? sourcePlatform)
    {
        var url = Normalize(sourceUrl);
        if (url is not null &&
            (url.Length > 2000 ||
             !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
             string.IsNullOrWhiteSpace(uri.Host)))
        {
            return "sourceUrl muss eine absolute HTTP-/HTTPS-URL mit maximal 2000 Zeichen sein.";
        }

        var platform = Normalize(sourcePlatform);
        if (platform is not null && platform is not ("instagram" or "facebook"))
            return "sourcePlatform muss instagram oder facebook sein.";

        return null;
    }
}
