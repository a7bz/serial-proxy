using System;
using System.Drawing;
using System.Windows.Forms;
using SerialProxy.Ui;

namespace SerialProxy
{
    /// <summary>可滚动、可复制的使用说明窗口（比 MessageBox 更易读）。</summary>
    internal sealed class HelpDialog : Form
    {
        public HelpDialog()
        {
            SuspendLayout();

            Text = "使用说明";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = UiTheme.Background;
            Font = UiTheme.Ui;
            MinimumSize = new Size(600, 460);
            ClientSize = new Size(740, 660);

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 66,
                Padding = new Padding(20, 12, 20, 0),
                BackColor = UiTheme.Background
            };
            header.Controls.Add(new Label
            {
                Text = "使用说明",
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                AutoSize = true,
                Location = new Point(20, 10)
            });
            header.Controls.Add(new Label
            {
                Text = "端口怎么接、数据往哪走、每个按钮和快捷键做什么。",
                Font = UiTheme.Small,
                ForeColor = UiTheme.Muted,
                AutoSize = true,
                Location = new Point(22, 38)
            });

            var bodyWrap = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 0, 20, 10),
                BackColor = UiTheme.Background
            };
            var card = new CardPanel { Dock = DockStyle.Fill };

            var text = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.Text,
                Font = BodyFont,
                DetectUrls = false,
                WordWrap = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                ShortcutsEnabled = true,
                HideSelection = false
            };
            AppendContent(text);

            var inner = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14, 12, 10, 12),
                BackColor = UiTheme.Surface
            };
            inner.Controls.Add(text);

            card.Controls.Add(inner);
            bodyWrap.Controls.Add(card);

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 54,
                Padding = new Padding(20, 10, 20, 0),
                BackColor = UiTheme.Background
            };

            var copy = UiTheme.CreateSecondaryButton("复制全文", 96);
            copy.Dock = DockStyle.Left;
            copy.Click += (s, e) => CopyAll();

            var close = UiTheme.CreatePrimaryButton("关闭", 88);
            close.Dock = DockStyle.Right;
            close.Click += (s, e) => Close();

            footer.Controls.Add(close);
            footer.Controls.Add(copy);

            Controls.Add(bodyWrap);
            Controls.Add(footer);
            Controls.Add(header);

            ResumeLayout(false);
            PerformLayout();
        }

        private static readonly Font BodyFont = new Font("Microsoft YaHei UI", 9.5F);
        private static readonly Font HeaderFont = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
        private static readonly Font DiagramFont = new Font("Consolas", 9.5F);

        /// <summary>章节标题加粗着色，拓扑/数据流使用等宽字体，长文更容易扫读。</summary>
        private static void AppendContent(RichTextBox box)
        {
            box.Clear();

            var lines = Content.Replace("\r\n", "\n").Split('\n');
            foreach (var line in lines)
            {
                bool header = line.StartsWith("【", StringComparison.Ordinal);
                bool diagram = line.IndexOf('→') >= 0 || line.IndexOf('│') >= 0 ||
                               line.IndexOf('⇢') >= 0 || line.IndexOf('⇄') >= 0 ||
                               line.IndexOf("──", StringComparison.Ordinal) >= 0;

                box.SelectionStart = box.TextLength;
                box.SelectionLength = 0;
                box.SelectionFont = header ? HeaderFont : (diagram ? DiagramFont : BodyFont);
                box.SelectionColor = header ? UiTheme.PrimaryDark : UiTheme.Text;
                box.AppendText(line + "\n");
            }

            box.Select(0, 0);
        }

        private void CopyAll()
        {
            try
            {
                Clipboard.SetText(Content);
                MessageBox.Show(this, "已复制全文到剪贴板。", "使用说明",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "复制失败（剪贴板被占用）：" + ex.Message, "使用说明",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private const string Content =
@"【端口拓扑】

  老设备 ──RS232──> COM2（真实串口）
                       │
                 SerialProxy（本工具）
                       │
  上位机 <── COM3 ──> COM4（com0com 创建的虚拟串口对）

  一个真实串口同一时刻只能被一个程序打开。代理要抓数据就必须独占
  COM2，上位机就打不开它了。所以用 com0com 建一对虚拟串口来“分身”：

    1. 打开右上角「环境准备」，安装 com0com 并创建一对虚拟串口，
       例如 COM3 ⇔ COM4；
    2. 本工具「真实口」选 COM2，「虚拟口」选 COM4；
    3. 上位机的串口改选 COM3；
    4. 点「开始」（F5）。

  本工具只打开 COM2 和 COM4，绝不占用 COM3。


【数据流】

  设备 → COM2 → 本工具 → 写 COM4 ⇢ 从 COM3 出来 → 上位机
       （本工具显示为「输入 RX」，蓝色）

  上位机 → 写 COM3 ⇢ 从 COM4 出来 → 本工具 → 写 COM2 → 设备
       （本工具显示为「输出 TX」，绿色）


【命令栏】

  开始 / 停止    打开或关闭代理（F5）。运行时串口参数会锁住，避免误改。
  暂停           只停止列表刷新；串口照常接收、计数、写 CSV（F6）。
  清空           清空界面和内存归档，已写出的 CSV 文件不受影响（Ctrl+L）。
  导出           把内存里的全部记录导出为 CSV（Excel 直接打开）或 TXT（Ctrl+E）。
  详情           显示 / 隐藏下方帧详情区（F7）。
  双栏/输入/输出  切换视图，也可以拖中间分隔条调整比例（Ctrl+1 / 2 / 3）。
  格式           HEX+ASCII、HEX、ASCII 三种显示方式。
  编码           ASCII / GBK / UTF-8，只影响 ASCII 列的解读。
  搜索           按 HEX（01 03 或 0103）或文本过滤，只影响显示（Ctrl+F）。


【列表与详情】

  · 双击某一行 = 复制这一行；
  · 右键 = 复制选中 / 复制全部可见 / 全选 / 重发这一帧 / 清除这一栏；
  · 选中任意一行，下方显示完整 HEX dump（偏移 + HEX + ASCII）；
  · 手动发送的行会标「〔手动〕」并加粗，避免和设备数据混淆；
  · 每栏最多保留最近 5000 行，完整记录以 CSV 为准。


【手动发送】

  直接写入真实串口发给设备，不经过上位机，用来试探协议。
  · 勾「HEX」时按十六进制解析，支持 01 03 0A、0x01,0x03、01030A；
  · 不勾 HEX 时按当前编码发文本，可勾「追加 CRLF」；
  · 「定时发送」按设定间隔重复发送；
  · 发送框下拉保留最近 20 条历史；F8 重发上一次。


【快捷键】

  F5            开始 / 停止代理
  F6            暂停 / 恢复列表刷新
  F7            显示 / 隐藏帧详情
  F8            重发上一次
  Ctrl+F        聚焦搜索框
  Ctrl+L        清空列表
  Ctrl+E        导出
  Ctrl+Enter    发送
  Ctrl+1/2/3    双栏 / 仅输入 / 仅输出


【文件位置】

  配置：exe 同目录\serialproxy.ini（不可写时回退到 %APPDATA%\SerialProxy\）
  日志：exe 同目录\logs\（不可写时回退到 %LOCALAPPDATA%\SerialProxy\logs\）


【注意】

  * 虚拟对的编号可以换成别的，只要成对且没被占用。
  * 串口参数必须与设备一致；DTR/RTS 在开始代理时设置一次。
  * 上位机运行中动态改 DTR/RTS 无法透传到真实串口。
  * 手动发送会让设备直接回包，实验时建议先暂停上位机操作。
  * 抓到的数据是按“读事件”切的，不是按协议帧切的，分析时按帧头和时间间隔重组。";
    }
}
