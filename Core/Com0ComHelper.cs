using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SerialProxy.Core
{
    /// <summary>检测、启动 com0com 安装程序，以及创建虚拟串口对。</summary>
    public static class Com0ComHelper
    {
        public const string DownloadPage =
            "https://sourceforge.net/projects/com0com/files/com0com/";

        /// <summary>签名版安装包（i386 + x64）的候选下载地址。</summary>
        public static readonly string[] DownloadUrls =
        {
            "https://sourceforge.net/projects/com0com/files/com0com/3.0.0.0/com0com-3.0.0.0-i386-and-x64-signed.zip/download",
            "https://master.dl.sourceforge.net/project/com0com/com0com/3.0.0.0/com0com-3.0.0.0-i386-and-x64-signed.zip?viasf=1",
            "https://downloads.sourceforge.net/project/com0com/com0com/3.0.0.0/com0com-3.0.0.0-i386-and-x64-signed.zip"
        };

        public static string BundleDirectory
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "com0com"); }
        }

        public static bool HasBundledInstaller
        {
            get
            {
                try
                {
                    if (!Directory.Exists(BundleDirectory)) return false;

                    if (File.Exists(Path.Combine(BundleDirectory, "setup.exe")) ||
                        File.Exists(Path.Combine(BundleDirectory, "setup_com0com_W7_x64_signed.exe")))
                        return true;

                    var found = Directory.GetFiles(BundleDirectory, "setup*.exe", SearchOption.AllDirectories);
                    return found.Length > 0;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static string InstalledVersion
        {
            get
            {
                var version = FindUninstallVersion();
                if (version != null) return version;

                // 没有卸载信息时，按常见安装目录判断
                foreach (var directory in InstallDirectories)
                {
                    try
                    {
                        if (Directory.Exists(directory) &&
                            (File.Exists(Path.Combine(directory, "setupc.exe")) ||
                             File.Exists(Path.Combine(directory, "x64", "setupc.exe"))))
                            return "已安装";
                    }
                    catch
                    {
                    }
                }

                return null;
            }
        }

        private static string FindUninstallVersion()
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var uninstall = baseKey.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (uninstall == null) continue;

                        foreach (var subName in uninstall.GetSubKeyNames())
                        {
                            using (var key = uninstall.OpenSubKey(subName))
                            {
                                if (key == null) continue;

                                var name = key.GetValue("DisplayName") as string;
                                if (string.IsNullOrEmpty(name) ||
                                    name.IndexOf("com0com", StringComparison.OrdinalIgnoreCase) < 0)
                                    continue;

                                var version = key.GetValue("DisplayVersion") as string;
                                return string.IsNullOrEmpty(version) ? "已安装" : version;
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static string[] InstallDirectories
        {
            get
            {
                return new[]
                {
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "com0com"),
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "com0com")
                };
            }
        }

        public static string SetupcPath
        {
            get { return FindTool("setupc.exe"); }
        }

        public static bool IsInstalled
        {
            get { return SetupcPath != null || InstalledVersion != null; }
        }

        public static string StatusText
        {
            get
            {
                if (IsInstalled)
                {
                    var version = InstalledVersion;
                    return string.IsNullOrEmpty(version) || version == "已安装"
                        ? "已安装"
                        : "已安装（" + version + "）";
                }

                return HasBundledInstaller
                    ? "未安装（已附带安装包）"
                    : "未安装";
            }
        }

        /// <summary>启动自带的 com0com 安装程序（需要管理员授权）。</summary>
        public static bool TryStartInstaller(out string message)
        {
            var setup = FindSetupInstaller();
            if (setup == null)
            {
                message = "没有找到安装程序。请把 com0com 的 setup.exe 放到程序目录的 com0com 文件夹中，或点击“打开下载页”手动下载。";
                return false;
            }

            try
            {
                var info = new ProcessStartInfo(setup)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = Path.GetDirectoryName(setup) ?? BundleDirectory
                };

                Process.Start(info);
                message = "已启动 com0com 安装程序。按提示完成后，回到这里点击“重新检测”。";
                return true;
            }
            catch (Win32Exception ex)
            {
                message = ex.NativeErrorCode == 1223
                    ? "已取消管理员授权，安装未开始。"
                    : "启动安装程序失败：" + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                message = "启动安装程序失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>创建一对虚拟串口，例如 COM3 / COM4（需要管理员授权）。</summary>
        public static bool TryCreatePair(string portA, string portB, out string message)
        {
            var setupc = SetupcPath;
            if (setupc == null)
            {
                message = "未找到 setupc.exe，请先安装 com0com。";
                return false;
            }

            if (!IsValidPortName(portA) || !IsValidPortName(portB))
            {
                message = "端口名格式应为 COM1 ~ COM256。";
                return false;
            }

            if (string.Equals(portA, portB, StringComparison.OrdinalIgnoreCase))
            {
                message = "两个端口不能相同。";
                return false;
            }

            var arguments = string.Format("install PortName={0} PortName={1}", portA, portB);

            try
            {
                var info = new ProcessStartInfo(setupc, arguments)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(setupc) ?? BundleDirectory
                };

                using (var process = Process.Start(info))
                {
                    if (process == null)
                    {
                        message = "无法启动 setupc.exe。";
                        return false;
                    }

                    if (!process.WaitForExit(30000))
                    {
                        message = "创建端口对超时（30 秒）。请稍后在设备管理器中确认。";
                        return false;
                    }

                    if (process.ExitCode == 0)
                    {
                        message = string.Format("已创建虚拟串口对：{0} ⇔ {1}。", portA, portB);
                        return true;
                    }

                    message = string.Format(
                        "setupc 返回错误码 {0}。端口对可能已存在，或端口号被占用。", process.ExitCode);
                    return false;
                }
            }
            catch (Win32Exception ex)
            {
                message = ex.NativeErrorCode == 1223
                    ? "已取消管理员授权，未创建端口对。"
                    : "创建端口对失败：" + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                message = "创建端口对失败：" + ex.Message;
                return false;
            }
        }

        public static bool IsValidPortName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            name = name.Trim();
            if (!name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)) return false;

            int number;
            return int.TryParse(name.Substring(3), out number) && number >= 1 && number <= 256;
        }

        /// <summary>自动下载并解压签名版安装包到程序目录的 com0com 文件夹。</summary>
        public static bool TryDownloadInstaller(out string message)
        {
            message = null;

            try
            {
                Directory.CreateDirectory(BundleDirectory);
            }
            catch (Exception ex)
            {
                message = "无法创建 com0com 目录：" + ex.Message;
                return false;
            }

            string lastError = null;

            foreach (var url in DownloadUrls)
            {
                string temp = null;

                try
                {
                    temp = DownloadFile(url);
                    var info = new FileInfo(temp);

                    if (info.Length >= 100 * 1024 && LooksLikeZip(temp))
                    {
                        ExtractZip(temp, BundleDirectory);
                        message = "已下载并解压 com0com 安装包。";
                        return true;
                    }

                    // SourceForge 的下载页会先返回一个 HTML 过渡页，里面有真正的镜像地址
                    var redirect = FindMetaRefreshUrl(temp);
                    if (!string.IsNullOrEmpty(redirect))
                    {
                        var redirected = DownloadFile(redirect);
                        try
                        {
                            if (LooksLikeZip(redirected))
                            {
                                ExtractZip(redirected, BundleDirectory);
                                message = "已下载并解压 com0com 安装包。";
                                return true;
                            }
                        }
                        finally
                        {
                            DeleteQuietly(redirected);
                        }
                    }

                    lastError = "下载到的不是安装包（可能是网页或错误页）。";
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
                finally
                {
                    DeleteQuietly(temp);
                }
            }

            message = "自动下载失败" +
                      (string.IsNullOrEmpty(lastError) ? "。" : "：" + lastError) +
                      "\r\n请点击“打开下载页”手动下载，并把 setup.exe 放到：" + BundleDirectory;
            return false;
        }

        private static string DownloadFile(string url)
        {
            string temp = Path.Combine(
                Path.GetTempPath(),
                "com0com-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".zip");

            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) SerialProxy/1.0";
                request.AllowAutoRedirect = true;
                request.Timeout = 30000;
                request.ReadWriteTimeout = 60000;

                using (var response = request.GetResponse())
                using (var remote = response.GetResponseStream())
                using (var local = File.Create(temp))
                {
                    if (remote == null)
                        throw new IOException("没有收到数据。");

                    remote.CopyTo(local);
                }

                return temp;
            }
            catch
            {
                DeleteQuietly(temp);
                throw;
            }
        }

        private static string FindMetaRefreshUrl(string htmlPath)
        {
            try
            {
                if (!File.Exists(htmlPath)) return null;

                string html = File.ReadAllText(htmlPath, Encoding.UTF8);
                var match = Regex.Match(html,
                    @"<meta[^>]*http-equiv\s*=\s*[""']?refresh[""']?[^>]*content\s*=\s*[""'][^""']*url=([^""']+)",
                    RegexOptions.IgnoreCase);

                if (!match.Success)
                {
                    match = Regex.Match(html,
                        @"https?://downloads\.sourceforge\.net/[^""'<>\s]+",
                        RegexOptions.IgnoreCase);
                    if (match.Success)
                        return WebUtility.HtmlDecode(match.Value.Trim());
                }
                else
                {
                    return WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
                }
            }
            catch
            {
            }

            return null;
        }

        private static void DeleteQuietly(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        private static bool LooksLikeZip(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    return stream.ReadByte() == 0x50 && stream.ReadByte() == 0x4B;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void ExtractZip(string zipPath, string destination)
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                string root = Path.GetFullPath(destination);

                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    var target = Path.GetFullPath(Path.Combine(
                        destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));

                    // 防止压缩包里的路径跑到目标目录之外
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;

                    var directory = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    entry.ExtractToFile(target, true);
                }
            }
        }

        private static string FindSetupInstaller()
        {
            var directories = new[]
            {
                BundleDirectory,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "com0com"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "com0com")
            };

            foreach (var directory in directories)
            {
                try
                {
                    if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) continue;

                    var direct = Path.Combine(directory, "setup.exe");
                    if (File.Exists(direct)) return direct;

                    // 官方签名包里的文件名是 Setup_com0com_v3.0.0.0_W7_x64_signed.exe
                    var files = Directory.GetFiles(directory, "setup*.exe", SearchOption.AllDirectories);
                    if (files.Length > 0)
                        return PreferArchitecture(files);
                }
                catch
                {
                }
            }

            return null;
        }

        private static string PreferArchitecture(string[] files)
        {
            string wanted = Environment.Is64BitOperatingSystem ? "x64" : "x86";

            foreach (var file in files)
            {
                if (Path.GetFileName(file).IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
                    return file;
            }

            return files[0];
        }

        private static string FindTool(string fileName)
        {
            var directories = new[]
            {
                BundleDirectory,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "com0com"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "com0com")
            };

            foreach (var directory in directories)
            {
                try
                {
                    if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) continue;

                    var path = Path.Combine(directory, fileName);
                    if (File.Exists(path)) return path;

                    var x64 = Path.Combine(directory, "x64", fileName);
                    if (File.Exists(x64)) return x64;

                    // 安装包里常常带一层版本目录，递归找一下
                    var found = Directory.GetFiles(directory, fileName, SearchOption.AllDirectories);
                    if (found.Length > 0) return found[0];
                }
                catch
                {
                }
            }

            return null;
        }
    }
}
