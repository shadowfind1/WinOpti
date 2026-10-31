using System;
using System.Linq;
using System.Runtime.Serialization;

namespace WinOpti.Fps
{
    /// <summary>
    /// Статистика по времени кадров. Определения как у PresentMon / CapFrameX:
    /// средний FPS = кадры / время; 1% low = FPS при 99-м перцентиле времени кадра; 0.1% low — при 99.9-м.
    /// Фриз = кадр дольше 2.5 медианы и дольше 1000/30 мс (заметная на глаз задержка).
    /// </summary>
    [DataContract]
    public sealed class FrameStats
    {
        [DataMember] public int Frames;
        [DataMember] public double Seconds;
        [DataMember] public double AvgFps;
        [DataMember] public double Low1;
        [DataMember] public double Low01;
        [DataMember] public int Stutters;
        [DataMember] public double WorstMs;

        public static FrameStats From(double[] frameMs)
        {
            var ms = frameMs.Where(x => x > 0 && x < 5000).ToArray();
            if (ms.Length < 30) throw new InvalidOperationException("Слишком мало кадров для замера: игра была свёрнута или не запущена.");
            double total = ms.Sum();
            var sorted = (double[])ms.Clone();
            Array.Sort(sorted);
            double median = Percentile(sorted, 50);
            double stutterMs = Math.Max(median * 2.5, 1000.0 / 30);
            return new FrameStats
            {
                Frames = ms.Length,
                Seconds = total / 1000,
                AvgFps = ms.Length / (total / 1000),
                Low1 = 1000 / Percentile(sorted, 99),
                Low01 = 1000 / Percentile(sorted, 99.9),
                Stutters = ms.Count(x => x > stutterMs),
                WorstMs = sorted[sorted.Length - 1],
            };
        }

        /// <summary>Перцентиль с линейной интерполяцией по отсортированному массиву.</summary>
        public static double Percentile(double[] sorted, double p)
        {
            double pos = (sorted.Length - 1) * p / 100;
            int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
            return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
        }
    }
}
