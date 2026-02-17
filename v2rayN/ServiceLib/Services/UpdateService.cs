namespace ServiceLib.Services;

public class UpdateService(Config config, Func<bool, string, Task> updateFunc)
{
    private readonly record struct GeoUpdateResult(bool Downloaded, bool Failed)
    {
        public static GeoUpdateResult None => new(false, false);

        public GeoUpdateResult Merge(GeoUpdateResult other)
            => new(Downloaded || other.Downloaded, Failed || other.Failed);
    }

    private readonly Config? _config = config;
    private readonly Func<bool, string, Task>? _updateFunc = updateFunc;
    private readonly int _timeout = 30;
    private const bool _systemProxyFirst = true;
    private static readonly string _tag = "UpdateService";

    public async Task CheckUpdateGuiN(bool preRelease)
    {
        var url = string.Empty;
        var fileName = string.Empty;

        DownloadService downloadHandle = new();
        downloadHandle.UpdateCompleted += (sender2, args) =>
        {
            if (args.Success)
            {
                _ = UpdateFunc(false, ResUI.MsgDownloadV2rayCoreSuccessfully);
                _ = UpdateFunc(true, Utils.UrlEncode(fileName));
            }
            else
            {
                _ = UpdateFunc(false, args.Msg);
            }
        };
        downloadHandle.Error += (sender2, args) =>
        {
            _ = UpdateFunc(false, args.GetException().Message);
        };

        await UpdateFunc(false, string.Format(ResUI.MsgStartUpdating, ECoreType.v2rayN));
        var result = await CheckUpdateAsync(downloadHandle, ECoreType.v2rayN, preRelease);
        if (result.Success)
        {
            await UpdateFunc(false, string.Format(ResUI.MsgParsingSuccessfully, ECoreType.v2rayN));
            await UpdateFunc(false, result.Msg);

            url = result.Url.ToString();
            fileName = Utils.GetTempPath(Utils.GetGuid());
            await downloadHandle.DownloadFileAsync(url, fileName, true, _timeout, _systemProxyFirst);
        }
        else
        {
            await UpdateFunc(false, result.Msg);
        }
    }

    public async Task CheckUpdateCore(ECoreType type, bool preRelease)
    {
        var url = string.Empty;
        var fileName = string.Empty;

        DownloadService downloadHandle = new();
        downloadHandle.UpdateCompleted += (sender2, args) =>
        {
            if (args.Success)
            {
                _ = UpdateFunc(false, ResUI.MsgDownloadV2rayCoreSuccessfully);
                _ = UpdateFunc(false, ResUI.MsgUnpacking);

                try
                {
                    _ = UpdateFunc(true, fileName);
                }
                catch (Exception ex)
                {
                    _ = UpdateFunc(false, ex.Message);
                }
            }
            else
            {
                _ = UpdateFunc(false, args.Msg);
            }
        };
        downloadHandle.Error += (sender2, args) =>
        {
            _ = UpdateFunc(false, args.GetException().Message);
        };

        await UpdateFunc(false, string.Format(ResUI.MsgStartUpdating, type));
        var result = await CheckUpdateAsync(downloadHandle, type, preRelease);
        if (result.Success)
        {
            await UpdateFunc(false, string.Format(ResUI.MsgParsingSuccessfully, type));
            await UpdateFunc(false, result.Msg);

            url = result.Url.ToString();
            var ext = url.Contains(".tar.gz") ? ".tar.gz" : Path.GetExtension(url);
            fileName = Utils.GetTempPath(Utils.GetGuid() + ext);
            await downloadHandle.DownloadFileAsync(url, fileName, true, _timeout, _systemProxyFirst);
        }
        else
        {
            if (!result.Msg.IsNullOrEmpty())
            {
                await UpdateFunc(false, result.Msg);
            }
        }
    }

    public async Task UpdateGeoFileAll()
    {
        var result = GeoUpdateResult.None;
        result = result.Merge(await UpdateGeoFiles());
        result = result.Merge(await UpdateOtherFiles());
        result = result.Merge(await UpdateSrsFileAll());

        if (result.Downloaded)
        {
            await UpdateFunc(true, string.Format(ResUI.MsgDownloadGeoFileSuccessfully, "geo"));
        }
        else if (!result.Failed)
        {
            await UpdateFunc(true, string.Format(ResUI.IsLatestN, "Geo", "files"));
        }
    }

    public async Task<UpdateResult> CheckUpdateOnly(ECoreType type, bool preRelease)
    {
        try
        {
            DownloadService downloadHandle = new();
            var result = await CheckUpdateAsync(downloadHandle, type, preRelease);
            if (result.Success)
            {
                var version = result.Version?.ToString() ?? "unknown";
                return new UpdateResult(true, $"{type} 发现新版本: {version}") { Version = result.Version, Url = result.Url };
            }

            if (result.Msg.IsNullOrEmpty())
            {
                return new UpdateResult(false, $"{type} 已是最新版本");
            }
            return result;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return new UpdateResult(false, ex.Message);
        }
    }

    #region CheckUpdate private

    private async Task<UpdateResult> CheckUpdateAsync(DownloadService downloadHandle, ECoreType type, bool preRelease)
    {
        try
        {
            var result = await GetRemoteVersion(downloadHandle, type, preRelease);
            if (!result.Success || result.Version is null)
            {
                return result;
            }
            return await ParseDownloadUrl(type, result);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            await UpdateFunc(false, ex.Message);
            return new UpdateResult(false, ex.Message);
        }
    }

    private async Task<UpdateResult> GetRemoteVersion(DownloadService downloadHandle, ECoreType type, bool preRelease)
    {
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(type);
        var tagName = string.Empty;
        if (preRelease)
        {
            var url = coreInfo?.ReleaseApiUrl;
            var result = await downloadHandle.TryDownloadString(url, true, Global.AppName, _systemProxyFirst);
            if (result.IsNullOrEmpty())
            {
                return new UpdateResult(false, "");
            }

            var gitHubReleases = JsonUtils.Deserialize<List<GitHubRelease>>(result);
            var gitHubRelease = preRelease ? gitHubReleases?.First() : gitHubReleases?.First(r => r.Prerelease == false);
            tagName = gitHubRelease?.TagName;
            //var body = gitHubRelease?.Body;
        }
        else
        {
            var url = Path.Combine(coreInfo.Url, "latest");
            var lastUrl = await downloadHandle.UrlRedirectAsync(url, true, _systemProxyFirst);
            if (lastUrl == null)
            {
                return new UpdateResult(false, "");
            }

            tagName = lastUrl?.Split("/tag/").LastOrDefault();
        }
        return new UpdateResult(true, new SemanticVersion(tagName));
    }

    private async Task<SemanticVersion> GetCoreVersion(ECoreType type)
    {
        try
        {
            var coreInfo = CoreInfoManager.Instance.GetCoreInfo(type);
            var filePath = string.Empty;
            foreach (var name in coreInfo.CoreExes)
            {
                var vName = Utils.GetBinPath(Utils.GetExeName(name), coreInfo.CoreType.ToString());
                if (File.Exists(vName))
                {
                    filePath = vName;
                    break;
                }
            }

            if (!File.Exists(filePath))
            {
                var msg = string.Format(ResUI.NotFoundCore, @"", "", "");
                //ShowMsg(true, msg);
                return new SemanticVersion("");
            }

            var result = await Utils.GetCliWrapOutput(filePath, coreInfo.VersionArg);
            var echo = result ?? "";
            var version = string.Empty;
            switch (type)
            {
                case ECoreType.v2fly:
                case ECoreType.Xray:
                case ECoreType.v2fly_v5:
                    version = Regex.Match(echo, $"{coreInfo.Match} ([0-9.]+) \\(").Groups[1].Value;
                    break;

                case ECoreType.mihomo:
                    version = Regex.Match(echo, $"v[0-9.]+").Groups[0].Value;
                    break;

                case ECoreType.sing_box:
                    version = Regex.Match(echo, $"([0-9.]+)").Groups[1].Value;
                    break;
            }
            return new SemanticVersion(version);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            await UpdateFunc(false, ex.Message);
            return new SemanticVersion("");
        }
    }

    private async Task<UpdateResult> ParseDownloadUrl(ECoreType type, UpdateResult result)
    {
        try
        {
            var version = result.Version ?? new SemanticVersion(0, 0, 0);
            var coreInfo = CoreInfoManager.Instance.GetCoreInfo(type);
            var coreUrl = await GetUrlFromCore(coreInfo) ?? string.Empty;
            SemanticVersion curVersion;
            string message;
            string? url;
            switch (type)
            {
                case ECoreType.v2fly:
                case ECoreType.Xray:
                case ECoreType.v2fly_v5:
                    {
                        curVersion = await GetCoreVersion(type);
                        message = string.Format(ResUI.IsLatestCore, type, curVersion.ToVersionString("v"));
                        url = string.Format(coreUrl, version.ToVersionString("v"));
                        break;
                    }
                case ECoreType.mihomo:
                    {
                        curVersion = await GetCoreVersion(type);
                        message = string.Format(ResUI.IsLatestCore, type, curVersion);
                        url = string.Format(coreUrl, version.ToVersionString("v"));
                        break;
                    }
                case ECoreType.sing_box:
                    {
                        curVersion = await GetCoreVersion(type);
                        message = string.Format(ResUI.IsLatestCore, type, curVersion.ToVersionString("v"));
                        url = string.Format(coreUrl, version.ToVersionString("v"), version);
                        break;
                    }
                case ECoreType.v2rayN:
                    {
                        curVersion = new SemanticVersion(Utils.GetVersionInfo());
                        message = string.Format(ResUI.IsLatestN, type, curVersion);
                        url = string.Format(coreUrl, version);
                        break;
                    }
                default:
                    throw new ArgumentException("Type");
            }

            if (curVersion >= version && version != new SemanticVersion(0, 0, 0))
            {
                return new UpdateResult(false, message);
            }

            result.Url = url;
            return result;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            await UpdateFunc(false, ex.Message);
            return new UpdateResult(false, ex.Message);
        }
    }

    private async Task<string?> GetUrlFromCore(CoreInfo? coreInfo)
    {
        if (Utils.IsWindows())
        {
            var url = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => coreInfo?.DownloadUrlWinArm64,
                Architecture.X64 => coreInfo?.DownloadUrlWin64,
                _ => null,
            };

            if (coreInfo?.CoreType != ECoreType.v2rayN)
            {
                return url;
            }

            //Check for avalonia desktop windows version
            if (File.Exists(Path.Combine(Utils.GetBaseDirectory(), "libHarfBuzzSharp.dll")))
            {
                return url?.Replace(".zip", "-desktop.zip");
            }

            return url;
        }
        else if (Utils.IsLinux())
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => coreInfo?.DownloadUrlLinuxArm64,
                Architecture.X64 => coreInfo?.DownloadUrlLinux64,
                _ => null,
            };
        }
        else if (Utils.IsMacOS())
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => coreInfo?.DownloadUrlOSXArm64,
                Architecture.X64 => coreInfo?.DownloadUrlOSX64,
                _ => null,
            };
        }
        return await Task.FromResult("");
    }

    #endregion CheckUpdate private

    #region Geo private

    private async Task<GeoUpdateResult> UpdateGeoFiles()
    {
        var geoUrl = string.IsNullOrEmpty(_config?.ConstItem.GeoSourceUrl)
            ? Global.GeoUrl
            : _config.ConstItem.GeoSourceUrl;

        var result = GeoUpdateResult.None;
        List<string> files = ["geosite", "geoip"];
        foreach (var geoName in files)
        {
            var fileName = $"{geoName}.dat";
            var targetPath = Utils.GetBinPath($"{fileName}");
            var url = string.Format(geoUrl, geoName);

            result = result.Merge(await DownloadGeoFile(url, fileName, targetPath));
        }

        return result;
    }

    private async Task<GeoUpdateResult> UpdateOtherFiles()
    {
        //If it is not in China area, no update is required
        if (_config.ConstItem.GeoSourceUrl.IsNotEmpty())
        {
            return GeoUpdateResult.None;
        }

        var result = GeoUpdateResult.None;
        foreach (var url in Global.OtherGeoUrls)
        {
            var fileName = Path.GetFileName(url);
            var targetPath = Utils.GetBinPath($"{fileName}");

            result = result.Merge(await DownloadGeoFile(url, fileName, targetPath));
        }

        return result;
    }

    private async Task<GeoUpdateResult> UpdateSrsFileAll()
    {
        var geoipFiles = new List<string>();
        var geoSiteFiles = new List<string>();

        //Collect used files list
        var routingItems = await AppManager.Instance.RoutingItems();
        foreach (var routing in routingItems)
        {
            var rules = JsonUtils.Deserialize<List<RulesItem>>(routing.RuleSet);
            foreach (var item in rules ?? [])
            {
                foreach (var ip in item.Ip ?? [])
                {
                    var prefix = "geoip:";
                    if (ip.StartsWith(prefix))
                    {
                        geoipFiles.Add(ip.Substring(prefix.Length));
                    }
                }

                foreach (var domain in item.Domain ?? [])
                {
                    var prefix = "geosite:";
                    if (domain.StartsWith(prefix))
                    {
                        geoSiteFiles.Add(domain.Substring(prefix.Length));
                    }
                }
            }
        }

        //append dns items TODO
        geoSiteFiles.Add("google");
        geoSiteFiles.Add("cn");
        geoSiteFiles.Add("geolocation-cn");
        geoSiteFiles.Add("category-ads-all");

        var path = Utils.GetBinPath("srss");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        var result = GeoUpdateResult.None;
        foreach (var item in geoipFiles.Distinct())
        {
            result = result.Merge(await UpdateSrsFile("geoip", item));
        }

        foreach (var item in geoSiteFiles.Distinct())
        {
            result = result.Merge(await UpdateSrsFile("geosite", item));
        }

        return result;
    }

    private async Task<GeoUpdateResult> UpdateSrsFile(string type, string srsName)
    {
        var srsUrl = string.IsNullOrEmpty(_config.ConstItem.SrsSourceUrl)
                        ? Global.SingboxRulesetUrl
                        : _config.ConstItem.SrsSourceUrl;

        var fileName = $"{type}-{srsName}.srs";
        var targetPath = Path.Combine(Utils.GetBinPath("srss"), fileName);
        var url = string.Format(srsUrl, type, $"{type}-{srsName}", srsName);

        return await DownloadGeoFile(url, fileName, targetPath);
    }

    private async Task<GeoUpdateResult> DownloadGeoFile(string url, string fileName, string targetPath)
    {
        try
        {
            if (await IsGeoFileUpToDate(url, targetPath))
            {
                return GeoUpdateResult.None;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag} CheckGeoUpToDate failed. file={fileName}; {ex.Message}");
        }

        var tmpFileName = Utils.GetTempPath(Utils.GetGuid());
        var result = GeoUpdateResult.None;

        DownloadService downloadHandle = new();
        downloadHandle.UpdateCompleted += (sender2, args) =>
        {
            if (args.Success)
            {
                try
                {
                    if (File.Exists(tmpFileName))
                    {
                        File.Copy(tmpFileName, targetPath, true);

                        File.Delete(tmpFileName);
                        result = result with { Downloaded = true };
                        _ = UpdateFunc(false, string.Format(ResUI.MsgDownloadGeoFileSuccessfully, fileName));
                    }
                }
                catch (Exception ex)
                {
                    result = result with { Failed = true };
                    _ = UpdateFunc(false, ex.Message);
                }
            }
            else
            {
                _ = UpdateFunc(false, args.Msg);
            }
        };
        downloadHandle.Error += (sender2, args) =>
        {
            result = result with { Failed = true };
            _ = UpdateFunc(false, args.GetException().Message);
        };

        await downloadHandle.DownloadFileAsync(url, tmpFileName, true, _timeout, _systemProxyFirst);
        return result;
    }

    private async Task<bool> IsGeoFileUpToDate(string url, string targetPath)
    {
        if (!File.Exists(targetPath))
        {
            return false;
        }

        var remoteLastModified = await GetRemoteLastModified(url);
        if (remoteLastModified is null)
        {
            return false;
        }

        var localLastWriteTime = File.GetLastWriteTimeUtc(targetPath);
        return remoteLastModified.Value.UtcDateTime <= localLastWriteTime.AddSeconds(1);
    }

    private async Task<DateTimeOffset?> GetRemoteLastModified(string url)
    {
        var lastModified = await TryGetRemoteLastModified(url, true);
        if (lastModified is not null)
        {
            return lastModified;
        }

        return await TryGetRemoteLastModified(url, false);
    }

    private async Task<DateTimeOffset?> TryGetRemoteLastModified(string url, bool useSystemProxy)
    {
        try
        {
            using var handler = new SocketsHttpHandler
            {
                Proxy = useSystemProxy ? GetSystemProxy() : null,
                UseProxy = useSystemProxy
            };
            using var client = new HttpClient(handler);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeout));

            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                return response.Content.Headers.LastModified;
            }

            if (response.StatusCode == HttpStatusCode.MethodNotAllowed || response.StatusCode == HttpStatusCode.NotImplemented)
            {
                using var getRequest = new HttpRequestMessage(HttpMethod.Get, url);
                using var getResponse = await client.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (getResponse.IsSuccessStatusCode)
                {
                    return getResponse.Content.Headers.LastModified;
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag} TryGetRemoteLastModified failed. proxy={useSystemProxy}; {ex.Message}");
        }

        return null;
    }

    private static IWebProxy? GetSystemProxy()
    {
        IWebProxy? proxy = null;

        try
        {
            proxy = HttpClient.DefaultProxy;
        }
        catch
        {
        }

        if (proxy == null)
        {
            try
            {
                proxy = WebRequest.GetSystemWebProxy();
            }
            catch
            {
            }
        }

        if (proxy != null)
        {
            try
            {
                proxy.Credentials ??= CredentialCache.DefaultCredentials;
            }
            catch
            {
            }
        }

        return proxy;
    }

    #endregion Geo private

    private async Task UpdateFunc(bool notify, string msg)
    {
        await _updateFunc?.Invoke(notify, msg);
    }
}
