using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using WinOpti.Sys;

namespace WinOpti.Fps
{
    /// <summary>Итог сравнения: медианы похожих ПК. Count меньше 5 — данных пока мало, медиан нет.</summary>
    public sealed class Similar
    {
        public int Count;
        public double? Avg, Low1;
    }

    /// <summary>
    /// «Твой ПК против похожих». Только с согласия: отправляем игру, модели процессора и видеокарты, объём памяти,
    /// разрешение экрана и FPS. Плюс случайный id установки — чтобы не сравнивать человека с самим собой. Ничего личного.
    /// </summary>
    public static class Community
    {
        /// <summary>Адрес сервера (Cloudflare Worker). Пусто — функция скрыта.</summary>
        const string DefaultEndpoint = "";

        static string _endpoint;
        public static string Endpoint
        {
            get
            {
                if (_endpoint != null) return _endpoint;
#if DEBUG
                var arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--community="));
                if (arg != null) return _endpoint = arg.Substring("--community=".Length).TrimEnd('/');
#endif
                return _endpoint = DefaultEndpoint;
            }
        }

        public static bool Available => !string.IsNullOrEmpty(Endpoint);

        static string ConsentFile => Path.Combine(App.DataDir, "community.txt");

        public static bool Consent
        {
            get { try { return File.Exists(ConsentFile) && File.ReadAllText(ConsentFile).Trim() == "on"; } catch { return false; } }
            set { Directory.CreateDirectory(App.DataDir); File.WriteAllText(ConsentFile, value ? "on" : "off"); }
        }

        static string InstallId
        {
            get
            {
                var f = Path.Combine(App.DataDir, "install_id.txt");
                try { if (File.Exists(f)) { var id = File.ReadAllText(f).Trim(); if (Regex.IsMatch(id, "^[0-9a-f]{32}$")) return id; } } catch { }
                var fresh = Guid.NewGuid().ToString("N");
                try { Directory.CreateDirectory(App.DataDir); File.WriteAllText(f, fresh); } catch { }
                return fresh;
            }
        }

        public sealed class Spec { public string Cpu, Gpu, Res; public int Ram; }

        /// <summary>Железо в том виде, в каком его сравниваем: «Ryzen 5 3600», «GeForce GTX 1070 Ti», «1920x1080».</summary>
        public static Spec Hardware()
        {
            string Clean(string s) => Regex.Replace(Regex.Replace(s ?? "", @"\((R|TM|tm|r)\)|CPU|@.*$|\d+-Core Processor|with Radeon.*$", ""), @"\s+", " ").Trim();
            var cpu = Clean(SysInfo.Wmi("SELECT Name FROM Win32_Processor").Select(o => o["Name"] as string).FirstOrDefault());
            var gpus = Tests.GpuCheck.Gpus();
            var gpu = (gpus.FirstOrDefault(g => !g.Integrated) ?? gpus.FirstOrDefault())?.Name ?? "";
            gpu = Clean(gpu.Replace("NVIDIA ", "").Replace("AMD ", ""));
            double ram = SysInfo.Wmi("SELECT Capacity FROM Win32_PhysicalMemory").Sum(o => Convert.ToDouble(o["Capacity"] ?? 0)) / (1L << 30);
            var d = Displays.All().FirstOrDefault(x => x.Primary);
            return new Spec { Cpu = cpu, Gpu = gpu, Ram = (int)Math.Round(ram), Res = d == null ? "0x0" : $"{d.Width}x{d.Height}" };
        }

        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        static WebClient Client()
        {
            var w = new WebClient { Encoding = Encoding.UTF8 };
            w.Headers[HttpRequestHeader.UserAgent] = "WinOpti/2";
            return w;
        }

        public static void Submit(FpsRun run, Spec spec)
        {
            var body = Json.Serialize(new Dictionary<string, object>
            {
                ["install"] = InstallId, ["game"] = run.Game, ["cpu"] = spec.Cpu, ["gpu"] = spec.Gpu, ["ram"] = spec.Ram, ["res"] = spec.Res,
                ["avg"] = Math.Round(run.Stats.AvgFps, 1), ["low1"] = Math.Round(run.Stats.Low1, 1), ["version"] = "2.0.0",
            });
            using (var w = Client())
            {
                w.Headers[HttpRequestHeader.ContentType] = "application/json";
                w.UploadString(Endpoint + "/v1/runs", body);
            }
        }

        /// <summary>-> (то же железо и разрешение, та же видеокарта с любым процессором).</summary>
        public static (Similar same, Similar sameGpu) Compare(string game, Spec spec)
        {
            string q(string s) => Uri.EscapeDataString(s ?? "");
            string json;
            using (var w = Client())
                json = w.DownloadString($"{Endpoint}/v1/compare?game={q(game)}&cpu={q(spec.Cpu)}&gpu={q(spec.Gpu)}&res={q(spec.Res)}&install={InstallId}");
            var d = Json.DeserializeObject(json) as Dictionary<string, object>;
            Similar Read(string key)
            {
                var g = d?[key] as Dictionary<string, object>;
                if (g == null) return new Similar();
                double? N(string k) => g.TryGetValue(k, out var v) && v != null ? Convert.ToDouble(v) : (double?)null;
                return new Similar { Count = Convert.ToInt32(g["count"]), Avg = N("avg"), Low1 = N("low1") };
            }
            return (Read("same"), Read("sameGpu"));
        }
    }
}
