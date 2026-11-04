using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Threading.Tasks;
using System.Windows.Forms;
using SerialProxy.Core;
using SerialProxy.Ui;

namespace SerialProxy
{
    /// <summary>com0com 环境准备：检测、安装、创建虚拟串口对。</summary>
    internal sealed class SetupDialog : Form
    {
        private Label _lblStatus;
        private Label _lblPackage;
        private Button _btnInstall;
        private Button _btnRecheck;
        private TextBox _txtPortA;
        private TextBox _txtPortB;
        private Button _btnCreate;
        private Label _lblPairHint;
        private Label _lblMessage;
        private bool _created;

        /// <summary>创建端口对后，上位机应使用的端口（虚拟对的另一端）。</summary>
        public string ClientPort { get; private set; }

        /// <summary>创建端口对后，本工具应使用的虚拟端口。</summary>
        public string ProxyPort { get; private set; }

        public SetupDialog()
        {
            BuildUi();
            SuggestPorts();
            RefreshStatus();
        }

        private void BuildUi()
        {
            SuspendLayout();

            Text = "com0com 虚拟串口";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = UiTheme.Background;
            Font = UiTheme.Ui;
            ClientSize = new Size(640, 502);

            // 标题
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 66,
                Padding = new Padding(20, 14, 20, 0),
                BackColor = UiTheme.Background
            };
            header.Controls.Add(new Label
            {
                Text = "环境准备",
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                AutoSize = true,
                Location = new Point(20, 12)
            });
            header.Controls.Add(new Label
            {
                Text = "抓包需要一对虚拟串口：本工具用一端，上位机用另一端。",
                Font = UiTheme.Small,
                ForeColor = UiTheme.Muted,
                AutoSize = true,
                Location = new Point(22, 42)
            });

            // 状态卡片
            var statusWrap = new Panel
            {
                Dock = DockStyle.Top,
                Height = 82,
                Padding = new Padding(20, 0, 20, 8)
            };
            var statusCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 10) };

            _lblStatus = new Label
            {
                Text = "正在检测…",
                Font = UiTheme.UiBold,
                ForeColor = UiTheme.Text,
                AutoSize = false,
                AutoEllipsis = true,
                Height = 20,
                Location = new Point(14, 12)
            };
            _lblPackage = new Label
            {
                Text = "",
                Font = UiTheme.Small,
                ForeColor = UiTheme.Muted,
                AutoSize = false,
                Height = 36,
                Location = new Point(14, 34)
            };
            _btnRecheck = UiTheme.CreateSecondaryButton("重新检测", 88);
            _btnRecheck.Location = new Point(0, 0);
            _btnRecheck.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnRecheck.Click += (s, e) => RefreshStatus();

            statusCard.Controls.Add(_lblStatus);
            statusCard.Controls.Add(_lblPackage);
            statusCard.Controls.Add(_btnRecheck);
            statusCard.Resize += (s, e) =>
            {
                _btnRecheck.Location = new Point(
                    Math.Max(14, statusCard.ClientSize.Width - _btnRecheck.Width - 14), 14);

                _lblStatus.Width = Math.Max(120,
                    statusCard.ClientSize.Width - _btnRecheck.Width - 48);
                _lblPackage.Width = Math.Max(120,
                    statusCard.ClientSize.Width - _btnRecheck.Width - 48);
            };

            statusWrap.Controls.Add(statusCard);

            // 步骤卡片
            var stepsWrap = new Panel
            {
                Dock = DockStyle.Top,
                Height = 116,
                Padding = new Padding(20, 0, 20, 8)
            };
            var stepsCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 10) };

            var stepsTitle = new Label
            {
                Text = "按顺序完成三步",
                Font = UiTheme.UiBold,
                ForeColor = UiTheme.Text,
                AutoSize = true,
                Location = new Point(14, 10)
            };
            stepsCard.Controls.Add(stepsTitle);
            stepsCard.Controls.Add(new Label
            {
                Text = "1. 安装 com0com（只需一次，会弹出管理员授权）\r\n" +
                       "2. 创建一对虚拟串口，例如 COM3 ⇔ COM4\r\n" +
                       "3. 回到主界面：虚拟口选 COM4，上位机改连 COM3",
                Font = UiTheme.Small,
                ForeColor = UiTheme.Muted,
                AutoSize = true,
                Location = new Point(14, 36)
            });

            stepsWrap.Controls.Add(stepsCard);

            // 创建端口对
            var pairWrap = new Panel
            {
                Dock = DockStyle.Top,
                Height = 116,
                Padding = new Padding(20, 0, 20, 8)
            };
            var pairCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 10) };

            var pairTitle = new Label
            {
                Text = "创建虚拟串口对",
                Font = UiTheme.UiBold,
                ForeColor = UiTheme.Text,
                AutoSize = true,
                Location = new Point(14, 10)
            };

            var labelA = new Label
            {
                Text = "上位机端口",
                Font = UiTheme.Small,
                ForeColor = UiTheme.Muted,
                AutoSize = true,
                Location = new Point(14, 31)
            };

            _txtPortA = new TextBox
            {
                Font = UiTheme.Mono,
                Text = "COM3"
            };

            _txtPortB = new TextBox
            {
                Font = UiTheme.Mono,
                Text = "COM4"
            };

            var hostA = new InputHost(_txtPortA)
            {
                Location = new Point(14, 50),
                Size = new Size(104, 30)
            };
            var hostB = new InputHost(_txtPortB)
            {
                Location = new Point(154, 50),
                Size = new Size(104, 30)
            };

            var arrow = new Label
            {
                Text = "⇄",
                Font = new Font("Microsoft YaHei UI", 11F),
                ForeColor = UiTheme.Muted,
                AutoSize = true,
                Location = new Point(129, 55)
            };

            var labelB = new Label
            {
                Text = "本工具端口",
                Font = UiTheme.Small,
                ForeColor = UiTheme.Muted,
                AutoSize = true,
                Location = new Point(154, 31)
            };

            _btnCreate = UiTheme.CreatePrimaryButton("创建端口对", 112);
            _btnCreate.Location = new Point(274, 50);
            _btnCreate.Click += (s, e) => CreatePair();

            var pairHint = new Label
            {
                Text = "",
                Font = UiTheme.Small,
                ForeColor = UiTheme.Faint,
                AutoSize = false,
                AutoEllipsis = true,
                Height = 20,
                Location = new Point(14, 82)
            };

            pairCard.Controls.Add(pairTitle);
            pairCard.Controls.Add(labelA);
            pairCard.Controls.Add(hostA);
            pairCard.Controls.Add(arrow);
            pairCard.Controls.Add(labelB);
            pairCard.Controls.Add(hostB);
            pairCard.Controls.Add(_btnCreate);
            pairCard.Controls.Add(pairHint);
            pairWrap.Controls.Add(pairCard);

            _lblPairHint = pairHint;
            pairCard.Resize += (s, e) =>
            {
                pairHint.Width = Math.Max(120, pairCard.ClientSize.Width - 28);
            };

            // 提示信息
            _lblMessage = new Label
            {
                Dock = DockStyle.Top,
                Height = 62,
                Padding = new Padding(22, 4, 22, 0),
                Font = UiTheme.Small,
                ForeColor = UiTheme.Muted,
                Text = "安装过程中会弹出 Windows 管理员授权，请选择“是”。"
            };

            // 底部按钮
            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                Padding = new Padding(20, 12, 20, 0),
                BackColor = UiTheme.Background
            };

            var close = UiTheme.CreateSecondaryButton("关闭", 88);
            close.Dock = DockStyle.Right;
            close.Click += (s, e) => Close();

            _btnInstall = UiTheme.CreatePrimaryButton("安装 com0com", 128);
            _btnInstall.Dock = DockStyle.Right;
            _btnInstall.Click += (s, e) => InstallCom0Com();

            var download = UiTheme.CreateSecondaryButton("打开下载页", 108);
            download.Dock = DockStyle.Left;
            download.Click += (s, e) => OpenDownloadPage();

            footer.Controls.Add(close);
            footer.Controls.Add(_btnInstall);
            footer.Controls.Add(download);

            Controls.Add(footer);
            Controls.Add(_lblMessage);
            Controls.Add(pairWrap);
            Controls.Add(stepsWrap);
            Controls.Add(statusWrap);
            Controls.Add(header);

            ResumeLayout(false);
            PerformLayout();
        }

        private void SuggestPorts()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var name in SerialPort.GetPortNames())
                    used.Add(name);
            }
            catch
            {
            }

            int number = 3;
            _txtPortA.Text = NextFreePort(used, ref number);
            _txtPortB.Text = NextFreePort(used, ref number);
        }

        private static string NextFreePort(HashSet<string> used, ref int number)
        {
            while (number < 256)
            {
                var name = "COM" + number;
                number++;
                if (!used.Contains(name)) return name;
            }

            return "COM256";
        }

        private void RefreshStatus()
        {
            bool installed = Com0ComHelper.IsInstalled;
            bool bundled = Com0ComHelper.HasBundledInstaller;

            _lblStatus.Text = installed
                ? "● com0com 已就绪：" + Com0ComHelper.StatusText
                : "● 未检测到 com0com 虚拟串口驱动";

            _lblStatus.ForeColor = installed ? UiTheme.Tx : UiTheme.Warning;

            var ports = SerialPort.GetPortNames();
            Array.Sort(ports, (a, b) => a.CompareTo(b));
            _lblPairHint.Text = installed
                ? "当前串口：" + (ports.Length == 0 ? "无" : string.Join("、", ports)) +
                  "　左边给上位机，右边给本工具。"
                : "先点右下角「安装 com0com」，装好后再创建端口对。";

            _lblPackage.Text = bundled
                ? "安装包：已随程序附带。"
                : "安装包：未附带，可自动下载，或打开下载页手动下载。";

            _btnInstall.Text = installed ? "重新运行安装程序" : "安装 com0com";
            UiTheme.SetPrimaryEnabled(_btnCreate, installed);

            if (installed)
            {
                _lblMessage.ForeColor = UiTheme.Muted;
                _lblMessage.Text = "已安装 com0com。请创建端口对；如果已经创建过，直接关闭本窗口即可。";
            }
        }

        private async void InstallCom0Com()
        {
            if (!Com0ComHelper.HasBundledInstaller)
            {
                var result = MessageBox.Show(this,
                    "程序目录里没有 com0com 安装包。\r\n\r\n" +
                    "是否现在自动下载？（约 0.5 MB，来自 SourceForge）\r\n" +
                    "如果下载失败，可以再点“打开下载页”手动下载。",
                    "下载 com0com",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result != DialogResult.Yes)
                {
                    OpenDownloadPage();
                    return;
                }

                SetBusy(true, "正在下载 com0com 安装包，请稍候…");

                string downloadMessage = "";
                bool downloaded = await Task.Run(() =>
                {
                    string message;
                    bool ok = Com0ComHelper.TryDownloadInstaller(out message);
                    downloadMessage = message;
                    return ok;
                });

                if (IsDisposed || Disposing) return;

                SetBusy(false, downloadMessage);

                if (!downloaded)
                {
                    _lblMessage.ForeColor = UiTheme.Danger;
                    _lblMessage.Text = downloadMessage;

                    if (MessageBox.Show(this,
                            "自动下载失败，要打开下载页手动下载吗？",
                            "下载失败",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning) == DialogResult.Yes)
                        OpenDownloadPage();

                    return;
                }

                RefreshStatus();
            }

            string message;
            bool started = Com0ComHelper.TryStartInstaller(out message);
            _lblMessage.ForeColor = started ? UiTheme.Tx : UiTheme.Danger;
            _lblMessage.Text = message;

            if (started)
            {
                // 给安装程序一点启动时间，随后自动检测一次
                var timer = new Timer { Interval = 4000 };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    if (!IsDisposed) RefreshStatus();
                };
                timer.Start();
            }
        }

        private void SetBusy(bool busy, string message)
        {
            _btnInstall.Enabled = !busy;
            _btnRecheck.Enabled = !busy;
            UiTheme.SetPrimaryEnabled(_btnCreate, !busy && Com0ComHelper.IsInstalled);
            UseWaitCursor = busy;

            if (message != null)
            {
                _lblMessage.ForeColor = busy ? UiTheme.Muted : UiTheme.Text;
                _lblMessage.Text = message;
            }
        }

        private void CreatePair()
        {
            string portA = (_txtPortA.Text ?? "").Trim().ToUpperInvariant();
            string portB = (_txtPortB.Text ?? "").Trim().ToUpperInvariant();

            string message;
            if (!Com0ComHelper.TryCreatePair(portA, portB, out message))
            {
                _lblMessage.ForeColor = UiTheme.Danger;
                _lblMessage.Text = message;
                return;
            }

            ClientPort = portA;
            ProxyPort = portB;
            _created = true;

            _lblMessage.ForeColor = UiTheme.Tx;
            _lblMessage.Text = message + "\r\n主界面已填入虚拟口 " + portB + "，请把上位机的串口改成 " + portA + "。";
        }

        private void OpenDownloadPage()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Com0ComHelper.DownloadPage)
                {
                    UseShellExecute = true
                });
                _lblMessage.ForeColor = UiTheme.Muted;
                _lblMessage.Text =
                    "已在浏览器中打开下载页。下载 signed 版压缩包，解压后把里面的 Setup_*.exe " +
                    "放到：" + Com0ComHelper.BundleDirectory;
            }
            catch (Exception ex)
            {
                _lblMessage.ForeColor = UiTheme.Danger;
                _lblMessage.Text = "打开下载页失败：" + ex.Message + "\r\n" +
                                   "可以手动访问 sourceforge.net/projects/com0com";
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_created)
                DialogResult = DialogResult.OK;

            base.OnFormClosing(e);
        }
    }
}
