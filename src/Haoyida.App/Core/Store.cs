using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Haoyida.Core;

/// <summary>JSON 单文件存储：原子写入 + 自动备份，避免突然断电把门店配置写坏。</summary>
public sealed class DataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _gate = new();

    public string DataDirectory { get; }
    public string FilePath { get; }
    public AppData Data { get; private set; } = new();

    public DataStore(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Haoyida");
        Directory.CreateDirectory(DataDirectory);
        FilePath = Path.Combine(DataDirectory, "store.json");
        Data = Load();
    }

    public string CacheDirectory
    {
        get
        {
            var p = Path.Combine(DataDirectory, "cache");
            Directory.CreateDirectory(p);
            return p;
        }
    }

    public string ExportDirectory
    {
        get
        {
            var p = Path.Combine(DataDirectory, "exports");
            Directory.CreateDirectory(p);
            return p;
        }
    }

    private AppData Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var data = JsonSerializer.Deserialize<AppData>(json, JsonOptions);
                if (data is not null)
                {
                    data.Store ??= new StoreProfile();
                    data.Scripts ??= new List<ScriptItem>();
                    data.Products ??= new List<ProductItem>();
                    data.Tasks ??= new List<BroadcastTask>();
                    data.Logs ??= new List<LogEntry>();
                    data.MusicTracks ??= new List<MusicTrack>();
                    data.MusicTasks ??= new List<MusicTask>();
                    data.CustomModels ??= new List<CustomTtsModel>();
                    Seed.AddMusicDefaults(data);
                    return data;
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                var broken = FilePath + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                File.Copy(FilePath, broken, overwrite: true);
            }
            catch { /* 备份失败也不能阻止启动 */ }
            System.Diagnostics.Debug.WriteLine("读取配置失败：" + ex.Message);
        }

        return Seed.Create();
    }

    public void Save()
    {
        lock (_gate)
        {
            try
            {
                if (Data.Logs.Count > 500) Data.Logs.RemoveRange(0, Data.Logs.Count - 500);

                var json = JsonSerializer.Serialize(Data, JsonOptions);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);

                if (File.Exists(FilePath))
                    File.Replace(tmp, FilePath, FilePath + ".bak", ignoreMetadataErrors: true);
                else
                    File.Move(tmp, FilePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("保存配置失败：" + ex.Message);
            }
        }
    }

    public void ResetToSeed()
    {
        Data = Seed.Create();
        Save();
    }
}

/// <summary>首次启动的示例数据：让软件打开就能直接试听，而不是一片空白。</summary>
public static class Seed
{
    /// <summary>给老数据补音乐任务（用户升级后不会丢配置，也不会空列表）。</summary>
    public static void AddMusicDefaults(AppData data)
    {
        // 老数据也要补齐新增的内置曲（按文件名去重）
        void Ensure(string title, string file, string mood)
        {
            if (data.MusicTracks.Any(x => x.IsBuiltIn && x.FilePath == file)) return;
            data.MusicTracks.Add(new MusicTrack { Title = title, FilePath = file, IsBuiltIn = true, Mood = mood });
        }

        Ensure("晨光", "morning.wav", "轻快明亮");
        Ensure("午间", "midday.wav", "舒缓日常");
        Ensure("暮色", "evening.wav", "温暖收尾");
        Ensure("喜庆", "festive.wav", "节日促销");
        Ensure("欢快", "upbeat.wav", "动感活力");
        Ensure("轻松", "relax.wav", "轻松闲适");
        Ensure("温馨", "warm.wav", "温情慢板");

        if (data.MusicTasks.Count > 0) return;

        MusicTrack T(string file) => data.MusicTracks.First(x => x.FilePath == file);
        var morning = T("morning.wav");
        var midday = T("midday.wav");
        var evening = T("evening.wav");
        var festive = T("festive.wav");
        var upbeat = T("upbeat.wav");
        var relax = T("relax.wav");
        var warm = T("warm.wav");

        data.MusicTasks.Add(new MusicTask
        {
            Name = "晨间音乐", StartTime = "07:00", EndTime = "07:30",
            Loop = true, Shuffle = false, TrackIds = new List<string> { morning.Id, relax.Id }
        });
        data.MusicTasks.Add(new MusicTask
        {
            Name = "营业期间背景音乐", StartTime = "07:30", EndTime = "21:30",
            Loop = true, Shuffle = true, TrackIds = new List<string> { midday.Id, morning.Id, evening.Id, upbeat.Id, relax.Id, festive.Id }
        });
        data.MusicTasks.Add(new MusicTask
        {
            Name = "晚间音乐", StartTime = "21:30", EndTime = "22:00",
            Loop = true, Shuffle = false, TrackIds = new List<string> { evening.Id, warm.Id }
        });
    }

    public static AppData Create()
    {
        var data = new AppData();
        data.Store.StoreName = "耗易达生活超市";
        data.Store.Branch = "旗舰店";
        data.Store.Phone = "400-000-0000";

        data.Scripts.AddRange(new[]
        {
            new ScriptItem
            {
                Title = "迎宾开场",
                Category = "迎宾",
                Body = "亲爱的顾客朋友们，早上好！欢迎光临{店名}{分店}。本店生鲜果蔬每日清晨直采到店，祝您购物愉快。"
            },
            new ScriptItem
            {
                Title = "生鲜特惠",
                Category = "促销",
                Body = "顾客朋友们请注意，本店{商品}限时特惠，原价{原价}，现价{现价}，每{单位}，{促销语}数量有限，售完为止。"
            },
            new ScriptItem
            {
                Title = "防损提示",
                Category = "安全",
                Body = "温馨提示：请顾客朋友们看管好随身物品，照顾好老人和小孩，购物车请勿推行至扶梯。感谢您的配合。"
            },
            new ScriptItem
            {
                Title = "称重提醒",
                Category = "服务",
                Body = "散装商品请先到称重台称重后再到收银台结账，称重台位于生鲜区入口处，谢谢您的配合。"
            },
            new ScriptItem
            {
                Title = "会员招募",
                Category = "服务",
                Body = "本店会员正在招募中，免费办理，积分可抵现，会员日双倍积分，详情请咨询服务台。"
            },
            new ScriptItem
            {
                Title = "闭店提醒",
                Category = "闭店",
                Body = "亲爱的顾客朋友们，本店将于{时间}停止营业，请您抓紧时间选购，感谢您的光临，祝您一路平安。"
            }
        });

        data.Products.AddRange(new[]
        {
            new ProductItem { Name = "红富士苹果", Unit = "斤", Category = "水果", OriginalPrice = 6.98m, PromoPrice = 4.98m, PromoText = "脆甜多汁，" },
            new ProductItem { Name = "本地小油菜", Unit = "斤", Category = "蔬菜", OriginalPrice = 3.50m, PromoPrice = 1.99m },
            new ProductItem { Name = "鲜鸡蛋", Unit = "斤", Category = "蛋品", OriginalPrice = 6.80m, PromoPrice = 5.28m },
            new ProductItem { Name = "五花肉", Unit = "斤", Category = "肉类", OriginalPrice = 18.80m, PromoPrice = 14.90m },
            new ProductItem { Name = "东北大米", Unit = "袋", Category = "粮油", OriginalPrice = 59.90m, PromoPrice = 45.90m },
            new ProductItem { Name = "纯牛奶", Unit = "箱", Category = "乳品", OriginalPrice = 68.00m, PromoPrice = 49.90m }
        });

        data.Tasks.AddRange(new[]
        {
            new BroadcastTask
            {
                Name = "早间迎宾",
                Kind = ScheduleKind.Daily,
                Time = "07:35",
                SourceKind = SourceKind.Script,
                ScriptId = data.Scripts[0].Id,
                Priority = 10
            },
            new BroadcastTask
            {
                Name = "生鲜轮播",
                Kind = ScheduleKind.Interval,
                StartTime = "08:00",
                EndTime = "21:30",
                IntervalMinutes = 45,
                SourceKind = SourceKind.Product,
                ProductId = data.Products[0].Id,
                Priority = 20
            },
            new BroadcastTask
            {
                Name = "午间防损",
                Kind = ScheduleKind.Daily,
                Time = "12:00",
                SourceKind = SourceKind.Script,
                ScriptId = data.Scripts[2].Id,
                Priority = 10
            },
            new BroadcastTask
            {
                Name = "闭店提醒",
                Kind = ScheduleKind.Daily,
                Time = "21:45",
                SourceKind = SourceKind.Script,
                ScriptId = data.Scripts[5].Id,
                Priority = 10
            }
        });

        AddMusicDefaults(data);
        return data;    }
}
