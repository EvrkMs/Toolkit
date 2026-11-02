using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Toolkit
{
    /// <summary>
    /// Докачивает отсутствующие .ico для steam-ярлыков на рабочем столе. Имя файла в
    /// IconFile= — это хэш clienticon, поэтому appinfo.vdf разбирать не нужно: appid берётся
    /// из URL=steam://rungameid/N, хэш из имени иконки, файл качается с CDN Steam.
    /// </summary>
    internal static class SteamIcons
    {
        private const string C = "STEAMICO";

        private static readonly Regex AppIdRx = new Regex(@"^steam://rungameid/(\d+)\s*$", RegexOptions.IgnoreCase);
        private static readonly Regex HashRx = new Regex(@"^[0-9a-f]{40}$", RegexOptions.IgnoreCase);

        private static readonly string[] CdnBases =
        {
            "https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/",
            "https://media.steampowered.com/steamcommunity/public/images/apps/"
        };

        private sealed class Job
        {
            public string UrlFile;
            public string AppId;
            public string Hash;
            public string IconPath;
        }

        /// <summary>Сканирует папки и качает недостающие иконки параллельно. Результат — число скачанных.</summary>
        public static Task<int> EnsureAsync(IEnumerable<string> folders)
        {
            List<Job> jobs = Collect(folders);
            if (jobs.Count == 0) return Task.FromResult(0);

            Log.Write(C, "missing icons: " + jobs.Count);
            return Task.Run(delegate { return Download(jobs); });
        }

        private static List<Job> Collect(IEnumerable<string> folders)
        {
            List<Job> jobs = new List<Job>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;

                foreach (string file in Directory.GetFiles(folder, "*.url", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        Job job = Parse(file);
                        if (job != null && !File.Exists(job.IconPath) && seen.Add(job.IconPath)) jobs.Add(job);
                    }
                    catch (Exception ex)
                    {
                        Log.Write(C, "WARN parse " + file + " :: " + ex.Message);
                    }
                }
            }

            return jobs;
        }

        private static Job Parse(string urlFile)
        {
            string url = null;
            string icon = null;

            foreach (string raw in File.ReadAllLines(urlFile))
            {
                string line = raw.Trim();
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) url = line.Substring(4).Trim();
                else if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase)) icon = line.Substring(9).Trim();
            }

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(icon)) return null;

            Match m = AppIdRx.Match(url);
            if (!m.Success) return null;

            if (!string.Equals(Path.GetExtension(icon), ".ico", StringComparison.OrdinalIgnoreCase)) return null;

            string hash = Path.GetFileNameWithoutExtension(icon);
            if (!HashRx.IsMatch(hash)) return null;

            Job job = new Job();
            job.UrlFile = urlFile;
            job.AppId = m.Groups[1].Value;
            job.Hash = hash.ToLowerInvariant();
            job.IconPath = Environment.ExpandEnvironmentVariables(icon);
            return job;
        }

        /// <summary>
        /// Для всех установленных игр (appmanifest_*.acf во всех библиотеках) берёт clienticon
        /// из appcache\appinfo.vdf и докачивает недостающие &lt;хэш&gt;.ico в steam\games.
        /// Запускать там, где steam\games ссылается на общий диск — файлы окажутся на нём.
        /// </summary>
        public static int DownloadInstalled(string steamDir)
        {
            HashSet<string> installed = InstalledAppIds(steamDir);
            Log.Write(C, "installed apps: " + installed.Count);
            if (installed.Count == 0) return 0;

            string appInfo = Path.Combine(steamDir, "appcache", "appinfo.vdf");
            if (!File.Exists(appInfo))
            {
                Log.Write(C, "FAIL нет " + appInfo + " (запусти Steam хотя бы раз)");
                return 0;
            }

            Dictionary<string, string> icons;
            try
            {
                icons = ReadClientIcons(appInfo, installed);
            }
            catch (Exception ex)
            {
                Log.Write(C, "FAIL appinfo.vdf parse :: " + ex.Message);
                return 0;
            }

            string gamesDir = Path.Combine(steamDir, "steam", "games");
            List<Job> jobs = new List<Job>();
            int present = 0;
            foreach (KeyValuePair<string, string> kv in icons)
            {
                Job job = new Job();
                job.AppId = kv.Key;
                job.Hash = kv.Value.ToLowerInvariant();
                job.IconPath = Path.Combine(gamesDir, job.Hash + ".ico");

                if (File.Exists(job.IconPath)) present++;
                else jobs.Add(job);
            }

            Log.Write(C, "clienticon found=" + icons.Count + "/" + installed.Count +
                         " already=" + present + " missing=" + jobs.Count + " -> " + gamesDir);

            return jobs.Count == 0 ? 0 : Download(jobs);
        }

        private static HashSet<string> InstalledAppIds(string steamDir)
        {
            HashSet<string> ids = new HashSet<string>();
            List<string> libraries = new List<string>();
            libraries.Add(steamDir);

            string vdf = Path.Combine(steamDir, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                }
            }

            foreach (string lib in libraries)
            {
                string apps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;

                foreach (string acf in Directory.GetFiles(apps, "appmanifest_*.acf"))
                {
                    Match m = Regex.Match(Path.GetFileName(acf), @"appmanifest_(\d+)\.acf", RegexOptions.IgnoreCase);
                    if (m.Success) ids.Add(m.Groups[1].Value);
                }
            }

            return ids;
        }

        /// <summary>Бинарный appinfo.vdf (v27/v28/v29): appid -> appinfo.common.clienticon.</summary>
        private static Dictionary<string, string> ReadClientIcons(string path, HashSet<string> wanted)
        {
            byte[] file;
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (MemoryStream ms = new MemoryStream())
            {
                fs.CopyTo(ms);
                file = ms.ToArray();
            }

            Dictionary<string, string> result = new Dictionary<string, string>();

            using (BinaryReader r = new BinaryReader(new MemoryStream(file)))
            {
                uint magic = r.ReadUInt32();
                uint version = magic & 0xFF;
                if ((magic >> 8) != 0x075644 || version < 0x27 || version > 0x29)
                {
                    throw new InvalidDataException("unknown appinfo magic 0x" + magic.ToString("X8"));
                }

                r.ReadUInt32(); // universe

                string[] table = null;
                if (version >= 0x29)
                {
                    long tableOffset = r.ReadInt64();
                    long dataStart = r.BaseStream.Position;
                    r.BaseStream.Position = tableOffset;
                    uint count = r.ReadUInt32();
                    table = new string[count];
                    for (uint i = 0; i < count; i++) table[i] = ReadCString(r);
                    r.BaseStream.Position = dataStart;
                }

                // До sha1 текста: infoState, lastUpdated, picsToken, sha1, changeNumber (+ binary sha1 с v28).
                int headerBytes = version >= 0x28 ? 60 : 40;

                while (true)
                {
                    uint appId = r.ReadUInt32();
                    if (appId == 0) break;

                    uint size = r.ReadUInt32();
                    long next = r.BaseStream.Position + size;

                    if (wanted.Contains(appId.ToString()))
                    {
                        r.BaseStream.Position += headerBytes;
                        string icon = FindClientIcon(r, table);
                        if (!string.IsNullOrEmpty(icon)) result[appId.ToString()] = icon;
                    }

                    r.BaseStream.Position = next;
                }
            }

            return result;
        }

        private static string FindClientIcon(BinaryReader r, string[] table)
        {
            List<string> stack = new List<string>();
            string found = null;
            ReadKv(r, table, stack, ref found);
            return found;
        }

        private static void ReadKv(BinaryReader r, string[] table, List<string> stack, ref string found)
        {
            while (true)
            {
                byte type = r.ReadByte();
                if (type == 8) return;

                string key = table != null ? table[r.ReadUInt32()] : ReadCString(r);

                switch (type)
                {
                    case 0:
                        stack.Add(key);
                        ReadKv(r, table, stack, ref found);
                        stack.RemoveAt(stack.Count - 1);
                        break;
                    case 1:
                        string value = ReadCString(r);
                        if (found == null && key == "clienticon" && stack.Count > 0 && stack[stack.Count - 1] == "common")
                        {
                            found = value;
                        }
                        break;
                    case 2:
                    case 3:
                    case 4:
                    case 6:
                        r.ReadBytes(4);
                        break;
                    case 5:
                        while (r.ReadUInt16() != 0) { }
                        break;
                    case 7:
                    case 10:
                        r.ReadBytes(8);
                        break;
                    default:
                        throw new InvalidDataException("unknown kv type " + type);
                }
            }
        }

        private static string ReadCString(BinaryReader r)
        {
            List<byte> bytes = new List<byte>();
            byte b;
            while ((b = r.ReadByte()) != 0) bytes.Add(b);
            return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
        }

        private static int Download(List<Job> jobs)
        {
            // Старый .NET Framework по умолчанию без TLS 1.2, а CDN другого не принимает.
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;

            int ok = 0;
            Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = 4 }, delegate(Job job)
            {
                if (TryDownload(job)) System.Threading.Interlocked.Increment(ref ok);
            });

            Log.Write(C, "downloaded " + ok + "/" + jobs.Count);
            return ok;
        }

        private static bool TryDownload(Job job)
        {
            foreach (string cdn in CdnBases)
            {
                string address = cdn + job.AppId + "/" + job.Hash + ".ico";
                try
                {
                    byte[] data;
                    using (WebClient client = new WebClient())
                    {
                        data = client.DownloadData(address);
                    }

                    // Заголовок ICO: 00 00 01 00. Иначе это страница ошибки, а не иконка.
                    if (data.Length < 22 || data[0] != 0 || data[1] != 0 || data[2] != 1 || data[3] != 0)
                    {
                        Log.Write(C, "WARN not an icon: " + address);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(job.IconPath));
                    string temp = job.IconPath + ".part";
                    File.WriteAllBytes(temp, data);
                    if (File.Exists(job.IconPath)) File.Delete(job.IconPath);
                    File.Move(temp, job.IconPath);

                    Log.Write(C, "OK app=" + job.AppId + " -> " + job.IconPath);
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Write(C, "WARN " + address + " :: " + ex.Message);
                }
            }

            Log.Write(C, "FAIL app=" + job.AppId + " (" + Path.GetFileName(job.UrlFile) + ")");
            return false;
        }
    }
}
