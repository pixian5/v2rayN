namespace ServiceLib.Services;

public class UpdateService(Config config, Func<bool, string, Task> updateFunc)
{
    private readonly record struct GeoUpdateResult(bool Downloaded, bool Failed)
    {
        public static GeoUpdateResult None => new(false, false);

        public GeoUpdateResult Merge(GeoUpdateResult other)
            => new(Downloaded || other.Downloaded, Failed || other.Failed);
    }

    private readonly record struct RemoteGeoFileInfo(string? ETag, DateTimeOffset? LastModified, long? ContentLength);

    private sealed class GeoFileMeta
    {
        public string? ETag { get; set; }
        public DateTimeOffset? LastModified { get; set; }
        public long? ContentLength { get; set; }
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
        RemoteGeoFileInfo? remoteInfo = null;

        try
        {
            remoteInfo = await GetRemoteGeoFileInfo(url);
            if (remoteInfo is not null && IsGeoFileUpToDate(targetPath, remoteInfo.Value))
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
                        SaveGeoFileMeta(targetPath, remoteInfo);

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

    private bool IsGeoFileUpToDate(string targetPath, RemoteGeoFileInfo remoteInfo)
    {
        if (!File.Exists(targetPath))
        {
            return false;
        }

        var localMeta = LoadGeoFileMeta(targetPath);
        var remoteEtag = NormalizeETag(remoteInfo.ETag);
        var localEtag = NormalizeETag(localMeta?.ETag);
        if (remoteEtag.IsNotEmpty() && localEtag.IsNotEmpty() && string.Equals(remoteEtag, localEtag, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (remoteInfo.LastModified is not null)
        {
            if (localMeta?.LastModified is not null
                && remoteInfo.LastModified.Value.UtcDateTime <= localMeta.LastModified.Value.UtcDateTime.AddSeconds(1))
            {
                return true;
            }

            var localLastWriteTime = File.GetLastWriteTimeUtc(targetPath);
            if (remoteInfo.LastModified.Value.UtcDateTime <= localLastWriteTime.AddSeconds(1))
            {
                return true;
            }
        }

        if (remoteInfo.ContentLength is > 0)
        {
            var localLength = new FileInfo(targetPath).Length;
            if (localLength == remoteInfo.ContentLength.Value
                && localMeta?.ContentLength == remoteInfo.ContentLength.Value)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<RemoteGeoFileInfo?> GetRemoteGeoFileInfo(string url)
    {
        if (_systemProxyFirst)
        {
            var info = await TryGetRemoteGeoFileInfo(url, GetSystemProxy(), true, "system");
            if (info is not null)
            {
                return info;
            }
        }

        var localProxy = await GetLocalSocksProxy();
        if (localProxy is not null)
        {
            var info = await TryGetRemoteGeoFileInfo(url, localProxy, true, "local");
            if (info is not null)
            {
                return info;
            }
        }

        return await TryGetRemoteGeoFileInfo(url, null, false, "direct");
    }

    private async Task<RemoteGeoFileInfo?> TryGetRemoteGeoFileInfo(string url, IWebProxy? proxy, bool useProxy, string proxyMode)
    {
        try
        {
            using var handler = new SocketsHttpHandler
            {
                Proxy = useProxy ? proxy : null,
                UseProxy = useProxy
            };
            using var client = new HttpClient(handler);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeout));

            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var info = ReadRemoteGeoFileInfo(response);
                if (HasRemoteGeoIdentifier(info))
                {
                    return info;
                }
            }

            using var getRequest = new HttpRequestMessage(HttpMethod.Get, url);
            using var getResponse = await client.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (getResponse.IsSuccessStatusCode)
            {
                var info = ReadRemoteGeoFileInfo(getResponse);
                if (HasRemoteGeoIdentifier(info))
                {
                    return info;
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag} TryGetRemoteGeoFileInfo failed. proxy={proxyMode}; {ex.Message}");
        }

        return null;
    }

    private static RemoteGeoFileInfo ReadRemoteGeoFileInfo(HttpResponseMessage response)
    {
        return new RemoteGeoFileInfo(
            NormalizeETag(response.Headers.ETag?.Tag),
            response.Content.Headers.LastModified,
            response.Content.Headers.ContentLength);
    }

    private static bool HasRemoteGeoIdentifier(RemoteGeoFileInfo info)
    {
        return info.ETag.IsNotEmpty() || info.LastModified is not null || info.ContentLength is > 0;
    }

    private static string NormalizeETag(string? etag)
    {
        if (etag.IsNullOrEmpty())
        {
            return string.Empty;
        }

        etag = etag.Trim();
        if (etag.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
        {
            etag = etag[2..];
        }
        return etag.Trim('"');
    }

    private static string GetGeoFileMetaPath(string targetPath)
    {
        return $"{targetPath}.meta";
    }

    private static GeoFileMeta? LoadGeoFileMeta(string targetPath)
    {
        var metaPath = GetGeoFileMetaPath(targetPath);
        if (!File.Exists(metaPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(metaPath);
            return JsonSerializer.Deserialize<GeoFileMeta>(json);
        }
        catch
        {
            return null;
        }
    }

    private static void SaveGeoFileMeta(string targetPath, RemoteGeoFileInfo? remoteInfo)
    {
        try
        {
            var meta = new GeoFileMeta
            {
                ETag = NormalizeETag(remoteInfo?.ETag),
                LastModified = remoteInfo?.LastModified,
                ContentLength = remoteInfo?.ContentLength ?? new FileInfo(targetPath).Length
            };

            var json = JsonSerializer.Serialize(meta);
            File.WriteAllText(GetGeoFileMetaPath(targetPath), json);
        }
        catch
        {
        }
    }

    private async Task<IWebProxy?> GetLocalSocksProxy()
    {
        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        if (await SocketCheck(Global.Loopback, port) == false)
        {
            return null;
        }

        return new WebProxy($"{Global.Socks5Protocol}{Global.Loopback}:{port}");
    }

    private static async Task<bool> SocketCheck(string ip, int port)
    {
        try
        {
            IPEndPoint point = new(IPAddress.Parse(ip), port);
            using Socket sock = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await sock.ConnectAsync(point);
            return true;
        }
        catch
        {
            return false;
        }
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
