using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Microsoft.Win32;

namespace Toolkit
{
    internal static class TaskbarVerb
    {
        private const string TaskbandRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband";

        /// <summary>Состояние панели. Расхождение здесь требует перезапуска explorer.</summary>
        private static readonly string[] SignificantValues = new[]
        {
            "Favorites",
            "FavoritesResolve",
            "FavoritesVersion"
        };

        /// <summary>
        /// Счётчики событий: Explorer инкрементирует их при каждом откреплении,
        /// на отображение они не влияют. Пишем для полноты снимка, но перезапуск
        /// из-за них не запрашиваем — иначе explorer перезагружается на каждой
        /// загрузке при полностью исправной панели.
        /// </summary>
        private static readonly string[] CosmeticValues = new[]
        {
            "FavoritesChanges",
            "FavoritesRemovedChanges"
        };

        private static readonly string[] AllowedExtensions = new[] { ".lnk", ".url", ".website" };

        private static string PinnedDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
            }
        }

        public static int Save()
        {
            Directory.CreateDirectory(Paths.ProfilesDir);
            Directory.CreateDirectory(PinnedDir);

            Snapshot snapshot = new Snapshot();
            snapshot.CreatedAt = DateTime.Now;
            snapshot.PinnedFiles = CapturePinnedFiles();
            snapshot.TaskbandValues = CaptureTaskbandValues();

            Json.Write(Paths.TaskbarProfile, snapshot);
            Log.Write("TASKBAR", "saved " + Paths.TaskbarProfile +
                                 " files=" + snapshot.PinnedFiles.Count +
                                 " taskbandValues=" + snapshot.TaskbandValues.Count);
            return 0;
        }

        public static int Load()
        {
            if (!File.Exists(Paths.TaskbarProfile))
            {
                Log.Write("TASKBAR", "no profile: " + Paths.TaskbarProfile);
                return ExitCode.Failed;
            }

            Snapshot snapshot = Json.Read<Snapshot>(Paths.TaskbarProfile);
            if (snapshot == null)
            {
                Log.Write("TASKBAR", "profile parse failed: " + Paths.TaskbarProfile);
                return ExitCode.Failed;
            }

            Directory.CreateDirectory(PinnedDir);

            List<PinnedFile> pinnedFiles = snapshot.PinnedFiles ?? new List<PinnedFile>();
            List<TaskbandValue> taskbandValues = snapshot.TaskbandValues ?? new List<TaskbandValue>();

            int removedExtra = RemoveExtraPinnedFiles(pinnedFiles);

            int restoredMissing;
            int refreshedContent;
            RestorePinnedFiles(pinnedFiles, out restoredMissing, out refreshedContent);

            int regRemoved;
            int regSignificant;
            int regCosmetic;
            RestoreTaskbandValues(taskbandValues, out regRemoved, out regSignificant, out regCosmetic);

            int significant = removedExtra + restoredMissing + regRemoved + regSignificant;
            if (significant > 0)
            {
                ExplorerRestart.Request("taskbar layout changed");
            }

            Log.Write("TASKBAR", "loaded " + Paths.TaskbarProfile +
                                 " restoredMissing=" + restoredMissing +
                                 " refreshedContent=" + refreshedContent +
                                 " removedExtra=" + removedExtra +
                                 " regRemoved=" + regRemoved +
                                 " regSignificant=" + regSignificant +
                                 " regCosmetic=" + regCosmetic +
                                 " needsRestart=" + (significant > 0));
            return 0;
        }

        private static List<PinnedFile> CapturePinnedFiles()
        {
            List<PinnedFile> result = new List<PinnedFile>();
            if (!Directory.Exists(PinnedDir)) return result;

            List<string> files = new List<string>(Directory.GetFiles(PinnedDir, "*", SearchOption.TopDirectoryOnly));
            files.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (string file in files)
            {
                if (!IsAllowed(file)) continue;

                PinnedFile item = new PinnedFile();
                item.FileName = Path.GetFileName(file);
                item.ContentBase64 = Convert.ToBase64String(File.ReadAllBytes(file));
                result.Add(item);
            }

            return result;
        }

        private static int RemoveExtraPinnedFiles(List<PinnedFile> expectedFiles)
        {
            if (!Directory.Exists(PinnedDir)) return 0;

            HashSet<string> expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PinnedFile file in expectedFiles)
            {
                if (!string.IsNullOrWhiteSpace(file.FileName)) expected.Add(file.FileName);
            }

            int removed = 0;
            foreach (string path in Directory.GetFiles(PinnedDir, "*", SearchOption.TopDirectoryOnly))
            {
                if (!IsAllowed(path)) continue;
                if (expected.Contains(Path.GetFileName(path))) continue;

                try
                {
                    File.Delete(path);
                    removed++;
                    Log.Write("TASKBAR", "removed extra: " + Path.GetFileName(path));
                }
                catch (Exception ex)
                {
                    Log.Write("TASKBAR", "WARN remove failed: " + path + " :: " + ex.Message);
                }
            }

            return removed;
        }

        private static void RestorePinnedFiles(List<PinnedFile> files, out int restoredMissing, out int refreshedContent)
        {
            restoredMissing = 0;
            refreshedContent = 0;

            foreach (PinnedFile file in files)
            {
                if (string.IsNullOrWhiteSpace(file.FileName) || string.IsNullOrWhiteSpace(file.ContentBase64))
                {
                    continue;
                }

                string path = Path.Combine(PinnedDir, file.FileName);

                byte[] content;
                try
                {
                    content = Convert.FromBase64String(file.ContentBase64);
                }
                catch
                {
                    Log.Write("TASKBAR", "WARN invalid base64: " + file.FileName);
                    continue;
                }

                try
                {
                    bool existed = File.Exists(path);
                    if (existed && Bytes.Equal(File.ReadAllBytes(path), content))
                    {
                        continue;
                    }

                    File.WriteAllBytes(path, content);

                    if (existed)
                    {
                        // Ярлык на месте, но байты разошлись: Windows переписывает
                        // внутри .lnk трекинг-данные (LinkTrackerDataBlock) при резолве
                        // цели. Состав панели при этом не менялся — рестарт не нужен.
                        refreshedContent++;
                        Log.Write("TASKBAR", "refreshed content: " + file.FileName);
                    }
                    else
                    {
                        restoredMissing++;
                        Log.Write("TASKBAR", "restored missing: " + file.FileName);
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("TASKBAR", "WARN restore failed: " + path + " :: " + ex.Message);
                }
            }
        }

        private static List<TaskbandValue> CaptureTaskbandValues()
        {
            List<TaskbandValue> result = new List<TaskbandValue>();

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(TaskbandRegistryPath, false))
            {
                if (key == null) return result;

                foreach (string name in TrackedValueNames())
                {
                    object value = key.GetValue(name);
                    if (value == null) continue;

                    TaskbandValue item = CreateTaskbandValue(name, key.GetValueKind(name), value);
                    if (item != null) result.Add(item);
                }
            }

            return result;
        }

        private static void RestoreTaskbandValues(
            List<TaskbandValue> values,
            out int removedCount,
            out int significantCount,
            out int cosmeticCount)
        {
            removedCount = 0;
            significantCount = 0;
            cosmeticCount = 0;

            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(TaskbandRegistryPath))
            {
                if (key == null)
                {
                    Log.Write("TASKBAR", "WARN cannot open taskband key for write");
                    return;
                }

                HashSet<string> expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (TaskbandValue value in values)
                {
                    if (!string.IsNullOrWhiteSpace(value.Name)) expected.Add(value.Name);
                }

                foreach (string existingName in key.GetValueNames())
                {
                    if (!existingName.StartsWith("Favorites", StringComparison.OrdinalIgnoreCase)) continue;
                    if (expected.Contains(existingName)) continue;

                    key.DeleteValue(existingName, false);
                    removedCount++;
                    Log.Write("TASKBAR", "removed extra taskband value: " + existingName);
                }

                foreach (TaskbandValue value in values)
                {
                    if (string.IsNullOrWhiteSpace(value.Name)) continue;

                    RegistryValueKind kind;
                    object rawValue;
                    if (!TryGetRawRegistryValue(value, out kind, out rawValue)) continue;
                    if (!NeedsUpdate(key, value.Name, kind, rawValue)) continue;

                    key.SetValue(value.Name, rawValue, kind);

                    if (IsCosmetic(value.Name))
                    {
                        cosmeticCount++;
                        Log.Write("TASKBAR", "updated counter (no restart): " + value.Name);
                    }
                    else
                    {
                        significantCount++;
                        Log.Write("TASKBAR", "updated taskband value: " + value.Name);
                    }
                }
            }
        }

        private static IEnumerable<string> TrackedValueNames()
        {
            foreach (string name in SignificantValues) yield return name;
            foreach (string name in CosmeticValues) yield return name;
        }

        private static bool IsCosmetic(string name)
        {
            foreach (string cosmetic in CosmeticValues)
            {
                if (string.Equals(cosmetic, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool IsAllowed(string path)
        {
            string ext = Path.GetExtension(path);
            foreach (string allowed in AllowedExtensions)
            {
                if (string.Equals(allowed, ext, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool NeedsUpdate(RegistryKey key, string name, RegistryValueKind expectedKind, object expectedValue)
        {
            object existing = key.GetValue(name);
            if (existing == null) return true;
            if (key.GetValueKind(name) != expectedKind) return true;

            if (expectedKind == RegistryValueKind.Binary)
            {
                return !Bytes.Equal(existing as byte[], expectedValue as byte[]);
            }

            if (expectedKind == RegistryValueKind.MultiString)
            {
                string[] left = existing as string[] ?? new string[0];
                string[] right = expectedValue as string[] ?? new string[0];
                if (left.Length != right.Length) return true;
                for (int i = 0; i < left.Length; i++)
                {
                    if (!string.Equals(left[i], right[i], StringComparison.Ordinal)) return true;
                }
                return false;
            }

            return !object.Equals(existing, expectedValue);
        }

        private static TaskbandValue CreateTaskbandValue(string name, RegistryValueKind kind, object value)
        {
            TaskbandValue item = new TaskbandValue();
            item.Name = name;
            item.Kind = kind.ToString();

            switch (kind)
            {
                case RegistryValueKind.Binary:
                    item.BinaryBase64 = Convert.ToBase64String((byte[])value);
                    return item;
                case RegistryValueKind.DWord:
                    item.DwordValue = Convert.ToInt32(value);
                    return item;
                case RegistryValueKind.QWord:
                    item.QwordValue = Convert.ToInt64(value);
                    return item;
                case RegistryValueKind.String:
                case RegistryValueKind.ExpandString:
                    item.StringValue = Convert.ToString(value);
                    return item;
                case RegistryValueKind.MultiString:
                    item.MultiStringValue = value as string[] ?? new string[0];
                    return item;
                default:
                    Log.Write("TASKBAR", "WARN unsupported registry kind for " + name + ": " + kind);
                    return null;
            }
        }

        private static bool TryGetRawRegistryValue(TaskbandValue item, out RegistryValueKind kind, out object value)
        {
            kind = RegistryValueKind.Binary;
            value = null;

            RegistryValueKind parsedKind;
            if (!TryParseKind(item.Kind, out parsedKind))
            {
                Log.Write("TASKBAR", "WARN invalid registry kind in profile: " + item.Name + " kind=" + item.Kind);
                return false;
            }

            kind = parsedKind;
            switch (parsedKind)
            {
                case RegistryValueKind.Binary:
                    value = string.IsNullOrWhiteSpace(item.BinaryBase64)
                        ? new byte[0]
                        : Convert.FromBase64String(item.BinaryBase64);
                    return true;
                case RegistryValueKind.DWord:
                    value = item.DwordValue.HasValue ? item.DwordValue.Value : 0;
                    return true;
                case RegistryValueKind.QWord:
                    value = item.QwordValue.HasValue ? item.QwordValue.Value : 0L;
                    return true;
                case RegistryValueKind.String:
                case RegistryValueKind.ExpandString:
                    value = item.StringValue ?? string.Empty;
                    return true;
                case RegistryValueKind.MultiString:
                    value = item.MultiStringValue ?? new string[0];
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryParseKind(string raw, out RegistryValueKind kind)
        {
            kind = RegistryValueKind.Unknown;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            try
            {
                kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), raw, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        [DataContract]
        internal sealed class Snapshot
        {
            [DataMember(Name = "createdAt")]
            public DateTime CreatedAt { get; set; }

            [DataMember(Name = "pinnedFiles")]
            public List<PinnedFile> PinnedFiles { get; set; }

            [DataMember(Name = "taskbandValues")]
            public List<TaskbandValue> TaskbandValues { get; set; }
        }

        [DataContract]
        internal sealed class PinnedFile
        {
            [DataMember(Name = "fileName")]
            public string FileName { get; set; }

            [DataMember(Name = "contentBase64")]
            public string ContentBase64 { get; set; }
        }

        [DataContract]
        internal sealed class TaskbandValue
        {
            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "kind")]
            public string Kind { get; set; }

            [DataMember(Name = "binaryBase64")]
            public string BinaryBase64 { get; set; }

            [DataMember(Name = "dwordValue")]
            public int? DwordValue { get; set; }

            [DataMember(Name = "qwordValue")]
            public long? QwordValue { get; set; }

            [DataMember(Name = "stringValue")]
            public string StringValue { get; set; }

            [DataMember(Name = "multiStringValue")]
            public string[] MultiStringValue { get; set; }
        }
    }
}
