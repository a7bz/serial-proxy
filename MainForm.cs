using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using SerialProxy.Core;
using SerialProxy.Ui;
using WinTimer = System.Windows.Forms.Timer;

namespace SerialProxy
{
    internal enum StreamView
    {
        Both,
        RxOnly,
        TxOnly
    }

    public sealed class MainForm : Form
    {
        private const int MaxListLines = 5000;
        private const int ListTrimCount = 1000;
        private const int PreviewBytes = 56;
        private const int MaxArchiveEntries = 200000;
        private const int MaxFilterEntries = 20000;
        private const int MaxSendHistory = 20;

        private readonly AppConfig _config;
        private readonly ConcurrentQueue<LogEntry> _rxPending = new ConcurrentQueue<LogEntry>();
        private readonly ConcurrentQueue<LogEntry> _txPending = new ConcurrentQueue<LogEntry>();
        private readonly List<LogEntry> _archive = new List<LogEntry>();
        private readonly object _archiveLock = new object();
        private readonly List<string> _sendHistory = new List<string>();

        private SerialBridge _bridge;
        private CsvLogger _csvLogger;
        private WinTimer _uiTimer;
        private WinTimer _repeatTimer;
        private WinTimer _filterTimer;

        private bool _errorShown;
        private bool _paused;
        private volatile bool _errorActive;
        private StreamView _viewLayout = StreamView.Both;
        private bool _detailVisible = true;
        private int _liveSplitDistance;
        private string _filterLower = "";
        private byte[] _lastSent;
        private string _lastSentText = "";
        private ListView _lastSelectedList;

        private long _rxBytes;
        private long _txBytes;
        private long _rateRxBytes;
        private long _rateTxBytes;
        private double _rxRate;
        private double _txRate;
        private DateTime _rateAt = DateTime.UtcNow;

        // 顶部
        private Panel _header;
        private StatusPill _pill;
        private Label _lblC0c;
        private Button _btnSetup;
        private Button _btnHelp;

        // 命令栏
        private Panel _cmdWrap;
        private Button _btnStart;
        private Button _btnStop;
        private Button _btnPause;
        private Button _btnClear;
        private Button _btnExport;
        private Button _btnDetail;
        private SegmentedControl _segView;
        private SegmentedControl _segFormat;
        private SegmentedControl _segEncoding;
        private TextBox _txtSearch;
        private InputHost _searchHost;
        private FlowLayoutPanel _cmdFlow;
        private Control _cmdBreakControl;
        private Control _cmdBreakAnchor;

        // 端口栏
        private Panel _portWrap;
        private ComboBox _cboReal;
        private ComboBox _cboVirtual;
        private ComboBox _cboBaud;
        private ComboBox _cboDataBits;
        private ComboBox _cboParity;
        private ComboBox _cboStopBits;
        private ComboBox _cboHandshake;
        private CheckBox _chkDtr;
        private CheckBox _chkRts;
        private Button _btnRefresh;
        private FlowLayoutPanel _portFlow;
        private Control _portBaudGroup;
        private Control _portBreakControl;
        private Control _portBreakAnchor;
        private bool _cmdNarrow;
        private bool _portNarrow;

        // 环境提示条
        private Panel _bannerWrap;

        // 数据区
        private Panel _workspace;
        private SplitContainer _split;
        private LogListView _lstRx;
        private LogListView _lstTx;
        private Label _lblRxStat;
        private Label _lblTxStat;
        private CheckBox _chkAutoRx;
        private CheckBox _chkAutoTx;
        private Label _emptyRx;
        private Label _emptyTx;
        private Panel _detailWrap;
        private Label _lblDetailMeta;
        private TextBox _txtDetail;
        private Button _btnDetailCopy;

        // 发送区
        private TextBox _txtSend;
        private InputHost _sendHost;
        private CheckBox _chkHex;
        private CheckBox _chkCrlf;
        private CheckBox _chkRepeat;
        private NumericUpDown _numInterval;
        private Button _btnSend;

        // 状态栏
        private Panel _statusBar;
        private Label _lblStatus;
        private Label _lblRxTotal;
        private Label _lblTxTotal;
        private LinkLabel _lnkLog;
        private ToolTip _tips;
        private string _statusMessage;
        private DateTime _statusUntil = DateTime.MinValue;

        public MainForm(AppConfig config)
        {
            _config = config ?? new AppConfig();

            SuspendLayout();
            BuildUi();
            ResumeLayout(true);

            _uiTimer = new WinTimer { Interval = 100 };
            _uiTimer.Tick += UiTimerOnTick;

            _repeatTimer = new WinTimer();
            _repeatTimer.Tick += RepeatTimerOnTick;

            _filterTimer = new WinTimer { Interval = 260 };
            _filterTimer.Tick += FilterTimerOnTick;

            ApplyConfig();

            _uiTimer.Start();

            Shown += (s, e) =>
            {
                UpdateResponsiveLayout();
                RefreshCom0ComState();
                ApplySplitDefaults();
                AdjustDetailHeight();
            };

            FormClosing += OnFormClosing;
        }

        // ---------------------------------------------------------------- 界面骨架

        private void BuildUi()
        {
            Text = "SerialProxy · 串口代理";
            StartPosition = FormStartPosition.CenterScreen;

            var workArea = Screen.PrimaryScreen.WorkingArea;
            MinimumSize = new Size(1000, 700);
            ClientSize = new Size(
                Math.Min(_config.WindowWidth > 400 ? _config.WindowWidth : 1180, workArea.Width - 60),
                Math.Min(_config.WindowHeight > 300 ? _config.WindowHeight : 800, workArea.Height - 60));
            Font = UiTheme.Ui;
            BackColor = UiTheme.Background;
            KeyPreview = true;

            BuildHeader();
            BuildCommandBar();
            BuildPortBar();
            BuildInfoBanner();
            BuildWorkspace();
            BuildSendBar();
            BuildStatusBar();

            // 添加顺序决定停靠位置：先 Fill，再 Bottom，最后 Top
            Controls.Add(_workspace);
            Controls.Add(_sendWrap);
            Controls.Add(_statusBar);
            Controls.Add(_bannerWrap);
            Controls.Add(_portWrap);
            Controls.Add(_cmdWrap);
            Controls.Add(_header);

            _tips = new ToolTip { AutoPopDelay = 8000, InitialDelay = 400, ReshowDelay = 100 };
            _tips.SetToolTip(_btnStart, "打开串口并开始转发（F5）");
            _tips.SetToolTip(_btnStop, "停止代理（F5）");
            _tips.SetToolTip(_btnPause, "暂停 / 恢复列表刷新，数据仍会接收并写入日志（F6）");
            _tips.SetToolTip(_btnClear, "清空列表与内存归档，CSV 文件不受影响（Ctrl+L）");
            _tips.SetToolTip(_btnExport, "导出当前记录为 CSV 或 TXT（Ctrl+E）");
            _tips.SetToolTip(_btnDetail, "显示 / 隐藏下方的帧详情（F7）");
            _tips.SetToolTip(_segView, "切换双栏 / 仅输入 / 仅输出（Ctrl+1/2/3）");
            _tips.SetToolTip(_btnRefresh, "重新枚举串口");
            _tips.SetToolTip(_txtSearch, "按 HEX 或 ASCII 文本过滤列表（Ctrl+F）");
            _tips.SetToolTip(_txtSend, "输入 HEX（如 01 03 00 00 00 0A）或文本，Ctrl+Enter 发送");
            _tips.SetToolTip(_btnSend, "发送到真实串口，直接交给设备（Ctrl+Enter）");

            Resize += (s, e) => UpdateResponsiveLayout();
            UpdateResponsiveLayout();
        }

        private Panel _sendWrap;

        private void BuildHeader()
        {
            _header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 64,
                Padding = new Padding(20, 8, 20, 4),
                BackColor = UiTheme.Background
            };

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var titleBox = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            titleBox.Controls.Add(new Label
            {
                Text = "串口代理",
                Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                AutoSize = true,
                Location = new Point(0, 2)
            });
            titleBox.Controls.Add(new Label
            {
                Text = "SerialProxy · 双向抓取 RS232 真实字节",
                Font = UiTheme.Small,
                ForeColor = UiTheme.Muted,
                AutoSize = true,
                Location = new Point(2, 32)
            });

            var right = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Margin = new Padding(0, 8, 0, 0),
                BackColor = Color.Transparent
            };

            _pill = new StatusPill { Margin = new Padding(0, 2, 14, 0) };
            _pill.SetState("未运行", UiTheme.Faint);

            _lblC0c = new Label
            {
                Text = "● 检测 com0com…",
                Font = UiTheme.Small,
                ForeColor = UiTheme.Muted,
                AutoSize = true,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 8, 12, 0)
            };
            _lblC0c.Click += (s, e) => OpenSetupDialog();

            _btnSetup = UiTheme.CreateSecondaryButton("环境准备", 88);
            _btnSetup.Margin = new Padding(0, 0, 8, 0);
            _btnSetup.Click += (s, e) => OpenSetupDialog();

            _btnHelp = UiTheme.CreateSecondaryButton("使用说明", 88);
            _btnHelp.Margin = new Padding(0);
            _btnHelp.Click += (s, e) => ShowHelp();

            right.Controls.Add(_pill);
            right.Controls.Add(_lblC0c);
            right.Controls.Add(_btnSetup);
            right.Controls.Add(_btnHelp);

            table.Controls.Add(titleBox, 0, 0);
            table.Controls.Add(right, 1, 0);
            _header.Controls.Add(table);
        }

        private void BuildCommandBar()
        {
            _cmdWrap = new Panel
            {
                Dock = DockStyle.Top,
                Height = 56,
                Padding = new Padding(20, 0, 20, 8),
                BackColor = UiTheme.Background
            };

            var card = new CardPanel { Dock = DockStyle.Fill };
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(12, 8, 12, 8),
                BackColor = UiTheme.Surface
            };
            _cmdFlow = flow;

            _btnStart = UiTheme.CreatePrimaryButton("▶ 开始", 80);
            _btnStop = UiTheme.CreateSecondaryButton("■ 停止", 62);
            _btnStop.Enabled = false;
            _btnPause = UiTheme.CreateSecondaryButton("暂停", 54);
            _btnClear = UiTheme.CreateSecondaryButton("清空", 46);
            _btnExport = UiTheme.CreateSecondaryButton("导出", 46);
            _btnDetail = UiTheme.CreateSecondaryButton("详情", 46);

            var actionButtons = new[] { _btnStart, _btnStop, _btnPause, _btnClear, _btnExport, _btnDetail };
            foreach (var button in actionButtons)
                button.Margin = new Padding(0, 0, 6, 0);

            _btnStart.Click += (s, e) => StartProxy();
            _btnStop.Click += (s, e) => StopProxy(true);
            _btnPause.Click += (s, e) => TogglePause();
            _btnClear.Click += (s, e) => ClearAll();
            _btnExport.Click += (s, e) => ExportRecords();
            _btnDetail.Click += (s, e) => ToggleDetail();

            _segView = new SegmentedControl(50, "双栏", "输入", "输出");
            _segFormat = new SegmentedControl(78, "HEX+ASCII", "HEX", "ASCII");
            _segEncoding = new SegmentedControl(46, "ASCII", "GBK", "UTF-8");

            _segView.SelectedIndexChanged += (s, e) => SetViewLayout((StreamView)_segView.SelectedIndex);
            _segFormat.SelectedIndexChanged += (s, e) => RebuildViews();
            _segEncoding.SelectedIndexChanged += (s, e) => RebuildViews();

            _txtSearch = new TextBox
            {
                Font = UiTheme.Ui,
                BackColor = UiTheme.Surface
            };
            _txtSearch.TextChanged += (s, e) =>
            {
                if (_searchHost != null)
                    _searchHost.TrailingText = _txtSearch.TextLength > 0 ? "×" : "";

                if (_filterTimer == null) return;
                _filterTimer.Stop();
                _filterTimer.Start();
            };
            _txtSearch.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Escape || _txtSearch.TextLength == 0) return;

                _txtSearch.Clear();
                e.SuppressKeyPress = true;
            };

            _searchHost = new InputHost(_txtSearch)
            {
                Width = 152,
                Margin = new Padding(0, 0, 0, 0)
            };
            _searchHost.TrailingClicked += (s, e) =>
            {
                _txtSearch.Clear();
                _txtSearch.Focus();
            };
            UiTheme.SetCueBanner(_txtSearch, "搜索 HEX / 文本");

            var viewGroup = InlineGroup(_segView);
            var formatGroup = InlineGroup(FieldLabel("格式"), _segFormat);
            var encodingGroup = InlineGroup(FieldLabel("编码"), _segEncoding);
            var searchGroup = InlineGroup(FieldLabel("搜索"), _searchHost);

            flow.Controls.Add(_btnStart);
            flow.Controls.Add(_btnStop);
            flow.Controls.Add(Separator());
            flow.Controls.Add(_btnPause);
            flow.Controls.Add(_btnClear);
            flow.Controls.Add(_btnExport);
            flow.Controls.Add(_btnDetail);
            flow.Controls.Add(Separator());
            flow.Controls.Add(viewGroup);
            _cmdBreakAnchor = viewGroup;
            _cmdBreakControl = Separator();
            flow.Controls.Add(_cmdBreakControl);
            flow.Controls.Add(formatGroup);
            flow.Controls.Add(encodingGroup);
            flow.Controls.Add(searchGroup);

            card.Controls.Add(flow);
            _cmdWrap.Controls.Add(card);
            AutoFitCard(_cmdWrap, card, flow, 8);
            _cmdWrap.Resize += (s, e) => UpdateResponsiveLayout();
        }

        /// <summary>窗口变窄时把显示设置整体换到第二行，避免控件被拆散。</summary>
        private void UpdateResponsiveLayout()
        {
            if (_cmdFlow == null || _cmdFlow.IsDisposed) return;

            bool cmdNarrow = NeedsWrap(_cmdFlow);
            if (cmdNarrow != _cmdNarrow)
            {
                _cmdNarrow = cmdNarrow;
                if (_cmdBreakAnchor != null)
                    _cmdFlow.SetFlowBreak(_cmdBreakAnchor, cmdNarrow);
                if (_cmdBreakControl != null)
                    _cmdBreakControl.Visible = !cmdNarrow;
            }

            if (_portFlow != null && !_portFlow.IsDisposed)
            {
                bool portNarrow = NeedsWrap(_portFlow);
                if (portNarrow != _portNarrow)
                {
                    _portNarrow = portNarrow;
                    if (_portBreakAnchor != null)
                        _portFlow.SetFlowBreak(_portBreakAnchor, portNarrow);
                    if (_portBreakControl != null)
                        _portBreakControl.Visible = !portNarrow;
                }
            }
        }

        private static bool NeedsWrap(FlowLayoutPanel flow)
        {
            if (flow == null) return false;

            int available = flow.ClientSize.Width;
            if (available <= 0) return false;

            int needed = flow.Padding.Horizontal;
            foreach (Control control in flow.Controls)
            {
                // 隐藏的分隔条仍计入宽度，避免换行状态来回抖动
                needed += control.Width + control.Margin.Horizontal;
            }

            return needed > available + 2;
        }

        private void BuildPortBar()
        {
            _portWrap = new Panel
            {
                Dock = DockStyle.Top,
                Height = 56,
                Padding = new Padding(20, 0, 20, 8),
                BackColor = UiTheme.Background
            };

            var card = new CardPanel { Dock = DockStyle.Fill };
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(12, 8, 12, 8),
                BackColor = UiTheme.Surface
            };
            _portFlow = flow;

            _cboReal = CreateCombo(null, 84, true, UiTheme.Ui);
            _cboVirtual = CreateCombo(null, 84, true, UiTheme.Ui);
            _btnRefresh = UiTheme.CreateSecondaryButton("刷新", 54);
            _btnRefresh.Margin = new Padding(0, 1, 10, 0);
            _btnRefresh.Height = 26;
            _btnRefresh.Click += (s, e) => RefreshPorts();

            _cboBaud = CreateCombo(new[]
            {
                "1200", "2400", "4800", "9600", "19200", "38400",
                "57600", "115200", "230400", "460800", "921600"
            }, 80, true, UiTheme.Ui);

            _cboDataBits = CreateCombo(new[] { "5", "6", "7", "8" }, 50, false, UiTheme.Ui);
            _cboParity = CreateCombo(new[] { "None", "Odd", "Even", "Mark", "Space" }, 72, false, UiTheme.Ui);
            _cboStopBits = CreateCombo(new[] { "One", "OnePointFive", "Two" }, 92, false, UiTheme.Ui);
            _cboHandshake = CreateCombo(new[]
            {
                "None", "XOnXOff", "RequestToSend", "RequestToSendXOnXOff"
            }, 104, false, UiTheme.Ui);

            _chkDtr = new ToggleChip("DTR", 48);
            _chkDtr.Margin = new Padding(0, 2, 6, 0);
            _chkRts = new ToggleChip("RTS", 48);
            _chkRts.Margin = new Padding(0, 2, 0, 0);

            _portBaudGroup = InlineGroup(FieldLabel("波特率"), _cboBaud);

            flow.Controls.Add(InlineGroup(FieldLabel("真实口"), _cboReal));
            flow.Controls.Add(InlineGroup(FieldLabel("虚拟口"), _cboVirtual));
            flow.Controls.Add(_btnRefresh);
            _portBreakAnchor = _btnRefresh;
            _portBreakControl = Separator();
            flow.Controls.Add(_portBreakControl);
            flow.Controls.Add(_portBaudGroup);
            flow.Controls.Add(InlineGroup(FieldLabel("数据"), _cboDataBits));
            flow.Controls.Add(InlineGroup(FieldLabel("校验"), _cboParity));
            flow.Controls.Add(InlineGroup(FieldLabel("停止"), _cboStopBits));
            flow.Controls.Add(InlineGroup(FieldLabel("流控"), _cboHandshake));
            flow.Controls.Add(InlineGroup(_chkDtr, _chkRts));

            card.Controls.Add(flow);
            _portWrap.Controls.Add(card);
            AutoFitCard(_portWrap, card, flow, 8);
            _portWrap.Resize += (s, e) => UpdateResponsiveLayout();
        }

        private void BuildInfoBanner()
        {
            _bannerWrap = new Panel
            {
                Dock = DockStyle.Top,
                Height = 66,
                Padding = new Padding(20, 0, 20, 8),
                BackColor = UiTheme.Background,
                Visible = false
            };

            var card = new CardPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(239, 246, 255),
                BorderColor = Color.FromArgb(191, 219, 254)
            };

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(14, 10, 14, 10),
                BackColor = Color.Transparent
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var info = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            info.Controls.Add(new Label
            {
                Text = "还缺少虚拟串口环境",
                Font = UiTheme.UiBold,
                ForeColor = Color.FromArgb(30, 64, 175),
                AutoSize = true,
                Location = new Point(0, 0)
            });
            info.Controls.Add(new Label
            {
                Text = "抓包需要一对虚拟串口。点「环境准备」，按提示安装 com0com 并创建端口对。",
                Font = UiTheme.Small,
                ForeColor = Color.FromArgb(51, 65, 85),
                AutoSize = true,
                Location = new Point(0, 20)
            });

            var actions = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };

            var btnPrepare = UiTheme.CreatePrimaryButton("环境准备", 90);
            btnPrepare.Margin = new Padding(0, 0, 8, 0);
            btnPrepare.Click += (s, e) => OpenSetupDialog();

            var btnLater = UiTheme.CreateSecondaryButton("稍后", 64);
            btnLater.Margin = new Padding(0);
            btnLater.Click += (s, e) =>
            {
                _config.HideSetupBanner = true;
                _bannerWrap.Visible = false;
            };

            actions.Controls.Add(btnPrepare);
            actions.Controls.Add(btnLater);

            table.Controls.Add(info, 0, 0);
            table.Controls.Add(actions, 1, 0);
            card.Controls.Add(table);
            _bannerWrap.Controls.Add(card);
        }

        private void BuildWorkspace()
        {
            _workspace = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 0, 20, 8),
                BackColor = UiTheme.Background
            };

            _split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 10,
                BackColor = UiTheme.Background
            };

            _split.Panel1.Controls.Add(BuildStreamCard(true));
            _split.Panel2.Controls.Add(BuildStreamCard(false));
            _split.SplitterMoved += (s, e) =>
            {
                try
                {
                    if (!_split.Panel1Collapsed && !_split.Panel2Collapsed)
                        _liveSplitDistance = _split.SplitterDistance;
                }
                catch
                {
                }
            };

            _detailWrap = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 170,
                Padding = new Padding(0, 8, 0, 0),
                BackColor = UiTheme.Background
            };
            _detailWrap.Controls.Add(BuildDetailCard());

            _workspace.Controls.Add(_split);
            _workspace.Controls.Add(_detailWrap);
            _workspace.Resize += (s, e) => AdjustDetailHeight();
        }

        /// <summary>窗口变矮时压缩详情区，优先保证收发列表有足够高度。</summary>
        private void AdjustDetailHeight()
        {
            if (_detailWrap == null || _workspace == null) return;
            if (!_detailVisible)
            {
                _detailWrap.Visible = false;
                _btnDetail.Enabled = true;
                StyleToggle(_btnDetail, false);
                return;
            }

            int available = _workspace.ClientSize.Height;
            if (available <= 0) return;

            if (available < 280)
            {
                // 高度不够时自动收起详情区，并暂时禁用开关避免误操作
                _detailWrap.Visible = false;
                _btnDetail.Enabled = false;
                StyleToggle(_btnDetail, false);
                return;
            }

            _btnDetail.Enabled = true;
            StyleToggle(_btnDetail, true);

            int height = Math.Min(190, Math.Max(96, (int)(available * 0.32)));
            _detailWrap.Visible = true;
            if (_detailWrap.Height != height)
                _detailWrap.Height = height;
        }

        private CardPanel BuildStreamCard(bool isRx)
        {
            var card = new CardPanel { Dock = DockStyle.Fill };

            var head = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 44,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(12, 4, 12, 0),
                BackColor = UiTheme.Surface
            };
            UiTheme.AddBottomBorder(head);
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var left = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };

            var accent = new Panel
            {
                Width = 4,
                Height = 16,
                BackColor = isRx ? UiTheme.Rx : UiTheme.Tx,
                Margin = new Padding(0, 9, 8, 0)
            };
            UiTheme.RoundRegion(accent, 2);
            accent.Resize += (s, e) => UiTheme.RoundRegion(accent, 2);
            left.Controls.Add(accent);

            var title = new Label
            {
                Text = isRx ? "输入 (RX)" : "输出 (TX)",
                Font = UiTheme.UiBold,
                ForeColor = isRx ? UiTheme.Rx : UiTheme.Tx,
                AutoSize = true,
                Margin = new Padding(0, 8, 8, 0)
            };
            left.Controls.Add(title);

            var subtitle = new Label
            {
                Text = isRx ? "设备 → 上位机" : "上位机 → 设备",
                Font = UiTheme.Small,
                ForeColor = UiTheme.Faint,
                AutoSize = true,
                Margin = new Padding(0, 10, 0, 0)
            };
            left.Controls.Add(subtitle);

            var right = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };

            var stat = new Label
            {
                Text = "0 条",
                Font = UiTheme.Small,
                ForeColor = UiTheme.Muted,
                AutoSize = true,
                Margin = new Padding(0, 9, 12, 0)
            };

            var auto = new ToggleChip("自动滚动", 76)
            {
                Margin = new Padding(0, 1, 0, 0)
            };

            right.Controls.Add(stat);
            right.Controls.Add(auto);

            head.Controls.Add(left, 0, 0);
            head.Controls.Add(right, 1, 0);

            var list = new LogListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                HideSelection = false,
                MultiSelect = true,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9.5F),
                BorderStyle = BorderStyle.None,
                BackColor = UiTheme.Surface,
                ShowItemToolTips = true,
                ForeColor = isRx ? UiTheme.Rx : UiTheme.Tx,
                DataColor = isRx ? UiTheme.Rx : UiTheme.Tx
            };
            list.Columns.Add("时间", 108, HorizontalAlignment.Left);
            list.Columns.Add("长度", 52, HorizontalAlignment.Right);
            list.Columns.Add("数据", 480, HorizontalAlignment.Left);
            list.SmallImageList = new ImageList { ImageSize = new Size(1, 24) };
            list.Resize += (s, e) => FitDataColumn(list);
            list.MouseDoubleClick += (s, e) => CopyList(list, true);
            list.SelectedIndexChanged += (s, e) =>
            {
                if (list.SelectedItems.Count > 0)
                    _lastSelectedList = list;
                ShowDetail(SelectedEntry());
            };
            list.Enter += (s, e) =>
            {
                _lastSelectedList = list;
                list.Invalidate();
            };
            list.Leave += (s, e) => list.Invalidate();

            var divider = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = UiTheme.Border };

            var empty = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = UiTheme.Small,
                ForeColor = UiTheme.Faint,
                BackColor = UiTheme.Surface,
                Padding = new Padding(24, 0, 24, 0),
                Visible = false
            };

            card.Controls.Add(list);
            card.Controls.Add(empty);
            card.Controls.Add(divider);
            card.Controls.Add(head);

            card.Resize += (s, e) =>
            {
                // 栏位很窄时收起方向副标题，避免标题被裁切
                subtitle.Visible = card.ClientSize.Width >= 360;

                int top = head.Height + divider.Height;
                empty.SetBounds(1, top, Math.Max(1, card.ClientSize.Width - 2),
                    Math.Max(1, card.ClientSize.Height - top - 1));
            };

            list.ContextMenuStrip = BuildListMenu(list);

            if (isRx)
            {
                _lstRx = list;
                _lblRxStat = stat;
                _chkAutoRx = auto;
                _emptyRx = empty;
            }
            else
            {
                _lstTx = list;
                _lblTxStat = stat;
                _chkAutoTx = auto;
                _emptyTx = empty;
            }

            return card;
        }

        private CardPanel BuildDetailCard()
        {
            var card = new CardPanel { Dock = DockStyle.Fill };

            var head = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 40,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(12, 6, 12, 0),
                BackColor = UiTheme.Surface
            };
            UiTheme.AddBottomBorder(head);
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var left = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };
            left.Controls.Add(new Label
            {
                Text = "帧详情",
                Font = UiTheme.UiBold,
                ForeColor = UiTheme.Text,
                AutoSize = true,
                Margin = new Padding(0, 5, 10, 0)
            });

            _lblDetailMeta = new Label
            {
                Text = "未选择记录（点一行查看完整 HEX 与 ASCII）",
                Font = UiTheme.Small,
                ForeColor = UiTheme.Faint,
                AutoSize = true,
                Margin = new Padding(0, 7, 0, 0)
            };
            left.Controls.Add(_lblDetailMeta);

            _btnDetailCopy = UiTheme.CreateSecondaryButton("复制详情", 84);
            _btnDetailCopy.Height = 26;
            _btnDetailCopy.Margin = new Padding(0, 2, 0, 0);
            _btnDetailCopy.Enabled = false;
            _btnDetailCopy.Click += (s, e) => CopyDetail();

            head.Controls.Add(left, 0, 0);
            head.Controls.Add(_btnDetailCopy, 1, 0);

            var bodyWrap = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 2, 8, 8),
                BackColor = UiTheme.Surface
            };

            _txtDetail = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.Faint,
                Font = UiTheme.Mono,
                Text = "选中任意一行，这里会显示完整 HEX、字节偏移与 ASCII。\r\n" +
                       "右键列表可以复制或重发选中的帧。"
            };

            bodyWrap.Controls.Add(_txtDetail);
            var divider = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = UiTheme.Border };
            card.Controls.Add(bodyWrap);
            card.Controls.Add(divider);
            card.Controls.Add(head);

            return card;
        }

        private void BuildSendBar()
        {
            _sendWrap = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 92,
                Padding = new Padding(20, 0, 20, 8),
                BackColor = UiTheme.Background
            };

            var card = new CardPanel { Dock = DockStyle.Fill };

            var head = new Panel
            {
                Dock = DockStyle.Top,
                Height = 32,
                Padding = new Padding(12, 7, 12, 0),
                BackColor = UiTheme.Surface
            };
            head.Controls.Add(new Label
            {
                Text = "手动发送",
                Font = UiTheme.UiBold,
                ForeColor = UiTheme.Text,
                AutoSize = true,
                Location = new Point(12, 8)
            });
            head.Controls.Add(new Label
            {
                Text = "直接写入真实串口发给设备，不经过上位机",
                Font = UiTheme.Small,
                ForeColor = UiTheme.Faint,
                AutoSize = true,
                Location = new Point(96, 9)
            });
            UiTheme.AddBottomBorder(head);

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 7,
                RowCount = 1,
                Padding = new Padding(12, 4, 12, 8),
                BackColor = UiTheme.Surface
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 1; i < 7; i++)
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _txtSend = new TextBox
            {
                Font = new Font("Consolas", 10F),
                BackColor = UiTheme.Surface
            };
            _txtSend.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.Enter)
                {
                    ManualSend(true);
                    e.SuppressKeyPress = true;
                }
            };
            UiTheme.SetCueBanner(_txtSend, "输入 HEX（如 01 03 00 00 00 0A）或文本，Ctrl+Enter 发送");

            _sendHost = new InputHost(_txtSend)
            {
                Dock = DockStyle.Fill,
                TrailingText = "▾",
                TrailingWidth = 30,
                Margin = new Padding(0, 3, 10, 3)
            };
            _sendHost.TrailingClicked += (s, e) => ShowSendHistory();

            _chkHex = new ToggleChip("HEX", 48);
            _chkHex.Margin = new Padding(0, 8, 8, 0);
            _chkCrlf = new ToggleChip("追加 CRLF", 78);
            _chkCrlf.Margin = new Padding(0, 8, 8, 0);

            _btnSend = UiTheme.CreatePrimaryButton("发送 (Ctrl+Enter)", 132);
            _btnSend.Margin = new Padding(0, 4, 10, 0);
            _btnSend.Height = 30;
            _btnSend.Click += (s, e) => ManualSend(true);

            _chkRepeat = new ToggleChip("定时发送", 72);
            _chkRepeat.Margin = new Padding(0, 8, 8, 0);
            _chkRepeat.CheckedChanged += (s, e) => UpdateRepeatTimer();

            _numInterval = new NumericUpDown
            {
                Minimum = 50,
                Maximum = 600000,
                Value = 1000,
                Increment = 100,
                Width = 78,
                Font = UiTheme.Ui,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Right,
                Margin = new Padding(0, 9, 4, 0)
            };

            grid.Controls.Add(_sendHost, 0, 0);
            grid.Controls.Add(_chkHex, 1, 0);
            grid.Controls.Add(_chkCrlf, 2, 0);
            grid.Controls.Add(_btnSend, 3, 0);
            grid.Controls.Add(_chkRepeat, 4, 0);
            grid.Controls.Add(_numInterval, 5, 0);
            grid.Controls.Add(new Label
            {
                Text = "ms",
                Font = UiTheme.Small,
                ForeColor = UiTheme.Muted,
                AutoSize = true,
                Margin = new Padding(0, 13, 0, 0)
            }, 6, 0);

            card.Controls.Add(grid);
            card.Controls.Add(head);
            _sendWrap.Controls.Add(card);
        }

        private void BuildStatusBar()
        {
            _statusBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 32,
                BackColor = UiTheme.StatusBackground
            };
            _statusBar.Paint += (s, e) =>
            {
                using (var pen = new Pen(UiTheme.Border))
                    e.Graphics.DrawLine(pen, 0, 0, _statusBar.Width, 0);
            };

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(20, 0, 20, 0),
                BackColor = Color.Transparent
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _lblStatus = new Label
            {
                Text = "就绪 · 点击「开始」或按 F5",
                Font = UiTheme.Small,
                ForeColor = UiTheme.Muted,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 9, 0, 0)
            };

            var right = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };

            _lblRxTotal = new Label
            {
                Text = "输入 0 B",
                Font = UiTheme.Small,
                ForeColor = UiTheme.Muted,
                AutoSize = true,
                Margin = new Padding(0, 9, 0, 0)
            };
            _lblTxTotal = new Label
            {
                Text = "输出 0 B",
                Font = UiTheme.Small,
                ForeColor = UiTheme.Muted,
                AutoSize = true,
                Margin = new Padding(14, 9, 0, 0)
            };

            _lnkLog = new LinkLabel
            {
                Text = "日志文件夹",
                Font = UiTheme.Small,
                LinkColor = UiTheme.Primary,
                ActiveLinkColor = UiTheme.PrimaryDark,
                AutoSize = true,
                Margin = new Padding(18, 9, 0, 0)
            };
            _lnkLog.LinkClicked += (s, e) => OpenLogFolder();

            right.Controls.Add(_lblRxTotal);
            right.Controls.Add(_lblTxTotal);
            right.Controls.Add(_lnkLog);

            table.Controls.Add(_lblStatus, 0, 0);
            table.Controls.Add(right, 1, 0);
            _statusBar.Controls.Add(table);
        }

        // ------------------------------------------------------------ 小工具

        private static Label FieldLabel(string text)
        {
            return new Label
            {
                Text = text,
                Font = UiTheme.Small,
                ForeColor = UiTheme.Muted,
                AutoSize = true,
                Margin = new Padding(0, 9, 5, 0)
            };
        }

        /// <summary>把“标签 + 控件”绑成一组，窄窗口换行时不会被拆散。</summary>
        private static FlowLayoutPanel InlineGroup(params Control[] items)
        {
            var group = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };

            group.Controls.AddRange(items);
            return group;
        }

        private static Panel Separator()
        {
            return new Panel
            {
                Width = 1,
                Height = 18,
                BackColor = UiTheme.Border,
                Margin = new Padding(6, 7, 10, 0)
            };
        }

        private static ComboBox CreateCombo(string[] items, int width, bool editable, Font font)
        {
            var combo = new ComboBox
            {
                DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Width = width,
                Font = font ?? UiTheme.Ui,
                Margin = new Padding(0, 3, 8, 0)
            };

            if (items != null && items.Length > 0)
                combo.Items.AddRange(items);

            return combo;
        }

        /// <summary>命令栏/端口栏按内容自动增高，窗口变窄时允许换行。</summary>
        private static void AutoFitCard(Panel wrapper, CardPanel card, FlowLayoutPanel flow, int verticalPadding)
        {
            Action sync = () =>
            {
                int available = Math.Max(200, card.ClientSize.Width - 2);
                if (flow.MaximumSize.Width != available)
                    flow.MaximumSize = new Size(available, 0);

                int height = flow.PreferredSize.Height + 2;
                if (card.Height != height) card.Height = height;

                int total = card.Height + verticalPadding;
                if (wrapper.Height != total) wrapper.Height = total;
            };

            flow.SizeChanged += (s, e) => sync();
            card.Resize += (s, e) => sync();
            wrapper.Resize += (s, e) => sync();
            sync();
        }

        private static void FitDataColumn(ListView list)
        {
            if (list.Columns.Count < 3) return;

            int width = list.ClientSize.Width - list.Columns[0].Width - list.Columns[1].Width
                        - SystemInformation.VerticalScrollBarWidth - 8;
            if (width > 40) list.Columns[2].Width = width;
        }

        // ------------------------------------------------------------ 配置

        private void ApplyConfig()
        {
            RefreshPorts();

            _cboBaud.Text = _config.BaudRate.ToString(CultureInfo.InvariantCulture);
            SelectOrSet(_cboDataBits, _config.DataBits.ToString(CultureInfo.InvariantCulture), "8");
            SelectOrSet(_cboParity, _config.Parity, "None");
            SelectOrSet(_cboStopBits, _config.StopBits, "One");
            SelectOrSet(_cboHandshake, _config.Handshake, "None");

            _chkDtr.Checked = _config.Dtr;
            _chkRts.Checked = _config.Rts;

            SelectSegment(_segFormat, _config.ViewMode, 0);
            SelectSegment(_segEncoding, _config.EncodingName, 0);

            _chkAutoRx.Checked = _config.AutoScrollRx;
            _chkAutoTx.Checked = _config.AutoScrollTx;

            _chkHex.Checked = _config.SendHex;
            _chkCrlf.Checked = _config.SendCrlf;
            _numInterval.Value = Math.Max(_numInterval.Minimum,
                Math.Min(_numInterval.Maximum, _config.SendInterval));

            LoadSendHistory(_config.SendHistory);

            _viewLayout = ParseView(_config.ViewLayout);
            _detailVisible = _config.DetailVisible;
            _liveSplitDistance = _config.SplitDistance;
            ApplyViewLayout();
            ApplyDetailVisible();
        }

        private static StreamView ParseView(string value)
        {
            if (string.Equals(value, "RxOnly", StringComparison.OrdinalIgnoreCase))
                return StreamView.RxOnly;
            if (string.Equals(value, "TxOnly", StringComparison.OrdinalIgnoreCase))
                return StreamView.TxOnly;
            return StreamView.Both;
        }

        private static string ViewName(StreamView view)
        {
            if (view == StreamView.RxOnly) return "RxOnly";
            if (view == StreamView.TxOnly) return "TxOnly";
            return "Both";
        }

        private static void SelectOrSet(ComboBox combo, string value, string fallback)
        {
            int index = combo.FindStringExact(value);
            if (index >= 0)
                combo.SelectedIndex = index;
            else
                combo.Text = string.IsNullOrEmpty(value) ? fallback : value;
        }

        private static void SelectSegment(SegmentedControl segment, string value, int fallback)
        {
            int index = segment.IndexOf(value);
            segment.SelectedIndex = index >= 0 ? index : fallback;
        }

        private void SaveConfig()
        {
            _config.RealPort = _cboReal.Text.Trim();
            _config.VirtualPort = _cboVirtual.Text.Trim();
            _config.BaudRate = ParseInt(_cboBaud.Text, 9600);
            _config.DataBits = ParseInt(_cboDataBits.Text, 8);
            _config.Parity = _cboParity.Text;
            _config.StopBits = _cboStopBits.Text;
            _config.Handshake = _cboHandshake.Text;
            _config.Dtr = _chkDtr.Checked;
            _config.Rts = _chkRts.Checked;
            _config.ViewMode = _segFormat.SelectedText;
            _config.EncodingName = _segEncoding.SelectedText;
            _config.AutoScrollRx = _chkAutoRx.Checked;
            _config.AutoScrollTx = _chkAutoTx.Checked;
            _config.SendHex = _chkHex.Checked;
            _config.SendCrlf = _chkCrlf.Checked;
            _config.SendInterval = (int)_numInterval.Value;
            _config.WindowWidth = ClientSize.Width;
            _config.WindowHeight = ClientSize.Height;
            _config.ViewLayout = ViewName(_viewLayout);
            _config.DetailVisible = _detailVisible;
            _config.SendHistory = string.Join("\u001F", _sendHistory.ToArray());

            try
            {
                if (!_split.Panel1Collapsed && !_split.Panel2Collapsed)
                    _config.SplitDistance = _split.SplitterDistance;
                else if (_liveSplitDistance > 0)
                    _config.SplitDistance = _liveSplitDistance;
            }
            catch
            {
            }

            _config.Save();
        }

        private void LoadSendHistory(string text)
        {
            _sendHistory.Clear();

            if (!string.IsNullOrEmpty(text))
            {
                foreach (var item in text.Split(new[] { '\u001F' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (_sendHistory.Count >= MaxSendHistory) break;
                    _sendHistory.Add(item);
                }
            }
        }

        private void AddSendHistory(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            _sendHistory.RemoveAll(s => string.Equals(s, text, StringComparison.Ordinal));
            _sendHistory.Insert(0, text);
            if (_sendHistory.Count > MaxSendHistory)
                _sendHistory.RemoveRange(MaxSendHistory, _sendHistory.Count - MaxSendHistory);
        }

        /// <summary>发送框右侧的下拉：历史命令，点选后直接填回输入框。</summary>
        private void ShowSendHistory()
        {
            var menu = new ContextMenuStrip { Font = UiTheme.Ui, ShowImageMargin = false };
            menu.Closed += (s, e) => menu.Dispose();

            if (_sendHistory.Count == 0)
            {
                menu.Items.Add(new ToolStripMenuItem("暂无发送历史") { Enabled = false });
            }
            else
            {
                foreach (var item in _sendHistory)
                {
                    string text = item;
                    var entry = new ToolStripMenuItem(Shorten(text, 64));
                    entry.ToolTipText = text;
                    entry.Click += (s, e) =>
                    {
                        _txtSend.Text = text;
                        _txtSend.Focus();
                        _txtSend.SelectionStart = _txtSend.TextLength;
                    };
                    menu.Items.Add(entry);
                }

                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("清空历史", null, (s, e) => _sendHistory.Clear());
            }

            menu.Show(_sendHost, new Point(0, _sendHost.Height + 2));
        }

        private static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\r", " ").Replace("\n", " ");
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }

        private void RefreshPorts()
        {
            string keepReal = _cboReal.Text;
            string keepVirtual = _cboVirtual.Text;

            var names = SafeGetPorts();
            Array.Sort(names, ComparePortName);

            _cboReal.BeginUpdate();
            _cboVirtual.BeginUpdate();
            try
            {
                _cboReal.Items.Clear();
                _cboVirtual.Items.Clear();
                foreach (var name in names)
                {
                    _cboReal.Items.Add(name);
                    _cboVirtual.Items.Add(name);
                }
            }
            finally
            {
                _cboReal.EndUpdate();
                _cboVirtual.EndUpdate();
            }

            _cboReal.Text = string.IsNullOrEmpty(keepReal) ? _config.RealPort : keepReal;
            _cboVirtual.Text = string.IsNullOrEmpty(keepVirtual) ? _config.VirtualPort : keepVirtual;

            if (names.Length == 0)
                ShowStatus("未检测到串口。请确认设备已连接，或先用 com0com 创建虚拟串口对。", 8);
        }

        private static string[] SafeGetPorts()
        {
            try
            {
                return SerialPort.GetPortNames();
            }
            catch
            {
                return new string[0];
            }
        }

        private static bool ContainsPort(string[] ports, string name)
        {
            foreach (var port in ports)
            {
                if (string.Equals(port, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static int ComparePortName(string a, string b)
        {
            return PortNumber(a).CompareTo(PortNumber(b));
        }

        private static int PortNumber(string name)
        {
            if (string.IsNullOrEmpty(name)) return int.MaxValue;

            var digits = new StringBuilder();
            foreach (char c in name)
            {
                if (char.IsDigit(c)) digits.Append(c);
            }

            int value;
            return int.TryParse(digits.ToString(), out value) ? value : int.MaxValue;
        }

        // ------------------------------------------------------------ 视图

        private void SetViewLayout(StreamView view)
        {
            if (_viewLayout == view)
                return;

            _viewLayout = view;
            ApplyViewLayout();
        }

        private void ApplyViewLayout()
        {
            bool showRx = _viewLayout != StreamView.TxOnly;
            bool showTx = _viewLayout != StreamView.RxOnly;

            try
            {
                if (_split.IsHandleCreated && _split.Visible && _split.Width > 0 &&
                    !_split.Panel1Collapsed && !_split.Panel2Collapsed)
                    _liveSplitDistance = _split.SplitterDistance;
            }
            catch
            {
            }

            _split.Panel1Collapsed = !showRx;
            _split.Panel2Collapsed = !showTx;
            _split.SplitterWidth = showRx && showTx ? 10 : 1;

            if (_segView != null && _segView.SelectedIndex != (int)_viewLayout)
                _segView.SelectedIndex = (int)_viewLayout;

            if (showRx && showTx)
                ApplySplitDefaults();
        }

        private static void StyleToggle(Button button, bool active)
        {
            if (button == null) return;

            button.BackColor = active ? Color.FromArgb(219, 234, 254) : UiTheme.Surface;
            button.ForeColor = active ? UiTheme.PrimaryDark : UiTheme.Text;
            button.Font = active ? UiTheme.UiBold : UiTheme.Ui;
            button.FlatAppearance.BorderColor = active
                ? Color.FromArgb(191, 219, 254)
                : UiTheme.Border;
            button.FlatAppearance.MouseOverBackColor = active
                ? Color.FromArgb(203, 221, 254)
                : UiTheme.Hover;
        }

        private void ApplySplitDefaults()
        {
            if (_split.Panel1Collapsed || _split.Panel2Collapsed) return;

            try
            {
                if (_split.Width < 600) return;

                _split.Panel1MinSize = 240;
                _split.Panel2MinSize = 240;

                int total = _split.Width;
                int minimum = _split.Panel1MinSize + _split.Panel2MinSize + _split.SplitterWidth;
                if (total <= minimum) return;

                int distance = _liveSplitDistance > 0 ? _liveSplitDistance : _config.SplitDistance;
                if (distance < _split.Panel1MinSize || distance > total - _split.Panel2MinSize - _split.SplitterWidth)
                    distance = total / 2;

                _split.SplitterDistance = distance;
                _liveSplitDistance = distance;
            }
            catch
            {
            }
        }

        private void ToggleDetail()
        {
            _detailVisible = !_detailVisible;
            ApplyDetailVisible();
        }

        private void ApplyDetailVisible()
        {
            if (_detailWrap == null) return;

            if (!_detailVisible)
            {
                _detailWrap.Visible = false;
                StyleToggle(_btnDetail, false);
                return;
            }

            AdjustDetailHeight();
        }

        private void TogglePause()
        {
            _paused = !_paused;
            _btnPause.Text = _paused ? "继续" : "暂停";
            _btnPause.ForeColor = _paused ? UiTheme.Warning : UiTheme.Text;

            ShowStatus(_paused
                ? "已暂停界面刷新。数据仍在接收、计数并写入日志。"
                : "已恢复界面刷新。", 4);
        }

        // ------------------------------------------------------------ 启停

        private void StartProxy()
        {
            if (_bridge != null && _bridge.IsRunning) return;

            string realPort = _cboReal.Text.Trim();
            string virtualPort = _cboVirtual.Text.Trim();

            if (realPort.Length == 0 || virtualPort.Length == 0)
            {
                MessageBox.Show(this, "请先选择真实串口和虚拟串口。", "SerialProxy",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.Equals(realPort, virtualPort, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "真实串口和虚拟串口不能是同一个端口。", "SerialProxy",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var ports = SafeGetPorts();

            if (!ContainsPort(ports, virtualPort))
            {
                var result = MessageBox.Show(this,
                    "虚拟串口 " + virtualPort + " 不存在。\r\n\r\n" +
                    "通常是还没有安装 com0com，或者还没有创建虚拟串口对。\r\n\r\n" +
                    "要现在打开「环境准备」吗？",
                    "找不到虚拟串口",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                    OpenSetupDialog();

                return;
            }

            if (!ContainsPort(ports, realPort))
            {
                MessageBox.Show(this,
                    "真实串口 " + realPort + " 不存在。\r\n\r\n" +
                    "请检查设备是否插好、驱动是否正常，然后点「刷新」。",
                    "找不到真实串口",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var settings = new SerialSettings
            {
                BaudRate = ParseInt(_cboBaud.Text, 9600),
                DataBits = ParseInt(_cboDataBits.Text, 8),
                Parity = ParseEnum(_cboParity.Text, Parity.None),
                StopBits = ParseEnum(_cboStopBits.Text, StopBits.One),
                Handshake = ParseEnum(_cboHandshake.Text, Handshake.None),
                Dtr = _chkDtr.Checked,
                Rts = _chkRts.Checked
            };

            var bridge = new SerialBridge(settings);
            bridge.EntryReceived += OnEntryReceived;
            bridge.Error += OnBridgeError;

            try
            {
                bridge.Start(realPort, virtualPort);
            }
            catch (Exception ex)
            {
                bridge.Dispose();
                MessageBox.Show(this,
                    "打开串口失败：\n\n" + ex.Message +
                    "\n\n请检查：\n" +
                    "1. 真实串口已连接设备，且没有被其他程序占用；\n" +
                    "2. 虚拟串口对已由 com0com 创建（例如 COM3 ↔ COM4）；\n" +
                    "3. 本工具用的是虚拟对的一端（如 COM4），上位机用另一端（如 COM3）；\n" +
                    "4. 串口参数与设备一致。",
                    "SerialProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            _bridge = bridge;
            _errorShown = false;
            _errorActive = false;
            _paused = false;
            _btnPause.Text = "暂停";
            _btnPause.ForeColor = UiTheme.Text;

            _csvLogger = new CsvLogger();
            if (_config.AutoSave)
            {
                try
                {
                    _csvLogger.Start(AppConfig.LogDirectory);
                }
                catch (Exception ex)
                {
                    ShowStatus("日志保存失败：" + ex.Message, 10);
                }
            }

            SetRunningState(true);
            UpdateEmptyHints();
        }

        private void StopProxy(bool notify)
        {
            var bridge = _bridge;
            _bridge = null;

            if (bridge != null)
            {
                bridge.EntryReceived -= OnEntryReceived;
                bridge.Error -= OnBridgeError;
                try
                {
                    bridge.Stop();
                }
                catch
                {
                }
                bridge.Dispose();
            }

            var logger = _csvLogger;
            _csvLogger = null;
            if (logger != null)
            {
                try
                {
                    logger.Stop();
                }
                catch
                {
                }
                logger.Dispose();
            }

            _errorActive = false;
            SetRunningState(false);
            UpdateEmptyHints();

            if (notify)
                ShowStatus("已停止代理。", 4);
        }

        private void SetRunningState(bool running)
        {
            UiTheme.SetPrimaryEnabled(_btnStart, !running);
            _btnStop.Enabled = running;
            _cboReal.Enabled = !running;
            _cboVirtual.Enabled = !running;
            _cboBaud.Enabled = !running;
            _cboDataBits.Enabled = !running;
            _cboParity.Enabled = !running;
            _cboStopBits.Enabled = !running;
            _cboHandshake.Enabled = !running;
            _btnRefresh.Enabled = !running;
            _chkDtr.Enabled = !running;
            _chkRts.Enabled = !running;

            if (running)
                ShowStatus("已连接：" + _cboReal.Text.Trim() + " ⇄ " + _cboVirtual.Text.Trim(), 10);
            else if (!_errorActive)
                ShowStatus("就绪 · 点击「开始」或按 F5", 4);

            UpdatePill();
        }

        private void OnBridgeError(string message)
        {
            if (IsDisposed || !IsHandleCreated) return;

            try
            {
                BeginInvoke(new Action(() =>
                {
                    StopProxy(false);

                    // StopProxy 会清掉错误标记，这里在停止之后再置位
                    _errorActive = true;
                    UpdatePill();
                    ShowStatus(message, 15);

                    if (_errorShown) return;
                    _errorShown = true;

                    MessageBox.Show(this,
                        message + "\n\n转发已停止。修正问题后请重新点击「开始」。",
                        "SerialProxy",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }));
            }
            catch
            {
            }
        }

        private void UpdatePill()
        {
            if (_errorActive)
                _pill.SetState("转发出错", UiTheme.Danger);
            else if (_paused)
                _pill.SetState("显示已暂停", UiTheme.Warning);
            else if (_bridge != null && _bridge.IsRunning)
                _pill.SetState("代理中", UiTheme.Tx);
            else
                _pill.SetState("未运行", UiTheme.Faint);
        }

        // ------------------------------------------------------------ 数据

        private void OnEntryReceived(LogEntry entry)
        {
            if (entry == null) return;

            lock (_archiveLock)
            {
                _archive.Add(entry);
                if (_archive.Count > MaxArchiveEntries)
                    _archive.RemoveRange(0, MaxArchiveEntries / 20);
            }

            if (entry.IsRx)
            {
                Interlocked.Add(ref _rxBytes, entry.Data.Length);
                if (_rxPending.Count < 50000) _rxPending.Enqueue(entry);
            }
            else
            {
                Interlocked.Add(ref _txBytes, entry.Data.Length);
                if (_txPending.Count < 50000) _txPending.Enqueue(entry);
            }

            var logger = _csvLogger;
            if (logger != null)
            {
                try
                {
                    logger.Enqueue(entry);
                }
                catch
                {
                }
            }
        }

        private void UiTimerOnTick(object sender, EventArgs e)
        {
            if (!_paused)
            {
                Drain(_rxPending, _lstRx, _emptyRx, _chkAutoRx.Checked);
                Drain(_txPending, _lstTx, _emptyTx, _chkAutoTx.Checked);
            }

            UpdateStats();
        }

        private void Drain(ConcurrentQueue<LogEntry> queue, LogListView list, Label empty, bool autoScroll)
        {
            if (queue.IsEmpty) return;

            var mode = CurrentViewMode();
            var encoding = CurrentEncoding();
            string filter = _filterLower;
            bool added = false;

            list.BeginUpdate();
            try
            {
                LogEntry entry;
                int handled = 0;

                while (handled < 400 && queue.TryDequeue(out entry))
                {
                    handled++;

                    if (filter.Length > 0 && !MatchesFilter(entry, filter, mode, encoding))
                        continue;

                    var item = CreateItem(entry, mode, encoding, list.Items.Count);
                    item.Tag = entry;
                    list.Items.Add(item);
                    added = true;
                }

                TrimList(list);
            }
            finally
            {
                list.EndUpdate();
            }

            if (added)
            {
                FitDataColumn(list);
                UpdateEmptyState(empty, list);

                if (autoScroll && list.Items.Count > 0)
                    list.EnsureVisible(list.Items.Count - 1);
            }
        }

        private static void TrimList(ListView list)
        {
            if (list.Items.Count <= MaxListLines) return;

            list.BeginUpdate();
            try
            {
                int remove = Math.Min(ListTrimCount, list.Items.Count - MaxListLines + ListTrimCount);
                for (int i = 0; i < remove; i++)
                    list.Items.RemoveAt(0);
            }
            finally
            {
                list.EndUpdate();
            }
        }

        private void RebuildViews()
        {
            List<LogEntry> snapshot;
            lock (_archiveLock)
            {
                int start = Math.Max(0, _archive.Count - MaxFilterEntries);
                snapshot = _archive.GetRange(start, _archive.Count - start);
            }

            // 快照里已经包含待处理队列的数据，清掉避免重复显示
            LogEntry ignored;
            while (_rxPending.TryDequeue(out ignored)) { }
            while (_txPending.TryDequeue(out ignored)) { }

            _filterLower = (_txtSearch.Text ?? "").Trim();

            var mode = CurrentViewMode();
            var encoding = CurrentEncoding();

            _lstRx.BeginUpdate();
            _lstTx.BeginUpdate();
            try
            {
                _lstRx.Items.Clear();
                _lstTx.Items.Clear();

                for (int i = 0; i < snapshot.Count; i++)
                {
                    var entry = snapshot[i];
                    if (!MatchesFilter(entry, _filterLower, mode, encoding)) continue;

                    var list = entry.IsRx ? _lstRx : _lstTx;
                    var item = CreateItem(entry, mode, encoding, list.Items.Count);
                    item.Tag = entry;
                    list.Items.Add(item);
                }

                TrimList(_lstRx);
                TrimList(_lstTx);
                FitDataColumn(_lstRx);
                FitDataColumn(_lstTx);
            }
            finally
            {
                _lstRx.EndUpdate();
                _lstTx.EndUpdate();
            }

            if (_chkAutoRx.Checked && _lstRx.Items.Count > 0)
                _lstRx.EnsureVisible(_lstRx.Items.Count - 1);
            if (_chkAutoTx.Checked && _lstTx.Items.Count > 0)
                _lstTx.EnsureVisible(_lstTx.Items.Count - 1);

            UpdateEmptyState(_emptyRx, _lstRx);
            UpdateEmptyState(_emptyTx, _lstTx);

            if (_filterLower.Length > 0)
                ShowStatus("已过滤：输入 " + _lstRx.Items.Count + " 条，输出 " + _lstTx.Items.Count + " 条。", 4);
        }

        private void FilterTimerOnTick(object sender, EventArgs e)
        {
            _filterTimer.Stop();
            RebuildViews();
        }

        private static bool MatchesFilter(LogEntry entry, string filter, string mode, Encoding encoding)
        {
            if (filter.Length == 0) return true;

            if (entry.Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)
                .IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            var body = LogFormat.FormatBody(entry, mode, encoding, 0);
            if (body.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // 支持不带空格的连续 HEX：0103 也能匹配到 "01 03"
            string compact = CompactHex(filter);
            if (compact.Length >= 4 && (compact.Length & 1) == 0 && entry.Data.Length > 0)
            {
                string flat = LogFormat.Hex(entry.Data, 0).Replace(" ", "");
                if (flat.IndexOf(compact, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        /// <summary>把 "01 03 0A" / "0x0103" 之类整理成纯十六进制字符串，非 HEX 输入返回空。</summary>
        private static string CompactHex(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (Uri.IsHexDigit(c))
                {
                    sb.Append(c);
                }
                else if (c == 'x' || c == 'X')
                {
                    // 跳过 0x / \x 前缀
                    continue;
                }
                else if (char.IsWhiteSpace(c) || c == ',' || c == ';' || c == '-' || c == ':' || c == '_')
                {
                    continue;
                }
                else
                {
                    return "";
                }
            }

            return sb.ToString();
        }

        private static ListViewItem CreateItem(LogEntry entry, string mode, Encoding encoding, int index)
        {
            var item = new ListViewItem(entry.Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
            item.SubItems.Add(entry.Data.Length.ToString(CultureInfo.InvariantCulture));
            item.SubItems.Add(LogFormat.FormatBody(entry, mode, encoding, PreviewBytes));
            item.ToolTipText = LogFormat.FormatBody(entry, mode, encoding, 400);
            return item;
        }

        private string CurrentViewMode()
        {
            var value = _segFormat == null ? "" : _segFormat.SelectedText;
            return string.IsNullOrEmpty(value) ? "HEX+ASCII" : value;
        }

        private Encoding CurrentEncoding()
        {
            var value = _segEncoding == null ? "" : _segEncoding.SelectedText;
            return LogFormat.GetEncoding(value);
        }

        private void UpdateStats()
        {
            UpdateRates();

            long rx = Interlocked.Read(ref _rxBytes);
            long tx = Interlocked.Read(ref _txBytes);

            _lblRxStat.Text = _lstRx.Items.Count + " 条 · " + FormatRate(_rxRate);
            _lblTxStat.Text = _lstTx.Items.Count + " 条 · " + FormatRate(_txRate);
            _lblRxTotal.Text = "输入 " + FormatBytes(rx);
            _lblTxTotal.Text = "输出 " + FormatBytes(tx);

            if (_statusMessage != null && DateTime.UtcNow >= _statusUntil)
            {
                _statusMessage = null;
                _lblStatus.Text = DefaultStatusText();
            }

            if (_statusMessage == null)
                _lblStatus.Text = DefaultStatusText();

            UpdatePill();
            UpdateEmptyState(_emptyRx, _lstRx);
            UpdateEmptyState(_emptyTx, _lstTx);

            var logger = _csvLogger;
            _lnkLog.LinkColor = logger != null && !string.IsNullOrEmpty(logger.FilePath)
                ? UiTheme.Primary
                : UiTheme.Muted;
            _lnkLog.Text = logger != null && !string.IsNullOrEmpty(logger.FilePath)
                ? "日志文件夹（" + Path.GetFileName(logger.FilePath) + "）"
                : "日志文件夹";
        }

        private void UpdateRates()
        {
            var now = DateTime.UtcNow;
            double seconds = (now - _rateAt).TotalSeconds;
            if (seconds < 0.8) return;

            long rx = Interlocked.Read(ref _rxBytes);
            long tx = Interlocked.Read(ref _txBytes);

            _rxRate = Math.Max(0, (rx - _rateRxBytes) / seconds);
            _txRate = Math.Max(0, (tx - _rateTxBytes) / seconds);

            _rateRxBytes = rx;
            _rateTxBytes = tx;
            _rateAt = now;
        }

        private string DefaultStatusText()
        {
            if (_bridge != null && _bridge.IsRunning)
                return "代理中：" + _cboReal.Text.Trim() + " ⇄ " + _cboVirtual.Text.Trim() +
                       "（上位机应连虚拟对的另一端）";

            return "就绪 · 点击「开始」或按 F5";
        }

        /// <summary>在状态栏显示一条限时消息，避免被每秒刷新覆盖。</summary>
        private void ShowStatus(string message, int seconds)
        {
            _statusMessage = message;
            _statusUntil = DateTime.UtcNow.AddSeconds(seconds);
            _lblStatus.Text = message;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024)
                return (bytes / 1024.0).ToString("F1", CultureInfo.InvariantCulture) + " KB";
            return (bytes / (1024.0 * 1024.0)).ToString("F2", CultureInfo.InvariantCulture) + " MB";
        }

        private static string FormatRate(double bytesPerSecond)
        {
            if (bytesPerSecond < 1) return "0 B/s";
            if (bytesPerSecond < 1024)
                return ((long)bytesPerSecond).ToString(CultureInfo.InvariantCulture) + " B/s";
            if (bytesPerSecond < 1024 * 1024)
                return (bytesPerSecond / 1024.0).ToString("F1", CultureInfo.InvariantCulture) + " KB/s";
            return (bytesPerSecond / (1024.0 * 1024.0)).ToString("F2", CultureInfo.InvariantCulture) + " MB/s";
        }

        private void UpdateEmptyState(Label empty, LogListView list)
        {
            if (empty == null || list == null) return;

            bool isRx = ReferenceEquals(list, _lstRx);
            string hint;

            if (_filterLower.Length > 0)
            {
                hint = isRx
                    ? "没有匹配的输入记录\r\n换个关键词，或点搜索框右侧的 × 清除过滤"
                    : "没有匹配的输出记录\r\n换个关键词，或点搜索框右侧的 × 清除过滤";
            }
            else if (_bridge != null && _bridge.IsRunning)
            {
                hint = isRx
                    ? "正在等待设备的输入…\r\n设备一旦发来数据，会立即显示在这里"
                    : "正在等待上位机的输出…\r\n上位机一旦发出命令，会立即显示在这里";
            }
            else
            {
                hint = isRx
                    ? "还没有输入数据\r\n点「开始」后，这里会显示设备发来的字节"
                    : "还没有输出数据\r\n点「开始」后，这里会显示上位机发出的字节";
            }

            if (empty.Text != hint) empty.Text = hint;

            bool show = list.Items.Count == 0;

            // ListView 是原生控件，和 Label 重叠时层级不可靠；
            // 列表为空时直接隐藏列表，让提示层稳定显示。
            if (list.Visible == show) list.Visible = !show;
            if (empty.Visible != show) empty.Visible = show;
        }

        private void UpdateEmptyHints()
        {
            UpdateEmptyState(_emptyRx, _lstRx);
            UpdateEmptyState(_emptyTx, _lstTx);
        }

        private void ClearAll()
        {
            _lstRx.Items.Clear();
            _lstTx.Items.Clear();

            LogEntry ignored;
            while (_rxPending.TryDequeue(out ignored)) { }
            while (_txPending.TryDequeue(out ignored)) { }

            lock (_archiveLock)
            {
                _archive.Clear();
            }

            Interlocked.Exchange(ref _rxBytes, 0);
            Interlocked.Exchange(ref _txBytes, 0);
            _rateRxBytes = 0;
            _rateTxBytes = 0;
            _rxRate = 0;
            _txRate = 0;
            _rateAt = DateTime.UtcNow;

            ShowDetail(null);
            UpdateStats();
            ShowStatus("已清空界面与内存归档（CSV 日志不受影响）。", 5);
        }

        // ------------------------------------------------------------ 详情

        private LogEntry SelectedEntry()
        {
            if (_lastSelectedList != null && _lastSelectedList.SelectedItems.Count > 0)
                return _lastSelectedList.SelectedItems[0].Tag as LogEntry;

            if (_lstTx != null && _lstTx.SelectedItems.Count > 0)
                return _lstTx.SelectedItems[0].Tag as LogEntry;

            if (_lstRx != null && _lstRx.SelectedItems.Count > 0)
                return _lstRx.SelectedItems[0].Tag as LogEntry;

            return null;
        }

        private void ShowDetail(LogEntry entry)
        {
            if (_lblDetailMeta == null) return;

            if (entry == null)
            {
                _lblDetailMeta.Text = "未选择记录（点一行查看完整 HEX 与 ASCII）";
                _lblDetailMeta.ForeColor = UiTheme.Faint;
                _txtDetail.ForeColor = UiTheme.Faint;
                _txtDetail.Text = "选中任意一行，这里会显示完整 HEX、字节偏移与 ASCII。\r\n" +
                                  "右键列表可以复制或重发选中的帧。";
                _btnDetailCopy.Enabled = false;
                return;
            }

            _lblDetailMeta.Text =
                (entry.IsRx ? "输入 (RX)" : "输出 (TX)") +
                (entry.IsManual ? " · 手动发送" : "") +
                " · " + entry.Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                " · " + entry.Data.Length + " 字节";
            _lblDetailMeta.ForeColor = entry.IsRx ? UiTheme.Rx : UiTheme.Tx;

            _txtDetail.ForeColor = UiTheme.Text;
            _txtDetail.Text = HexDump(entry.Data);
            _btnDetailCopy.Enabled = true;
            _txtDetail.Select(0, 0);
        }

        private static string HexDump(byte[] data)
        {
            if (data == null || data.Length == 0) return "(空)";

            var sb = new StringBuilder(data.Length * 5);
            for (int offset = 0; offset < data.Length; offset += 16)
            {
                int count = Math.Min(16, data.Length - offset);

                sb.Append(offset.ToString("X4", CultureInfo.InvariantCulture)).Append("  ");

                for (int i = 0; i < 16; i++)
                {
                    if (i < count)
                        sb.Append(data[offset + i].ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
                    else
                        sb.Append("   ");

                    if (i == 7) sb.Append(' ');
                }

                sb.Append(" |");
                for (int i = 0; i < count; i++)
                {
                    byte b = data[offset + i];
                    sb.Append(b >= 32 && b <= 126 ? (char)b : '.');
                }

                sb.Append('|');
                if (offset + 16 < data.Length) sb.AppendLine();
            }

            return sb.ToString();
        }

        private void CopyDetail()
        {
            if (string.IsNullOrEmpty(_txtDetail.Text)) return;

            try
            {
                Clipboard.SetText(_txtDetail.Text);
                ShowStatus("已复制帧详情。", 4);
            }
            catch (Exception ex)
            {
                ShowStatus("复制失败（剪贴板被占用）：" + ex.Message, 6);
            }
        }

        // ------------------------------------------------------------ 发送

        private void ManualSend(bool showError)
        {
            if (_bridge == null || !_bridge.IsRunning)
            {
                if (showError)
                    MessageBox.Show(this, "请先点击「开始」（F5）。", "SerialProxy",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            byte[] data;
            string text = _txtSend.Text;

            if (_chkHex.Checked)
            {
                string error;
                if (!HexUtil.TryParse(text, out data, out error))
                {
                    if (showError)
                        MessageBox.Show(this, error, "HEX 格式有误",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            else
            {
                if (_chkCrlf.Checked) text += "\r\n";
                data = CurrentEncoding().GetBytes(text);

                if (data.Length == 0)
                {
                    if (showError)
                        MessageBox.Show(this, "请输入要发送的内容。", "SerialProxy",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            if (SendBytes(data, _txtSend.Text, showError))
                _txtSend.SelectAll();
        }

        private bool SendBytes(byte[] data, string displayText, bool showError)
        {
            if (_bridge == null || !_bridge.IsRunning)
            {
                if (showError)
                    MessageBox.Show(this, "请先点击「开始」（F5）。", "SerialProxy",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            try
            {
                _bridge.SendToDevice(data);
                _lastSent = (byte[])data.Clone();
                _lastSentText = displayText ?? "";
                AddSendHistory(displayText);
                ShowStatus("已发送 " + data.Length + " 字节到设备。", 3);
                return true;
            }
            catch (Exception ex)
            {
                if (showError)
                    MessageBox.Show(this, "发送失败：" + ex.Message, "SerialProxy",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else
                    ShowStatus("发送失败：" + ex.Message, 6);
                return false;
            }
        }

        private void ResendLast()
        {
            if (_lastSent != null && _lastSent.Length > 0)
            {
                SendBytes(_lastSent, _lastSentText, true);
                return;
            }

            var entry = SelectedEntry();
            if (entry != null)
            {
                SendBytes(entry.Data, HexDump(entry.Data).Replace("\r", " ").Replace("\n", " "), true);
                return;
            }

            ShowStatus("还没有可重发的数据。先在发送框里发一次，或选中一帧再按 F8。", 6);
        }

        private void ResendSelected()
        {
            var entry = SelectedEntry();
            if (entry == null)
            {
                ShowStatus("请先在列表里选中一帧。", 4);
                return;
            }

            SendBytes(entry.Data, "重发 " + entry.Data.Length + " 字节", true);
        }

        private void UpdateRepeatTimer()
        {
            if (_chkRepeat.Checked)
            {
                _repeatTimer.Interval = (int)Math.Max(50, Math.Min(600000, _numInterval.Value));
                _repeatTimer.Start();
            }
            else
            {
                _repeatTimer.Stop();
            }
        }

        private void RepeatTimerOnTick(object sender, EventArgs e)
        {
            ManualSend(false);
        }

        // ------------------------------------------------------------ 复制/导出

        private ContextMenuStrip BuildListMenu(ListView list)
        {
            var menu = new ContextMenuStrip { Font = UiTheme.Ui };
            menu.Items.Add("复制选中行", null, (s, e) => CopyList(list, true));
            menu.Items.Add("复制全部可见行", null, (s, e) => CopyList(list, false));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("重发这一帧（写入设备）", null, (s, e) => ResendListSelection(list));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("全选", null, (s, e) =>
            {
                foreach (ListViewItem item in list.Items) item.Selected = true;
            });
            menu.Items.Add("清除这一栏", null, (s, e) =>
            {
                list.Items.Clear();
                if (list == _lstRx) _lblRxStat.Text = "0 条";
                else _lblTxStat.Text = "0 条";
                UpdateEmptyHints();
            });
            return menu;
        }

        private void ResendListSelection(ListView list)
        {
            if (list.SelectedItems.Count == 0)
            {
                ShowStatus("请先在列表里选中一帧。", 4);
                return;
            }

            var entry = list.SelectedItems[0].Tag as LogEntry;
            if (entry == null) return;

            SendBytes(entry.Data, "重发 " + entry.Data.Length + " 字节", true);
        }

        private void CopyList(ListView list, bool selectedOnly)
        {
            IEnumerable source = selectedOnly ? (IEnumerable)list.SelectedItems : list.Items;
            var sb = new StringBuilder();
            int count = 0;

            foreach (ListViewItem item in source)
            {
                sb.Append(item.Text);

                for (int i = 1; i < item.SubItems.Count; i++)
                    sb.Append('\t').Append(item.SubItems[i].Text);

                sb.AppendLine();
                count++;
            }

            if (count == 0)
            {
                ShowStatus("没有可复制的内容。", 4);
                return;
            }

            try
            {
                Clipboard.SetText(sb.ToString());
                ShowStatus("已复制 " + count + " 行。", 4);
            }
            catch (Exception ex)
            {
                ShowStatus("复制失败（剪贴板被占用）：" + ex.Message, 6);
            }
        }

        private void ExportRecords()
        {
            List<LogEntry> snapshot;
            lock (_archiveLock)
            {
                snapshot = new List<LogEntry>(_archive);
            }

            if (snapshot.Count == 0)
            {
                MessageBox.Show(this, "当前没有记录可导出。", "SerialProxy",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string dir = string.IsNullOrEmpty(_config.LastExportDir)
                ? AppConfig.LogDirectory
                : _config.LastExportDir;

            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "导出记录";
                dialog.Filter = "CSV 文件 (*.csv)|*.csv|文本文件 (*.txt)|*.txt";
                dialog.InitialDirectory = dir;
                dialog.FileName = "proxy_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv";

                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                bool isCsv = Path.GetExtension(dialog.FileName).ToLowerInvariant() != ".txt";
                var mode = CurrentViewMode();
                var encoding = CurrentEncoding();

                try
                {
                    using (var writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true)))
                    {
                        writer.WriteLine(isCsv ? LogFormat.CsvHeader : "方向\t时间\t长度\t数据");

                        foreach (var entry in snapshot)
                        {
                            if (isCsv)
                                writer.WriteLine(LogFormat.CsvLine(entry));
                            else
                                writer.WriteLine((entry.IsRx ? "RX" : "TX") + "\t" +
                                    LogFormat.DisplayLine(entry, mode, encoding, 0));
                        }
                    }

                    _config.LastExportDir = Path.GetDirectoryName(dialog.FileName) ?? dir;
                    ShowStatus("已导出 " + snapshot.Count + " 条记录：" + dialog.FileName, 8);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导出失败：" + ex.Message, "SerialProxy",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OpenLogFolder()
        {
            try
            {
                var dir = AppConfig.LogDirectory;
                Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch (Exception ex)
            {
                ShowStatus("打开日志文件夹失败：" + ex.Message, 6);
            }
        }

        // ------------------------------------------------------------ 环境准备

        private void OpenSetupDialog()
        {
            using (var dialog = new SetupDialog())
            {
                var result = dialog.ShowDialog(this);

                RefreshCom0ComState();
                RefreshPorts();

                if (result == DialogResult.OK && !string.IsNullOrEmpty(dialog.ProxyPort))
                {
                    _cboVirtual.Text = dialog.ProxyPort;
                    _config.HideSetupBanner = true;
                    _bannerWrap.Visible = false;

                    ShowStatus("已创建虚拟串口对。本工具用 " + dialog.ProxyPort +
                               "，请把上位机改成 " + dialog.ClientPort + "。", 15);
                }
            }
        }

        private void RefreshCom0ComState()
        {
            bool installed = Com0ComHelper.IsInstalled;
            bool virtualReady = false;

            try
            {
                virtualReady = ContainsPort(SafeGetPorts(), _cboVirtual.Text.Trim());
            }
            catch
            {
            }

            _lblC0c.Text = installed ? "● com0com 已就绪" : "● 未检测到 com0com";
            _lblC0c.ForeColor = installed ? UiTheme.Tx : UiTheme.Warning;

            _btnSetup.Text = virtualReady ? "串口对" : "环境准备";
            _bannerWrap.Visible = !virtualReady && !_config.HideSetupBanner;
        }

        // ------------------------------------------------------------ 演示数据

        /// <summary>开发辅助：把搜索词填进去并立即过滤，方便截图检查。</summary>
        internal void InjectDemoFilter(string text)
        {
            if (_txtSearch == null) return;

            _txtSearch.Text = text ?? "";
            _filterTimer.Stop();
            RebuildViews();
        }

        /// <summary>开发辅助：注入几帧示例数据，方便检查界面。</summary>
        internal void InjectDemoData()
        {
            var now = DateTime.Now;
            var samples = new[]
            {
                new { Rx = true, Manual = false, Ms = -2600, Hex = "01 03 00 00 00 0A C5 CD" },
                new { Rx = false, Manual = false, Ms = -2520, Hex = "01 03 14 00 64 00 C8 00 00 00 00 00 00 00 00 00 00 00 00 00 00 96" },
                new { Rx = true, Manual = false, Ms = -1800, Hex = "4F 4B 20 53 45 54 55 50 20 52 45 41 44 59 0D 0A" },
                new { Rx = false, Manual = true, Ms = -1500, Hex = "AA 55 01 02 03 04 05 06" },
                new { Rx = true, Manual = false, Ms = -900, Hex = "AA 55 10 00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F 10 11 12 13 14 15 16 17 18 19 1A 1B 1C 1D 1E 1F 20 21 22 23 24 25 26 27 28 29 2A 2B 2C 2D 2E 2F 30" },
                new { Rx = false, Manual = false, Ms = -240, Hex = "01 06 00 01 00 64 99 9A" },
                new { Rx = true, Manual = false, Ms = -60, Hex = "01 06 00 01 00 64 99 9A" }
            };

            foreach (var sample in samples)
            {
                byte[] data;
                string error;
                if (!HexUtil.TryParse(sample.Hex, out data, out error)) continue;

                OnEntryReceived(new LogEntry(
                    now.AddMilliseconds(sample.Ms), sample.Rx, sample.Manual, data));
            }

            var timer = new WinTimer { Interval = 500 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();

                if (_lstRx.Items.Count > 2)
                {
                    _lstRx.Items[2].Selected = true;
                    _lstRx.Items[2].Focused = true;
                }
            };
            timer.Start();
        }

        // ------------------------------------------------------------ 帮助/快捷键

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F5:
                    if (_bridge != null && _bridge.IsRunning) StopProxy(true);
                    else StartProxy();
                    return true;

                case Keys.F6:
                    TogglePause();
                    return true;

                case Keys.F7:
                    if (_btnDetail == null || _btnDetail.Enabled) ToggleDetail();
                    return true;

                case Keys.F8:
                    ResendLast();
                    return true;

                case Keys.Control | Keys.F:
                    _txtSearch.Focus();
                    _txtSearch.SelectAll();
                    return true;

                case Keys.Control | Keys.L:
                    ClearAll();
                    return true;

                case Keys.Control | Keys.E:
                    ExportRecords();
                    return true;

                case Keys.Control | Keys.D1:
                    SetViewLayout(StreamView.Both);
                    return true;

                case Keys.Control | Keys.D2:
                    SetViewLayout(StreamView.RxOnly);
                    return true;

                case Keys.Control | Keys.D3:
                    SetViewLayout(StreamView.TxOnly);
                    return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ShowHelp()
        {
            using (var dialog = new HelpDialog())
                dialog.ShowDialog(this);
        }

        // ------------------------------------------------------------ 工具

        private static int ParseInt(string text, int fallback)
        {
            int value;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : fallback;
        }

        private static T ParseEnum<T>(string text, T fallback) where T : struct
        {
            T value;
            return Enum.TryParse(text, true, out value) ? value : fallback;
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_bridge != null && _bridge.IsRunning)
            {
                var result = MessageBox.Show(this,
                    "代理正在运行，确定退出？",
                    "SerialProxy",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            _uiTimer.Stop();
            _repeatTimer.Stop();
            _filterTimer.Stop();
            StopProxy(false);
            SaveConfig();
        }
    }

    /// <summary>自绘的收发列表：时间/长度弱化，数据列保留方向色，选中时用浅蓝底。</summary>
    internal sealed class LogListView : ListView
    {

        private static readonly Font ManualFont = new Font("Consolas", 9.5F, FontStyle.Bold);
        private static readonly Color Alternate = Color.FromArgb(250, 251, 253);
        private static readonly Color HeaderBackground = Color.FromArgb(248, 250, 252);
        private static readonly Color RowLine = Color.FromArgb(244, 246, 250);
        private static readonly Color SelectedFocused = Color.FromArgb(219, 234, 254);
        private static readonly Color SelectedUnfocused = Color.FromArgb(235, 243, 255);
        private static readonly Color SecondaryText = Color.FromArgb(100, 116, 139);

        public Color DataColor = UiTheme.Text;

        public LogListView()
        {
            DoubleBuffered = true;
            OwnerDraw = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

            DrawColumnHeader += OnDrawColumnHeader;
            DrawItem += OnDrawItem;
            DrawSubItem += OnDrawSubItem;
        }

        private void OnDrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var brush = new SolidBrush(HeaderBackground))
                e.Graphics.FillRectangle(brush, e.Bounds);

            using (var pen = new Pen(UiTheme.Border))
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1,
                    e.Bounds.Right, e.Bounds.Bottom - 1);

            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

            var rect = e.Bounds;
            rect.Inflate(-8, 0);

            if (e.ColumnIndex == 1)
            {
                flags |= TextFormatFlags.Right;
            }
            else
            {
                flags |= TextFormatFlags.Left;
            }

            TextRenderer.DrawText(e.Graphics, e.Header.Text, UiTheme.UiBold, rect,
                SecondaryText, flags);
        }

        private void OnDrawItem(object sender, DrawListViewItemEventArgs e)
        {
            bool selected = (e.State & ListViewItemStates.Selected) != 0;
            Color background = selected
                ? (Focused ? SelectedFocused : SelectedUnfocused)
                : ((e.Item.Index & 1) == 0 ? UiTheme.Surface : Alternate);

            using (var brush = new SolidBrush(background))
                e.Graphics.FillRectangle(brush, e.Bounds);

            if (selected) return;

            using (var pen = new Pen(RowLine))
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1,
                    e.Bounds.Right, e.Bounds.Bottom - 1);
        }

        private void OnDrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            if (e.Item == null || e.SubItem == null) return;

            bool selected = (e.ItemState & ListViewItemStates.Selected) != 0;
            var entry = e.Item.Tag as LogEntry;
            int column = e.ColumnIndex;

            Color color = column == 2
                ? DataColor
                : (selected ? UiTheme.Text : SecondaryText);

            var font = column == 2 && entry != null && entry.IsManual ? ManualFont : Font;

            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

            var rect = e.Bounds;

            if (column == 1)
            {
                flags |= TextFormatFlags.Right;
                rect.Width -= 8;
            }
            else if (column == 2)
            {
                flags |= TextFormatFlags.Left;
                rect.X += 8;
                rect.Width -= 16;
            }
            else
            {
                flags |= TextFormatFlags.Left;
                rect.X += 8;
                rect.Width -= 12;
            }

            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, font, rect, color, flags);
        }
    }
}
