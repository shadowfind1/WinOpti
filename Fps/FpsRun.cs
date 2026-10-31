using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace WinOpti.Fps
{
    /// <summary>Один замер: игра, когда, итоги. Кадры целиком не храним — только статистику.</summary>
    [DataContract]
    public sealed class FpsRun
    {
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Game;        // имя процесса без .exe, например cs2
        [DataMember] public DateTime When;
        [DataMember] public FrameStats Stats;
        [DataMember] public string Label;       // «До», «После» или пусто
        [DataMember] public List<double> Passes; // средний FPS каждого прохода серии (null — один проход)
        [DataMember] public List<Thief> Thieves; // что мешало игре во время замера
        [DataMember] public bool Shared;          // отправлен в общую базу для сравнения с похожими ПК

        /// <summary>Разброс между проходами серии, % от среднего. 0 — один проход.</summary>
        public double Spread => Passes == null || Passes.Count < 2 ? 0 : (Passes.Max() - Passes.Min()) / Passes.Average() * 100;
    }

    /// <summary>История замеров в %LOCALAPPDATA%\WinOpti\fps.json.</summary>
    public static class FpsStore
    {
        static string FilePath => Path.Combine(App.DataDir, "fps.json");
        static readonly DataContractJsonSerializer Json = new DataContractJsonSerializer(typeof(List<FpsRun>),
            new DataContractJsonSerializerSettings { DateTimeFormat = new DateTimeFormat("o") });

        public static List<FpsRun> Load()
        {
            try
            {
                using (var f = File.OpenRead(FilePath))
                    return ((List<FpsRun>)Json.ReadObject(f)).OrderByDescending(r => r.When).ToList();
            }
            catch { return new List<FpsRun>(); }
        }

        public static void Save(List<FpsRun> runs)
        {
            Directory.CreateDirectory(App.DataDir);
            var tmp = FilePath + ".tmp";
            using (var f = File.Create(tmp)) Json.WriteObject(f, runs.Take(100).ToList());
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }
    }

    /// <summary>Откуда берутся кадры. Реализация — PresentMon или собственный ETW-трейсер.</summary>
    public interface IFrameSource
    {
        /// <summary>Записать кадры всех процессов за duration.</summary>
        FrameCapture Capture(TimeSpan duration, System.Threading.CancellationToken cancel);
    }

    /// <summary>Кадры по процессам: длительность кадра в мс и момент кадра в секундах от начала записи (параллельные списки).</summary>
    public sealed class FrameCapture
    {
        public Dictionary<string, List<double>> Frames = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<double>> Times = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
    }

    public static class GamePicker
    {
        static readonly HashSet<string> NotGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "dwm", "explorer", "WinOpti", "chrome", "msedge", "firefox", "opera", "browser", "Discord", "Telegram",
            "steamwebhelper", "obs64", "Code", "ApplicationFrameHost", "SearchHost", "StartMenuExperienceHost",
            "TextInputHost", "ShellExperienceHost", "NVIDIA Overlay", "GameBar", "Widgets", "msedgewebview2",
            "vlc", "mpc-hc64", "PotPlayerMini64", "Spotify", "LockApp",
        };

        public static bool IsNotGame(string process) => NotGames.Contains(process);

        /// <summary>Игра — та, что была открыта на весь экран (preferred), иначе процесс, который выдал больше всего кадров.
        /// Браузеры, мессенджеры, оверлеи и сама Windows не считаются.</summary>
        public static string Pick(Dictionary<string, List<double>> frames, string preferred = null)
        {
            if (preferred != null && frames.TryGetValue(preferred, out var own) && own.Count >= 30) return preferred;
            return frames
                .Where(kv => !NotGames.Contains(kv.Key) && kv.Value.Count >= 30)
                .OrderByDescending(kv => kv.Value.Count)
                .Select(kv => kv.Key)
                .FirstOrDefault();
        }
    }
}
