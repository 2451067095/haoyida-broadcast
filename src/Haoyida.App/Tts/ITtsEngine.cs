namespace Haoyida.Tts;

/// <summary>音色：不同引擎、不同音色、不同“调调”都收敛成这一个结构。</summary>
public sealed record VoiceOption(
    string Id,
    string Name,
    string EngineId,
    string Gender,
    string Style,
    string Description,
    string SampleText);

public sealed record TtsRequest(
    string Text,
    string EngineId,
    string VoiceId,
    double Rate,
    double Pitch);

public sealed record TtsResult(string FilePath, string EngineId, string VoiceId, bool FromCache, long ElapsedMs);

public interface ITtsEngine
{
    string Id { get; }
    string Name { get; }
    string Vendor { get; }
    string License { get; }
    string Kind { get; }           // 离线 / 在线 / 录音
    string Note { get; }
    bool IsAvailable(out string reason);
    IReadOnlyList<VoiceOption> Voices { get; }
    Task<string> SynthesizeAsync(string text, VoiceOption voice, double rate, double pitch, string outputPath, CancellationToken ct);
}

public static class VoiceStyles
{
    public sealed record Preset(string Id, string Name, double Rate, double Pitch, string Hint);

    public static readonly Preset[] All =
    {
        new("warm",   "亲切",  0.95, 1.06, "语速略慢、音调微升，适合日常促销"),
        new("steady", "稳重",  0.88, 0.94, "语速偏慢、音调下沉，适合防损提示"),
        new("active", "活力",  1.12, 1.10, "语速较快、音调上扬，适合大促造势"),
        new("soft",   "温柔",  0.92, 1.02, "轻声细语，适合早晚时段"),
        new("news",   "播报腔", 1.0,  1.0,  "标准播音节奏，字正腔圆"),
        new("clear",  "清晰",  0.85, 1.0,  "放慢速度，适合老年顾客较多的门店")
    };

    public static Preset ById(string? id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];
}
