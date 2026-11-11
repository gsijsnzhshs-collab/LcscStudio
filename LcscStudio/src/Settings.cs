using System;
using System.IO;
using LcscLoader;
using Newtonsoft.Json;

namespace LcscStudio
{
    internal sealed class AppSettings
    {
        public string NewLibraryFolder = DefaultLibraryFolder();
        public string NewLibraryName = "LCSC";
        public bool MergeIntoExisting;
        public string MergeSchLib = "";
        public string MergePcbLib = "";
        public bool ImportSymbol = true;
        public bool ImportFootprint = true;
        public bool ImportModel = true;
        public bool Overwrite = true;
        public bool PlaceAfterImport;
        public int SearchPageSize = 30;

        public AppSettings Clone()
        {
            return (AppSettings)MemberwiseClone();
        }

        public static string DefaultLibraryFolder()
        {
            return StoragePaths.DefaultLibraryFolder;
        }

        public string NewSchLibPath
        {
            get
            {
                var name = string.IsNullOrWhiteSpace(NewLibraryName) ? "LCSC" : NewLibraryName.Trim();
                return Path.Combine(NewLibraryFolder, name + ".SchLib");
            }
        }

        public string NewPcbLibPath
        {
            get
            {
                var name = string.IsNullOrWhiteSpace(NewLibraryName) ? "LCSC" : NewLibraryName.Trim();
                return Path.Combine(NewLibraryFolder, name + ".PcbLib");
            }
        }

        public string ResolveSchLib()
        {
            if (MergeIntoExisting) return MergeSchLib;
            return NewSchLibPath;
        }

        public string ResolvePcbLib()
        {
            if (MergeIntoExisting) return MergePcbLib;
            return NewPcbLibPath;
        }

        public string TargetSummary()
        {
            var sch = ImportSymbol ? ResolveSchLib() : null;
            var pcb = ImportFootprint ? ResolvePcbLib() : null;
            if (MergeIntoExisting)
                return "合并到：" + (sch ?? "(未选 SchLib)") + "  |  " + (pcb ?? "(未选 PcbLib)");
            return "新建库：" + NewSchLibPath + "  |  " + NewPcbLibPath;
        }

        public bool Validate(out string error)
        {
            error = null;
            if (ImportSymbol && string.IsNullOrWhiteSpace(ResolveSchLib()))
            {
                error = "请选择原理图库文件";
                return false;
            }
            if (ImportFootprint && string.IsNullOrWhiteSpace(ResolvePcbLib()))
            {
                error = "请选择 PCB 库文件";
                return false;
            }
            return true;
        }
    }

    internal static class SettingsStore
    {
        private static string Folder => StoragePaths.SettingsDir;
        private static string FilePath => Path.Combine(Folder, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var loaded = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(FilePath));
                    if (loaded != null)
                    {
                        if (string.IsNullOrWhiteSpace(loaded.NewLibraryFolder))
                            loaded.NewLibraryFolder = AppSettings.DefaultLibraryFolder();
                        return loaded;
                    }
                }
            }
            catch
            {
                // Fall back to defaults on a corrupted settings file.
            }
            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(settings, Formatting.Indented));
        }
    }
}
