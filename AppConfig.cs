using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SerialProxy
{
    /// <summary>简单的 INI 配置，优先放在 exe 同目录，不可写时回退到 %APPDATA%。</summary>
    public sealed class AppConfig
    {
        public string RealPort = "COM2";
        public string VirtualPort = "COM4";
        public int BaudRate = 9600;
        public int DataBits = 8;
        public string Parity = "None";
        public string StopBits = "One";
        public string Handshake = "None";
        public bool Dtr = false;
        public bool Rts = false;
        public string ViewMode = "HEX+ASCII";
        public string EncodingName = "ASCII";
        public bool AutoSave = true;
        public bool AutoScrollRx = true;
        public bool AutoScrollTx = true;
        public bool SendHex = true;
        public bool SendCrlf = false;
        public int SendInterval = 1000;
        public int WindowWidth = 1200;
        public int WindowHeight = 800;
        public string LastExportDir = "";
        public int SplitDistance = 0;
        public bool DetailVisible = true;
        public string ViewLayout = "Both";
        public string SendHistory = "";
        public bool HideSetupBanner = false;

        private const string FileName = "serialproxy.ini";

        public static string ConfigPath
        {
            get
            {
                var local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
                try
                {
                    var dir = Path.GetDirectoryName(local);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        return local;
                }
                catch
                {
                    // 忽略，回退到 AppData
                }

                var appData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SerialProxy");
                Directory.CreateDirectory(appData);
                return Path.Combine(appData, FileName);
            }
        }

        public static string LogDirectory
        {
            get
            {
                var local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                try
                {
                    Directory.CreateDirectory(local);
                    return local;
                }
                catch
                {
                    var appData = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "SerialProxy", "logs");
                    Directory.CreateDirectory(appData);
                    return appData;
                }
            }
        }

        public static AppConfig Load()
        {
            var cfg = new AppConfig();
            try
            {
                var path = FindExistingConfig();
                if (path == null) return cfg;

                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var text = line.Trim();
                    if (text.Length == 0 || text.StartsWith("#") || text.StartsWith(";"))
                        continue;

                    var idx = text.IndexOf('=');
                    if (idx <= 0) continue;
                    var key = text.Substring(0, idx).Trim();
                    var value = text.Substring(idx + 1).Trim();
                    map[key] = value;
                }

                cfg.RealPort = GetString(map, "RealPort", cfg.RealPort);
                cfg.VirtualPort = GetString(map, "VirtualPort", cfg.VirtualPort);
                cfg.BaudRate = GetInt(map, "BaudRate", cfg.BaudRate);
                cfg.DataBits = GetInt(map, "DataBits", cfg.DataBits);
                cfg.Parity = GetString(map, "Parity", cfg.Parity);
                cfg.StopBits = GetString(map, "StopBits", cfg.StopBits);
                cfg.Handshake = GetString(map, "Handshake", cfg.Handshake);
                cfg.Dtr = GetBool(map, "Dtr", cfg.Dtr);
                cfg.Rts = GetBool(map, "Rts", cfg.Rts);
                cfg.ViewMode = GetString(map, "ViewMode", cfg.ViewMode);
                cfg.EncodingName = GetString(map, "Encoding", cfg.EncodingName);
                cfg.AutoSave = GetBool(map, "AutoSave", cfg.AutoSave);
                cfg.AutoScrollRx = GetBool(map, "AutoScrollRx", cfg.AutoScrollRx);
                cfg.AutoScrollTx = GetBool(map, "AutoScrollTx", cfg.AutoScrollTx);
                cfg.SendHex = GetBool(map, "SendHex", cfg.SendHex);
                cfg.SendCrlf = GetBool(map, "SendCrlf", cfg.SendCrlf);
                cfg.SendInterval = GetInt(map, "SendInterval", cfg.SendInterval);
                cfg.WindowWidth = GetInt(map, "WindowWidth", cfg.WindowWidth);
                cfg.WindowHeight = GetInt(map, "WindowHeight", cfg.WindowHeight);
                cfg.LastExportDir = GetString(map, "LastExportDir", cfg.LastExportDir);
                cfg.SplitDistance = GetInt(map, "SplitDistance", cfg.SplitDistance);
                cfg.DetailVisible = GetBool(map, "DetailVisible", cfg.DetailVisible);
                cfg.ViewLayout = GetString(map, "ViewLayout", cfg.ViewLayout);
                cfg.SendHistory = GetString(map, "SendHistory", cfg.SendHistory);
                cfg.HideSetupBanner = GetBool(map, "HideSetupBanner", cfg.HideSetupBanner);
            }
            catch
            {
                // 配置损坏时使用默认值，不影响启动
            }

            return cfg;
        }

        public void Save()
        {
            var lines = new[]
            {
                "# SerialProxy 配置",
                "RealPort=" + RealPort,
                "VirtualPort=" + VirtualPort,
                "BaudRate=" + BaudRate.ToString(CultureInfo.InvariantCulture),
                "DataBits=" + DataBits.ToString(CultureInfo.InvariantCulture),
                "Parity=" + Parity,
                "StopBits=" + StopBits,
                "Handshake=" + Handshake,
                "Dtr=" + BoolText(Dtr),
                "Rts=" + BoolText(Rts),
                "ViewMode=" + ViewMode,
                "Encoding=" + EncodingName,
                "AutoSave=" + BoolText(AutoSave),
                "AutoScrollRx=" + BoolText(AutoScrollRx),
                "AutoScrollTx=" + BoolText(AutoScrollTx),
                "SendHex=" + BoolText(SendHex),
                "SendCrlf=" + BoolText(SendCrlf),
                "SendInterval=" + SendInterval.ToString(CultureInfo.InvariantCulture),
                "WindowWidth=" + WindowWidth.ToString(CultureInfo.InvariantCulture),
                "WindowHeight=" + WindowHeight.ToString(CultureInfo.InvariantCulture),
                "LastExportDir=" + LastExportDir,
                "SplitDistance=" + SplitDistance.ToString(CultureInfo.InvariantCulture),
                "DetailVisible=" + BoolText(DetailVisible),
                "ViewLayout=" + ViewLayout,
                "SendHistory=" + SendHistory,
                "HideSetupBanner=" + BoolText(HideSetupBanner),
            };

            try
            {
                File.WriteAllLines(ConfigPath, lines, new UTF8Encoding(true));
            }
            catch
            {
                // 保存失败不影响使用
            }
        }

        private static string FindExistingConfig()
        {
            var local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
            if (File.Exists(local)) return local;

            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SerialProxy", FileName);
            return File.Exists(appData) ? appData : null;
        }

        private static string GetString(Dictionary<string, string> map, string key, string fallback)
        {
            string value;
            return map.TryGetValue(key, out value) && !string.IsNullOrEmpty(value) ? value : fallback;
        }

        private static int GetInt(Dictionary<string, string> map, string key, int fallback)
        {
            string value;
            int result;
            return map.TryGetValue(key, out value) &&
                   int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
                ? result
                : fallback;
        }

        private static bool GetBool(Dictionary<string, string> map, string key, bool fallback)
        {
            string value;
            if (!map.TryGetValue(key, out value)) return fallback;

            bool result;
            if (bool.TryParse(value, out result)) return result;
            if (value == "1") return true;
            if (value == "0") return false;
            return fallback;
        }

        private static string BoolText(bool value)
        {
            return value ? "1" : "0";
        }
    }
}
