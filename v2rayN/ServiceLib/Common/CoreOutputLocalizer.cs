namespace ServiceLib.Common;

public static class CoreOutputLocalizer
{
    private const string VlessDeprecatedText = "The feature VLESS (with no Flow, etc.) is deprecated, not recommended for using and might be removed. Please migrate to VLESS with Flow & Seed as soon as possible.";
    private const string WebSocketDeprecatedText = "The feature WebSocket transport (with ALPN http/1.1, etc.) is deprecated, not recommended for using and might be removed. Please migrate to XHTTP H2 & H3 as soon as possible.";
    private static readonly Regex _xrayStartedRegex = new(@"^(?<prefix>.*?core:\s*)Xray\s+(?<version>[\w\.\-]+)\s+started$", RegexOptions.Compiled);
    private static readonly Regex _allowInsecureDeprecatedRegex = new(@"^(?<prefix>.*?infra/conf:\s*)""allowInsecure""\s+will be removed automatically after\s+(?<date>\d{4}-\d{2}-\d{2}),\s+please use\s+""pinnedPeerCertSha256""\(pcs\)\s+and\s+""verifyPeerCertByName""\(vcn\)\s+instead,\s+PLEASE CONTACT YOUR SERVICE PROVIDER \(AIRPORT\)\s*$", RegexOptions.Compiled);
    private static readonly Regex _fromAcceptedRegex = new(@"^(?<prefix>.*?)(?<from>from)\s+(?<source>\S+)\s+(?<accepted>accepted|已接受)\s+(?<target>.+)$", RegexOptions.Compiled);

    public static string Localize(string? line)
    {
        if (line.IsNullOrEmpty())
        {
            return line ?? string.Empty;
        }

        var result = line;

        if (result.Contains("Penetrates Everything.", StringComparison.Ordinal))
        {
            result = result.Replace(
                "Penetrates Everything.",
                GetLocalizedText("CoreLogPenetratesEverything", "Penetrates Everything."),
                StringComparison.Ordinal);
        }

        if (result.Contains("A unified platform for anti-censorship.", StringComparison.Ordinal))
        {
            result = result.Replace(
                "A unified platform for anti-censorship.",
                GetLocalizedText("CoreLogUnifiedPlatform", "A unified platform for anti-censorship."),
                StringComparison.Ordinal);
        }

        if (result.Contains("infra/conf/serial: Reading config:", StringComparison.Ordinal))
        {
            result = result.Replace(
                "infra/conf/serial: Reading config:",
                GetLocalizedText("CoreLogReadingConfigPrefix", "infra/conf/serial: Reading config:"),
                StringComparison.Ordinal);
        }

        if (result.Contains("Name:", StringComparison.Ordinal))
        {
            result = result.Replace(
                "Name:",
                GetLocalizedText("CoreLogConfigNameField", "Name:"),
                StringComparison.Ordinal);
        }

        if (result.Contains("Format:", StringComparison.Ordinal))
        {
            result = result.Replace(
                "Format:",
                GetLocalizedText("CoreLogConfigFormatField", "Format:"),
                StringComparison.Ordinal);
        }

        if (result.Contains(VlessDeprecatedText, StringComparison.Ordinal))
        {
            result = result.Replace(
                VlessDeprecatedText,
                GetLocalizedText("CoreLogVlessDeprecated", VlessDeprecatedText),
                StringComparison.Ordinal);
        }

        if (result.Contains(WebSocketDeprecatedText, StringComparison.Ordinal))
        {
            result = result.Replace(
                WebSocketDeprecatedText,
                GetLocalizedText("CoreLogWebsocketDeprecated", WebSocketDeprecatedText),
                StringComparison.Ordinal);
        }

        var match = _xrayStartedRegex.Match(result);
        if (match.Success)
        {
            var localized = GetLocalizedText("CoreLogXrayStarted", "Xray {0} started", match.Groups["version"].Value);
            return $"{match.Groups["prefix"].Value}{localized}";
        }

        match = _allowInsecureDeprecatedRegex.Match(result);
        if (match.Success)
        {
            var localized = GetLocalizedText(
                "CoreLogAllowInsecureDeprecated",
                "\"allowInsecure\" will be removed automatically after {0}, please use \"pinnedPeerCertSha256\"(pcs) and \"verifyPeerCertByName\"(vcn) instead, please contact your service provider (airport).",
                match.Groups["date"].Value);
            return $"{match.Groups["prefix"].Value}{localized}";
        }

        if (result.Contains("from ", StringComparison.Ordinal) || result.Contains("已接受", StringComparison.Ordinal))
        {
            var fromMatch = _fromAcceptedRegex.Match(result);
            if (fromMatch.Success)
            {
                return $"{fromMatch.Groups["prefix"].Value}{GetLocalizedText("CoreLogFromPrefix", "from")} {fromMatch.Groups["source"].Value} {GetLocalizedText("CoreLogAcceptedVerb", "accepted")} {fromMatch.Groups["target"].Value}";
            }

            if (result.Contains(" accepted ", StringComparison.Ordinal))
            {
                result = result.Replace(
                    " accepted ",
                    $" {GetLocalizedText("CoreLogAcceptedVerb", "accepted")} ",
                    StringComparison.Ordinal);
            }
        }

        return result;
    }

    private static string GetLocalizedText(string resourceKey, string fallback, params object[] args)
    {
        var template = ResUI.ResourceManager.GetString(resourceKey, System.Globalization.CultureInfo.CurrentUICulture);
        template = string.IsNullOrWhiteSpace(template) ? fallback : template;

        try
        {
            return args.Length <= 0
                ? template
                : string.Format(template, args);
        }
        catch
        {
            return args.Length <= 0
                ? fallback
                : string.Format(fallback, args);
        }
    }
}
