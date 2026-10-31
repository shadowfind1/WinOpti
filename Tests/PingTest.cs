using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WinOpti.Tests
{
    /// <summary>Сервер для замера: имя для человека и адрес.</summary>
    public sealed class PingTarget
    {
        public string Name, Address;
        public double FirstMs; // пинг при выборе ближайших
    }

    /// <summary>Итог серии пингов до одного адреса.</summary>
    public sealed class PingStats
    {
        public PingTarget Target;
        public int Sent, Lost;
        public double Avg, Min, Max, Jitter;
        public double LossPercent => Sent == 0 ? 0 : Lost * 100.0 / Sent;

        public static PingStats From(PingTarget t, List<double?> results)
        {
            var ok = results.Where(r => r.HasValue).Select(r => r.Value).ToList();
            var s = new PingStats { Target = t, Sent = results.Count, Lost = results.Count - ok.Count };
            if (ok.Count == 0) return s;
            s.Avg = ok.Average();
            s.Min = ok.Min();
            s.Max = ok.Max();
            // джиттер — средняя разница соседних пингов (как в RFC 3550): то, что игрок ощущает как «скачки»
            s.Jitter = ok.Count < 2 ? 0 : ok.Zip(ok.Skip(1), (a, b) => Math.Abs(a - b)).Average();
            return s;
        }
    }

    /// <summary>
    /// Пинг до игровых серверов. Серверы Valve (CS2 и Dota 2) берём из открытого списка Steam (GetSDRConfig):
    /// это те самые ретрансляторы, через которые идёт игра. Плюс роутер — чтобы отличить проблемы Wi-Fi от проблем интернета.
    /// </summary>
    public static class PingTest
    {
        static readonly Dictionary<string, string> CityRu = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sto"] = "Стокгольм", ["sto2"] = "Стокгольм", ["waw"] = "Варшава", ["fra"] = "Франкфурт", ["vie"] = "Вена", ["ams"] = "Амстердам",
            ["par"] = "Париж", ["lhr"] = "Лондон", ["mad"] = "Мадрид", ["hel"] = "Хельсинки", ["dxb"] = "Дубай", ["bom2"] = "Мумбаи",
            ["maa2"] = "Ченнаи", ["sgp"] = "Сингапур", ["hkg"] = "Гонконг", ["tyo"] = "Токио", ["seo"] = "Сеул", ["syd"] = "Сидней",
            ["jnb"] = "Йоханнесбург", ["gru"] = "Сан-Паулу", ["eze"] = "Буэнос-Айрес", ["scl"] = "Сантьяго", ["lim"] = "Лима",
            ["iad"] = "Вашингтон", ["atl"] = "Атланта", ["ord"] = "Чикаго", ["dfw"] = "Даллас", ["lax"] = "Лос-Анджелес", ["sea"] = "Сиэтл",
            ["dfra"] = "Франкфурт", ["dvie"] = "Вена", ["dlhr"] = "Лондон", ["dfjr"] = "Фуджейра",
        };

        /// <summary>Шлюз (роутер) и тип подключения основного сетевого адаптера.</summary>
        public static (string gateway, bool wifi, string adapter) Connection()
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
            {
                var gw = nic.GetIPProperties().GatewayAddresses.Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
                if (gw == null) continue;
                return (gw.ToString(), nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211, nic.Description);
            }
            return (null, false, null);
        }

        /// <summary>Ретрансляторы Valve: по одному адресу на город, только те, что отвечают на пинг.</summary>
        public static List<PingTarget> ValveRelays()
        {
            string json;
            using (var web = new WebClient())
            {
                web.Headers[HttpRequestHeader.UserAgent] = "WinOpti";
                json = web.DownloadString("https://api.steampowered.com/ISteamApps/GetSDRConfig/v1/?appid=730");
            }
            var root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as Dictionary<string, object>;
            var pops = root?["pops"] as Dictionary<string, object> ?? throw new InvalidOperationException("Steam вернул неожиданный ответ");
            var list = new List<PingTarget>();
            foreach (var kv in pops)
            {
                var pop = kv.Value as Dictionary<string, object>;
                if (!(pop?.TryGetValue("relays", out var r) == true && r is object[] relays && relays.Length > 0)) continue;
                var ip = (relays[0] as Dictionary<string, object>)?["ipv4"] as string;
                if (ip == null) continue;
                var desc = pop.TryGetValue("desc", out var d) ? d as string : kv.Key;
                list.Add(new PingTarget { Name = CityRu.TryGetValue(kv.Key, out var ru) ? ru : desc, Address = ip });
            }
            return list;
        }

        static double? Once(string address, int timeout = 1000)
        {
            try
            {
                using (var p = new Ping())
                {
                    var r = p.Send(address, timeout);
                    return r.Status == IPStatus.Success ? r.RoundtripTime : (double?)null;
                }
            }
            catch (PingException) { return null; }
        }

        /// <summary>Самые близкие серверы: каждый пингуем дважды параллельно, берём лучшие по разным городам.</summary>
        public static List<PingTarget> Nearest(List<PingTarget> all, int count)
        {
            Parallel.ForEach(all, new ParallelOptions { MaxDegreeOfParallelism = 16 }, t =>
            {
                var a = Once(t.Address, 800);
                var b = Once(t.Address, 800);
                t.FirstMs = new[] { a, b }.Where(x => x.HasValue).Select(x => x.Value).DefaultIfEmpty(double.MaxValue).Min();
            });
            return all.Where(t => t.FirstMs < double.MaxValue)
                      .GroupBy(t => t.Name).Select(g => g.OrderBy(t => t.FirstMs).First())
                      .OrderBy(t => t.FirstMs).Take(count).ToList();
        }

        /// <summary>Серия пингов ко всем целям одновременно: count штук с шагом intervalMs. progress — доля выполненного.</summary>
        public static List<PingStats> Series(List<PingTarget> targets, int count, int intervalMs, Action<double> progress, CancellationToken cancel)
        {
            var results = targets.Select(_ => new List<double?>()).ToList();
            for (int i = 0; i < count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var started = Environment.TickCount;
                Parallel.For(0, targets.Count, k =>
                {
                    var r = Once(targets[k].Address);
                    lock (results[k]) results[k].Add(r);
                });
                progress?.Invoke((i + 1.0) / count);
                int left = intervalMs - (Environment.TickCount - started);
                if (left > 0) Thread.Sleep(left);
            }
            return targets.Select((t, k) => PingStats.From(t, results[k])).ToList();
        }
    }
}
