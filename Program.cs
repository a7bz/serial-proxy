using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Windows.Forms;

namespace SerialProxy
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                // com0com 的下载页与镜像只支持 TLS 1.2+，老系统上显式开启
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
            }

            Application.ThreadException += (sender, e) => ReportFatal(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                ReportFatal(e.ExceptionObject as Exception);

            try
            {
                if (args != null && args.Length >= 2 &&
                    string.Equals(args[0], "--screenshot-setup", StringComparison.OrdinalIgnoreCase))
                {
                    var dialog = new SetupDialog();
                    CaptureAndExit(dialog, args[1]);
                    Application.Run(dialog);
                    return;
                }

                if (args != null && args.Length >= 2 &&
                    string.Equals(args[0], "--screenshot-help", StringComparison.OrdinalIgnoreCase))
                {
                    var dialog = new HelpDialog();
                    CaptureAndExit(dialog, args[1]);
                    Application.Run(dialog);
                    return;
                }

                var config = AppConfig.Load();
                var form = new MainForm(config);

                if (HasArg(args, "--demo"))
                    form.InjectDemoData();

                string demoFilter = ArgValue(args, "--filter");
                if (!string.IsNullOrEmpty(demoFilter))
                    form.InjectDemoFilter(demoFilter);

                // 开发辅助：SerialProxy.exe --screenshot out.png 渲染界面后退出
                if (args != null && args.Length >= 2 &&
                    string.Equals(args[0], "--screenshot", StringComparison.OrdinalIgnoreCase))
                {
                    CaptureAndExit(form, args[1]);
                }

                Application.Run(form);
            }
            catch (Exception ex)
            {
                ReportFatal(ex);
            }
        }

        private static bool HasArg(string[] args, string name)
        {
            if (args == null) return false;

            foreach (var arg in args)
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string ArgValue(string[] args, string name)
        {
            if (args == null) return null;

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return null;
        }

        private static void CaptureAndExit(Form form, string path)
        {
            form.Shown += (sender, e) =>
            {
                var timer = new Timer { Interval = 700 };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    timer.Dispose();

                    try
                    {
                        using (var bitmap = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                            bitmap.Save(path, ImageFormat.Png);
                        }
                    }
                    catch (Exception ex)
                    {
                        ReportFatal(ex);
                    }

                    form.Close();
                };
                timer.Start();
            };
        }

        private static void ReportFatal(Exception ex)
        {
            if (ex == null) return;

            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
                File.AppendAllText(path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + ex + Environment.NewLine);
            }
            catch
            {
                // 忽略日志写入失败
            }

            MessageBox.Show(
                "程序遇到未处理的错误：\n\n" + ex.Message +
                "\n\n详细信息已写入 error.log。",
                "SerialProxy",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
