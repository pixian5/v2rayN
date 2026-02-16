namespace ServiceLib.Manager;

using ServiceLib.ViewModels;

public class TaskManager
{
    private static readonly Lazy<TaskManager> _instance = new(() => new());
    public static TaskManager Instance => _instance.Value;
    private Config _config;
    private Func<bool, string, Task>? _updateFunc;

    public void RegUpdateTask(Config config, Func<bool, string, Task> updateFunc)
    {
        _config = config;
        _updateFunc = updateFunc;

        Task.Run(ScheduledTasks);
    }

    private async Task ScheduledTasks()
    {
        Logging.SaveLog("Setup Scheduled Tasks");

        var numOfExecuted = 1;
        while (true)
        {
            //1 minute
            await Task.Delay(1000 * 60);

            //Execute once 1 minute
            try
            {
                await UpdateTaskRunSubscription();
            }
            catch (Exception ex)
            {
                Logging.SaveLog("ScheduledTasks - UpdateTaskRunSubscription", ex);
            }

            //Execute once 20 minute
            if (numOfExecuted % 20 == 0)
            {
                //Logging.SaveLog("Execute save config");

                try
                {
                    await ConfigHandler.SaveConfig(_config);
                    await ProfileExManager.Instance.SaveTo();
                }
                catch (Exception ex)
                {
                    Logging.SaveLog("ScheduledTasks - SaveConfig", ex);
                }
            }

            //Execute once 1 hour
            if (numOfExecuted % 60 == 0)
            {
                //Logging.SaveLog("Execute delete expired files");

                FileUtils.DeleteExpiredFiles(Utils.GetBinConfigPath(), DateTime.Now.AddHours(-1));
                FileUtils.DeleteExpiredFiles(Utils.GetLogPath(), DateTime.Now.AddMonths(-1));
                FileUtils.DeleteExpiredFiles(Utils.GetTempPath(), DateTime.Now.AddMonths(-1));

                try
                {
                    await UpdateTaskRunGeo(numOfExecuted / 60);
                }
                catch (Exception ex)
                {
                    Logging.SaveLog("ScheduledTasks - UpdateTaskRunGeo", ex);
                }

                try
                {
                    await UpdateTaskRunCheckUpdate();
                }
                catch (Exception ex)
                {
                    Logging.SaveLog("ScheduledTasks - UpdateTaskRunCheckUpdate", ex);
                }
            }

            numOfExecuted++;
        }
    }

    private async Task UpdateTaskRunSubscription()
    {
        var updateTime = ((DateTimeOffset)DateTime.Now).ToUnixTimeSeconds();
        var lstSubs = (await AppManager.Instance.SubItems())?
            .Where(t => t.AutoUpdateInterval > 0)
            .Where(t => updateTime - t.UpdateTime >= t.AutoUpdateInterval * 60)
            .ToList();

        if (lstSubs is not { Count: > 0 })
        {
            return;
        }

        Logging.SaveLog("Execute update subscription");

        foreach (var item in lstSubs)
        {
            await SubscriptionHandler.UpdateProcess(_config, item.Id, true, async (success, msg) =>
            {
                await _updateFunc?.Invoke(success, msg);
                if (success)
                {
                    Logging.SaveLog($"Update subscription end. {msg}");
                }
            });
            item.UpdateTime = updateTime;
            await ConfigHandler.AddSubItem(_config, item);
            await Task.Delay(1000);
        }
    }

    private async Task UpdateTaskRunGeo(int hours)
    {
        if (_config.GuiItem.AutoUpdateInterval > 0 && hours > 0 && hours % _config.GuiItem.AutoUpdateInterval == 0)
        {
            Logging.SaveLog("Execute update geo files");

            await new UpdateService(_config, async (success, msg) =>
            {
                await _updateFunc?.Invoke(false, msg);
            }).UpdateGeoFileAll();
        }
    }

    private async Task UpdateTaskRunCheckUpdate()
    {
        var utcHour = Math.Clamp(_config.CheckUpdateItem.AutoCheckUpdateUtcHour, 0, 23);
        var nowUtc = DateTime.UtcNow;
        if (nowUtc.Hour != utcHour)
        {
            return;
        }

        var utcDay = nowUtc.Year * 10000 + nowUtc.Month * 100 + nowUtc.Day;
        if (_config.CheckUpdateItem.LastAutoCheckUpdateUtcDay == utcDay)
        {
            return;
        }

        _config.CheckUpdateItem.LastAutoCheckUpdateUtcDay = utcDay;
        await ConfigHandler.SaveConfig(_config);
        if (_config.CheckUpdateItem.AutoCheckUpdateType != EAutoCheckUpdateType.CheckAndUpdate)
        {
            Logging.SaveLog($"Skip scheduled check update. UTC hour={utcHour}, mode={_config.CheckUpdateItem.AutoCheckUpdateType}");
            return;
        }

        Logging.SaveLog($"Execute scheduled check update. UTC hour={utcHour}, mode={_config.CheckUpdateItem.AutoCheckUpdateType}");
        await _updateFunc?.Invoke(false, $"执行定时更新任务 (UTC{utcHour})");

        var vm = new CheckUpdateViewModel(
            (_, _) => Task.FromResult(true),
            async (success, msg) => await _updateFunc?.Invoke(success, msg));

        await vm.ScheduledCheckAndUpdateAsync();
    }
}

