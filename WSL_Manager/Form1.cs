using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace WslManagerFramework
{
    public class AvailableDistro
    {
        public string Name { get; set; }
        public string FriendlyName { get; set; }
        public string Description { get; set; }
        public bool IsInstalled { get; set; }
        
        public AvailableDistro(string name, string friendlyName, string description)
        {
            Name = name;
            FriendlyName = friendlyName;
            Description = description;
            IsInstalled = false;
        }
    }

    public class InstallProgress
    {
        public Process InstallProcess { get; set; }
        public CancellationTokenSource CancellationTokenSource { get; set; }
        public bool IsPaused { get; set; }
        public ProgressBar ProgressBar { get; set; }
        public Button StopButton { get; set; }
        public Button PauseButton { get; set; }
        public Label StatusLabel { get; set; }
        public Panel ControlPanel { get; set; }
        
        public InstallProgress()
        {
            CancellationTokenSource = new CancellationTokenSource();
            IsPaused = false;
        }
    }

    [Serializable]
    public class AppSettings
    {
        public bool MinimizeToTray { get; set; } = true;
        
        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WSLManager", "settings.xml");

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new AppSettings();

                var serializer = new XmlSerializer(typeof(AppSettings));
                using (var reader = new FileStream(SettingsPath, FileMode.Open))
                {
                    return (AppSettings)serializer.Deserialize(reader);
                }
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var serializer = new XmlSerializer(typeof(AppSettings));
                using (var writer = new FileStream(SettingsPath, FileMode.Create))
                {
                    serializer.Serialize(writer, this);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定の保存に失敗しました: {ex.Message}", "エラー", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
    public class DistroRow
    {
        public string DistroName { get; }
        public Panel Panel { get; }
        public Label StatusLabel { get; }
        public Button BackgroundButton { get; }
        public Button LaunchButton { get; }
        public Button DropdownButton { get; }
        
        private Func<string, Task> _launchAction;
        private Func<string, Task> _stopAction;
        private System.Action<string> _updateStatusAction;
        private ToolTip _tooltip;

        public DistroRow(string distroName, Panel panel, Label statusLabel, Button backgroundButton, Button launchButton, Button dropdownButton, 
                        Func<string, Task> launchAction, Func<string, Task> stopAction, System.Action<string> updateStatusAction, ToolTip tooltip)
        {
            DistroName = distroName;
            Panel = panel;
            StatusLabel = statusLabel;
            BackgroundButton = backgroundButton;
            LaunchButton = launchButton;
            DropdownButton = dropdownButton;
            _launchAction = launchAction;
            _stopAction = stopAction;
            _updateStatusAction = updateStatusAction;
            _tooltip = tooltip;
            
            // 初期化時にイベントハンドラを設定
            BackgroundButton.Click += OnBackgroundButtonClick;
        }

        public void UpdateStatus(string newStatus)
        {
            StatusLabel.Text = newStatus;
            
            // ステータスに応じて色を変更
            switch (newStatus.ToLower())
            {
                case "running":
                    StatusLabel.ForeColor = System.Drawing.Color.FromArgb(92, 184, 92); // 緑色
                    StatusLabel.Text = "Running";
                    break;
                case "stopped":
                    StatusLabel.ForeColor = System.Drawing.Color.FromArgb(217, 83, 79); // 赤色
                    StatusLabel.Text = "Stopped";
                    break;
                default:
                    StatusLabel.ForeColor = System.Drawing.Color.FromArgb(204, 204, 204); // グレー
                    StatusLabel.Text = "Unknown";
                    break;
            }

            UpdateBackgroundButton(newStatus);
        }

        private void UpdateBackgroundButton(string status)
        {
            // 既存のイベントハンドラをクリア
            BackgroundButton.Click -= OnBackgroundButtonClick;
            
            if (status.ToLower() == "running")
            {
                BackgroundButton.Text = "停止";
                BackgroundButton.BackColor = System.Drawing.Color.FromArgb(217, 83, 79);
                BackgroundButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(217, 83, 79);
                _tooltip.SetToolTip(BackgroundButton, "WSLを停止します");
            }
            else
            {
                BackgroundButton.Text = "起動";
                BackgroundButton.BackColor = System.Drawing.Color.FromArgb(46, 125, 50);
                BackgroundButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(46, 125, 50);
                _tooltip.SetToolTip(BackgroundButton, "WSLをバックグラウンドで起動します");
            }
            
            // 新しいイベントハンドラを追加
            BackgroundButton.Click += OnBackgroundButtonClick;
        }

        private async void OnBackgroundButtonClick(object sender, EventArgs e)
        {
            // ボタンを無効化して二重実行を防止
            BackgroundButton.Enabled = false;
            var originalText = BackgroundButton.Text;
            BackgroundButton.Text = "処理中...";
            
            try
            {
                if (originalText == "停止")
                {
                    await _stopAction(DistroName);
                }
                else
                {
                    await _launchAction(DistroName);
                }
                
                // 少し待ってからステータス更新
                await Task.Delay(1000);
                
                // UIスレッドでステータス更新を実行
                if (BackgroundButton.InvokeRequired)
                {
                    BackgroundButton.Invoke(new Action(() => _updateStatusAction(DistroName)));
                }
                else
                {
                    _updateStatusAction(DistroName);
                }
            }
            finally
            {
                // ボタンを再有効化
                BackgroundButton.Enabled = true;
            }
        }
    }

    public partial class Form1 : Form
    {
        private readonly TabControl _tabControl = new TabControl();
        private readonly FlowLayoutPanel _panel = new FlowLayoutPanel();
        private readonly Button _refresh = new Button();
        private readonly Label _status = new Label();
        private readonly ToolTip _tip = new ToolTip();
        private readonly TextBox _searchBox = new TextBox();
        private readonly Label _searchLabel = new Label();
        private readonly RichTextBox _logBox = new RichTextBox();
        private string[] _allDistros = new string[0];
        private readonly System.Collections.Generic.Dictionary<string, DistroRow> _distroRows = new System.Collections.Generic.Dictionary<string, DistroRow>();
        private readonly NotifyIcon _notifyIcon = new NotifyIcon();
        private readonly ContextMenuStrip _trayContextMenu = new ContextMenuStrip();
        
        // 設定関連
        private AppSettings _settings;
        private CheckBox _minimizeToTrayCheckBox;
        
        // インストール関連
        private FlowLayoutPanel _installPanel = new FlowLayoutPanel();
        private Button _refreshAvailable = new Button();
        private Label _installStatus = new Label();
        private List<AvailableDistro> _availableDistros = new List<AvailableDistro>();
        private Dictionary<string, InstallProgress> _activeInstalls = new Dictionary<string, InstallProgress>();

        public Form1()
        {
            InitializeComponent();

            Text = "WSL Manager";
            Width = 800;
            Height = 500; // タブ分の高さを追加

            // 設定を読み込み
            _settings = AppSettings.Load();

            // ウィンドウアイコンの設定
            var iconPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "tax.ico");
            if (System.IO.File.Exists(iconPath))
            {
                Icon = new System.Drawing.Icon(iconPath);
            }

            // ダークテーマの設定
            BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            ForeColor = System.Drawing.Color.White;

            InitializeTabs();
        }

        private void InitializeTabs()
        {
            // TabControl の設定
            _tabControl.Dock = DockStyle.Fill;
            _tabControl.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            _tabControl.ForeColor = System.Drawing.Color.White;
            Controls.Add(_tabControl);

            // メインタブ
            var mainTab = new TabPage("コンソール");
            mainTab.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            mainTab.ForeColor = System.Drawing.Color.White;
            _tabControl.TabPages.Add(mainTab);

            // インストールタブ
            var installTab = new TabPage("インストール");
            installTab.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            installTab.ForeColor = System.Drawing.Color.White;
            _tabControl.TabPages.Add(installTab);

            // 設定タブ
            var settingsTab = new TabPage("設定");
            settingsTab.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            settingsTab.ForeColor = System.Drawing.Color.White;
            _tabControl.TabPages.Add(settingsTab);

            InitializeMainTab(mainTab);
            InitializeInstallTab(installTab);
            InitializeSettingsTab(settingsTab);
            
            // デフォルトでメインタブを選択
            _tabControl.SelectedIndex = 0;
            
            InitializeNotifyIcon();
            
            // ウィンドウイベントの設定
            WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            Resize += Form1_Resize;
            FormClosing += Form1_FormClosing;
            Load += Form1_Load;

            // フォーム表示時に初回読み込み（1回だけ実行）
            Shown += OnFormShown;
        }

        private void InitializeMainTab(TabPage mainTab)
        {
            // 上部バー
            _refresh.Text = "↺";
            _refresh.Size = new System.Drawing.Size(35, 30);
            _refresh.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular);
            _refresh.BackColor = System.Drawing.Color.FromArgb(63, 63, 70);
            _refresh.ForeColor = System.Drawing.Color.White;
            _refresh.FlatStyle = FlatStyle.Flat;
            _refresh.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(104, 104, 104);
            _refresh.Click += (_, __) => Reload();   // 非async版
            _tip.SetToolTip(_refresh, "ディストリリストを手動更新");

            _searchLabel.Text = "検索:";
            _searchLabel.AutoSize = true;
            _searchLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular);
            _searchLabel.ForeColor = System.Drawing.Color.White;
            _searchLabel.Margin = new Padding(8, 12, 4, 8);

            _searchBox.Width = 200;
            _searchBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular);
            _searchBox.BackColor = System.Drawing.Color.FromArgb(62, 62, 66);
            _searchBox.ForeColor = System.Drawing.Color.White;
            _searchBox.BorderStyle = BorderStyle.FixedSingle;
            _searchBox.Margin = new Padding(4, 8, 8, 8);
            _searchBox.TextChanged += (_, __) => FilterDistros();

            _status.AutoSize = true;
            _status.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular);
            _status.ForeColor = System.Drawing.Color.FromArgb(204, 204, 204);
            _status.Margin = new Padding(8);

            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 48,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(8),
                AutoSize = true,
                BackColor = System.Drawing.Color.FromArgb(45, 45, 48)
            };
            top.Controls.Add(_refresh);
            top.Controls.Add(_searchLabel);
            top.Controls.Add(_searchBox);
            top.Controls.Add(_status);

            // 一覧エリア
            _panel.Dock = DockStyle.Fill;
            _panel.FlowDirection = FlowDirection.TopDown;
            _panel.WrapContents = false;
            _panel.AutoScroll = true;
            _panel.Padding = new Padding(8);
            _panel.BackColor = System.Drawing.Color.FromArgb(37, 37, 38);

            // ログエリア
            _logBox.Dock = DockStyle.Bottom;
            _logBox.Height = 120;
            _logBox.ReadOnly = true;
            _logBox.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
            _logBox.ForeColor = System.Drawing.Color.White;
            _logBox.Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Regular);
            _logBox.BorderStyle = BorderStyle.FixedSingle;
            _logBox.ScrollBars = RichTextBoxScrollBars.Vertical;

            mainTab.Controls.Add(_panel);
            mainTab.Controls.Add(_logBox);
            mainTab.Controls.Add(top);
        }

        private void InitializeInstallTab(TabPage installTab)
        {
            // 上部バー
            _refreshAvailable.Text = "↺";
            _refreshAvailable.Size = new System.Drawing.Size(35, 30);
            _refreshAvailable.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular);
            _refreshAvailable.BackColor = System.Drawing.Color.FromArgb(63, 63, 70);
            _refreshAvailable.ForeColor = System.Drawing.Color.White;
            _refreshAvailable.FlatStyle = FlatStyle.Flat;
            _refreshAvailable.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(104, 104, 104);
            _refreshAvailable.Click += (_, __) => RefreshAvailableDistros();
            _tip.SetToolTip(_refreshAvailable, "利用可能なディストリ一覧を更新");

            _installStatus.AutoSize = true;
            _installStatus.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular);
            _installStatus.ForeColor = System.Drawing.Color.FromArgb(204, 204, 204);
            _installStatus.Margin = new Padding(8);
            _installStatus.Text = "利用可能なディストリ一覧";

            var installTop = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 48,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(8),
                AutoSize = true,
                BackColor = System.Drawing.Color.FromArgb(45, 45, 48)
            };
            installTop.Controls.Add(_refreshAvailable);
            installTop.Controls.Add(_installStatus);

            // インストール可能ディストリ一覧エリア
            _installPanel.Dock = DockStyle.Fill;
            _installPanel.FlowDirection = FlowDirection.TopDown;
            _installPanel.WrapContents = false;
            _installPanel.AutoScroll = true;
            _installPanel.Padding = new Padding(8);
            _installPanel.BackColor = System.Drawing.Color.FromArgb(37, 37, 38);

            installTab.Controls.Add(_installPanel);
            installTab.Controls.Add(installTop);

            // 初期データを設定
            InitializeAvailableDistros();
            DisplayAvailableDistros();
        }

        private void InitializeSettingsTab(TabPage settingsTab)
        {
            var settingsPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20),
                BackColor = System.Drawing.Color.FromArgb(37, 37, 38)
            };

            // タスクトレイ最小化設定
            var minimizeLabel = new Label
            {
                Text = "ウィンドウ設定",
                Location = new System.Drawing.Point(20, 20),
                Size = new System.Drawing.Size(200, 25),
                Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold),
                ForeColor = System.Drawing.Color.White,
                BackColor = System.Drawing.Color.Transparent
            };

            _minimizeToTrayCheckBox = new CheckBox
            {
                Text = "最小化時にタスクトレイに格納する",
                Location = new System.Drawing.Point(20, 60),
                Size = new System.Drawing.Size(300, 25),
                Font = new System.Drawing.Font("Microsoft Sans Serif", 10F),
                ForeColor = System.Drawing.Color.White,
                BackColor = System.Drawing.Color.Transparent,
                Checked = _settings.MinimizeToTray
            };
            _minimizeToTrayCheckBox.CheckedChanged += OnMinimizeToTrayChanged;

            var saveButton = new Button
            {
                Text = "設定を保存",
                Location = new System.Drawing.Point(20, 120),
                Size = new System.Drawing.Size(120, 35),
                Font = new System.Drawing.Font("Microsoft Sans Serif", 10F),
                BackColor = System.Drawing.Color.FromArgb(0, 122, 204),
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat
            };
            saveButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(0, 122, 204);
            saveButton.Click += OnSaveSettingsClick;

            var resetButton = new Button
            {
                Text = "初期設定に戻す",
                Location = new System.Drawing.Point(150, 120),
                Size = new System.Drawing.Size(120, 35),
                Font = new System.Drawing.Font("Microsoft Sans Serif", 10F),
                BackColor = System.Drawing.Color.FromArgb(217, 83, 79),
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat
            };
            resetButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(217, 83, 79);
            resetButton.Click += OnResetSettingsClick;

            settingsPanel.Controls.Add(minimizeLabel);
            settingsPanel.Controls.Add(_minimizeToTrayCheckBox);
            settingsPanel.Controls.Add(saveButton);
            settingsPanel.Controls.Add(resetButton);

            settingsTab.Controls.Add(settingsPanel);
        }

        private void OnMinimizeToTrayChanged(object sender, EventArgs e)
        {
            _settings.MinimizeToTray = _minimizeToTrayCheckBox.Checked;
        }

        private void OnSaveSettingsClick(object sender, EventArgs e)
        {
            _settings.Save();
            MessageBox.Show("設定を保存しました。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnResetSettingsClick(object sender, EventArgs e)
        {
            var result = MessageBox.Show("設定を初期値に戻しますか？", "確認", 
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            
            if (result == DialogResult.Yes)
            {
                _settings = new AppSettings();
                _minimizeToTrayCheckBox.Checked = _settings.MinimizeToTray;
                MessageBox.Show("設定を初期値に戻しました。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void InitializeNotifyIcon()
        {
            // タスクトレイアイコンの設定
            var iconPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "tax.ico");
            if (System.IO.File.Exists(iconPath))
            {
                _notifyIcon.Icon = new System.Drawing.Icon(iconPath);
            }
            else
            {
                _notifyIcon.Icon = System.Drawing.SystemIcons.Information;
            }
            _notifyIcon.Text = "WSL Manager";
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += (_, __) => ShowWindow();
            
            // タスクトレイコンテキストメニューの設定
            _trayContextMenu.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            _trayContextMenu.ForeColor = System.Drawing.Color.White;
            _notifyIcon.ContextMenuStrip = _trayContextMenu;
        }

        private bool _initialLoadCompleted = false;

        private void Form1_Load(object sender, EventArgs e)
        {
            // フォームのLoad時は何もしない（Shownイベントで処理）
            AddLog("フォームが読み込まれました。", System.Drawing.Color.LightYellow);
        }

        private async void OnFormShown(object sender, EventArgs e)
        {
            if (_initialLoadCompleted)
                return;

            _initialLoadCompleted = true;
            
            AddLog("WSL Manager を起動しました。", System.Drawing.Color.LightYellow);
            // UI初期化完了を待つ
            await Task.Delay(100);
            Reload();
            UpdateTrayMenu();
        }

        private void InitializeAvailableDistros()
        {
            // コマンドから利用可能なディストリを取得
            RefreshAvailableDistros();
        }

        private List<AvailableDistro> GetAvailableDistrosFromCommand()
        {
            var availableDistros = new List<AvailableDistro>();
            
            try
            {
                var systemDir = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                    ? @"C:\Windows\Sysnative"
                    : @"C:\Windows\System32";
                var wslPath = System.IO.Path.Combine(systemDir, "wsl.exe");

                var psi = new ProcessStartInfo
                {
                    FileName = wslPath,
                    Arguments = "--list --online",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.Unicode
                };

                using (var process = Process.Start(psi))
                {
                    if (process != null)
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit();

                        if (process.ExitCode == 0)
                        {
                            var cleanOutput = output.Replace("\0", "");
                            SafeAddLog($"WSL --list --online 出力:\n{cleanOutput}", System.Drawing.Color.Cyan);
                            
                            var lines = cleanOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                            SafeAddLog($"解析対象行数: {lines.Length}", System.Drawing.Color.Gray);

                            bool dataStarted = false;
                            for (int i = 0; i < lines.Length; i++)
                            {
                                var line = lines[i];
                                var trimmedLine = line.Trim();
                                SafeAddLog($"行 {i}: '{trimmedLine}'", System.Drawing.Color.Gray);
                                
                                // 説明行やヘッダー行をスキップ
                                if (trimmedLine.Contains("インストールできる有効なディストリビューション") ||
                                    trimmedLine.Contains("wsl.exe --install") ||
                                    trimmedLine.Contains("NAME") ||
                                    trimmedLine.Contains("----") ||
                                    string.IsNullOrWhiteSpace(trimmedLine))
                                {
                                    if (trimmedLine.Contains("NAME"))
                                    {
                                        dataStarted = true;
                                        SafeAddLog("ヘッダー行検出、データ開始", System.Drawing.Color.Yellow);
                                    }
                                    continue;
                                }

                                // ヘッダー行の後はすべてディストリデータ
                                if (dataStarted)
                                {
                                    // 行の形式を柔軟に解析
                                    var parts = trimmedLine.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
                                    SafeAddLog($"パーツ数: {parts.Length}, パーツ: [{string.Join("], [", parts)}]", System.Drawing.Color.LightBlue);
                                    
                                    if (parts.Length >= 1)
                                    {
                                        var name = parts[0];
                                        var friendlyName = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : name;
                                        
                                        SafeAddLog($"ディストリ追加: {name} ({friendlyName})", System.Drawing.Color.LightGreen);
                                        availableDistros.Add(new AvailableDistro(name, friendlyName, $"{friendlyName} ディストリビューション"));
                                    }
                                }
                            }
                        }
                        else
                        {
                            SafeAddLog($"WSLコマンド終了コード: {process.ExitCode}", System.Drawing.Color.Orange);
                            SafeAddLog($"標準出力: {output}", System.Drawing.Color.Orange);
                            SafeAddLog($"エラー出力: {error}", System.Drawing.Color.LightCoral);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SafeAddLog($"利用可能ディストリ取得例外: {ex.Message}", System.Drawing.Color.LightCoral);
            }

            return availableDistros;
        }

        private void RefreshAvailableDistros()
        {
            try
            {
                _installStatus.Text = "確認中...";
                
                // コマンドから利用可能なディストリを取得
                _availableDistros = GetAvailableDistrosFromCommand();
                
                // 現在インストールされているディストリを取得
                var installedDistros = ListWslDistros();
                
                // インストール状況を更新
                foreach (var distro in _availableDistros)
                {
                    distro.IsInstalled = installedDistros.Contains(distro.Name, StringComparer.OrdinalIgnoreCase);
                }
                
                DisplayAvailableDistros();
                _installStatus.Text = $"利用可能なディストリ: {_availableDistros.Count}個";
                
                if (_availableDistros.Count == 0)
                {
                    SafeAddLog("利用可能なディストリが見つかりませんでした。インターネット接続を確認してください。", System.Drawing.Color.Orange);
                }
            }
            catch (Exception ex)
            {
                _installStatus.Text = "確認に失敗しました。";
                SafeAddLog($"利用可能ディストリ確認エラー: {ex.Message}", System.Drawing.Color.LightCoral);
            }
        }

        private void DisplayAvailableDistros()
        {
            _installPanel.Controls.Clear();
            
            foreach (var distro in _availableDistros)
            {
                _installPanel.Controls.Add(MakeAvailableDistroRow(distro));
            }
        }

        private Control MakeAvailableDistroRow(AvailableDistro distro)
        {
            var row = new Panel
            {
                Height = 60, // プログレスバー非表示時の高さ
                Width = 750,
                Padding = new Padding(6),
                Margin = new Padding(2),
                BackColor = System.Drawing.Color.FromArgb(51, 51, 55),
            };

            var lblName = new Label
            {
                Text = distro.FriendlyName,
                Location = new System.Drawing.Point(8, 8),
                Size = new System.Drawing.Size(250, 18),
                Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold),
                ForeColor = System.Drawing.Color.White,
                BackColor = System.Drawing.Color.Transparent,
            };

            var lblDesc = new Label
            {
                Text = distro.Description,
                Location = new System.Drawing.Point(8, 28),
                Size = new System.Drawing.Size(400, 16),
                Font = new System.Drawing.Font("Microsoft Sans Serif", 7.5F, System.Drawing.FontStyle.Regular),
                ForeColor = System.Drawing.Color.LightGray,
                BackColor = System.Drawing.Color.Transparent,
            };

            // プログレスバー（初期は非表示）
            var progressBar = new ProgressBar
            {
                Location = new System.Drawing.Point(8, 50),
                Size = new System.Drawing.Size(350, 16),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 50,
                Visible = false
            };

            // ステータスラベル（初期は非表示）
            var statusLabel = new Label
            {
                Location = new System.Drawing.Point(365, 50),
                Size = new System.Drawing.Size(80, 16),
                Font = new System.Drawing.Font("Microsoft Sans Serif", 7.5F, System.Drawing.FontStyle.Regular),
                ForeColor = System.Drawing.Color.Yellow,
                BackColor = System.Drawing.Color.Transparent,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Visible = false
            };

            var btnInstall = new Button
            {
                Location = new System.Drawing.Point(600, 12),
                Size = new System.Drawing.Size(80, 32),
                Font = new System.Drawing.Font("Microsoft Sans Serif", 8F, System.Drawing.FontStyle.Regular),
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            };

            btnInstall.Text = "インストール";
            btnInstall.BackColor = System.Drawing.Color.FromArgb(0, 122, 204);
            btnInstall.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(0, 122, 204);
            btnInstall.Click += async (_, __) => await InstallDistroWithNamePromptAsync(distro, row, btnInstall, progressBar, statusLabel);

            // 制御パネル（初期は非表示）
            var controlPanel = new Panel
            {
                Location = new System.Drawing.Point(450, 50),
                Size = new System.Drawing.Size(140, 24),
                Visible = false
            };

            var stopButton = new Button
            {
                Text = "停止",
                Location = new System.Drawing.Point(0, 0),
                Size = new System.Drawing.Size(45, 22),
                Font = new System.Drawing.Font("Microsoft Sans Serif", 7F),
                BackColor = System.Drawing.Color.FromArgb(217, 83, 79),
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat
            };

            var pauseButton = new Button
            {
                Text = "一時停止",
                Location = new System.Drawing.Point(50, 0),
                Size = new System.Drawing.Size(60, 22),
                Font = new System.Drawing.Font("Microsoft Sans Serif", 7F),
                BackColor = System.Drawing.Color.FromArgb(240, 173, 78),
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat
            };

            controlPanel.Controls.Add(stopButton);
            controlPanel.Controls.Add(pauseButton);

            row.Controls.Add(lblName);
            row.Controls.Add(lblDesc);
            row.Controls.Add(progressBar);
            row.Controls.Add(statusLabel);
            row.Controls.Add(btnInstall);
            row.Controls.Add(controlPanel);

            return row;
        }

        private async Task InstallDistroWithNamePromptAsync(AvailableDistro distro, Panel row, Button installButton, 
            ProgressBar progressBar, Label statusLabel)
        {
            // デフォルト名の決定
            string defaultName = distro.Name;
            var installedDistros = ListWslDistros();
            
            // 既にインストール済みの場合はデフォルト名を変更
            if (installedDistros.Contains(distro.Name, StringComparer.OrdinalIgnoreCase))
            {
                defaultName = $"{distro.Name}-2";
                int counter = 2;
                while (installedDistros.Contains(defaultName, StringComparer.OrdinalIgnoreCase))
                {
                    counter++;
                    defaultName = $"{distro.Name}-{counter}";
                }
            }

            // 名前入力ダイアログ
            string installName = PromptForInstallName(distro, defaultName);
            if (string.IsNullOrWhiteSpace(installName))
            {
                return; // キャンセルされた
            }

            // 既存の名前と重複チェック
            if (installedDistros.Contains(installName, StringComparer.OrdinalIgnoreCase))
            {
                MessageBox.Show($"'{installName}' は既に使用されています。別の名前を指定してください。", 
                    "名前の重複", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // インストール進行状況の表示を開始
            var controlPanel = row.Controls.OfType<Panel>().FirstOrDefault();
            var stopButton = controlPanel?.Controls.OfType<Button>().FirstOrDefault(b => b.Text == "停止");
            var pauseButton = controlPanel?.Controls.OfType<Button>().FirstOrDefault(b => b.Text == "一時停止");

            var installProgress = new InstallProgress
            {
                ProgressBar = progressBar,
                StatusLabel = statusLabel,
                StopButton = stopButton,
                PauseButton = pauseButton,
                ControlPanel = controlPanel
            };

            // プログレスUIを表示
            ShowInstallProgress(installProgress, distro.FriendlyName, installName);
            
            // ボタンの状態を変更
            installButton.Text = "インストール中...";
            installButton.Enabled = false;

            // 進行中のインストールに追加
            _activeInstalls[installName] = installProgress;

            // ボタンイベントの設定
            stopButton.Click += (_, __) => StopInstall(installName);
            pauseButton.Click += (_, __) => TogglePauseInstall(installName);

            try
            {
                SafeAddLog($"{distro.FriendlyName} を '{installName}' としてインストール開始...", System.Drawing.Color.Yellow);
                
                if (installName == distro.Name)
                {
                    // デフォルト名の場合は通常のインストール
                    await Task.Run(() => InstallDistroWithProgress(distro.Name, installProgress, installName));
                }
                else
                {
                    // カスタム名の場合はカスタム名インストール
                    await Task.Run(() => InstallDistroWithCustomNameAndProgress(distro.Name, installName, installProgress));
                }
                
                SafeAddLog($"{distro.FriendlyName} を '{installName}' としてインストール完了しました。", System.Drawing.Color.LightGreen);
                
                // メインタブの一覧を更新
                Reload();
            }
            catch (OperationCanceledException)
            {
                SafeAddLog($"{distro.FriendlyName} のインストールが停止されました。", System.Drawing.Color.Orange);
            }
            catch (Exception ex)
            {
                SafeAddLog($"{distro.FriendlyName} のインストールに失敗しました: {ex.Message}", System.Drawing.Color.LightCoral);
            }
            finally
            {
                // プログレスUIを非表示にして元に戻す
                HideInstallProgress(installProgress);
                installButton.Text = "インストール";
                installButton.Enabled = true;
                _activeInstalls.Remove(installName);
            }
        }

        private string PromptForInstallName(AvailableDistro distro, string defaultName)
        {
            var form = new Form
            {
                Text = "インストール名を入力",
                Size = new System.Drawing.Size(450, 200),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = System.Drawing.Color.FromArgb(45, 45, 48),
                ForeColor = System.Drawing.Color.White
            };

            var titleLabel = new Label
            {
                Text = $"{distro.FriendlyName} をインストールします",
                Location = new System.Drawing.Point(20, 15),
                Size = new System.Drawing.Size(400, 20),
                Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Bold),
                ForeColor = System.Drawing.Color.White
            };

            var label = new Label
            {
                Text = "インストール名を入力してください:",
                Location = new System.Drawing.Point(20, 45),
                Size = new System.Drawing.Size(350, 20),
                ForeColor = System.Drawing.Color.White
            };

            var textBox = new TextBox
            {
                Text = defaultName,
                Location = new System.Drawing.Point(20, 75),
                Size = new System.Drawing.Size(400, 25),
                BackColor = System.Drawing.Color.FromArgb(62, 62, 66),
                ForeColor = System.Drawing.Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            textBox.SelectAll(); // テキストを全選択

            var okButton = new Button
            {
                Text = "インストール",
                Location = new System.Drawing.Point(250, 120),
                Size = new System.Drawing.Size(80, 30),
                DialogResult = DialogResult.OK,
                BackColor = System.Drawing.Color.FromArgb(0, 122, 204),
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat
            };

            var cancelButton = new Button
            {
                Text = "キャンセル",
                Location = new System.Drawing.Point(340, 120),
                Size = new System.Drawing.Size(80, 30),
                DialogResult = DialogResult.Cancel,
                BackColor = System.Drawing.Color.FromArgb(108, 108, 108),
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat
            };

            form.Controls.AddRange(new Control[] { titleLabel, label, textBox, okButton, cancelButton });
            form.AcceptButton = okButton;
            form.CancelButton = cancelButton;

            return form.ShowDialog(this) == DialogResult.OK ? textBox.Text.Trim() : null;
        }

        private void ShowInstallProgress(InstallProgress progress, string friendlyName, string installName)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => ShowInstallProgress(progress, friendlyName, installName)));
                return;
            }

            // パネルの高さを拡張
            var parentPanel = progress.ProgressBar.Parent;
            parentPanel.Height = 90;

            progress.ProgressBar.Visible = true;
            progress.StatusLabel.Visible = true;
            progress.ControlPanel.Visible = true;
            progress.StatusLabel.Text = "準備中...";
        }

        private void HideInstallProgress(InstallProgress progress)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => HideInstallProgress(progress)));
                return;
            }

            // パネルの高さを元に戻す
            var parentPanel = progress.ProgressBar.Parent;
            parentPanel.Height = 60;

            progress.ProgressBar.Visible = false;
            progress.StatusLabel.Visible = false;
            progress.ControlPanel.Visible = false;
        }

        private void UpdateInstallStatus(string installName, string status)
        {
            if (_activeInstalls.ContainsKey(installName))
            {
                var progress = _activeInstalls[installName];
                if (InvokeRequired)
                {
                    Invoke(new Action(() => UpdateInstallStatus(installName, status)));
                    return;
                }
                progress.StatusLabel.Text = status;
            }
        }

        private void StopInstall(string installName)
        {
            if (_activeInstalls.ContainsKey(installName))
            {
                var progress = _activeInstalls[installName];
                progress.CancellationTokenSource.Cancel();
                
                if (progress.InstallProcess != null && !progress.InstallProcess.HasExited)
                {
                    try
                    {
                        progress.InstallProcess.Kill();
                        SafeAddLog($"インストールプロセス '{installName}' を停止しました。", System.Drawing.Color.Orange);
                    }
                    catch (Exception ex)
                    {
                        SafeAddLog($"インストール停止エラー: {ex.Message}", System.Drawing.Color.LightCoral);
                    }
                }
            }
        }

        private void TogglePauseInstall(string installName)
        {
            if (_activeInstalls.ContainsKey(installName))
            {
                var progress = _activeInstalls[installName];
                
                if (progress.IsPaused)
                {
                    // 再開
                    ResumeProcess(progress.InstallProcess);
                    progress.IsPaused = false;
                    progress.PauseButton.Text = "一時停止";
                    progress.PauseButton.BackColor = System.Drawing.Color.FromArgb(240, 173, 78);
                    
                    // プログレスバーを再開
                    if (InvokeRequired)
                    {
                        Invoke(new Action(() => {
                            progress.ProgressBar.Style = ProgressBarStyle.Marquee;
                            progress.ProgressBar.MarqueeAnimationSpeed = 50;
                        }));
                    }
                    else
                    {
                        progress.ProgressBar.Style = ProgressBarStyle.Marquee;
                        progress.ProgressBar.MarqueeAnimationSpeed = 50;
                    }
                    
                    UpdateInstallStatus(installName, "インストール中...");
                    SafeAddLog($"インストール '{installName}' を再開しました。", System.Drawing.Color.Yellow);
                }
                else
                {
                    // 一時停止
                    SuspendProcess(progress.InstallProcess);
                    progress.IsPaused = true;
                    progress.PauseButton.Text = "再開";
                    progress.PauseButton.BackColor = System.Drawing.Color.FromArgb(92, 184, 92);
                    
                    // プログレスバーを停止
                    if (InvokeRequired)
                    {
                        Invoke(new Action(() => {
                            progress.ProgressBar.Style = ProgressBarStyle.Blocks;
                            progress.ProgressBar.MarqueeAnimationSpeed = 0;
                        }));
                    }
                    else
                    {
                        progress.ProgressBar.Style = ProgressBarStyle.Blocks;
                        progress.ProgressBar.MarqueeAnimationSpeed = 0;
                    }
                    
                    UpdateInstallStatus(installName, "一時停止中");
                    SafeAddLog($"インストール '{installName}' を一時停止しました。", System.Drawing.Color.Yellow);
                }
            }
        }

        private void SuspendProcess(Process process)
        {
            if (process == null || process.HasExited) return;
            
            try
            {
                foreach (ProcessThread thread in process.Threads)
                {
                    var threadHandle = OpenThread(2, false, (uint)thread.Id);
                    if (threadHandle != IntPtr.Zero)
                    {
                        SuspendThread(threadHandle);
                        CloseHandle(threadHandle);
                    }
                }
            }
            catch (Exception ex)
            {
                SafeAddLog($"プロセス一時停止エラー: {ex.Message}", System.Drawing.Color.LightCoral);
            }
        }

        private void ResumeProcess(Process process)
        {
            if (process == null || process.HasExited) return;
            
            try
            {
                foreach (ProcessThread thread in process.Threads)
                {
                    var threadHandle = OpenThread(2, false, (uint)thread.Id);
                    if (threadHandle != IntPtr.Zero)
                    {
                        ResumeThread(threadHandle);
                        CloseHandle(threadHandle);
                    }
                }
            }
            catch (Exception ex)
            {
                SafeAddLog($"プロセス再開エラー: {ex.Message}", System.Drawing.Color.LightCoral);
            }
        }

        // Win32 API宣言
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);
        
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint SuspendThread(IntPtr hThread);
        
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint ResumeThread(IntPtr hThread);
        
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        private void InstallDistro(string distroName)
        {
            var systemDir = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                ? @"C:\Windows\Sysnative"
                : @"C:\Windows\System32";
            var wslPath = System.IO.Path.Combine(systemDir, "wsl.exe");

            var psi = new ProcessStartInfo
            {
                FileName = wslPath,
                Arguments = $"--install -d {distroName}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };

            using (var process = Process.Start(psi))
            {
                if (process != null)
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        string error = process.StandardError.ReadToEnd();
                        throw new Exception($"インストールプロセスが失敗しました (終了コード: {process.ExitCode}): {error}");
                    }
                }
            }
        }

        private void InstallDistroWithProgress(string distroName, InstallProgress progress, string installName)
        {
            var systemDir = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                ? @"C:\Windows\Sysnative"
                : @"C:\Windows\System32";
            var wslPath = System.IO.Path.Combine(systemDir, "wsl.exe");

            var psi = new ProcessStartInfo
            {
                FileName = wslPath,
                Arguments = $"--install -d {distroName}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };

            using (var process = Process.Start(psi))
            {
                if (process != null)
                {
                    progress.InstallProcess = process;
                    UpdateInstallStatus(installName, "インストール中...");
                    
                    while (!process.HasExited)
                    {
                        if (progress.CancellationTokenSource.Token.IsCancellationRequested)
                        {
                            process.Kill();
                            throw new OperationCanceledException();
                        }
                        
                        System.Threading.Thread.Sleep(100);
                    }
                    
                    if (process.ExitCode != 0)
                    {
                        string error = process.StandardError.ReadToEnd();
                        throw new Exception($"インストールプロセスが失敗しました (終了コード: {process.ExitCode}): {error}");
                    }
                    
                    UpdateInstallStatus(installName, "完了");
                }
            }
        }

        private void InstallDistroWithCustomName(string distroName, string customName)
        {
            var systemDir = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                ? @"C:\Windows\Sysnative"
                : @"C:\Windows\System32";
            var wslPath = System.IO.Path.Combine(systemDir, "wsl.exe");

            var psi = new ProcessStartInfo
            {
                FileName = wslPath,
                Arguments = $"--install -d {distroName} --name {customName}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };

            using (var process = Process.Start(psi))
            {
                if (process != null)
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        string error = process.StandardError.ReadToEnd();
                        throw new Exception($"カスタム名インストールプロセスが失敗しました (終了コード: {process.ExitCode}): {error}");
                    }
                }
            }
        }

        private void InstallDistroWithCustomNameAndProgress(string distroName, string customName, InstallProgress progress)
        {
            var systemDir = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                ? @"C:\Windows\Sysnative"
                : @"C:\Windows\System32";
            var wslPath = System.IO.Path.Combine(systemDir, "wsl.exe");

            var psi = new ProcessStartInfo
            {
                FileName = wslPath,
                Arguments = $"--install -d {distroName} --name {customName}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };

            using (var process = Process.Start(psi))
            {
                if (process != null)
                {
                    progress.InstallProcess = process;
                    UpdateInstallStatus(customName, "インストール中...");
                    
                    while (!process.HasExited)
                    {
                        if (progress.CancellationTokenSource.Token.IsCancellationRequested)
                        {
                            process.Kill();
                            throw new OperationCanceledException();
                        }
                        
                        System.Threading.Thread.Sleep(100);
                    }
                    
                    if (process.ExitCode != 0)
                    {
                        string error = process.StandardError.ReadToEnd();
                        throw new Exception($"カスタム名インストールプロセスが失敗しました (終了コード: {process.ExitCode}): {error}");
                    }
                    
                    UpdateInstallStatus(customName, "完了");
                }
            }
        }

        private void UpdateTrayMenu()
        {
            _trayContextMenu.Items.Clear();

            try
            {
                var distros = ListWslDistros();
                
                if (distros.Length > 0)
                {
                    foreach (var distro in distros)
                    {
                        var status = GetDistroStatus(distro);
                        var distroItem = new ToolStripMenuItem(distro);
                        distroItem.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
                        distroItem.ForeColor = System.Drawing.Color.White;

                        if (status.ToLower() == "running")
                        {
                            var stopItem = new ToolStripMenuItem("停止");
                            stopItem.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
                            stopItem.ForeColor = System.Drawing.Color.LightCoral;
                            stopItem.Click += async (_, __) => 
                            {
                                await StopWslAsync(distro);
                                UpdateTrayMenu();
                            };
                            distroItem.DropDownItems.Add(stopItem);
                        }
                        else
                        {
                            var startItem = new ToolStripMenuItem("バックグラウンド起動");
                            startItem.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
                            startItem.ForeColor = System.Drawing.Color.LightGreen;
                            startItem.Click += async (_, __) => 
                            {
                                await LaunchWslBackgroundAsync(distro);
                                UpdateTrayMenu();
                            };
                            distroItem.DropDownItems.Add(startItem);
                        }

                        var cmdItem = new ToolStripMenuItem("cmdで開く");
                        cmdItem.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
                        cmdItem.ForeColor = System.Drawing.Color.White;
                        cmdItem.Click += (_, __) => LaunchInCmd(distro);
                        distroItem.DropDownItems.Add(cmdItem);

                        var directItem = new ToolStripMenuItem("直接WSL起動");
                        directItem.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
                        directItem.ForeColor = System.Drawing.Color.White;
                        directItem.Click += (_, __) => LaunchWslDirect(distro);
                        distroItem.DropDownItems.Add(directItem);

                        // ステータス表示
                        var statusText = status.ToLower() == "running" ? "Running" : "Stopped";
                        var statusColor = status.ToLower() == "running" 
                            ? System.Drawing.Color.LightGreen 
                            : System.Drawing.Color.LightCoral;
                        distroItem.Text = $"{distro} ({statusText})";
                        distroItem.ForeColor = statusColor;

                        _trayContextMenu.Items.Add(distroItem);
                    }

                    _trayContextMenu.Items.Add(new ToolStripSeparator());
                }
            }
            catch (Exception ex)
            {
                AddLog($"タスクトレイメニュー更新エラー: {ex.Message}", System.Drawing.Color.LightCoral);
            }

            // 固定メニュー項目
            var showItem = new ToolStripMenuItem("ウィンドウを表示");
            showItem.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            showItem.ForeColor = System.Drawing.Color.White;
            showItem.Click += (_, __) => ShowWindow();
            _trayContextMenu.Items.Add(showItem);

            var refreshItem = new ToolStripMenuItem("更新");
            refreshItem.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            refreshItem.ForeColor = System.Drawing.Color.White;
            refreshItem.Click += (_, __) => 
            {
                Reload();
                UpdateTrayMenu();
            };
            _trayContextMenu.Items.Add(refreshItem);

            var exitItem = new ToolStripMenuItem("終了");
            exitItem.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            exitItem.ForeColor = System.Drawing.Color.White;
            exitItem.Click += (_, __) => 
            {
                _notifyIcon.Visible = false;
                Application.Exit();
            };
            _trayContextMenu.Items.Add(exitItem);
        }

        private void ShowWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            BringToFront();
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized && _settings.MinimizeToTray)
            {
                Hide();
                ShowInTaskbar = false;
                _notifyIcon.ShowBalloonTip(2000, "WSL Manager", "タスクトレイに最小化されました", ToolTipIcon.Info);
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && _settings.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                _notifyIcon.ShowBalloonTip(2000, "WSL Manager", "タスクトレイに最小化されました", ToolTipIcon.Info);
            }
            else if (e.CloseReason == CloseReason.UserClosing)
            {
                _notifyIcon.Visible = false;
            }
        }

        private void AddLog(string message, System.Drawing.Color? color = null)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => AddLog(message, color)));
                return;
            }

            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logMessage = $"[{timestamp}] {message}\n";
            
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.SelectionLength = 0;
            _logBox.SelectionColor = color ?? System.Drawing.Color.White;
            _logBox.AppendText(logMessage);
            _logBox.ScrollToCaret();
        }

        /// <summary>
        /// スレッドセーフなログ追加メソッド
        /// </summary>
        private void SafeAddLog(string message, System.Drawing.Color? color = null)
        {
            if (InvokeRequired)
            {
                try
                {
                    Invoke(new Action(() => SafeAddLog(message, color)));
                }
                catch (ObjectDisposedException)
                {
                    // フォームが破棄されている場合は無視
                    return;
                }
                catch (InvalidOperationException)
                {
                    // コントロールのハンドルが作成されていない場合は無視
                    return;
                }
                return;
            }

            try
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                var logMessage = $"[{timestamp}] {message}\n";
                
                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.SelectionLength = 0;
                _logBox.SelectionColor = color ?? System.Drawing.Color.White;
                _logBox.AppendText(logMessage);
                _logBox.ScrollToCaret();
            }
            catch (ObjectDisposedException)
            {
                // コントロールが破棄されている場合は無視
            }
        }

        private void Reload()
        {
            try
            {
                UseWaitCursor = true;
                _status.Text = "取得中...";
                _panel.Controls.Clear();
                _distroRows.Clear();

                _allDistros = ListWslDistros(); // 同期取得
                if (_allDistros.Length == 0)
                {
                    _status.Text = "WSL ディストリが見つかりません。";
                    return;
                }

                DisplayDistros(_allDistros);
                _status.Text = $"ディストリ数: {_allDistros.Length}";
            }
            catch (Exception ex)
            {
                _status.Text = "取得に失敗しました。";
                MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private void DisplayDistros(string[] distros)
        {
            _panel.Controls.Clear();
            _distroRows.Clear();
            foreach (var name in distros)
            {
                _panel.Controls.Add(MakeDistroRow(name));
            }
        }

        private void UpdateDistroStatus(string distroName)
        {
            if (_distroRows.ContainsKey(distroName))
            {
                var newStatus = GetDistroStatus(distroName);
                _distroRows[distroName].UpdateStatus(newStatus);
                
                // タスクトレイメニューも更新
                UpdateTrayMenu();
            }
        }

        private void FilterDistros()
        {
            if (_allDistros.Length == 0) return;

            var searchText = _searchBox.Text.ToLower();
            if (string.IsNullOrWhiteSpace(searchText))
            {
                DisplayDistros(_allDistros);
                _status.Text = $"ディストリ数: {_allDistros.Length}";
            }
            else
            {
                var filtered = _allDistros.Where(d => d.ToLower().Contains(searchText)).ToArray();
                DisplayDistros(filtered);
                _status.Text = $"ディストリ数: {filtered.Length} / {_allDistros.Length} (検索: \"{_searchBox.Text}\")";
            }
        }

        private Control MakeDistroRow(string distroName)
        {
            var row = new Panel
            {
                Height = 60,
                Width = 750, // 幅を拡張してステータス列を追加
                Padding = new Padding(8),
                Margin = new Padding(4),
                BackColor = System.Drawing.Color.FromArgb(51, 51, 55),
            };

            var lbl = new Label
            {
                Text = distroName,
                Location = new System.Drawing.Point(10, 18),
                Size = new System.Drawing.Size(180, 24),
                Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                ForeColor = System.Drawing.Color.White,
                BackColor = System.Drawing.Color.Transparent,
            };

            // ステータス表示を追加
            var status = GetDistroStatus(distroName);
            var lblStatus = new Label
            {
                Text = status,
                Location = new System.Drawing.Point(200, 18),
                Size = new System.Drawing.Size(80, 24),
                Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold),
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                BackColor = System.Drawing.Color.Transparent,
            };

            // ステータスに応じて色を変更
            switch (status.ToLower())
            {
                case "running":
                    lblStatus.ForeColor = System.Drawing.Color.FromArgb(92, 184, 92); // 緑色
                    lblStatus.Text = "Running";
                    break;
                case "stopped":
                    lblStatus.ForeColor = System.Drawing.Color.FromArgb(217, 83, 79); // 赤色
                    lblStatus.Text = "Stopped";
                    break;
                default:
                    lblStatus.ForeColor = System.Drawing.Color.FromArgb(204, 204, 204); // グレー
                    lblStatus.Text = "Unknown";
                    break;
            }

            // バックグラウンド起動/停止ボタンを追加
            var btnBackground = new Button
            {
                Location = new System.Drawing.Point(290, 12),
                Size = new System.Drawing.Size(60, 36),
                Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Regular),
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            };

            // ステータスに応じてボタンの表示を変更（イベントハンドラはDistroRowクラスで管理）
            if (status.ToLower() == "running")
            {
                btnBackground.Text = "停止";
                btnBackground.BackColor = System.Drawing.Color.FromArgb(217, 83, 79);
                btnBackground.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(217, 83, 79);
            }
            else
            {
                btnBackground.Text = "起動";
                btnBackground.BackColor = System.Drawing.Color.FromArgb(46, 125, 50);
                btnBackground.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(46, 125, 50);
            }

            // スプリットボタンを作成（メインボタン + ドロップダウン矢印）
            var btnLaunch = new Button
            {
                Text = "cmdで開く",
                Location = new System.Drawing.Point(360, 12), // バックグラウンドボタンの分だけ右にシフト
                Size = new System.Drawing.Size(130, 36),
                Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Regular),
                BackColor = System.Drawing.Color.FromArgb(0, 122, 204),
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            };
            btnLaunch.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(0, 122, 204);

            // 矢印部分のボタンを作成（メインボタンの右端に隣接）
            var btnDropdown = new Button
            {
                Text = "▼",
                Location = new System.Drawing.Point(360 + 130, 12), // メインボタンの右端に隣接
                Size = new System.Drawing.Size(25, 36),
                Font = new System.Drawing.Font("Microsoft Sans Serif", 8F, System.Drawing.FontStyle.Regular),
                BackColor = System.Drawing.Color.FromArgb(0, 100, 180),
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            };
            btnDropdown.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(0, 122, 204);

            // コンテキストメニューを作成
            var contextMenu = new ContextMenuStrip();
            contextMenu.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            contextMenu.ForeColor = System.Drawing.Color.White;

            var menuItemCmd = new ToolStripMenuItem("cmdで開く");
            menuItemCmd.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            menuItemCmd.ForeColor = System.Drawing.Color.White;
            menuItemCmd.Click += (_, __) => LaunchInCmd(distroName);

            var menuItemDirect = new ToolStripMenuItem("直接WSL起動（別コンソール）");
            menuItemDirect.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            menuItemDirect.ForeColor = System.Drawing.Color.White;
            menuItemDirect.Click += (_, __) => LaunchWslDirect(distroName);

            contextMenu.Items.Add(menuItemCmd);
            contextMenu.Items.Add(menuItemDirect);

            // メインボタンクリックでcmd起動
            btnLaunch.Click += (_, __) => LaunchInCmd(distroName);

            // 矢印ボタンクリックでドロップダウンメニューを表示
            btnDropdown.Click += (sender, e) =>
            {
                var btn = sender as Button;
                contextMenu.Show(btn, 0, btn.Height);
            };

            _tip.SetToolTip(btnLaunch, "cmdで開きます");
            _tip.SetToolTip(btnDropdown, "他の起動方法を選択");

            row.Controls.Add(lbl);
            row.Controls.Add(lblStatus);
            row.Controls.Add(btnBackground);
            row.Controls.Add(btnLaunch);
            row.Controls.Add(btnDropdown);

            // DistroRowオブジェクトを辞書に追加
            var distroRow = new DistroRow(distroName, row, lblStatus, btnBackground, btnLaunch, btnDropdown, 
                                        LaunchWslBackgroundAsync, StopWslAsync, UpdateDistroStatus, _tip);
            _distroRows[distroName] = distroRow;

            // 初期ステータスを設定してボタンとツールチップを正しく初期化
            distroRow.UpdateStatus(status);

            return row;
        }

        /// <summary>
        /// wsl.exe -l -v でディストロの状態を取得
        /// </summary>
        private static string GetDistroStatus(string distroName)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "wsl.exe",
                    Arguments = "-l -v",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.Unicode
                };

                using (var p = new Process { StartInfo = psi })
                {
                    if (!p.Start())
                        return "Unknown (Failed to start)";

                    string output = p.StandardOutput.ReadToEnd();
                    string error = p.StandardError.ReadToEnd();
                    p.WaitForExit();

                    if (p.ExitCode != 0)
                        return $"Unknown (Exit code: {p.ExitCode})";

                    // NULL文字を除去してからライン分割
                    var cleanOutput = output.Replace("\0", "");
                    var lines = cleanOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);


                    foreach (var line in lines)
                    {
                        var trimmedLine = line.Trim();
                        if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.Contains("NAME") || trimmedLine.Contains("----"))
                            continue;

                        // まず * を除去してから分割
                        var cleanLine = trimmedLine;
                        if (cleanLine.StartsWith("*"))
                        {
                            cleanLine = cleanLine.Substring(1).Trim();
                        }
                        
                        // 行の形式: "Ubuntu-20.04    Running    2" または "Ubuntu-18.04    Stopped    2"
                        var parts = cleanLine.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            var name = parts[0];
                            var status = parts[1];
                            
                            
                            if (name.Equals(distroName, StringComparison.OrdinalIgnoreCase))
                            {
                                return status;
                            }
                        }
                    }

                    // 見つからなかった場合
                    return "Unknown (Not found)";
                }
            }
            catch (Exception ex)
            {
                return $"Unknown (Exception: {ex.Message})";
            }
        }

        /// <summary>
        /// wsl.exe -l -q でディストリ一覧を取得（同期・.NET Framework 向け）
        /// </summary>
        private static string[] ListWslDistros()
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = "-l -q",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,

                // WSL コマンドは UTF-16LE を使用する
                // StandardOutputEncoding は .NET Framework 4.5+ なら使用可能
                StandardOutputEncoding = Encoding.Unicode
            };

            using (var p = new Process { StartInfo = psi })
            {
                if (!p.Start())
                    throw new InvalidOperationException("wsl.exe の起動に失敗しました。");

                string output = p.StandardOutput.ReadToEnd();
                string error = p.StandardError.ReadToEnd();
                p.WaitForExit();

                if (p.ExitCode != 0)
                    throw new InvalidOperationException("wsl.exe エラー: " + error);

                // NULL文字を除去してからライン分割
                var cleanOutput = output.Replace("\0", "");
                var lines = cleanOutput
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.StartsWith("*") ? s.Substring(1).Trim() : s) // デフォルトディストリのアスタリスクを除去
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()
                    .ToArray();

                return lines;
            }
        }

        /// <summary>
        /// 新しい cmd ウィンドウで指定のディストリを起動（ウィンドウは残す）
        /// </summary>
        private static void LaunchInCmd(string distroName)
        {
            var cleanName = distroName.Trim();
            // 32bitプロセスで System32 がリダイレクトされる問題に対応
            var systemDir = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                ? @"C:\Windows\Sysnative"
                : @"C:\Windows\System32";
            var wslPath = System.IO.Path.Combine(systemDir, "wsl.exe");
            // cmd.exe の /k に渡すコマンドは全体をもう一段の二重引用符で囲む必要がある
            // 例: cmd /k ""C:\path\wsl.exe" --distribution "Ubuntu-20.04""
            var commandLine = $"/k \"\"{wslPath}\" -d {cleanName}\"";
            
            
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = commandLine,
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"プロセス起動エラー: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 直接 wsl.exe を新しいコンソールで起動（cmd を介さない）
        /// </summary>
        private static void LaunchWslDirect(string distroName)
        {
            var cleanName = distroName.Trim();
            // 32bitプロセスで System32 がリダイレクトされる問題に対応
            var systemDir = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                ? @"C:\Windows\Sysnative"
                : @"C:\Windows\System32";
            var wslFullPath = System.IO.Path.Combine(systemDir, "wsl.exe");

            var psi = new ProcessStartInfo
            {
                FileName = wslFullPath,
                Arguments = $"-d {cleanName}",
                UseShellExecute = true,    // 新しいコンソールを作る
                CreateNoWindow = false,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), // ユーザーホームディレクトリに設定
            };
            Process.Start(psi);
        }

        /// <summary>
        /// WSLをバックグラウンドで起動（コンソールウィンドウを表示しない）
        /// </summary>
        private async Task LaunchWslBackgroundAsync(string distroName)
        {
            var cleanName = distroName.Trim();
            // 32bitプロセスで System32 がリダイレクトされる問題に対応
            var systemDir = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                ? @"C:\Windows\Sysnative"
                : @"C:\Windows\System32";
            var wslFullPath = System.IO.Path.Combine(systemDir, "wsl.exe");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = wslFullPath,
                    Arguments = $"-d {cleanName} --exec echo 'WSL started'",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                };

                using (var process = Process.Start(psi))
                {
                    if (process != null)
                    {
                        // 非同期でプロセスの完了を待つ
                        await Task.Run(() => process.WaitForExit(10000)); // 10秒でタイムアウト
                        
                        if (process.ExitCode == 0)
                        {
                            SafeAddLog($"{cleanName} をバックグラウンドで起動しました。", System.Drawing.Color.LightGreen);
                        }
                        else
                        {
                            var error = await Task.Run(() => process.StandardError.ReadToEnd());
                            SafeAddLog($"{cleanName} の起動に失敗しました。エラー: {error}", System.Drawing.Color.LightCoral);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SafeAddLog($"バックグラウンド起動エラー: {ex.Message}", System.Drawing.Color.LightCoral);
            }
        }

        private void LaunchWslBackground(string distroName)
        {
            Task.Run(async () => await LaunchWslBackgroundAsync(distroName));
        }

        /// <summary>
        /// WSLディストリビューションを停止する非同期版
        /// </summary>
        private async Task StopWslAsync(string distroName)
        {
            var cleanName = distroName.Trim();
            // 32bitプロセスで System32 がリダイレクトされる問題に対応
            var systemDir = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                ? @"C:\Windows\Sysnative"
                : @"C:\Windows\System32";
            var wslFullPath = System.IO.Path.Combine(systemDir, "wsl.exe");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = wslFullPath,
                    Arguments = $"--terminate {cleanName}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                };

                using (var process = Process.Start(psi))
                {
                    if (process != null)
                    {
                        // 非同期でプロセスの完了を待つ
                        await Task.Run(() => process.WaitForExit(10000)); // 10秒でタイムアウト
                        
                        if (process.ExitCode == 0)
                        {
                            SafeAddLog($"{cleanName} を停止しました。", System.Drawing.Color.LightBlue);
                        }
                        else
                        {
                            string error = await Task.Run(() => process.StandardError.ReadToEnd());
                            SafeAddLog($"{cleanName} の停止に失敗しました。エラー: {error}", System.Drawing.Color.LightCoral);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SafeAddLog($"WSL停止エラー: {ex.Message}", System.Drawing.Color.LightCoral);
            }
        }

        /// <summary>
        /// WSLディストリビューションを停止する
        /// </summary>
        private void StopWsl(string distroName)
        {
            Task.Run(async () => await StopWslAsync(distroName));
        }
    }
}
