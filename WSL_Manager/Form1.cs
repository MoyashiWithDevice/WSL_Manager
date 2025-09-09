using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WslManagerFramework
{
    public class DistroRow
    {
        public string DistroName { get; }
        public Panel Panel { get; }
        public Label StatusLabel { get; }
        public Button BackgroundButton { get; }
        public Button LaunchButton { get; }
        public Button DropdownButton { get; }
        
        private System.Action<string> _launchAction;
        private System.Action<string> _stopAction;
        private System.Action<string> _updateStatusAction;
        private ToolTip _tooltip;

        public DistroRow(string distroName, Panel panel, Label statusLabel, Button backgroundButton, Button launchButton, Button dropdownButton, 
                        System.Action<string> launchAction, System.Action<string> stopAction, System.Action<string> updateStatusAction, ToolTip tooltip)
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
                    await Task.Run(() => _stopAction(DistroName));
                }
                else
                {
                    await Task.Run(() => _launchAction(DistroName));
                }
                
                // 少し待ってからステータス更新
                await Task.Delay(1000);
                _updateStatusAction(DistroName);
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

        public Form1()
        {
            InitializeComponent();

            Text = "WSL Manager (.NET Framework)";
            Width = 800; // ステータス列のために幅を拡張
            Height = 450;

            // ダークテーマの設定
            BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            ForeColor = System.Drawing.Color.White;

            // 上部バー
            _refresh.Text = "更新（再読み込み）";
            _refresh.AutoSize = true;
            _refresh.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular);
            _refresh.BackColor = System.Drawing.Color.FromArgb(63, 63, 70);
            _refresh.ForeColor = System.Drawing.Color.White;
            _refresh.FlatStyle = FlatStyle.Flat;
            _refresh.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(104, 104, 104);
            _refresh.Click += (_, __) => Reload();   // 非async版

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

            Controls.Add(_panel);
            Controls.Add(_logBox);
            Controls.Add(top);

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

            // ウィンドウイベントの設定
            WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            Resize += Form1_Resize;
            FormClosing += Form1_FormClosing;

            // フォーム表示時に初回読み込み
            Shown += (_, __) => 
            {
                AddLog("WSL Manager を起動しました。", System.Drawing.Color.LightYellow);
                Reload();
                UpdateTrayMenu();
            };
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
                            stopItem.Click += (_, __) => 
                            {
                                StopWsl(distro);
                                UpdateTrayMenu();
                            };
                            distroItem.DropDownItems.Add(stopItem);
                        }
                        else
                        {
                            var startItem = new ToolStripMenuItem("バックグラウンド起動");
                            startItem.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
                            startItem.ForeColor = System.Drawing.Color.LightGreen;
                            startItem.Click += (_, __) => 
                            {
                                LaunchWslBackground(distro);
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
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                ShowInTaskbar = false;
                _notifyIcon.ShowBalloonTip(2000, "WSL Manager", "タスクトレイに最小化されました", ToolTipIcon.Info);
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                _notifyIcon.ShowBalloonTip(2000, "WSL Manager", "タスクトレイに最小化されました", ToolTipIcon.Info);
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
                                        LaunchWslBackground, StopWsl, UpdateDistroStatus, _tip);
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
