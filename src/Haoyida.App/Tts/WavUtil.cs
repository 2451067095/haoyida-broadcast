using System.IO;

namespace Haoyida.Tts;

/// <summary>
/// 极简 WAV 工具：读取 PCM16 单声道并做线性重采样。
/// 用途：Piper 只有一个中文音色，通过“先按 r 倍拉长、再按 r 倍重采样”实现
/// 语速不变、音调变化的变声（低沉 / 清亮），从而派生出不同“调调”。
/// </summary>
public static class WavUtil
{
    public sealed record Pcm(byte[] Data, int SampleRate, short Channels, short BitsPerSample, int DataOffset, int DataLength);

    public static Pcm Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 44) throw new InvalidDataException("WAV 文件过短");

        int pos = 12;
        int sampleRate = 22050;
        short channels = 1, bits = 16;
        int dataOffset = -1, dataLength = 0;

        while (pos + 8 <= bytes.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
            int size = BitConverter.ToInt32(bytes, pos + 4);
            int body = pos + 8;

            if (id == "fmt ")
            {
                channels = BitConverter.ToInt16(bytes, body + 2);
                sampleRate = BitConverter.ToInt32(bytes, body + 4);
                bits = BitConverter.ToInt16(bytes, body + 14);
            }
            else if (id == "data")
            {
                dataOffset = body;
                dataLength = Math.Min(size, bytes.Length - body);
                break;
            }

            pos = body + size + (size % 2);
        }

        if (dataOffset < 0 || bits != 16) throw new InvalidDataException("仅支持 16bit PCM WAV");
        return new Pcm(bytes, sampleRate, channels, bits, dataOffset, dataLength);
    }

    /// <summary>按 factor 重采样：factor&gt;1 音调升高、时长变短。</summary>
    public static void Resample(string sourcePath, string targetPath, double factor)
    {
        factor = Math.Clamp(factor, 0.7, 1.4);
        var pcm = Read(sourcePath);
        int frameBytes = pcm.Channels * 2;
        int frames = pcm.DataLength / frameBytes;
        int outFrames = Math.Max(1, (int)(frames / factor));

        var src = pcm.Data;
        var dst = new byte[outFrames * frameBytes];

        for (int i = 0; i < outFrames; i++)
        {
            double srcPos = i * factor;
            int i0 = (int)srcPos;
            int i1 = Math.Min(frames - 1, i0 + 1);
            double frac = srcPos - i0;

            for (int ch = 0; ch < pcm.Channels; ch++)
            {
                int o0 = pcm.DataOffset + i0 * frameBytes + ch * 2;
                int o1 = pcm.DataOffset + i1 * frameBytes + ch * 2;
                short s0 = BitConverter.ToInt16(src, o0);
                short s1 = BitConverter.ToInt16(src, o1);
                short v = (short)Math.Clamp(s0 + (s1 - s0) * frac, short.MinValue, short.MaxValue);
                BitConverter.TryWriteBytes(dst.AsSpan(i * frameBytes + ch * 2), v);
            }
        }

        using var fs = File.Create(targetPath);
        using var bw = new BinaryWriter(fs);

        int byteRate = pcm.SampleRate * pcm.Channels * 2;
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dst.Length);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write(pcm.Channels);
        bw.Write(pcm.SampleRate);
        bw.Write(byteRate);
        bw.Write((short)(pcm.Channels * 2));
        bw.Write((short)16);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dst.Length);
        bw.Write(dst);
    }

    public static double DurationSeconds(string path)
    {
        try
        {
            var pcm = Read(path);
            int frames = pcm.DataLength / (pcm.Channels * 2);
            return frames / (double)pcm.SampleRate;
        }
        catch { return 0; }
    }
}
