using NLog;
using NLog.Config;
using NLog.Targets;

namespace ServiceLib.Common;

public class Logging
{
    private static readonly Logger _logger1 = LogManager.GetLogger("Log1");
    private static readonly Logger _logger2 = LogManager.GetLogger("Log2");
    private static readonly Dictionary<string, string> _exactMessageResourceKeys = new(StringComparer.Ordinal)
    {
        ["AppExitAsync Begin"] = "LogAppExitBegin",
        ["AppExitAsync End"] = "LogAppExitEnd",
        ["LoadConfig Exception"] = "LogLoadConfigException",
        ["Setup Scheduled Tasks"] = "LogSetupScheduledTasks",
        ["Execute update subscription"] = "LogExecuteUpdateSubscription",
        ["Execute update geo files"] = "LogExecuteUpdateGeoFiles",
        ["MigrateProfileExtraGroup: No items to migrate."] = "LogMigrateProfileExtraGroupNoItems",
        ["StatusCode error"] = "LogStatusCodeError",
        ["UpgradeApp does not exist"] = "LogUpgradeAppNotExist",
        ["FailedImportSubscription"] = "LogFailedImportSubscription",
    };

    private static readonly Dictionary<string, string> _exactTitleResourceKeys = new(StringComparer.Ordinal)
    {
        ["UpdateSubscription"] = "LogUpdateSubscriptionTitle",
        ["CurrentDomain_UnhandledException"] = "LogUnhandledExceptionTitle",
        ["TaskScheduler_UnobservedTaskException"] = "LogUnobservedTaskExceptionTitle",
        ["ScheduledTasks - UpdateTaskRunSubscription"] = "LogScheduledTaskUpdateSubscription",
        ["ScheduledTasks - SaveConfig"] = "LogScheduledTaskSaveConfig",
        ["ScheduledTasks - UpdateTaskRunGeo"] = "LogScheduledTaskUpdateGeo",
        ["ScheduledTasks - UpdateTaskRunCheckUpdate"] = "LogScheduledTaskCheckUpdate",
    };

    private static readonly Regex _appStartupRegex = new(@"^v2rayN start up \| (?<runtime>.+)$", RegexOptions.Compiled);
    private static readonly Regex _onClosingRegex = new(@"^OnClosing -> (?<closeReason>.+)$", RegexOptions.Compiled);
    private static readonly Regex _updateSubscriptionEndRegex = new(@"^Update subscription end\. (?<detail>.+)$", RegexOptions.Compiled);
    private static readonly Regex _executeScheduledCheckUpdateRegex = new(@"^Execute scheduled check update\. UTC hour=(?<hour>[^,]+), mode=(?<mode>.+)$", RegexOptions.Compiled);
    private static readonly Regex _migrateProfileExtraErrorRegex = new(@"^MigrateProfileExtra Error: (?<error>.+)$", RegexOptions.Compiled);
    private static readonly Regex _migrateProfileExtraGroupUpdateErrorRegex = new(@"^MigrateProfileExtraGroup update error: (?<error>.+)$", RegexOptions.Compiled);
    private static readonly Regex _migrateProfileExtraGroupFoundRegex = new(@"^MigrateProfileExtraGroup: Found (?<count>.+) group items to migrate\.$", RegexOptions.Compiled);
    private static readonly Regex _migrateProfileExtraGroupItemErrorRegex = new(@"^MigrateProfileExtraGroup item error \[(?<id>.+)\]: (?<error>.+)$", RegexOptions.Compiled);
    private static readonly Regex _migrateProfileExtraGroupUpdatedRegex = new(@"^MigrateProfileExtraGroup: Successfully updated (?<count>.+) items\.$", RegexOptions.Compiled);
    private static readonly Regex _statusCodeErrorRegex = new(@"^StatusCode error: (?<url>.+)$", RegexOptions.Compiled);
    private static readonly Regex _downloadProxyFallbackRegex = new(@"^(?<tag>.+) DownloadFile proxy failed, fallback direct\. (?<error>.+)$", RegexOptions.Compiled);
    private static readonly Regex _assignProcessToJobObjectErrorRegex = new(@"^Failed to call AssignProcessToJobObject! GetLastError=(?<errorCode>.+)$", RegexOptions.Compiled);
    private static readonly Regex _connectionTimeoutRegex = new(@"^Connection timeout after (?<seconds>.+) seconds$", RegexOptions.Compiled);
    private static readonly Regex _setFileExecutionPermissionRegex = new(@"^Successfully set the file execution permission, (?<fileName>.+)$", RegexOptions.Compiled);

    public static void Setup()
    {
        LoggingConfiguration config = new();
        FileTarget fileTarget = new();
        config.AddTarget("file", fileTarget);
        fileTarget.Layout = "${longdate}-${level:uppercase=true} ${message}";
        fileTarget.FileName = Utils.GetLogPath("${shortdate}.txt");
        config.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, fileTarget));
        LogManager.Configuration = config;
    }

    public static void LoggingEnabled(bool enable)
    {
        if (!enable)
        {
            LogManager.SuspendLogging();
        }
    }

    public static void SaveLog(string strContent)
    {
        if (!LogManager.IsLoggingEnabled())
        {
            return;
        }

        _logger1.Info(LocalizeLogText(strContent));
    }

    public static void SaveLog(string strTitle, Exception ex)
    {
        if (!LogManager.IsLoggingEnabled())
        {
            return;
        }

        var title = LocalizeLogTitle(strTitle);
        var message = LocalizeLogText(ex.Message);
        _logger2.Debug($"{title},{message}");
        _logger2.Debug(ex.StackTrace);
        if (ex?.InnerException != null)
        {
            _logger2.Error(ex.InnerException);
        }
    }

    private static string LocalizeLogTitle(string strTitle)
    {
        if (strTitle.IsNullOrEmpty())
        {
            return strTitle;
        }

        if (_exactTitleResourceKeys.TryGetValue(strTitle, out var resourceKey))
        {
            return GetLocalizedText(resourceKey, strTitle);
        }

        return LocalizeLogText(strTitle);
    }

    private static string LocalizeLogText(string? strContent)
    {
        if (strContent.IsNullOrEmpty())
        {
            return strContent ?? string.Empty;
        }

        if (_exactMessageResourceKeys.TryGetValue(strContent, out var resourceKey))
        {
            return GetLocalizedText(resourceKey, strContent);
        }

        var match = _appStartupRegex.Match(strContent);
        if (match.Success)
        {
            return GetLocalizedText("LogAppStartup", "v2rayN start up | {0}", match.Groups["runtime"].Value);
        }

        match = _onClosingRegex.Match(strContent);
        if (match.Success)
        {
            return GetLocalizedText("LogOnClosing", "OnClosing -> {0}", match.Groups["closeReason"].Value);
        }

        match = _updateSubscriptionEndRegex.Match(strContent);
        if (match.Success)
        {
            return GetLocalizedText("LogUpdateSubscriptionEnd", "Update subscription end. {0}", match.Groups["detail"].Value);
        }

        match = _executeScheduledCheckUpdateRegex.Match(strContent);
        if (match.Success)
        {
            return GetLocalizedText("LogExecuteScheduledCheckUpdate", "Execute scheduled check update. UTC hour={0}, mode={1}", match.Groups["hour"].Value, match.Groups["mode"].Value);
        }

        match = _migrateProfileExtraErrorRegex.Match(strContent);
        if (match.Success)
        {
            return GetLocalizedText("LogMigrateProfileExtraError", "MigrateProfileExtra Error: {0}", match.Groups["error"].Value);
        }

        match = _migrateProfileExtraGroupUpdateErrorRegex.Match(strContent);
        if (match.Success)
        {
            return GetLocalizedText("LogMigrateProfileExtraGroupUpdateError", "MigrateProfileExtraGroup update error: {0}", match.Groups["error"].Value);
        }

        match = _migrateProfileExtraGroupFoundRegex.Match(strContent);
        if (match.Success)
        {
            return GetLocalizedText("LogMigrateProfileExtraGroupFoundItems", "MigrateProfileExtraGroup: Found {0} group items to migrate.", match.Groups["count"].Value);
        }

        match = _migrateProfileExtraGroupItemErrorRegex.Match(strContent);
        if (match.Success)
        {
            return GetLocalizedText("LogMigrateProfileExtraGroupItemError", "MigrateProfileExtraGroup item error [{0}]: {1}", match.Groups["id"].Value, match.Groups["error"].Value);
        }

        match = _migrateProfileExtraGroupUpdatedRegex.Match(strContent);
        if (match.Success)
        {
            return GetLocalizedText("LogMigrateProfileExtraGroupUpdatedItems", "MigrateProfileExtraGroup: Successfully updated {0} items.", match.Groups["count"].Value);
        }

        match = _statusCodeErrorRegex.Match(strContent);
        if (match.Success)
        {
            return GetLocalizedText("LogStatusCodeError", "StatusCode error: {0}", match.Groups["url"].Value);
        }

        match = _downloadProxyFallbackRegex.Match(strContent);
        if (match.Success)
        {
            return GetLocalizedText("LogDownloadFileProxyFallback", "{0} DownloadFile proxy failed, fallback direct. {1}", match.Groups["tag"].Value, match.Groups["error"].Value);
        }

        match = _assignProcessToJobObjectErrorRegex.Match(strContent);
        if (match.Success)
        {
            return GetLocalizedText("LogAssignProcessToJobObjectFailed", "Failed to call AssignProcessToJobObject! GetLastError={0}", match.Groups["errorCode"].Value);
        }

        match = _connectionTimeoutRegex.Match(strContent);
        if (match.Success)
        {
            return GetLocalizedText("LogConnectionTimeoutAfterSeconds", "Connection timeout after {0} seconds", match.Groups["seconds"].Value);
        }

        match = _setFileExecutionPermissionRegex.Match(strContent);
        if (match.Success)
        {
            return GetLocalizedText("LogSetFileExecutionPermissionSuccess", "Successfully set the file execution permission, {0}", match.Groups["fileName"].Value);
        }

        return strContent;
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
