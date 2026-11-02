using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Toolkit
{
    internal static class ShortcutsVerb
    {
        // .url (интернет-ярлыки) — обычные текстовые файлы, копируются как есть, как и .lnk.
        private static readonly string[] ShortcutPatterns = new[] { "*.lnk", "*.url" };

        // Профили до версии 2 .url не содержали: чистить по ним .url нельзя, иначе все
        // url-ярлыки на столе были бы удалены как «лишние».
        private const int ProfileVersion = 2;

        // Сколько максимум ждём докачки иконок Steam, чтобы не задерживать раскладку.
        private const int SteamIconsWaitMs = 30000;

        private static IEnumerable<string> ListShortcutFiles(string folder)
        {
            foreach (string pattern in ShortcutPatterns)
            {
                foreach (string file in Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly))
                {
                    // Маска "*.url" по правилам 8.3 может зацепить "*.urlx" — проверяем точно.
                    string ext = Path.GetExtension(file);
                    if (string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ext, ".url", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return file;
                    }
                }
            }
        }

        private static bool IsUrlFile(string fileName)
        {
            return string.Equals(Path.GetExtension(fileName), ".url", StringComparison.OrdinalIgnoreCase);
        }

        public static int Save(Cli cli)
        {
            Directory.CreateDirectory(Paths.ShortcutProfilesDir);

            IntPtr listView;
            List<DesktopItem> desktopItems;
            bool stabilized;

            // Снимок недостроенного стола записал бы профиль с половиной иконок,
            // и этот обрезанный профиль потом разъехался бы по всем машинам.
            if (!DesktopListView.TryGetStableItems(10000, out listView, out desktopItems, out stabilized))
            {
                Log.Write("SHORTCUT", "desktop ListView not found or empty");
                return 10;
            }

            if (!stabilized)
            {
                Log.Write("SHORTCUT", "ОТМЕНА save: рабочий стол не устоялся, профиль мог получиться неполным");
                return 10;
            }

            // Разрешение всегда берётся с текущего экрана; --resolution нужен только
            // чтобы сохранить тот же снимок под другим именем профиля.
            string actual = NativeMethods.GetScreenResolution();
            string target = cli == null ? actual : cli.Option("--resolution", actual);

            Snapshot snapshot = new Snapshot();
            snapshot.Version = ProfileVersion;
            snapshot.Resolution = target;
            snapshot.CreatedAt = DateTime.Now;
            snapshot.Shortcuts = CaptureShortcuts();
            snapshot.IconPositions = CapturePositions(desktopItems);

            string path = ProfilePath(target);
            Json.Write(path, snapshot);

            Log.Write("SHORTCUT", "saved " + path +
                                  " shortcuts=" + snapshot.Shortcuts.Count +
                                  " icons=" + snapshot.IconPositions.Count);

            if (!string.Equals(target, actual, StringComparison.OrdinalIgnoreCase))
            {
                Log.Write("SHORTCUT", "WARN снято на " + actual + ", сохранено как " + target +
                                      ": координаты остались пиксельными от " + actual);
            }

            return 0;
        }

        public static int Load(Cli cli)
        {
            Directory.CreateDirectory(Paths.ShortcutProfilesDir);

            string resolution = NativeMethods.GetScreenResolution();
            if (cli != null) resolution = cli.Option("--resolution", resolution);

            string profilePath = ProfilePath(resolution);
            if (!File.Exists(profilePath))
            {
                string[] candidates = Directory.GetFiles(Paths.ShortcutProfilesDir, "*.json");
                string fallback = candidates.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (fallback == null)
                {
                    Log.Write("SHORTCUT", "no profile for " + resolution + " and no fallback");
                    return 10;
                }

                profilePath = fallback;
                Log.Write("SHORTCUT", "no profile for " + resolution + ", using fallback: " + profilePath);
            }

            Snapshot snapshot = Json.Read<Snapshot>(profilePath);
            if (snapshot == null)
            {
                Log.Write("SHORTCUT", "profile parse failed: " + profilePath);
                return 10;
            }

            List<ShortcutData> shortcuts = snapshot.Shortcuts ?? new List<ShortcutData>();

            int removed = RemoveExtra(shortcuts, snapshot.Version >= ProfileVersion);
            int restored = Restore(shortcuts);

            // Иконки качаются параллельно с обновлением рабочего стола, до раскладки иконок:
            // обновление после раскладки могло бы сдвинуть уже расставленные позиции.
            Task<int> iconJob = SteamIcons.EnsureAsync(new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            });

            if (removed > 0 || restored > 0)
            {
                DesktopListView.Refresh();
                Thread.Sleep(400);
            }

            int icons = 0;
            try
            {
                if (iconJob.Wait(SteamIconsWaitMs)) icons = iconJob.Result;
                else Log.Write("SHORTCUT", "WARN иконки Steam не докачались за " + (SteamIconsWaitMs / 1000) + " c, продолжаем без ожидания");
            }
            catch (Exception ex)
            {
                Log.Write("SHORTCUT", "WARN steam icons: " + ex.Message);
            }

            if (icons > 0)
            {
                DesktopListView.Refresh();
                Thread.Sleep(400);
            }

            ApplyPositions(snapshot.IconPositions ?? new List<IconPosition>());

            Log.Write("SHORTCUT", "loaded " + profilePath + " restored=" + restored + " removedExtra=" + removed);
            return 0;
        }

        private static string ProfilePath(string resolution)
        {
            return Path.Combine(Paths.ShortcutProfilesDir, resolution + ".json");
        }

        private static List<ShortcutData> CaptureShortcuts()
        {
            List<ShortcutData> result = new List<ShortcutData>();
            string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

            CaptureFromFolder(userDesktop, "user", result);
            if (!string.Equals(userDesktop, commonDesktop, StringComparison.OrdinalIgnoreCase))
            {
                CaptureFromFolder(commonDesktop, "common", result);
            }

            return result;
        }

        private static void CaptureFromFolder(string folder, string source, List<ShortcutData> result)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;

            foreach (string file in ListShortcutFiles(folder))
            {
                ShortcutData item = new ShortcutData();
                item.Name = Path.GetFileNameWithoutExtension(file);
                item.FileName = Path.GetFileName(file);
                item.Source = source;
                item.ContentBase64 = Convert.ToBase64String(File.ReadAllBytes(file));
                result.Add(item);
            }
        }

        private static int Restore(List<ShortcutData> shortcuts)
        {
            string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            int restored = 0;

            foreach (ShortcutData item in shortcuts)
            {
                if (string.IsNullOrWhiteSpace(item.FileName) || string.IsNullOrWhiteSpace(item.ContentBase64)) continue;

                string primaryDir = string.Equals(item.Source, "common", StringComparison.OrdinalIgnoreCase)
                    ? commonDesktop
                    : userDesktop;

                string primaryPath = Path.Combine(primaryDir, item.FileName);
                string fallbackPath = Path.Combine(userDesktop, item.FileName);

                if (File.Exists(primaryPath) || File.Exists(fallbackPath)) continue;

                byte[] content;
                try
                {
                    content = Convert.FromBase64String(item.ContentBase64);
                }
                catch
                {
                    Log.Write("SHORTCUT", "WARN invalid base64: " + item.FileName);
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(primaryDir);
                    File.WriteAllBytes(primaryPath, content);
                    Log.Write("SHORTCUT", "restored: " + primaryPath);
                    restored++;
                }
                catch
                {
                    try
                    {
                        File.WriteAllBytes(fallbackPath, content);
                        Log.Write("SHORTCUT", "restored to fallback: " + fallbackPath);
                        restored++;
                    }
                    catch (Exception ex)
                    {
                        Log.Write("SHORTCUT", "WARN restore failed: " + item.FileName + " :: " + ex.Message);
                    }
                }
            }

            return restored;
        }

        private static int RemoveExtra(List<ShortcutData> shortcuts, bool includeUrl)
        {
            string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

            HashSet<string> allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ShortcutData item in shortcuts)
            {
                if (!string.IsNullOrWhiteSpace(item.FileName)) allowed.Add(item.FileName);
            }

            int removed = RemoveExtraFrom(userDesktop, allowed, includeUrl);
            if (!string.Equals(userDesktop, commonDesktop, StringComparison.OrdinalIgnoreCase))
            {
                removed += RemoveExtraFrom(commonDesktop, allowed, includeUrl);
            }

            return removed;
        }

        private static int RemoveExtraFrom(string folder, HashSet<string> allowed, bool includeUrl)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return 0;

            int removed = 0;
            foreach (string file in ListShortcutFiles(folder))
            {
                if (!includeUrl && IsUrlFile(file)) continue;
                if (allowed.Contains(Path.GetFileName(file))) continue;

                try
                {
                    File.Delete(file);
                    removed++;
                    Log.Write("SHORTCUT", "removed extra: " + file);
                }
                catch (Exception ex)
                {
                    Log.Write("SHORTCUT", "WARN remove failed: " + file + " :: " + ex.Message);
                }
            }

            return removed;
        }

        private static List<IconPosition> CapturePositions(List<DesktopItem> items)
        {
            List<IconPosition> result = new List<IconPosition>();
            foreach (DesktopItem item in items)
            {
                IconPosition p = new IconPosition();
                p.Index = item.Index;
                p.Name = item.Name;
                p.X = item.X;
                p.Y = item.Y;
                result.Add(p);
            }
            return result;
        }

        private static void ApplyPositions(List<IconPosition> positions)
        {
            if (positions.Count == 0)
            {
                Log.Write("SHORTCUT", "applied positions: 0 (empty profile)");
                return;
            }

            IntPtr listView;
            List<DesktopItem> readyItems;
            bool stabilized;

            // 20 c: после перезапуска explorer на холодной PXE-загрузке стол
            // наполняется заметно медленнее, чем в уже прогретой сессии.
            if (!DesktopListView.TryGetStableItems(20000, out listView, out readyItems, out stabilized))
            {
                Log.Write("SHORTCUT", "WARN desktop icons list empty, explorer desktop not ready");
                return;
            }

            int itemCount = readyItems.Count;

            bool wasAutoArrange = DesktopListView.DisableAutoArrange(listView);
            if (wasAutoArrange) Thread.Sleep(120);

            int nameCandidates = positions.Count(p => !string.IsNullOrWhiteSpace(p.Name));
            bool hasUsableName = nameCandidates > 0;
            int distinctIndexes = positions.Select(p => p.Index).Distinct().Count();
            bool hasUsableIndex = distinctIndexes > (positions.Count / 2);

            Log.Write("SHORTCUT", "apply input: positions=" + positions.Count +
                                  " desktopItems=" + itemCount +
                                  " settled=" + stabilized +
                                  " names=" + nameCandidates +
                                  " distinctIndexes=" + distinctIndexes +
                                  " autoArrangeWasOn=" + wasAutoArrange);

            int moved = 0;
            int movedByName = 0;
            int movedByIndex = 0;
            int skipped = 0;

            // Два прохода: после первого Explorer может пересортировать элементы,
            // и индексы, добытые до перемещения, успевают устареть.
            for (int pass = 1; pass <= 2; pass++)
            {
                try
                {
                    List<DesktopItem> currentItems;
                    if (!DesktopListView.TryGetItems(2500, out listView, out currentItems))
                    {
                        Thread.Sleep(220);
                        continue;
                    }

                    itemCount = currentItems.Count;
                    Dictionary<string, Queue<int>> nameToIndexes = BuildNameIndex(currentItems);

                    int passMoved = 0;
                    int passByName = 0;
                    int passByIndex = 0;
                    int passSkipped = 0;

                    foreach (IconPosition pos in positions)
                    {
                        int idx = -1;
                        bool byName = false;

                        if (hasUsableName)
                        {
                            idx = TakeIndexByName(nameToIndexes, pos.Name);
                            byName = idx >= 0;
                        }

                        if (idx < 0 && hasUsableIndex) idx = pos.Index;

                        if (idx < 0 || idx >= itemCount)
                        {
                            passSkipped++;
                            continue;
                        }

                        if (DesktopListView.SetPosition(listView, idx, pos.X, pos.Y))
                        {
                            passMoved++;
                            if (byName) passByName++; else passByIndex++;
                        }
                    }

                    moved = passMoved;
                    movedByName = passByName;
                    movedByIndex = passByIndex;
                    skipped = passSkipped;

                    Thread.Sleep(180);
                }
                catch (Exception ex)
                {
                    Log.Write("SHORTCUT", "WARN apply pass failed: " + ex.Message);
                }
            }

            Log.Write("SHORTCUT", "applied positions: " + moved +
                                  " byName=" + movedByName +
                                  " byIndexFallback=" + movedByIndex +
                                  " skipped=" + skipped);

            Verify(positions);
        }

        private static void Verify(List<IconPosition> expected)
        {
            IntPtr listView;
            List<DesktopItem> current;
            if (!DesktopListView.TryGetItems(3000, out listView, out current))
            {
                Log.Write("SHORTCUT", "verify positions: matched=0/0");
                return;
            }

            Dictionary<string, DesktopItem> byName = new Dictionary<string, DesktopItem>(StringComparer.OrdinalIgnoreCase);
            foreach (DesktopItem item in current)
            {
                string key = NormalizeName(item.Name);
                if (!byName.ContainsKey(key)) byName[key] = item;
            }

            int checkedCount = 0;
            int matched = 0;
            const int tolerance = 4;

            foreach (IconPosition pos in expected)
            {
                if (string.IsNullOrWhiteSpace(pos.Name)) continue;

                DesktopItem item;
                if (!byName.TryGetValue(NormalizeName(pos.Name), out item)) continue;

                checkedCount++;
                if (Math.Abs(item.X - pos.X) <= tolerance && Math.Abs(item.Y - pos.Y) <= tolerance) matched++;
            }

            Log.Write("SHORTCUT", "verify positions: matched=" + matched + "/" + checkedCount);

            if (checkedCount > 0 && matched < (checkedCount / 2))
            {
                Log.Write("SHORTCUT", "WARN иконки не встали на места: проверь авто-упорядочивание и выравнивание по сетке");
            }
        }

        private static Dictionary<string, Queue<int>> BuildNameIndex(List<DesktopItem> items)
        {
            Dictionary<string, Queue<int>> map = new Dictionary<string, Queue<int>>(StringComparer.OrdinalIgnoreCase);

            foreach (DesktopItem item in items)
            {
                string key = NormalizeName(item.Name);
                if (string.IsNullOrWhiteSpace(key)) continue;

                Queue<int> indexes;
                if (!map.TryGetValue(key, out indexes))
                {
                    indexes = new Queue<int>();
                    map[key] = indexes;
                }

                indexes.Enqueue(item.Index);
            }

            return map;
        }

        private static int TakeIndexByName(Dictionary<string, Queue<int>> map, string name)
        {
            if (map == null || string.IsNullOrWhiteSpace(name)) return -1;

            Queue<int> indexes;
            if (!map.TryGetValue(NormalizeName(name), out indexes)) return -1;
            if (indexes.Count == 0) return -1;

            return indexes.Dequeue();
        }

        private static string NormalizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return value.Trim().TrimEnd('.');
        }

        [DataContract]
        internal sealed class Snapshot
        {
            [DataMember(Name = "version")]
            public int Version { get; set; }

            [DataMember(Name = "resolution")]
            public string Resolution { get; set; }

            [DataMember(Name = "createdAt")]
            public DateTime CreatedAt { get; set; }

            [DataMember(Name = "shortcuts")]
            public List<ShortcutData> Shortcuts { get; set; }

            [DataMember(Name = "iconPositions")]
            public List<IconPosition> IconPositions { get; set; }
        }

        [DataContract]
        internal sealed class ShortcutData
        {
            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "fileName")]
            public string FileName { get; set; }

            [DataMember(Name = "source")]
            public string Source { get; set; }

            [DataMember(Name = "contentBase64")]
            public string ContentBase64 { get; set; }
        }

        [DataContract]
        internal sealed class IconPosition
        {
            [DataMember(Name = "index")]
            public int Index { get; set; }

            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "x")]
            public int X { get; set; }

            [DataMember(Name = "y")]
            public int Y { get; set; }
        }
    }
}
