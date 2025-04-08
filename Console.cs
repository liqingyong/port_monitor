using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Data.SQLite;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net;
using Microsoft.Win32;
using System.Reflection;
using System.Management;
using System.Text;
using System.Runtime.InteropServices;
using System.Drawing;

namespace port_minitor
{
    public partial class Console : Form, IDisposable
    {
        private const string DbFileName = "monitoring_db.sqlite";
        private string dbPath;
        private List<MonitoredService> monitoredServices;
        private System.Threading.Timer monitorTimer;
        private System.Windows.Forms.Timer countdownTimer;
        private readonly object lockObject = new object();
        private readonly string password = "123321";
        private int scanIntervalMinutes = 2; // 默认扫描间隔
        private int countdownSeconds;
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _disposed = false;
        private int lastUsedPort = 8080; // 保存上次使用的端口号
        private int memoryCleanupCounter = 0; // 内存清理计数器
        private bool isAdminMode = false; // 添加管理员模式标志
        private bool isInitializing = true; // 添加初始化标志，防止初始化时触发权限检查

        // 新增状态标签，用于显示操作状态
        private Label statusLabel;

        // 用于存储CMD窗口信息的类
        private class CmdWindowInfo
        {
            public IntPtr WindowHandle { get; set; }
            public int ProcessId { get; set; }
            public string Title { get; set; }
            public DateTime CreationTime { get; set; }
            public string CommandLine { get; set; } // 添加命令行信息
        }

        public Console()
        {
            InitializeComponent();
            dbPath = Path.Combine(Application.StartupPath, DbFileName);
            monitoredServices = new List<MonitoredService>();

            // 初始化端口号控件
            numericUpDown1.Minimum = 1;
            numericUpDown1.Maximum = 65535;
            numericUpDown1.Value = lastUsedPort; // 设置默认端口

            // 初始化状态标签
            InitializeStatusLabel();

            // 初始化组件和数据库
            SetupListView();
            InitializeDatabase();
            InitializeCountdownTimer();
        }

        // 初始化状态标签
        private void InitializeStatusLabel()
        {
            statusLabel = new Label();
            statusLabel.AutoSize = false;
            statusLabel.Dock = DockStyle.Bottom;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Height = 20;
            statusLabel.BackColor = System.Drawing.Color.LightGray;
            statusLabel.Padding = new Padding(5, 0, 0, 0);
            statusLabel.Text = "就绪";
            this.Controls.Add(statusLabel);
        }

        // 更新状态信息
        private void UpdateStatus(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(UpdateStatus), message);
                return;
            }

            statusLabel.Text = message;
            Application.DoEvents(); // 允许UI更新
        }

        private void SetupListView()
        {
            listView1.Items.Clear();
            listView1.Columns.Clear();
            listView1.View = View.Details;
            listView1.FullRowSelect = true;
            listView1.GridLines = true;
            listView1.Columns.Add("文件目录", 322); // 文件目录列
            listView1.Columns.Add("文件名", 325); // 文件名列
            listView1.Columns.Add("端口号", 80); // 端口列
            listView1.Columns.Add("是否在线", 80); // 在线状态列
            listView1.Columns.Add("扫描倒计时", 100); // 倒计时列
            listView1.Columns.Add("操作", 100); // 操作列
            listView1.MouseClick += listView1_MouseClick; // 绑定鼠标点击事件
            listView1.SelectedIndexChanged += listView1_SelectedIndexChanged; // 绑定选择项改变事件
        }

        private void InitializeCountdownTimer()
        {
            // 确保不创建多个计时器实例
            if (countdownTimer != null)
            {
                countdownTimer.Stop();
                countdownTimer.Tick -= CountdownTimer_Tick; // 解绑事件处理程序
                countdownTimer.Dispose();
            }

            countdownTimer = new System.Windows.Forms.Timer();
            countdownTimer.Interval = 1000; // 1秒
            countdownTimer.Tick += CountdownTimer_Tick;
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                isInitializing = true; // 标记初始化开始

                // 先禁用管理员控件
                DisableAdminControls();

                UpdateStatus("正在加载配置...");

                // 加载配置
                await LoadConfigurationAsync(_cts.Token);

                UpdateStatus("正在加载监控服务...");

                // 加载已保存的服务并开始监控
                await LoadMonitoredServicesAsync(_cts.Token);
                UpdateListView();
                StartMonitoring();

                UpdateStatus("监控已启动 - 只读模式");

                // 初始化完成
                isInitializing = false;

                // 显示登录窗口
                using (var loginForm = new LoginForm())
                {
                    loginForm.Password = password;
                    if (loginForm.ShowDialog() == DialogResult.OK)
                    {
                        // 登录成功，启用管理员控件
                        EnableAdminControls();
                        isAdminMode = true;
                        UpdateStatus("就绪 - 管理员模式");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化应用程序时发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateStatus("初始化发生错误");
                isInitializing = false;
            }
        }

        // 禁用管理员控件
        private void DisableAdminControls()
        {
            button1.Enabled = false; // 文件浏览按钮
            button2.Enabled = false; // 添加服务按钮
            numericUpDown1.Enabled = false; // 端口选择器
            numericUpDown2.Enabled = false; // 扫描间隔
            textBox1.Enabled = false; // 文件路径文本框
            checkBoxStartup.Enabled = false; // 自启动复选框
        }

        // 启用管理员控件
        private void EnableAdminControls()
        {
            button1.Enabled = true;
            button2.Enabled = true;
            numericUpDown1.Enabled = true;
            numericUpDown2.Enabled = true;
            textBox1.Enabled = true;
            checkBoxStartup.Enabled = true;
        }

        private async Task LoadConfigurationAsync(CancellationToken token)
        {
            using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
            {
                await connection.OpenAsync(token);
                // 检查Settings表是否存在
                if (await TableExistsAsync(connection, "Settings", token))
                {
                    string sql = "SELECT ScanInterval, StartupEnabled FROM Settings LIMIT 1;";
                    using (var command = new SQLiteCommand(sql, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync(token))
                        {
                            if (await reader.ReadAsync(token))
                            {
                                scanIntervalMinutes = reader.GetInt32(0);

                                // 记录自启动状态，但在初始化过程中不触发事件
                                bool startupEnabled = reader.GetBoolean(1);

                                // 解绑事件，设置值，再重新绑定
                                numericUpDown2.ValueChanged -= numericUpDown2_ValueChanged;
                                numericUpDown2.Value = scanIntervalMinutes;
                                numericUpDown2.ValueChanged += numericUpDown2_ValueChanged;

                                // 同理处理自启动复选框
                                bool systemStartupState = IsInStartup();
                                checkBoxStartup.CheckedChanged -= checkBoxStartup_CheckedChanged;
                                checkBoxStartup.Checked = systemStartupState;
                                checkBoxStartup.CheckedChanged += checkBoxStartup_CheckedChanged;
                            }
                        }
                    }
                }
                else
                {
                    // 表不存在时可以设置默认值
                    await CreateDefaultSettingsAsync(connection, token);

                    // 设置默认值，避免触发事件
                    numericUpDown2.ValueChanged -= numericUpDown2_ValueChanged;
                    numericUpDown2.Value = scanIntervalMinutes;
                    numericUpDown2.ValueChanged += numericUpDown2_ValueChanged;

                    checkBoxStartup.CheckedChanged -= checkBoxStartup_CheckedChanged;
                    checkBoxStartup.Checked = false;
                    checkBoxStartup.CheckedChanged += checkBoxStartup_CheckedChanged;
                }
            }

            // 清理SQLite连接池
            SQLiteConnection.ClearAllPools();
        }

        private async Task CreateDefaultSettingsAsync(SQLiteConnection connection, CancellationToken token)
        {
            string sqlSettings = @"
                CREATE TABLE Settings (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ScanInterval INTEGER NOT NULL,
                    StartupEnabled INTEGER DEFAULT 0
                );";

            using (var command = new SQLiteCommand(sqlSettings, connection))
            {
                await command.ExecuteNonQueryAsync(token);
            }

            // 插入默认配置
            string insertSettings = "INSERT INTO Settings (ScanInterval, StartupEnabled) VALUES (2, 0);";
            using (var command = new SQLiteCommand(insertSettings, connection))
            {
                await command.ExecuteNonQueryAsync(token);
            }
        }

        private async Task<bool> TableExistsAsync(SQLiteConnection connection, string tableName, CancellationToken token)
        {
            using (var command = new SQLiteCommand($"SELECT name FROM sqlite_master WHERE type='table' AND name='{tableName}';", connection))
            {
                using (var reader = await command.ExecuteReaderAsync(token))
                {
                    return reader.HasRows;
                }
            }
        }

        private void InitializeDatabase()
        {
            try
            {
                // 如果数据库文件不存在，则创建数据库
                if (!File.Exists(dbPath))
                {
                    SQLiteConnection.CreateFile(dbPath);
                    using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                    {
                        connection.Open();
                        CreateTables(connection); // 创建所需的表
                    }
                }
                else
                {
                    // 检查并升级数据库，如果缺少字段则添加
                    UpgradeDatabase();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化数据库时发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CreateTables(SQLiteConnection connection)
        {
            string sqlServices = @"
                CREATE TABLE Services (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FilePath TEXT NOT NULL,
                    FileName TEXT NOT NULL,
                    Port INTEGER NOT NULL,
                    IsOnline INTEGER DEFAULT 0,
                    RestartAttempts INTEGER DEFAULT 0
                );";

            string sqlSettings = @"
                CREATE TABLE Settings (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ScanInterval INTEGER NOT NULL,
                    StartupEnabled INTEGER DEFAULT 0
                );";

            using (var command = new SQLiteCommand(sqlServices, connection))
            {
                command.ExecuteNonQuery();
            }
            using (var command = new SQLiteCommand(sqlSettings, connection))
            {
                command.ExecuteNonQuery();
            }

            // 插入默认配置
            string insertSettings = "INSERT INTO Settings (ScanInterval, StartupEnabled) VALUES (2, 0);";
            using (var command = new SQLiteCommand(insertSettings, connection))
            {
                command.ExecuteNonQuery();
            }

            // 清理SQLite连接池
            SQLiteConnection.ClearAllPools();
        }

        private void UpgradeDatabase()
        {
            using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"PRAGMA table_info(Services);";
                List<string> existingColumns = new List<string>();

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        existingColumns.Add(reader.GetString(1)); // 添加字段名到列表
                    }
                }

                // 使用反射获取MonitoredService类的所有属性名
                var properties = typeof(MonitoredService).GetProperties(BindingFlags.Public | BindingFlags.Instance);
                foreach (var property in properties)
                {
                    // 检查并添加缺失的字段
                    if (!existingColumns.Contains(property.Name))
                    {
                        string columnType = GetSQLiteType(property.PropertyType);
                        command.CommandText = $"ALTER TABLE Services ADD COLUMN {property.Name} {columnType} DEFAULT 0;";
                        command.ExecuteNonQuery();
                    }
                }
            }

            // 清理资源
            SQLiteConnection.ClearAllPools();
        }

        // 获取SQLite数据类型
        private string GetSQLiteType(Type type)
        {
            if (type == typeof(int) || type == typeof(long))
            {
                return "INTEGER";
            }
            else if (type == typeof(string))
            {
                return "TEXT";
            }
            return "TEXT"; // 默认类型
        }

        private async Task LoadMonitoredServicesAsync(CancellationToken token)
        {
            if (_disposed || token.IsCancellationRequested) return;

            try
            {
                List<MonitoredService> tempServices = new List<MonitoredService>();

                using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    await connection.OpenAsync(token);
                    string sql = "SELECT Id, FilePath, FileName, Port, IsOnline, RestartAttempts FROM Services;";
                    using (var command = new SQLiteCommand(sql, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync(token))
                        {
                            while (await reader.ReadAsync(token))
                            {
                                var service = new MonitoredService
                                {
                                    Id = reader.GetInt32(0),
                                    FilePath = reader.GetString(1),
                                    FileName = reader.GetString(2),
                                    Port = reader.GetInt32(3),
                                    IsOnline = reader.GetInt32(4) == 1,
                                    RestartAttempts = reader.GetInt32(5)
                                };
                                tempServices.Add(service);
                            }
                        }
                    }
                }

                // 异步检查每个服务的状态
                List<Task> tasks = new List<Task>();
                foreach (var service in tempServices)
                {
                    if (token.IsCancellationRequested || _disposed) break;
                    tasks.Add(CheckServiceStatusAsync(service, token));
                }

                try
                {
                    await Task.WhenAll(tasks);
                }
                catch (TaskCanceledException)
                {
                    // 任务被取消，不需要处理
                }

                // 只有在所有任务完成后再更新列表 - 避免并发修改
                lock (lockObject)
                {
                    if (_disposed) return;
                    monitoredServices.Clear();
                    monitoredServices.AddRange(tempServices);
                }

                // 清理资源
                tasks.Clear();
                tasks = null;
                tempServices = null;

                // 清理SQLite连接池
                SQLiteConnection.ClearAllPools();
            }
            catch (TaskCanceledException)
            {
                // 任务被取消，不需要处理
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested && !_disposed)
                {
                    MessageBox.Show($"加载服务数据时发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            // 强制执行一次内存清理
            CleanupMemory();
        }

        private async Task CheckServiceStatusAsync(MonitoredService service, CancellationToken token)
        {
            if (_disposed || token.IsCancellationRequested) return;

            service.IsOnline = await IsPortOpenAsync(service.Port, token);
            await UpdateServiceStatusInDbAsync(service, token);
        }

        private void StartMonitoring()
        {
            if (_disposed) return;

            scanIntervalMinutes = (int)numericUpDown2.Value;
            int interval = scanIntervalMinutes * 60 * 1000; // 将分钟转换为毫秒
            countdownSeconds = scanIntervalMinutes * 60; // 重置倒计时

            lock (lockObject)
            {
                // 停止并释放旧的定时器
                if (monitorTimer != null)
                {
                    monitorTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    monitorTimer.Dispose();
                    monitorTimer = null;
                }

                // 创建新的定时器
                monitorTimer = new System.Threading.Timer(MonitorServices, null, 0, interval);

                // 确保倒计时定时器正在运行
                if (countdownTimer != null && !countdownTimer.Enabled)
                {
                    countdownTimer.Start();
                }
            }
        }

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            if (_disposed) return;

            countdownSeconds--;
            if (countdownSeconds <= 0)
            {
                RestartOfflineServices();
                countdownSeconds = scanIntervalMinutes * 60; // 重置倒计时
            }

            // 更新UI
            if (!_disposed)
            {
                UpdateListView();
            }

            // 周期性内存清理
            memoryCleanupCounter++;
            if (memoryCleanupCounter >= 60) // 每60秒执行一次内存清理
            {
                CleanupMemory();
                memoryCleanupCounter = 0;
            }
        }

        // 内存清理方法
        private void CleanupMemory()
        {
            try
            {
                // 清理SQLite连接池
                SQLiteConnection.ClearAllPools();

                // 减少工作集大小
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    NativeMethods.SetProcessWorkingSetSize(
                        Process.GetCurrentProcess().Handle,
                        (IntPtr)(-1),
                        (IntPtr)(-1));
                }

                // 建议垃圾回收
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false, false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"内存清理时发生错误: {ex.Message}");
            }
        }

        private async void RestartOfflineServices()
        {
            if (_disposed) return;

            List<MonitoredService> servicesToRestart = new List<MonitoredService>();

            lock (lockObject)
            {
                if (_disposed) return;
                // 创建一个副本，避免在迭代时修改集合
                servicesToRestart.AddRange(monitoredServices.Where(s => !s.IsOnline));
            }

            foreach (var service in servicesToRestart)
            {
                // 检查是否已处置
                if (_disposed) break;

                service.RestartAttempts++;
                if (service.RestartAttempts <= 3) // 如果重启尝试次数不超过3次
                {
                    if (!await IsPortInUseAsync(service.Port))
                    {
                        await RestartServiceAsync(service);
                    }
                }
                else
                {
                    if (!_disposed)
                    {
                        MessageBox.Show($"服务 {service.FileName} 在三次尝试后无法启动，已停止重启尝试。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        //service.RestartAttempts = 0; // 重置尝试次数

                        // 异步更新数据库
                        StartBackgroundTask(async (token) =>
                        {
                            await UpdateServiceStatusInDbAsync(service, token);
                        });
                    }
                }
            }

            // 清理资源
            servicesToRestart.Clear();
            servicesToRestart = null;
        }

        // 启动后台任务的辅助方法，避免直接使用Task.Run导致的内存泄漏
        private void StartBackgroundTask(Func<CancellationToken, Task> taskFunc)
        {
            if (_disposed) return;

            var token = _cts.Token;
            Task.Run(async () =>
            {
                try
                {
                    if (token.IsCancellationRequested || _disposed) return;
                    await taskFunc(token);
                }
                catch (TaskCanceledException)
                {
                    // 任务被取消，不需要处理
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"后台任务执行错误: {ex.Message}");
                }
            }, token);
        }

        private async void MonitorServices(object state)
        {
            try
            {
                if (_disposed) return;

                await CheckAllServicesAsync(_cts.Token);

                // 重置倒计时
                countdownSeconds = scanIntervalMinutes * 60;

                // 更新UI，确保不在已释放的对象上调用
                if (!_disposed)
                {
                    if (this.InvokeRequired)
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            if (!_disposed) UpdateListView();
                        }));
                    }
                    else
                    {
                        UpdateListView();
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // 任务被取消，不需要处理
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"监控服务时发生错误: {ex.Message}");
            }
        }

        private async Task CheckAllServicesAsync(CancellationToken token)
        {
            if (_disposed || token.IsCancellationRequested) return;

            List<MonitoredService> localServices = new List<MonitoredService>();

            // 创建服务列表的副本
            lock (lockObject)
            {
                if (_disposed) return;
                localServices.AddRange(monitoredServices);
            }

            List<Task> tasks = new List<Task>();
            foreach (var service in localServices)
            {
                if (token.IsCancellationRequested || _disposed) break;

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        if (token.IsCancellationRequested || _disposed) return;

                        bool isOnline = await IsPortOpenAsync(service.Port, token);
                        bool statusChanged = service.IsOnline != isOnline;

                        if (statusChanged)
                        {
                            service.IsOnline = isOnline;
                            if (isOnline) { 
                                service.RestartAttempts = 0;
                            }
                            if (!token.IsCancellationRequested && !_disposed)
                            {
                                await UpdateServiceStatusInDbAsync(service, token);
                            }
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        // 任务被取消
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"检查服务状态时发生错误: {ex.Message}");
                    }
                }, token));
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (TaskCanceledException)
            {
                // 任务被取消，不需要处理
            }
            finally
            {
                // 清理资源
                tasks.Clear();
                tasks = null;
                localServices.Clear();
                localServices = null;

                // 清理SQLite连接池
                SQLiteConnection.ClearAllPools();
            }
        }

        private async Task<bool> IsPortOpenAsync(int port, CancellationToken token)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var connectTask = client.ConnectAsync("127.0.0.1", port);
                    var timeoutTask = Task.Delay(1000, token);

                    if (await Task.WhenAny(connectTask, timeoutTask) == connectTask)
                    {
                        // 连接成功
                        return true;
                    }
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task UpdateServiceStatusInDbAsync(MonitoredService service, CancellationToken token)
        {
            if (_disposed || token.IsCancellationRequested) return;

            try
            {
                using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    await connection.OpenAsync(token);
                    string sql = "UPDATE Services SET IsOnline = @IsOnline, RestartAttempts = @RestartAttempts WHERE Id = @Id;";
                    using (var command = new SQLiteCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@IsOnline", service.IsOnline ? 1 : 0);
                        command.Parameters.AddWithValue("@RestartAttempts", service.RestartAttempts);
                        command.Parameters.AddWithValue("@Id", service.Id);
                        await command.ExecuteNonQueryAsync(token);
                    }
                }

                // 清理连接池
                SQLiteConnection.ClearAllPools();
            }
            catch (TaskCanceledException)
            {
                // 任务被取消，不需要处理
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"更新服务状态时发生错误: {ex.Message}");
            }
        }

        private void UpdateListView()
        {
            // 确保在UI线程上执行
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(UpdateListView));
                return;
            }

            if (_disposed) return;

            // 保存当前选中的索引
            int selectedIndex = -1;
            if (listView1.SelectedIndices.Count > 0)
                selectedIndex = listView1.SelectedIndices[0];

            // 快照当前项以优化刷新
            Dictionary<int, ListViewItem> existingItems = new Dictionary<int, ListViewItem>();
            foreach (ListViewItem item in listView1.Items)
            {
                var service = item.Tag as MonitoredService;
                if (service != null)
                {
                    existingItems[service.Id] = item;
                }
            }

            // 创建本地副本，避免在UI更新过程中被修改
            List<MonitoredService> localServices = new List<MonitoredService>();
            lock (lockObject)
            {
                if (_disposed) return;
                localServices.AddRange(monitoredServices);
            }

            listView1.BeginUpdate(); // 开始批量更新以提高性能

            try
            {
                // 使用新的更新策略 - 只更新已改变的项，减少UI操作
                foreach (var service in localServices)
                {
                    if (_disposed) break;

                    ListViewItem item;
                    bool isNew = !existingItems.TryGetValue(service.Id, out item);

                    if (isNew)
                    {
                        // 创建新的ListViewItem
                        item = new ListViewItem(Path.GetDirectoryName(service.FilePath)); // 文件目录
                        item.SubItems.Add(service.FileName); // 文件名
                        item.SubItems.Add(service.Port.ToString()); // 端口
                        item.SubItems.Add(service.IsOnline ? "在线" : "离线"); // 在线状态

                        int minutes = countdownSeconds / 60;
                        int seconds = countdownSeconds % 60;
                        item.SubItems.Add($"{minutes:00}:{seconds:00}"); // 倒计时
                        item.SubItems.Add("重启 | 删除"); // 操作
                        item.Tag = service; // 将服务对象绑定到项的标签中
                        item.BackColor = service.IsOnline ? Color.LightGreen : Color.LightPink;

                        listView1.Items.Add(item);
                    }
                    else
                    {
                        // 更新现有项
                        existingItems.Remove(service.Id); // 从字典中移除，剩下的将是要删除的项

                        // 只更新可能发生变化的部分
                        item.SubItems[3].Text = service.IsOnline ? "在线" : "离线";

                        int minutes = countdownSeconds / 60;
                        int seconds = countdownSeconds % 60;
                        item.SubItems[4].Text = $"{minutes:00}:{seconds:00}";

                        item.BackColor = service.IsOnline ? Color.LightGreen : Color.LightPink;
                    }
                }

                // 删除不再存在的项
                foreach (var itemToRemove in existingItems.Values)
                {
                    listView1.Items.Remove(itemToRemove);
                }
            }
            finally
            {
                // 恢复选中状态
                if (selectedIndex >= 0 && selectedIndex < listView1.Items.Count)
                    listView1.Items[selectedIndex].Selected = true;

                listView1.EndUpdate(); // 结束批量更新
            }

            // 清理资源
            existingItems.Clear();
            existingItems = null;
            localServices.Clear();
            localServices = null;
        }

        private async void AddService(string filePath, int port)
        {
            if (_disposed) return;

            try
            {
                bool exists = false;
                lock (lockObject)
                {
                    if (_disposed) return;
                    exists = monitoredServices.Any(s => s.FilePath == filePath && s.Port == port);
                }

                if (exists)
                {
                    MessageBox.Show("该服务已存在于监控列表中！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string fileName = Path.GetFileName(filePath); // 提取文件名
                int newId = 0;

                // 使用事务保证数据完整性
                using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    await connection.OpenAsync(_cts.Token);
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            string sql = "INSERT INTO Services (FilePath, FileName, Port, IsOnline, RestartAttempts) VALUES (@FilePath, @FileName, @Port, 0, 0);";
                            using (var command = new SQLiteCommand(sql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@FilePath", filePath);
                                command.Parameters.AddWithValue("@FileName", fileName);
                                command.Parameters.AddWithValue("@Port", port);
                                await command.ExecuteNonQueryAsync(_cts.Token);

                                command.CommandText = "SELECT last_insert_rowid();";
                                var result = await command.ExecuteScalarAsync(_cts.Token);
                                newId = Convert.ToInt32(result);
                            }

                            transaction.Commit();
                        }
                        catch (Exception)
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }

                var service = new MonitoredService
                {
                    Id = newId,
                    FilePath = filePath,
                    FileName = fileName,
                    Port = port,
                    IsOnline = false,
                    RestartAttempts = 0
                };

                // 异步检查服务状态
                await CheckServiceStatusAsync(service, _cts.Token);

                // 添加到列表并更新UI
                lock (lockObject)
                {
                    if (_disposed) return;
                    monitoredServices.Add(service);
                }

                UpdateListView();

                // 清理SQLite连接池
                SQLiteConnection.ClearAllPools();
            }
            catch (TaskCanceledException)
            {
                // 任务被取消，不需要处理
            }
            catch (Exception ex)
            {
                if (!_cts.Token.IsCancellationRequested && !_disposed)
                {
                    MessageBox.Show($"添加服务时发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private async void RemoveService(int id)
        {
            if (_disposed) return;

            // 检查是否是管理员模式
            if (!isAdminMode)
            {
                MessageBox.Show("您需要以管理员模式登录才能删除服务。", "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // 从数据库删除
                using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    await connection.OpenAsync(_cts.Token);
                    string sql = "DELETE FROM Services WHERE Id = @Id;";
                    using (var command = new SQLiteCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@Id", id);
                        await command.ExecuteNonQueryAsync(_cts.Token);
                    }
                }

                // 从内存列表中删除
                lock (lockObject)
                {
                    if (_disposed) return;
                    monitoredServices.RemoveAll(s => s.Id == id);
                }

                UpdateListView();

                // 清理SQLite连接池
                SQLiteConnection.ClearAllPools();
            }
            catch (TaskCanceledException)
            {
                // 任务被取消，不需要处理
            }
            catch (Exception ex)
            {
                if (!_cts.Token.IsCancellationRequested && !_disposed)
                {
                    MessageBox.Show($"删除服务时发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // Windows API 常量和导入
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const uint WM_CLOSE = 0x0010;

        // 修改为异步方法，避免阻塞UI线程
        private async Task RestartServiceAsync(MonitoredService service)
        {
            if (_disposed) return;

            try
            {
                if (!File.Exists(service.FilePath))
                {
                    MessageBox.Show($"无法找到文件: {service.FilePath}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string processName = Path.GetFileNameWithoutExtension(service.FilePath);
                string extension = Path.GetExtension(service.FilePath).ToLower();
                int servicePort = service.Port;
                int serviceId = service.Id;

                // 显示状态
                UpdateStatus($"正在重启服务: {service.FileName}");

                // 将耗时操作放到后台线程
                await Task.Run(async () =>
                {
                    // 关闭现有进程
                    if (extension == ".bat")
                    {
                        await TerminateBatchProcessesAsync(service, _cts.Token);
                    }
                    else
                    {
                        await CloseProcessesByNameAsync(processName, _cts.Token);
                    }

                    // 杀死使用该端口的进程
                    await KillProcessesUsingPortAsync(servicePort, _cts.Token);

                    // 短暂等待确保进程已关闭
                    await Task.Delay(500, _cts.Token);
                }, _cts.Token);

                // 回到UI线程启动新进程
                if (extension == ".bat")
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/k cd /d \"{Path.GetDirectoryName(service.FilePath)}\" && \"{Path.GetFileName(service.FilePath)}\"",
                        UseShellExecute = true,
                        CreateNoWindow = false
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = service.FilePath,
                        WorkingDirectory = Path.GetDirectoryName(service.FilePath),
                        UseShellExecute = true
                    });
                }

                // 重置状态
                UpdateStatus("正在检查服务状态...");

                // 异步检查服务启动状态
                StartBackgroundTask(async (token) =>
                {
                    await Task.Delay(12000, token); // 等待12秒

                    if (token.IsCancellationRequested || _disposed) return;

                    // 获取当前服务实例的最新引用
                    MonitoredService currentService = null;
                    lock (lockObject)
                    {
                        if (_disposed) return;
                        currentService = monitoredServices.FirstOrDefault(s => s.Id == serviceId);
                    }

                    if (currentService != null)
                    {
                        // 检查端口状态
                        bool isPortOpen = await IsPortOpenAsync(servicePort, token);
                        currentService.IsOnline = isPortOpen;
                        if (isPortOpen) { 
                            currentService.RestartAttempts = 0;
                        }
                        await UpdateServiceStatusInDbAsync(currentService, token);
                    }

                    // 更新UI
                    if (!_disposed && !token.IsCancellationRequested)
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            if (!_disposed)
                            {
                                UpdateListView();
                                UpdateStatus("就绪");
                            }
                        }));
                    }
                });
            }
            catch (TaskCanceledException)
            {
                // 任务被取消，重置状态
                UpdateStatus("操作已取消");
            }
            catch (Exception ex)
            {
                UpdateStatus("操作失败");

                if (!_cts.Token.IsCancellationRequested && !_disposed)
                {
                    MessageBox.Show($"重启服务时发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // 获取使用指定端口的进程ID
        private HashSet<int> GetProcessIdsUsingPort(int port)
        {
            HashSet<int> result = new HashSet<int>();

            try
            {
                using (Process process = new Process())
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = "netstat",
                        Arguments = $"-ano | findstr :{port}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };

                    process.StartInfo = startInfo;
                    process.Start();

                    string output = process.StandardOutput.ReadToEnd();
                    if (!process.WaitForExit(5000))
                    {
                        process.Kill();
                        return result;
                    }

                    string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in lines)
                    {
                        try
                        {
                            string[] parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length > 4)
                            {
                                string pidString = parts[parts.Length - 1];
                                if (int.TryParse(pidString, out int pid))
                                {
                                    result.Add(pid);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"解析netstat输出时出错: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取使用端口进程时出错: {ex.Message}");
            }

            return result;
        }

        // 异步查找CMD窗口
        private async Task<List<CmdWindowInfo>> FindAllCmdWindowsAsync(CancellationToken token)
        {
            return await Task.Run(() =>
            {
                List<CmdWindowInfo> result = new List<CmdWindowInfo>();

                EnumWindows((hWnd, lParam) =>
                {
                    if (token.IsCancellationRequested)
                        return false;

                    // 检查窗口是否可见
                    if (!IsWindowVisible(hWnd))
                        return true;

                    // 获取窗口类名
                    StringBuilder className = new StringBuilder(256);
                    GetClassName(hWnd, className, className.Capacity);
                    string windowClass = className.ToString();

                    // 检查是否是控制台窗口
                    if (windowClass == "ConsoleWindowClass")
                    {
                        // 获取窗口标题
                        int length = GetWindowTextLength(hWnd);
                        string windowTitle = "";

                        if (length > 0)
                        {
                            StringBuilder title = new StringBuilder(length + 1);
                            GetWindowText(hWnd, title, title.Capacity);
                            windowTitle = title.ToString();
                        }

                        // 获取进程ID
                        GetWindowThreadProcessId(hWnd, out int processId);

                        try
                        {
                            using (var process = Process.GetProcessById(processId))
                            {
                                // 确认是CMD进程
                                if (process.ProcessName.ToLower() == "cmd")
                                {
                                    // 尝试获取命令行信息
                                    string commandLine = "";
                                    try
                                    {
                                        commandLine = GetProcessCommandLine(processId);
                                    }
                                    catch (Exception cmdEx)
                                    {
                                        Debug.WriteLine($"获取命令行失败: {cmdEx.Message}");
                                    }

                                    result.Add(new CmdWindowInfo
                                    {
                                        WindowHandle = hWnd,
                                        ProcessId = processId,
                                        Title = windowTitle,
                                        CreationTime = process.StartTime,
                                        CommandLine = commandLine
                                    });

                                    Debug.WriteLine($"找到CMD窗口: PID={processId}, 标题='{windowTitle}', 命令行='{commandLine}'");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"获取进程信息时出错: {ex.Message}");
                        }
                    }

                    return true; // 继续枚举
                }, IntPtr.Zero);

                return result;
            }, token);
        }

        private string GetProcessCommandLine(int processId)
        {
            try
            {
                // Using Windows built-in wmic command
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c wmic process where processid={processId} get commandline /format:list",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null) return string.Empty;

                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(3000); // 添加超时确保不会永久阻塞

                    // Parse the output
                    string result = output.Trim();
                    if (result.StartsWith("CommandLine=", StringComparison.OrdinalIgnoreCase))
                    {
                        return result.Substring("CommandLine=".Length).Trim();
                    }

                    return result;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Command execution error: {ex.Message}");
                return string.Empty;
            }
        }

        // 改进的异步终止批处理进程方法，避免关闭不相关窗口
        private async Task TerminateBatchProcessesAsync(MonitoredService service, CancellationToken token)
        {
            await Task.Run(() =>
            {
                try
                {
                    string batchFileName = service.FileName;
                    string batchFileNameWithoutExt = Path.GetFileNameWithoutExtension(service.FilePath);
                    string batchDirectory = Path.GetDirectoryName(service.FilePath);
                    string fullPath = service.FilePath;

                    Debug.WriteLine($"正在尝试关闭批处理相关进程: {batchFileName}, 目录: {batchDirectory}");

                    // 查找CMD窗口 - 同步版本用于任务中
                    List<CmdWindowInfo> foundWindows = new List<CmdWindowInfo>();

                    // 手动枚举窗口而不使用异步方法
                    EnumWindows((hWnd, lParam) =>
                    {
                        if (token.IsCancellationRequested) return false;

                        if (!IsWindowVisible(hWnd)) return true;

                        StringBuilder className = new StringBuilder(256);
                        GetClassName(hWnd, className, className.Capacity);
                        string windowClass = className.ToString();

                        if (windowClass == "ConsoleWindowClass")
                        {
                            int length = GetWindowTextLength(hWnd);
                            string windowTitle = "";

                            if (length > 0)
                            {
                                StringBuilder title = new StringBuilder(length + 1);
                                GetWindowText(hWnd, title, title.Capacity);
                                windowTitle = title.ToString();
                            }

                            GetWindowThreadProcessId(hWnd, out int processId);

                            try
                            {
                                using (var process = Process.GetProcessById(processId))
                                {
                                    if (process.ProcessName.ToLower() == "cmd")
                                    {
                                        string commandLine = GetProcessCommandLine(processId);

                                        foundWindows.Add(new CmdWindowInfo
                                        {
                                            WindowHandle = hWnd,
                                            ProcessId = processId,
                                            Title = windowTitle,
                                            CreationTime = process.StartTime,
                                            CommandLine = commandLine
                                        });
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"获取进程信息时出错: {ex.Message}");
                            }
                        }
                        return true;
                    }, IntPtr.Zero);

                    // 记录找到的所有CMD窗口信息
                    Debug.WriteLine($"系统中找到 {foundWindows.Count} 个CMD窗口");

                    // 1. 首先尝试完整命令行匹配 - 最精确的方法
                    List<CmdWindowInfo> targetWindows = foundWindows.Where(w =>
                        !string.IsNullOrEmpty(w.CommandLine) && (
                            // 检查完整路径或与文件名+目录的组合匹配
                            w.CommandLine.Contains(fullPath) ||
                            (w.CommandLine.Contains($"\"{batchFileName}\"") &&
                             w.CommandLine.Contains($"\"{batchDirectory}\""))
                        )
                    ).ToList();

                    // 记录命令行匹配情况
                    if (targetWindows.Count > 0)
                    {
                        Debug.WriteLine($"通过命令行精确匹配到 {targetWindows.Count} 个CMD窗口:");
                        foreach (var window in targetWindows)
                        {
                            Debug.WriteLine($"- PID: {window.ProcessId}, 命令行: '{window.CommandLine}'");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("没有通过命令行精确匹配找到匹配的CMD窗口，尝试更宽松的命令行匹配");

                        // 尝试更宽松的命令行匹配
                        targetWindows = foundWindows.Where(w =>
                            !string.IsNullOrEmpty(w.CommandLine) && (
                                w.CommandLine.Contains(batchFileName) &&
                                w.CommandLine.Contains(Path.GetFileName(batchDirectory))
                            )
                        ).ToList();

                        if (targetWindows.Count > 0)
                        {
                            Debug.WriteLine($"通过宽松命令行匹配到 {targetWindows.Count} 个CMD窗口");
                        }
                        else
                        {
                            Debug.WriteLine("没有通过任何命令行匹配找到匹配的CMD窗口，尝试通过窗口标题匹配");

                            // 2. 如果命令行匹配失败，尝试更严格的标题匹配
                            targetWindows = foundWindows.Where(w =>
                                !string.IsNullOrEmpty(w.Title) && (
                                    // 标题必须同时包含文件名和所在目录的最后一级
                                    (w.Title.Contains(batchFileName) && w.Title.Contains(Path.GetFileName(batchDirectory))) ||
                                    // 或者标题是典型的CMD命令提示符格式，显示完整目录
                                    (w.Title.Contains(">") && w.Title.Contains(batchDirectory))
                                )
                            ).ToList();

                            if (targetWindows.Count > 0)
                            {
                                Debug.WriteLine($"通过标题匹配到 {targetWindows.Count} 个CMD窗口");
                            }
                            else
                            {
                                Debug.WriteLine("没有通过标题找到匹配的CMD窗口，尝试通过端口关联查找");

                                // 3. 如果还是没找到，尝试检查同一端口相关进程
                                HashSet<int> portUsingPids = GetProcessIdsUsingPort(service.Port);

                                if (portUsingPids.Count > 0)
                                {
                                    Debug.WriteLine($"找到 {portUsingPids.Count} 个使用端口 {service.Port} 的进程");

                                    // 只关闭那些确实使用了指定端口的CMD进程
                                    targetWindows = foundWindows.Where(w =>
                                        portUsingPids.Contains(w.ProcessId)
                                    ).ToList();

                                    Debug.WriteLine($"其中有 {targetWindows.Count} 个是CMD窗口");
                                }
                                else
                                {
                                    Debug.WriteLine($"找不到使用端口 {service.Port} 的进程");
                                }
                            }
                        }
                    }

                    // 4. 如果仍然找不到，不采取任何操作而不是随意选择窗口
                    if (targetWindows.Count == 0)
                    {
                        Debug.WriteLine($"警告: 无法找到与服务 {batchFileName} 关联的CMD窗口，不会关闭任何窗口");
                        // 重要: 不再随意关闭最近的窗口
                        return;
                    }

                    // 关闭找到的目标窗口
                    foreach (var window in targetWindows)
                    {
                        if (token.IsCancellationRequested) break;

                        string matchReason = !string.IsNullOrEmpty(window.CommandLine) &&
                                          window.CommandLine.Contains(batchFileName) ?
                                          "命令行匹配" : "标题或端口匹配";

                        Debug.WriteLine($"关闭CMD窗口 ({matchReason}): PID={window.ProcessId}, 标题='{window.Title}'");

                        try
                        {
                            // 先尝试发送关闭消息到窗口
                            SendMessage(window.WindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

                            // 等待一小段时间，查看窗口是否关闭
                            Thread.Sleep(300);

                            // 再次发送异步关闭消息
                            PostMessage(window.WindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

                            // 再次等待
                            Thread.Sleep(200);

                            // 检查进程是否还存在
                            try
                            {
                                using (var proc = Process.GetProcessById(window.ProcessId))
                                {
                                    if (!proc.HasExited)
                                    {
                                        Debug.WriteLine($"窗口未响应WM_CLOSE消息，尝试终止进程: {window.ProcessId}");
                                        proc.Kill();
                                    }
                                }
                            }
                            catch (ArgumentException)
                            {
                                // 进程已经不存在了，无需处理
                                Debug.WriteLine($"进程 {window.ProcessId} 已经终止");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"关闭窗口时出错: {ex.Message}");
                        }
                    }

                    // 终止所有使用该端口的进程 - 不使用异步方法以减少复杂性
                    ProcessKillProcessesUsingPort(service.Port);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"终止批处理进程时发生错误: {ex.Message}");
                }
            }, token);
        }

        // 异步关闭进程
        private async Task CloseProcessesByNameAsync(string processName, CancellationToken token)
        {
            await Task.Run(() =>
            {
                try
                {
                    // 查找具有给定名称的运行进程
                    Process[] processes = Process.GetProcessesByName(processName);
                    Debug.WriteLine($"找到 {processes.Length} 个名为 {processName} 的进程");

                    foreach (var process in processes)
                    {
                        if (token.IsCancellationRequested) break;

                        try
                        {
                            if (!process.HasExited)
                            {
                                Debug.WriteLine($"正在尝试关闭进程: PID={process.Id}, 名称={process.ProcessName}");

                                if (process.MainWindowHandle != IntPtr.Zero)
                                {
                                    // 先尝试优雅地关闭窗口
                                    Debug.WriteLine($"尝试关闭主窗口");
                                    process.CloseMainWindow();
                                    if (!process.WaitForExit(5000)) // 等待最多5秒
                                    {
                                        Debug.WriteLine($"窗口未响应，强制终止进程");
                                        process.Kill(); // 如果窗口没有关闭，则强制终止
                                    }
                                }
                                else
                                {
                                    // 没有找到窗口，直接终止进程
                                    Debug.WriteLine($"进程没有主窗口，直接终止");
                                    process.Kill();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"关闭进程 {processName} 时发生错误: {ex.Message}");
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"CloseProcessesByName 方法中发生错误: {ex.Message}");
                }
            }, token);
        }

        // 异步终止使用端口的进程
        private async Task KillProcessesUsingPortAsync(int port, CancellationToken token)
        {
            await Task.Run(() => ProcessKillProcessesUsingPort(port), token);
        }

        // 同步版本用于后台任务
        private void ProcessKillProcessesUsingPort(int port)
        {
            try
            {
                Debug.WriteLine($"尝试终止使用端口 {port} 的所有进程");

                using (Process process = new Process())
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = "netstat",
                        Arguments = $"-ano | findstr :{port}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };

                    process.StartInfo = startInfo;
                    process.Start();

                    string output = process.StandardOutput.ReadToEnd();
                    if (!process.WaitForExit(10000)) // 添加10秒超时
                    {
                        process.Kill();
                        Debug.WriteLine("netstat命令执行超时，已强制终止");
                        return;
                    }

                    // 解析输出，查找使用此端口的进程ID
                    HashSet<int> pidsToKill = new HashSet<int>();

                    string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in lines)
                    {
                        try
                        {
                            string[] parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length > 4)
                            {
                                string pidString = parts[parts.Length - 1];
                                if (int.TryParse(pidString, out int pid))
                                {
                                    pidsToKill.Add(pid);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"解析netstat输出时出错: {ex.Message}");
                        }
                    }

                    // 终止所有找到的进程
                    foreach (int pid in pidsToKill)
                    {
                        try
                        {
                            Debug.WriteLine($"尝试终止使用端口 {port} 的进程: PID={pid}");

                            using (Process targetProcess = Process.GetProcessById(pid))
                            {
                                if (!targetProcess.HasExited)
                                {
                                    string processName = targetProcess.ProcessName;
                                    Debug.WriteLine($"正在终止进程: PID={pid}, 名称={processName}");

                                    if (targetProcess.MainWindowHandle != IntPtr.Zero)
                                    {
                                        targetProcess.CloseMainWindow();
                                        if (!targetProcess.WaitForExit(3000))
                                        {
                                            targetProcess.Kill();
                                        }
                                    }
                                    else
                                    {
                                        targetProcess.Kill();
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"终止进程 {pid} 时出错: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"端口进程终止操作出错: {ex.Message}");
            }
        }

        // 异步检查端口是否被使用
        private async Task<bool> IsPortInUseAsync(int port)
        {
            if (port < 1 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(port), "端口号必须在1到65535之间。");
            }

            return await Task.Run(() =>
            {
                try
                {
                    var listener = new TcpListener(IPAddress.Any, port);
                    listener.Start(); // 开始监听
                    listener.Stop(); // 立即停止监听
                    return false; // 端口未被使用
                }
                catch (SocketException)
                {
                    return true; // 端口已被使用
                }
            });
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (_disposed) return;

            // 检查是否是管理员模式
            if (!isAdminMode)
            {
                MessageBox.Show("您需要以管理员模式登录才能添加服务。", "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Executable files (*.exe;*.bat)|*.exe;*.bat|All files (*.*)|*.*";
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    textBox1.Text = openFileDialog.FileName;
                }
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (_disposed) return;

            // 检查是否是管理员模式
            if (!isAdminMode)
            {
                MessageBox.Show("您需要以管理员模式登录才能添加服务。", "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string filePath = textBox1.Text.Trim();
            int port = (int)numericUpDown1.Value;

            // 保存当前的端口号
            lastUsedPort = port;

            if (string.IsNullOrEmpty(filePath))
            {
                MessageBox.Show("请选择或输入文件路径！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (port <= 0 || port > 65535)
            {
                MessageBox.Show("请输入有效的端口号（1-65535）！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            AddService(filePath, port);
            textBox1.Clear();
            // 不清除端口号，保持用户输入的值
        }

        private void numericUpDown2_ValueChanged(object sender, EventArgs e)
        {
            if (_disposed || isInitializing) return;

            // 检查是否是管理员模式
            if (!isAdminMode)
            {
                MessageBox.Show("您需要以管理员模式登录才能修改扫描间隔。", "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                // 恢复原值，但避免再次触发事件
                numericUpDown2.ValueChanged -= numericUpDown2_ValueChanged;
                numericUpDown2.Value = scanIntervalMinutes;
                numericUpDown2.ValueChanged += numericUpDown2_ValueChanged;
                return;
            }

            scanIntervalMinutes = (int)numericUpDown2.Value;
            UpdateScanIntervalInDb(scanIntervalMinutes); // 更新数据库中的扫描间隔
            StartMonitoring();
        }

        private void UpdateScanIntervalInDb(int interval)
        {
            try
            {
                using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    connection.Open();
                    string sql = "UPDATE Settings SET ScanInterval = @ScanInterval;";
                    using (var command = new SQLiteCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@ScanInterval", interval);
                        command.ExecuteNonQuery();
                    }
                }

                // 清理SQLite连接池
                SQLiteConnection.ClearAllPools();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"更新扫描间隔时发生错误: {ex.Message}");
            }
        }

        private void AddToStartup(bool enable)
        {
            if (_disposed) return;

            try
            {
                string appPath = Application.ExecutablePath;
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (enable)
                    {
                        key.SetValue("PortMonitor", $"\"{appPath}\"");
                    }
                    else
                    {
                        if (key.GetValue("PortMonitor") != null)
                        {
                            key.DeleteValue("PortMonitor", false);
                        }
                    }
                }

                // 更新数据库中的自启状态
                UpdateStartupStatusInDb(enable);
            }
            catch (Exception ex)
            {
                if (!_disposed)
                {
                    MessageBox.Show($"设置自启动时发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void UpdateStartupStatusInDb(bool enabled)
        {
            try
            {
                using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    connection.Open();
                    string sql = "UPDATE Settings SET StartupEnabled = @StartupEnabled;";
                    using (var command = new SQLiteCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@StartupEnabled", enabled ? 1 : 0);
                        command.ExecuteNonQuery();
                    }
                }

                // 清理SQLite连接池
                SQLiteConnection.ClearAllPools();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"更新自启动状态时发生错误: {ex.Message}");
            }
        }

        private bool IsInStartup()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", false))
                {
                    return key.GetValue("PortMonitor") != null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"检查自启动状态时发生错误: {ex.Message}");
                return false;
            }
        }

        private void checkBoxStartup_CheckedChanged(object sender, EventArgs e)
        {
            if (_disposed || isInitializing) return;

            // 检查是否是管理员模式
            if (!isAdminMode)
            {
                MessageBox.Show("您需要以管理员模式登录才能修改自启动设置。", "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                // 恢复原值，但避免再次触发事件
                checkBoxStartup.CheckedChanged -= checkBoxStartup_CheckedChanged;
                checkBoxStartup.Checked = IsInStartup();
                checkBoxStartup.CheckedChanged += checkBoxStartup_CheckedChanged;
                return;
            }

            AddToStartup(checkBoxStartup.Checked);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                // 取消所有异步操作
                _cts.Cancel();

                // 释放资源
                Dispose(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"关闭窗体时发生错误: {ex.Message}");
            }

            base.OnFormClosing(e);
        }

        private async void listView1_MouseClick(object sender, MouseEventArgs e)
        {
            if (_disposed) return;

            var hitTest = listView1.HitTest(e.X, e.Y); // 获取鼠标点击位置的项
            if (hitTest.Item != null && hitTest.SubItem != null) // 确保点击了有效的项和子项
            {
                int colIndex = hitTest.Item.SubItems.IndexOf(hitTest.SubItem); // 找到点击的子项的索引
                var service = (MonitoredService)hitTest.Item.Tag; // 获取与该项关联的服务对象

                if (colIndex == 5) // 如果点击的是操作列
                {
                    if (e.X < hitTest.SubItem.Bounds.Left + (hitTest.SubItem.Bounds.Width / 2))
                    {
                        // 重启服务的操作 - 不需要管理员权限
                        if (MessageBox.Show($"确定要重启这个服务吗？\n文件: {service.FilePath}\n端口: {service.Port}",
                                          "确认重启", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        {
                            // 使用异步方法重启服务，防止UI冻结
                            await RestartServiceAsync(service);
                        }
                    }
                    else
                    {
                        // 检查是否是管理员模式
                        if (!isAdminMode)
                        {
                            MessageBox.Show("您需要以管理员模式登录才能删除服务。", "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        // 删除服务的操作
                        if (MessageBox.Show($"确定要删除这个监控项吗？\n文件: {service.FilePath}\n端口: {service.Port}",
                                          "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        {
                            RemoveService(service.Id); // 调用删除服务的方法
                        }
                    }
                }
            }
        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            // 可以在这里添加逻辑处理用户选择列表项时的操作
        }

        // 正确实现IDisposable模式
        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 释放托管资源
                    _cts?.Cancel();

                    // 停止并释放定时器
                    if (monitorTimer != null)
                    {
                        monitorTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        monitorTimer.Dispose();
                        monitorTimer = null;
                    }

                    if (countdownTimer != null)
                    {
                        countdownTimer.Stop();
                        countdownTimer.Tick -= CountdownTimer_Tick; // 解绑事件处理程序
                        countdownTimer.Dispose();
                        countdownTimer = null;
                    }

                    // 解绑事件处理
                    if (listView1 != null)
                    {
                        listView1.MouseClick -= listView1_MouseClick;
                        listView1.SelectedIndexChanged -= listView1_SelectedIndexChanged;
                    }

                    // 清空集合
                    if (monitoredServices != null)
                    {
                        monitoredServices.Clear();
                        monitoredServices = null;
                    }

                    // 清理SQLite连接池
                    SQLiteConnection.ClearAllPools();

                    // 释放取消令牌源
                    _cts?.Dispose();
                    _cts = null;
                }

                // 释放非托管资源
                _disposed = true;

                // 建议垃圾回收
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false, false);
            }

            base.Dispose(disposing);
        }
    }


    // P/Invoke调用，用于减少应用程序工作集大小
    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        internal static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimumWorkingSetSize, IntPtr maximumWorkingSetSize);
    }
}
