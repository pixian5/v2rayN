using System.Net.Http.Headers;

namespace ServiceLib.Services;

/// <summary>
///Download
/// </summary>
public class DownloadService
{
    public event EventHandler<UpdateResult>? UpdateCompleted;

    public event ErrorEventHandler? Error;

    private static readonly string _tag = "DownloadService";

    public async Task<int> DownloadDataAsync(string url, WebProxy webProxy, int downloadTimeout, Func<bool, string, Task> updateFunc)
    {
        try
        {
            var progress = new Progress<string>();
            progress.ProgressChanged += (sender, value) => updateFunc?.Invoke(false, $"{value}");

            await DownloaderHelper.Instance.DownloadDataAsync4Speed(webProxy,
                  url,
                  progress,
                  downloadTimeout);
        }
        catch (Exception ex)
        {
            await updateFunc?.Invoke(false, ex.Message);
            if (ex.InnerException != null)
            {
                await updateFunc?.Invoke(false, ex.InnerException.Message);
            }
        }
        return 0;
    }

    public async Task DownloadFileAsync(string url, string fileName, bool blProxy, int downloadTimeout, bool useSystemProxyFirst = false)
    {
        try
        {
            UpdateCompleted?.Invoke(this, new UpdateResult(false, $"{ResUI.DownloadProgress}: 0%{Environment.NewLine}{url}"));

            var progress = new Progress<double>();
            progress.ProgressChanged += (sender, value) =>
            {
                var percent = Math.Clamp((int)value, 0, 100);
                UpdateCompleted?.Invoke(this, new UpdateResult(value > 100, $"{ResUI.DownloadProgress}: {percent}%{Environment.NewLine}{url}"));
            };

            var useSystemProxy = useSystemProxyFirst && blProxy;
            var webProxy = await GetWebProxy(blProxy, useSystemProxy);

            if (useSystemProxy)
            {
                LogProxyUsage("DownloadFile(proxy)", url, true, webProxy);
                try
                {
                    await DownloadFileViaHttpClientAsync(url, fileName, progress, downloadTimeout, webProxy, true);
                }
                catch (Exception ex)
                {
                    Logging.SaveLog($"{_tag} DownloadFile proxy failed, fallback direct. {ex.Message}");
                    LogProxyUsage("DownloadFile(direct-fallback)", url, false, null);
                    await DownloadFileViaHttpClientAsync(url, fileName, progress, downloadTimeout, null, false);
                }
            }
            else
            {
                LogProxyUsage("DownloadFile(local-proxy)", url, webProxy != null, webProxy);
                await DownloaderHelper.Instance.DownloadFileAsync(webProxy,
                    url,
                    fileName,
                    progress,
                    downloadTimeout);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);

            Error?.Invoke(this, new ErrorEventArgs(ex));
            if (ex.InnerException != null)
            {
                Error?.Invoke(this, new ErrorEventArgs(ex.InnerException));
            }
        }
    }

    public async Task<string?> UrlRedirectAsync(string url, bool blProxy, bool useSystemProxyFirst = false)
    {
        async Task<string?> TryRedirectAsync(IWebProxy? webProxy)
        {
            var webRequestHandler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                Proxy = webProxy,
                UseProxy = useSystemProxyFirst || webProxy != null
            };
            var client = new HttpClient(webRequestHandler);

            var response = await client.GetAsync(url);
            if (response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location is not null)
            {
                return response.Headers.Location.ToString();
            }
            return null;
        }

        var useSystemProxy = useSystemProxyFirst && blProxy;
        var webProxy = await GetWebProxy(blProxy, useSystemProxy);
        LogProxyUsage("UrlRedirect(proxy)", url, useSystemProxyFirst || webProxy != null, webProxy);

        try
        {
            var redirectUrl = await TryRedirectAsync(webProxy);
            if (redirectUrl.IsNotEmpty())
            {
                return redirectUrl;
            }

            if (useSystemProxy && webProxy != null)
            {
                LogProxyUsage("UrlRedirect(direct-fallback)", url, false, null);
                return await TryRedirectAsync(null);
            }
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, new ErrorEventArgs(ex));
            Logging.SaveLog(_tag, ex);
        }

        Error?.Invoke(this, new ErrorEventArgs(new Exception("StatusCode error or no redirect")));
        Logging.SaveLog("StatusCode error: " + url);
        return null;
    }

    public async Task<string?> TryDownloadString(string url, bool blProxy, string userAgent, bool useSystemProxyFirst = false)
    {
        var useSystemProxy = useSystemProxyFirst && blProxy;

        var result = await TryDownloadStringCore(url, blProxy, userAgent, useSystemProxy);
        if (result.IsNotEmpty())
        {
            return result;
        }

        if (useSystemProxy)
        {
            return await TryDownloadStringCore(url, false, userAgent, false);
        }

        return null;
    }

    private async Task<string?> TryDownloadStringCore(string url, bool blProxy, string userAgent, bool useSystemProxy)
    {
        try
        {
            var result1 = await DownloadStringAsync(url, blProxy, userAgent, 15, useSystemProxy);
            if (result1.IsNotEmpty())
            {
                return result1;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            Error?.Invoke(this, new ErrorEventArgs(ex));
            if (ex.InnerException != null)
            {
                Error?.Invoke(this, new ErrorEventArgs(ex.InnerException));
            }
        }

        try
        {
            var result2 = await DownloadStringViaDownloader(url, blProxy, userAgent, 15, useSystemProxy);
            if (result2.IsNotEmpty())
            {
                return result2;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            Error?.Invoke(this, new ErrorEventArgs(ex));
            if (ex.InnerException != null)
            {
                Error?.Invoke(this, new ErrorEventArgs(ex.InnerException));
            }
        }

        return null;
    }

    /// <summary>
    /// DownloadString
    /// </summary>
    /// <param name="url"></param>
    private async Task<string?> DownloadStringAsync(string url, bool blProxy, string userAgent, int timeout, bool useSystemProxy = false)
    {
        try
        {
            var webProxy = await GetWebProxy(blProxy, useSystemProxy);
            LogProxyUsage("DownloadString", url, useSystemProxy || webProxy != null, webProxy);
            var client = new HttpClient(new SocketsHttpHandler()
            {
                Proxy = webProxy,
                UseProxy = useSystemProxy || webProxy != null
            });

            if (userAgent.IsNullOrEmpty())
            {
                userAgent = Utils.GetVersion(false);
            }
            client.DefaultRequestHeaders.UserAgent.TryParseAdd(userAgent);

            Uri uri = new(url);
            //Authorization Header
            if (uri.UserInfo.IsNotEmpty())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Utils.Base64Encode(uri.UserInfo));
            }

            using var cts = new CancellationTokenSource();
            var result = await client.GetStringAsync(url, cts.Token).WaitAsync(TimeSpan.FromSeconds(timeout), cts.Token);
            return result;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            Error?.Invoke(this, new ErrorEventArgs(ex));
            if (ex.InnerException != null)
            {
                Error?.Invoke(this, new ErrorEventArgs(ex.InnerException));
            }
        }
        return null;
    }

    /// <summary>
    /// DownloadString
    /// </summary>
    /// <param name="url"></param>
    private async Task<string?> DownloadStringViaDownloader(string url, bool blProxy, string userAgent, int timeout, bool useSystemProxy = false)
    {
        try
        {
            var webProxy = await GetWebProxy(blProxy, useSystemProxy);
            LogProxyUsage("DownloadStringViaDownloader", url, webProxy != null, webProxy);

            if (userAgent.IsNullOrEmpty())
            {
                userAgent = Utils.GetVersion(false);
            }
            var result = await DownloaderHelper.Instance.DownloadStringAsync(webProxy, url, userAgent, timeout);
            return result;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            Error?.Invoke(this, new ErrorEventArgs(ex));
            if (ex.InnerException != null)
            {
                Error?.Invoke(this, new ErrorEventArgs(ex.InnerException));
            }
        }
        return null;
    }

    private async Task<IWebProxy?> GetWebProxy(bool blProxy, bool useSystemProxy = false)
    {
        if (!blProxy)
        {
            return null;
        }

        if (useSystemProxy)
        {
            return GetSystemProxy();
        }

        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        if (await SocketCheck(Global.Loopback, port) == false)
        {
            return null;
        }

        return new WebProxy($"socks5://{Global.Loopback}:{port}");
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

    private async Task DownloadFileViaHttpClientAsync(
        string url,
        string fileName,
        IProgress<double> progress,
        int timeout,
        IWebProxy? webProxy,
        bool useProxy)
    {
        if (url.IsNullOrEmpty())
        {
            throw new ArgumentNullException(nameof(url));
        }
        if (fileName.IsNullOrEmpty())
        {
            throw new ArgumentNullException(nameof(fileName));
        }
        if (File.Exists(fileName))
        {
            File.Delete(fileName);
        }

        var handler = new SocketsHttpHandler
        {
            Proxy = webProxy,
            UseProxy = useProxy
        };
        using var client = new HttpClient(handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));

        progress?.Report(0);
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();

        var totalLength = response.Content.Headers.ContentLength;
        var canReport = totalLength is > 0;
        var progressPercentage = 0;
        long totalRead = 0;

        await using var fileStream = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await using var responseStream = await response.Content.ReadAsStreamAsync(cts.Token);
        var buffer = new byte[81920];
        while (true)
        {
            var read = await responseStream.ReadAsync(buffer, cts.Token);
            if (read <= 0)
            {
                break;
            }

            await fileStream.WriteAsync(buffer.AsMemory(0, read), cts.Token);
            totalRead += read;

            if (!canReport)
            {
                continue;
            }

            var percent = (int)(totalRead * 100 / totalLength!.Value);
            if (progressPercentage != percent && percent % 10 == 0)
            {
                progressPercentage = percent;
                progress?.Report(percent);
            }
        }

        progress?.Report(101);
    }

    private static void LogProxyUsage(string action, string url, bool useProxy, IWebProxy? proxy)
    {
        var proxyInfo = useProxy ? ResolveProxyInfo(proxy, url) : "direct";
        Logging.SaveLog($"{_tag} {action} | useProxy={useProxy} | proxy={proxyInfo} | url={GetSafeUrl(url)}");
    }

    private static string ResolveProxyInfo(IWebProxy? proxy, string url)
    {
        if (proxy == null)
        {
            return "default";
        }

        try
        {
            var proxyUri = proxy.GetProxy(new Uri(url));
            return proxyUri?.ToString() ?? "null";
        }
        catch (Exception ex)
        {
            return $"resolve_error:{ex.Message}";
        }
    }

    private static string GetSafeUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var builder = new UriBuilder(uri)
            {
                UserName = string.Empty,
                Password = string.Empty,
                Query = string.Empty
            };
            return builder.Uri.ToString();
        }
        catch
        {
            return url;
        }
    }

    private async Task<bool> SocketCheck(string ip, int port)
    {
        try
        {
            IPEndPoint point = new(IPAddress.Parse(ip), port);
            using Socket? sock = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await sock.ConnectAsync(point);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
