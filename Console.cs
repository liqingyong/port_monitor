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
        private int scanIntervalMinutes = 2; // Ĭ��ɨ����
        private int countdownSeconds;
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _disposed = false;
        private int lastUsedPort = 8080; // �����ϴ�ʹ�õĶ˿ں�
        private int memoryCleanupCounter = 0; // �ڴ����������
        private bool isAdminMode = false; // ��ӹ���Աģʽ��־
        private bool isInitializing = true; // ��ӳ�ʼ����־����ֹ��ʼ��ʱ����Ȩ�޼��

        // ����״̬��ǩ��������ʾ����״̬
        private Label statusLabel;

        // ���ڴ洢CMD������Ϣ����
        private class CmdWindowInfo
        {
            public IntPtr WindowHandle { get; set; }
            public int ProcessId { get; set; }
            public string Title { get; set; }
            public DateTime CreationTime { get; set; }
            public string CommandLine { get; set; } // �����������Ϣ
        }

        public Console()
        {
            InitializeComponent();
            dbPath = Path.Combine(Application.StartupPath, DbFileName);
            monitoredServices = new List<MonitoredService>();

            // ��ʼ���˿ںſؼ�
            numericUpDown1.Minimum = 1;
            numericUpDown1.Maximum = 65535;
            numericUpDown1.Value = lastUsedPort; // ����Ĭ�϶˿�

            // ��ʼ��״̬��ǩ
            InitializeStatusLabel();

            // ��ʼ����������ݿ�
            SetupListView();
            InitializeDatabase();
            InitializeCountdownTimer();
        }

        // ��ʼ��״̬��ǩ
        private void InitializeStatusLabel()
        {
            statusLabel = new Label();
            statusLabel.AutoSize = false;
            statusLabel.Dock = DockStyle.Bottom;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Height = 20;
            statusLabel.BackColor = System.Drawing.Color.LightGray;
            statusLabel.Padding = new Padding(5, 0, 0, 0);
            statusLabel.Text = "����";
            this.Controls.Add(statusLabel);
        }

        // ����״̬��Ϣ
        private void UpdateStatus(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(UpdateStatus), message);
                return;
            }

            statusLabel.Text = message;
            Application.DoEvents(); // ����UI����
        }

        private void SetupListView()
        {
            listView1.Items.Clear();
            listView1.Columns.Clear();
            listView1.View = View.Details;
            listView1.FullRowSelect = true;
            listView1.GridLines = true;
            listView1.Columns.Add("�ļ�Ŀ¼", 322); // �ļ�Ŀ¼��
            listView1.Columns.Add("�ļ���", 325); // �ļ�����
            listView1.Columns.Add("�˿ں�", 80); // �˿���
            listView1.Columns.Add("�Ƿ�����", 80); // ����״̬��
            listView1.Columns.Add("ɨ�赹��ʱ", 100); // ����ʱ��
            listView1.Columns.Add("����", 100); // ������
            listView1.MouseClick += listView1_MouseClick; // ��������¼�
            listView1.SelectedIndexChanged += listView1_SelectedIndexChanged; // ��ѡ����ı��¼�
        }

        private void InitializeCountdownTimer()
        {
            // ȷ�������������ʱ��ʵ��
            if (countdownTimer != null)
            {
                countdownTimer.Stop();
                countdownTimer.Tick -= CountdownTimer_Tick; // ����¼��������
                countdownTimer.Dispose();
            }

            countdownTimer = new System.Windows.Forms.Timer();
            countdownTimer.Interval = 1000; // 1��
            countdownTimer.Tick += CountdownTimer_Tick;
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                isInitializing = true; // ��ǳ�ʼ����ʼ

                // �Ƚ��ù���Ա�ؼ�
                DisableAdminControls();

                UpdateStatus("���ڼ�������...");

                // ��������
                await LoadConfigurationAsync(_cts.Token);

                UpdateStatus("���ڼ��ؼ�ط���...");

                // �����ѱ���ķ��񲢿�ʼ���
                await LoadMonitoredServicesAsync(_cts.Token);
                UpdateListView();
                StartMonitoring();

                UpdateStatus("��������� - ֻ��ģʽ");

                // ��ʼ�����
                isInitializing = false;

                // ��ʾ��¼����
                using (var loginForm = new LoginForm())
                {
                    loginForm.Password = password;
                    if (loginForm.ShowDialog() == DialogResult.OK)
                    {
                        // ��¼�ɹ������ù���Ա�ؼ�
                        EnableAdminControls();
                        isAdminMode = true;
                        UpdateStatus("���� - ����Աģʽ");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"��ʼ��Ӧ�ó���ʱ��������: {ex.Message}", "����", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateStatus("��ʼ����������");
                isInitializing = false;
            }
        }

        // ���ù���Ա�ؼ�
        private void DisableAdminControls()
        {
            button1.Enabled = false; // �ļ������ť
            button2.Enabled = false; // ��ӷ���ť
            numericUpDown1.Enabled = false; // �˿�ѡ����
            numericUpDown2.Enabled = false; // ɨ����
            textBox1.Enabled = false; // �ļ�·���ı���
            checkBoxStartup.Enabled = false; // ��������ѡ��
        }

        // ���ù���Ա�ؼ�
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
                // ���Settings���Ƿ����
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

                                // ��¼������״̬�����ڳ�ʼ�������в������¼�
                                bool startupEnabled = reader.GetBoolean(1);

                                // ����¼�������ֵ�������°�
                                numericUpDown2.ValueChanged -= numericUpDown2_ValueChanged;
                                numericUpDown2.Value = scanIntervalMinutes;
                                numericUpDown2.ValueChanged += numericUpDown2_ValueChanged;

                                // ͬ������������ѡ��
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
                    // ������ʱ��������Ĭ��ֵ
                    await CreateDefaultSettingsAsync(connection, token);

                    // ����Ĭ��ֵ�����ⴥ���¼�
                    numericUpDown2.ValueChanged -= numericUpDown2_ValueChanged;
                    numericUpDown2.Value = scanIntervalMinutes;
                    numericUpDown2.ValueChanged += numericUpDown2_ValueChanged;

                    checkBoxStartup.CheckedChanged -= checkBoxStartup_CheckedChanged;
                    checkBoxStartup.Checked = false;
                    checkBoxStartup.CheckedChanged += checkBoxStartup_CheckedChanged;
                }
            }

            // ����SQLite���ӳ�
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

            // ����Ĭ������
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
                // ������ݿ��ļ������ڣ��򴴽����ݿ�
                if (!File.Exists(dbPath))
                {
                    SQLiteConnection.CreateFile(dbPath);
                    using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                    {
                        connection.Open();
                        CreateTables(connection); // ��������ı�
                    }
                }
                else
                {
                    // ��鲢�������ݿ⣬���ȱ���ֶ������
                    UpgradeDatabase();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"��ʼ�����ݿ�ʱ��������: {ex.Message}", "����", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

            // ����Ĭ������
            string insertSettings = "INSERT INTO Settings (ScanInterval, StartupEnabled) VALUES (2, 0);";
            using (var command = new SQLiteCommand(insertSettings, connection))
            {
                command.ExecuteNonQuery();
            }

            // ����SQLite���ӳ�
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
                        existingColumns.Add(reader.GetString(1)); // ����ֶ������б�
                    }
                }

                // ʹ�÷����ȡMonitoredService�������������
                var properties = typeof(MonitoredService).GetProperties(BindingFlags.Public | BindingFlags.Instance);
                foreach (var property in properties)
                {
                    // ��鲢���ȱʧ���ֶ�
                    if (!existingColumns.Contains(property.Name))
                    {
                        string columnType = GetSQLiteType(property.PropertyType);
                        command.CommandText = $"ALTER TABLE Services ADD COLUMN {property.Name} {columnType} DEFAULT 0;";
                        command.ExecuteNonQuery();
                    }
                }
            }

            // ������Դ
            SQLiteConnection.ClearAllPools();
        }

        // ��ȡSQLite��������
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
            return "TEXT"; // Ĭ������
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

                // �첽���ÿ�������״̬
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
                    // ����ȡ��������Ҫ����
                }

                // ֻ��������������ɺ��ٸ����б� - ���Ⲣ���޸�
                lock (lockObject)
                {
                    if (_disposed) return;
                    monitoredServices.Clear();
                    monitoredServices.AddRange(tempServices);
                }

                // ������Դ
                tasks.Clear();
                tasks = null;
                tempServices = null;

                // ����SQLite���ӳ�
                SQLiteConnection.ClearAllPools();
            }
            catch (TaskCanceledException)
            {
                // ����ȡ��������Ҫ����
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested && !_disposed)
                {
                    MessageBox.Show($"���ط�������ʱ��������: {ex.Message}", "����", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            // ǿ��ִ��һ���ڴ�����
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
            int interval = scanIntervalMinutes * 60 * 1000; // ������ת��Ϊ����
            countdownSeconds = scanIntervalMinutes * 60; // ���õ���ʱ

            lock (lockObject)
            {
                // ֹͣ���ͷžɵĶ�ʱ��
                if (monitorTimer != null)
                {
                    monitorTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    monitorTimer.Dispose();
                    monitorTimer = null;
                }

                // �����µĶ�ʱ��
                monitorTimer = new System.Threading.Timer(MonitorServices, null, 0, interval);

                // ȷ������ʱ��ʱ����������
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
                countdownSeconds = scanIntervalMinutes * 60; // ���õ���ʱ
            }

            // ����UI
            if (!_disposed)
            {
                UpdateListView();
            }

            // �������ڴ�����
            memoryCleanupCounter++;
            if (memoryCleanupCounter >= 60) // ÿ60��ִ��һ���ڴ�����
            {
                CleanupMemory();
                memoryCleanupCounter = 0;
            }
        }

        // �ڴ�������
        private void CleanupMemory()
        {
            try
            {
                // ����SQLite���ӳ�
                SQLiteConnection.ClearAllPools();

                // ���ٹ�������С
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    NativeMethods.SetProcessWorkingSetSize(
                        Process.GetCurrentProcess().Handle,
                        (IntPtr)(-1),
                        (IntPtr)(-1));
                }

                // ������������
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false, false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"�ڴ�����ʱ��������: {ex.Message}");
            }
        }

        private async void RestartOfflineServices()
        {
            if (_disposed) return;

            List<MonitoredService> servicesToRestart = new List<MonitoredService>();

            lock (lockObject)
            {
                if (_disposed) return;
                // ����һ�������������ڵ���ʱ�޸ļ���
                servicesToRestart.AddRange(monitoredServices.Where(s => !s.IsOnline));
            }

            foreach (var service in servicesToRestart)
            {
                // ����Ƿ��Ѵ���
                if (_disposed) break;

                service.RestartAttempts++;
                if (service.RestartAttempts <= 3) // ����������Դ���������3��
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
                        MessageBox.Show($"���� {service.FileName} �����γ��Ժ��޷���������ֹͣ�������ԡ�", "����", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        //service.RestartAttempts = 0; // ���ó��Դ���

                        // �첽�������ݿ�
                        StartBackgroundTask(async (token) =>
                        {
                            await UpdateServiceStatusInDbAsync(service, token);
                        });
                    }
                }
            }

            // ������Դ
            servicesToRestart.Clear();
            servicesToRestart = null;
        }

        // ������̨����ĸ�������������ֱ��ʹ��Task.Run���µ��ڴ�й©
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
                    // ����ȡ��������Ҫ����
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"��̨����ִ�д���: {ex.Message}");
                }
            }, token);
        }

        private async void MonitorServices(object state)
        {
            try
            {
                if (_disposed) return;

                await CheckAllServicesAsync(_cts.Token);

                // ���õ���ʱ
                countdownSeconds = scanIntervalMinutes * 60;

                // ����UI��ȷ���������ͷŵĶ����ϵ���
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
                // ����ȡ��������Ҫ����
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"��ط���ʱ��������: {ex.Message}");
            }
        }

        private async Task CheckAllServicesAsync(CancellationToken token)
        {
            if (_disposed || token.IsCancellationRequested) return;

            List<MonitoredService> localServices = new List<MonitoredService>();

            // ���������б�ĸ���
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
                        // ����ȡ��
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"������״̬ʱ��������: {ex.Message}");
                    }
                }, token));
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (TaskCanceledException)
            {
                // ����ȡ��������Ҫ����
            }
            finally
            {
                // ������Դ
                tasks.Clear();
                tasks = null;
                localServices.Clear();
                localServices = null;

                // ����SQLite���ӳ�
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
                        // ���ӳɹ�
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

                // �������ӳ�
                SQLiteConnection.ClearAllPools();
            }
            catch (TaskCanceledException)
            {
                // ����ȡ��������Ҫ����
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"���·���״̬ʱ��������: {ex.Message}");
            }
        }

        private void UpdateListView()
        {
            // ȷ����UI�߳���ִ��
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(UpdateListView));
                return;
            }

            if (_disposed) return;

            // ���浱ǰѡ�е�����
            int selectedIndex = -1;
            if (listView1.SelectedIndices.Count > 0)
                selectedIndex = listView1.SelectedIndices[0];

            // ���յ�ǰ�����Ż�ˢ��
            Dictionary<int, ListViewItem> existingItems = new Dictionary<int, ListViewItem>();
            foreach (ListViewItem item in listView1.Items)
            {
                var service = item.Tag as MonitoredService;
                if (service != null)
                {
                    existingItems[service.Id] = item;
                }
            }

            // �������ظ�����������UI���¹����б��޸�
            List<MonitoredService> localServices = new List<MonitoredService>();
            lock (lockObject)
            {
                if (_disposed) return;
                localServices.AddRange(monitoredServices);
            }

            listView1.BeginUpdate(); // ��ʼ�����������������

            try
            {
                // ʹ���µĸ��²��� - ֻ�����Ѹı�������UI����
                foreach (var service in localServices)
                {
                    if (_disposed) break;

                    ListViewItem item;
                    bool isNew = !existingItems.TryGetValue(service.Id, out item);

                    if (isNew)
                    {
                        // �����µ�ListViewItem
                        item = new ListViewItem(Path.GetDirectoryName(service.FilePath)); // �ļ�Ŀ¼
                        item.SubItems.Add(service.FileName); // �ļ���
                        item.SubItems.Add(service.Port.ToString()); // �˿�
                        item.SubItems.Add(service.IsOnline ? "����" : "����"); // ����״̬

                        int minutes = countdownSeconds / 60;
                        int seconds = countdownSeconds % 60;
                        item.SubItems.Add($"{minutes:00}:{seconds:00}"); // ����ʱ
                        item.SubItems.Add("���� | ɾ��"); // ����
                        item.Tag = service; // ���������󶨵���ı�ǩ��
                        item.BackColor = service.IsOnline ? Color.LightGreen : Color.LightPink;

                        listView1.Items.Add(item);
                    }
                    else
                    {
                        // ����������
                        existingItems.Remove(service.Id); // ���ֵ����Ƴ���ʣ�µĽ���Ҫɾ������

                        // ֻ���¿��ܷ����仯�Ĳ���
                        item.SubItems[3].Text = service.IsOnline ? "����" : "����";

                        int minutes = countdownSeconds / 60;
                        int seconds = countdownSeconds % 60;
                        item.SubItems[4].Text = $"{minutes:00}:{seconds:00}";

                        item.BackColor = service.IsOnline ? Color.LightGreen : Color.LightPink;
                    }
                }

                // ɾ�����ٴ��ڵ���
                foreach (var itemToRemove in existingItems.Values)
                {
                    listView1.Items.Remove(itemToRemove);
                }
            }
            finally
            {
                // �ָ�ѡ��״̬
                if (selectedIndex >= 0 && selectedIndex < listView1.Items.Count)
                    listView1.Items[selectedIndex].Selected = true;

                listView1.EndUpdate(); // ������������
            }

            // ������Դ
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
                    MessageBox.Show("�÷����Ѵ����ڼ���б��У�", "��ʾ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string fileName = Path.GetFileName(filePath); // ��ȡ�ļ���
                int newId = 0;

                // ʹ������֤����������
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

                // �첽������״̬
                await CheckServiceStatusAsync(service, _cts.Token);

                // ��ӵ��б�����UI
                lock (lockObject)
                {
                    if (_disposed) return;
                    monitoredServices.Add(service);
                }

                UpdateListView();

                // ����SQLite���ӳ�
                SQLiteConnection.ClearAllPools();
            }
            catch (TaskCanceledException)
            {
                // ����ȡ��������Ҫ����
            }
            catch (Exception ex)
            {
                if (!_cts.Token.IsCancellationRequested && !_disposed)
                {
                    MessageBox.Show($"��ӷ���ʱ��������: {ex.Message}", "����", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private async void RemoveService(int id)
        {
            if (_disposed) return;

            // ����Ƿ��ǹ���Աģʽ
            if (!isAdminMode)
            {
                MessageBox.Show("����Ҫ�Թ���Աģʽ��¼����ɾ������", "Ȩ�޲���", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // �����ݿ�ɾ��
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

                // ���ڴ��б���ɾ��
                lock (lockObject)
                {
                    if (_disposed) return;
                    monitoredServices.RemoveAll(s => s.Id == id);
                }

                UpdateListView();

                // ����SQLite���ӳ�
                SQLiteConnection.ClearAllPools();
            }
            catch (TaskCanceledException)
            {
                // ����ȡ��������Ҫ����
            }
            catch (Exception ex)
            {
                if (!_cts.Token.IsCancellationRequested && !_disposed)
                {
                    MessageBox.Show($"ɾ������ʱ��������: {ex.Message}", "����", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // Windows API �����͵���
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

        // �޸�Ϊ�첽��������������UI�߳�
        private async Task RestartServiceAsync(MonitoredService service)
        {
            if (_disposed) return;

            try
            {
                if (!File.Exists(service.FilePath))
                {
                    MessageBox.Show($"�޷��ҵ��ļ�: {service.FilePath}", "����", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string processName = Path.GetFileNameWithoutExtension(service.FilePath);
                string extension = Path.GetExtension(service.FilePath).ToLower();
                int servicePort = service.Port;
                int serviceId = service.Id;

                // ��ʾ״̬
                UpdateStatus($"������������: {service.FileName}");

                // ����ʱ�����ŵ���̨�߳�
                await Task.Run(async () =>
                {
                    // �ر����н���
                    if (extension == ".bat")
                    {
                        await TerminateBatchProcessesAsync(service, _cts.Token);
                    }
                    else
                    {
                        await CloseProcessesByNameAsync(processName, _cts.Token);
                    }

                    // ɱ��ʹ�øö˿ڵĽ���
                    await KillProcessesUsingPortAsync(servicePort, _cts.Token);

                    // ���ݵȴ�ȷ�������ѹر�
                    await Task.Delay(500, _cts.Token);
                }, _cts.Token);

                // �ص�UI�߳������½���
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

                // ����״̬
                UpdateStatus("���ڼ�����״̬...");

                // �첽����������״̬
                StartBackgroundTask(async (token) =>
                {
                    await Task.Delay(12000, token); // �ȴ�12��

                    if (token.IsCancellationRequested || _disposed) return;

                    // ��ȡ��ǰ����ʵ������������
                    MonitoredService currentService = null;
                    lock (lockObject)
                    {
                        if (_disposed) return;
                        currentService = monitoredServices.FirstOrDefault(s => s.Id == serviceId);
                    }

                    if (currentService != null)
                    {
                        // ���˿�״̬
                        bool isPortOpen = await IsPortOpenAsync(servicePort, token);
                        currentService.IsOnline = isPortOpen;
                        if (isPortOpen) { 
                            currentService.RestartAttempts = 0;
                        }
                        await UpdateServiceStatusInDbAsync(currentService, token);
                    }

                    // ����UI
                    if (!_disposed && !token.IsCancellationRequested)
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            if (!_disposed)
                            {
                                UpdateListView();
                                UpdateStatus("����");
                            }
                        }));
                    }
                });
            }
            catch (TaskCanceledException)
            {
                // ����ȡ��������״̬
                UpdateStatus("������ȡ��");
            }
            catch (Exception ex)
            {
                UpdateStatus("����ʧ��");

                if (!_cts.Token.IsCancellationRequested && !_disposed)
                {
                    MessageBox.Show($"��������ʱ��������: {ex.Message}", "����", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ��ȡʹ��ָ���˿ڵĽ���ID
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
                            Debug.WriteLine($"����netstat���ʱ����: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"��ȡʹ�ö˿ڽ���ʱ����: {ex.Message}");
            }

            return result;
        }

        // �첽����CMD����
        private async Task<List<CmdWindowInfo>> FindAllCmdWindowsAsync(CancellationToken token)
        {
            return await Task.Run(() =>
            {
                List<CmdWindowInfo> result = new List<CmdWindowInfo>();

                EnumWindows((hWnd, lParam) =>
                {
                    if (token.IsCancellationRequested)
                        return false;

                    // ��鴰���Ƿ�ɼ�
                    if (!IsWindowVisible(hWnd))
                        return true;

                    // ��ȡ��������
                    StringBuilder className = new StringBuilder(256);
                    GetClassName(hWnd, className, className.Capacity);
                    string windowClass = className.ToString();

                    // ����Ƿ��ǿ���̨����
                    if (windowClass == "ConsoleWindowClass")
                    {
                        // ��ȡ���ڱ���
                        int length = GetWindowTextLength(hWnd);
                        string windowTitle = "";

                        if (length > 0)
                        {
                            StringBuilder title = new StringBuilder(length + 1);
                            GetWindowText(hWnd, title, title.Capacity);
                            windowTitle = title.ToString();
                        }

                        // ��ȡ����ID
                        GetWindowThreadProcessId(hWnd, out int processId);

                        try
                        {
                            using (var process = Process.GetProcessById(processId))
                            {
                                // ȷ����CMD����
                                if (process.ProcessName.ToLower() == "cmd")
                                {
                                    // ���Ի�ȡ��������Ϣ
                                    string commandLine = "";
                                    try
                                    {
                                        commandLine = GetProcessCommandLine(processId);
                                    }
                                    catch (Exception cmdEx)
                                    {
                                        Debug.WriteLine($"��ȡ������ʧ��: {cmdEx.Message}");
                                    }

                                    result.Add(new CmdWindowInfo
                                    {
                                        WindowHandle = hWnd,
                                        ProcessId = processId,
                                        Title = windowTitle,
                                        CreationTime = process.StartTime,
                                        CommandLine = commandLine
                                    });

                                    Debug.WriteLine($"�ҵ�CMD����: PID={processId}, ����='{windowTitle}', ������='{commandLine}'");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"��ȡ������Ϣʱ����: {ex.Message}");
                        }
                    }

                    return true; // ����ö��
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
                    process.WaitForExit(3000); // ��ӳ�ʱȷ��������������

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

        // �Ľ����첽��ֹ��������̷���������رղ���ش���
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

                    Debug.WriteLine($"���ڳ��Թر���������ؽ���: {batchFileName}, Ŀ¼: {batchDirectory}");

                    // ����CMD���� - ͬ���汾����������
                    List<CmdWindowInfo> foundWindows = new List<CmdWindowInfo>();

                    // �ֶ�ö�ٴ��ڶ���ʹ���첽����
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
                                Debug.WriteLine($"��ȡ������Ϣʱ����: {ex.Message}");
                            }
                        }
                        return true;
                    }, IntPtr.Zero);

                    // ��¼�ҵ�������CMD������Ϣ
                    Debug.WriteLine($"ϵͳ���ҵ� {foundWindows.Count} ��CMD����");

                    // 1. ���ȳ�������������ƥ�� - �ȷ�ķ���
                    List<CmdWindowInfo> targetWindows = foundWindows.Where(w =>
                        !string.IsNullOrEmpty(w.CommandLine) && (
                            // �������·�������ļ���+Ŀ¼�����ƥ��
                            w.CommandLine.Contains(fullPath) ||
                            (w.CommandLine.Contains($"\"{batchFileName}\"") &&
                             w.CommandLine.Contains($"\"{batchDirectory}\""))
                        )
                    ).ToList();

                    // ��¼������ƥ�����
                    if (targetWindows.Count > 0)
                    {
                        Debug.WriteLine($"ͨ�������о�ȷƥ�䵽 {targetWindows.Count} ��CMD����:");
                        foreach (var window in targetWindows)
                        {
                            Debug.WriteLine($"- PID: {window.ProcessId}, ������: '{window.CommandLine}'");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("û��ͨ�������о�ȷƥ���ҵ�ƥ���CMD���ڣ����Ը����ɵ�������ƥ��");

                        // ���Ը����ɵ�������ƥ��
                        targetWindows = foundWindows.Where(w =>
                            !string.IsNullOrEmpty(w.CommandLine) && (
                                w.CommandLine.Contains(batchFileName) &&
                                w.CommandLine.Contains(Path.GetFileName(batchDirectory))
                            )
                        ).ToList();

                        if (targetWindows.Count > 0)
                        {
                            Debug.WriteLine($"ͨ������������ƥ�䵽 {targetWindows.Count} ��CMD����");
                        }
                        else
                        {
                            Debug.WriteLine("û��ͨ���κ�������ƥ���ҵ�ƥ���CMD���ڣ�����ͨ�����ڱ���ƥ��");

                            // 2. ���������ƥ��ʧ�ܣ����Ը��ϸ�ı���ƥ��
                            targetWindows = foundWindows.Where(w =>
                                !string.IsNullOrEmpty(w.Title) && (
                                    // �������ͬʱ�����ļ���������Ŀ¼�����һ��
                                    (w.Title.Contains(batchFileName) && w.Title.Contains(Path.GetFileName(batchDirectory))) ||
                                    // ���߱����ǵ��͵�CMD������ʾ����ʽ����ʾ����Ŀ¼
                                    (w.Title.Contains(">") && w.Title.Contains(batchDirectory))
                                )
                            ).ToList();

                            if (targetWindows.Count > 0)
                            {
                                Debug.WriteLine($"ͨ������ƥ�䵽 {targetWindows.Count} ��CMD����");
                            }
                            else
                            {
                                Debug.WriteLine("û��ͨ�������ҵ�ƥ���CMD���ڣ�����ͨ���˿ڹ�������");

                                // 3. �������û�ҵ������Լ��ͬһ�˿���ؽ���
                                HashSet<int> portUsingPids = GetProcessIdsUsingPort(service.Port);

                                if (portUsingPids.Count > 0)
                                {
                                    Debug.WriteLine($"�ҵ� {portUsingPids.Count} ��ʹ�ö˿� {service.Port} �Ľ���");

                                    // ֻ�ر���Щȷʵʹ����ָ���˿ڵ�CMD����
                                    targetWindows = foundWindows.Where(w =>
                                        portUsingPids.Contains(w.ProcessId)
                                    ).ToList();

                                    Debug.WriteLine($"������ {targetWindows.Count} ����CMD����");
                                }
                                else
                                {
                                    Debug.WriteLine($"�Ҳ���ʹ�ö˿� {service.Port} �Ľ���");
                                }
                            }
                        }
                    }

                    // 4. �����Ȼ�Ҳ���������ȡ�κβ�������������ѡ�񴰿�
                    if (targetWindows.Count == 0)
                    {
                        Debug.WriteLine($"����: �޷��ҵ������ {batchFileName} ������CMD���ڣ�����ر��κδ���");
                        // ��Ҫ: ��������ر�����Ĵ���
                        return;
                    }

                    // �ر��ҵ���Ŀ�괰��
                    foreach (var window in targetWindows)
                    {
                        if (token.IsCancellationRequested) break;

                        string matchReason = !string.IsNullOrEmpty(window.CommandLine) &&
                                          window.CommandLine.Contains(batchFileName) ?
                                          "������ƥ��" : "�����˿�ƥ��";

                        Debug.WriteLine($"�ر�CMD���� ({matchReason}): PID={window.ProcessId}, ����='{window.Title}'");

                        try
                        {
                            // �ȳ��Է��͹ر���Ϣ������
                            SendMessage(window.WindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

                            // �ȴ�һС��ʱ�䣬�鿴�����Ƿ�ر�
                            Thread.Sleep(300);

                            // �ٴη����첽�ر���Ϣ
                            PostMessage(window.WindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

                            // �ٴεȴ�
                            Thread.Sleep(200);

                            // �������Ƿ񻹴���
                            try
                            {
                                using (var proc = Process.GetProcessById(window.ProcessId))
                                {
                                    if (!proc.HasExited)
                                    {
                                        Debug.WriteLine($"����δ��ӦWM_CLOSE��Ϣ��������ֹ����: {window.ProcessId}");
                                        proc.Kill();
                                    }
                                }
                            }
                            catch (ArgumentException)
                            {
                                // �����Ѿ��������ˣ����账��
                                Debug.WriteLine($"���� {window.ProcessId} �Ѿ���ֹ");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"�رմ���ʱ����: {ex.Message}");
                        }
                    }

                    // ��ֹ����ʹ�øö˿ڵĽ��� - ��ʹ���첽�����Լ��ٸ�����
                    ProcessKillProcessesUsingPort(service.Port);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"��ֹ���������ʱ��������: {ex.Message}");
                }
            }, token);
        }

        // �첽�رս���
        private async Task CloseProcessesByNameAsync(string processName, CancellationToken token)
        {
            await Task.Run(() =>
            {
                try
                {
                    // ���Ҿ��и������Ƶ����н���
                    Process[] processes = Process.GetProcessesByName(processName);
                    Debug.WriteLine($"�ҵ� {processes.Length} ����Ϊ {processName} �Ľ���");

                    foreach (var process in processes)
                    {
                        if (token.IsCancellationRequested) break;

                        try
                        {
                            if (!process.HasExited)
                            {
                                Debug.WriteLine($"���ڳ��Թرս���: PID={process.Id}, ����={process.ProcessName}");

                                if (process.MainWindowHandle != IntPtr.Zero)
                                {
                                    // �ȳ������ŵعرմ���
                                    Debug.WriteLine($"���Թر�������");
                                    process.CloseMainWindow();
                                    if (!process.WaitForExit(5000)) // �ȴ����5��
                                    {
                                        Debug.WriteLine($"����δ��Ӧ��ǿ����ֹ����");
                                        process.Kill(); // �������û�йرգ���ǿ����ֹ
                                    }
                                }
                                else
                                {
                                    // û���ҵ����ڣ�ֱ����ֹ����
                                    Debug.WriteLine($"����û�������ڣ�ֱ����ֹ");
                                    process.Kill();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"�رս��� {processName} ʱ��������: {ex.Message}");
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"CloseProcessesByName �����з�������: {ex.Message}");
                }
            }, token);
        }

        // �첽��ֹʹ�ö˿ڵĽ���
        private async Task KillProcessesUsingPortAsync(int port, CancellationToken token)
        {
            await Task.Run(() => ProcessKillProcessesUsingPort(port), token);
        }

        // ͬ���汾���ں�̨����
        private void ProcessKillProcessesUsingPort(int port)
        {
            try
            {
                Debug.WriteLine($"������ֹʹ�ö˿� {port} �����н���");

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
                    if (!process.WaitForExit(10000)) // ���10�볬ʱ
                    {
                        process.Kill();
                        Debug.WriteLine("netstat����ִ�г�ʱ����ǿ����ֹ");
                        return;
                    }

                    // �������������ʹ�ô˶˿ڵĽ���ID
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
                            Debug.WriteLine($"����netstat���ʱ����: {ex.Message}");
                        }
                    }

                    // ��ֹ�����ҵ��Ľ���
                    foreach (int pid in pidsToKill)
                    {
                        try
                        {
                            Debug.WriteLine($"������ֹʹ�ö˿� {port} �Ľ���: PID={pid}");

                            using (Process targetProcess = Process.GetProcessById(pid))
                            {
                                if (!targetProcess.HasExited)
                                {
                                    string processName = targetProcess.ProcessName;
                                    Debug.WriteLine($"������ֹ����: PID={pid}, ����={processName}");

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
                            Debug.WriteLine($"��ֹ���� {pid} ʱ����: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"�˿ڽ�����ֹ��������: {ex.Message}");
            }
        }

        // �첽���˿��Ƿ�ʹ��
        private async Task<bool> IsPortInUseAsync(int port)
        {
            if (port < 1 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(port), "�˿ںű�����1��65535֮�䡣");
            }

            return await Task.Run(() =>
            {
                try
                {
                    var listener = new TcpListener(IPAddress.Any, port);
                    listener.Start(); // ��ʼ����
                    listener.Stop(); // ����ֹͣ����
                    return false; // �˿�δ��ʹ��
                }
                catch (SocketException)
                {
                    return true; // �˿��ѱ�ʹ��
                }
            });
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (_disposed) return;

            // ����Ƿ��ǹ���Աģʽ
            if (!isAdminMode)
            {
                MessageBox.Show("����Ҫ�Թ���Աģʽ��¼������ӷ���", "Ȩ�޲���", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

            // ����Ƿ��ǹ���Աģʽ
            if (!isAdminMode)
            {
                MessageBox.Show("����Ҫ�Թ���Աģʽ��¼������ӷ���", "Ȩ�޲���", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string filePath = textBox1.Text.Trim();
            int port = (int)numericUpDown1.Value;

            // ���浱ǰ�Ķ˿ں�
            lastUsedPort = port;

            if (string.IsNullOrEmpty(filePath))
            {
                MessageBox.Show("��ѡ��������ļ�·����", "����", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (port <= 0 || port > 65535)
            {
                MessageBox.Show("��������Ч�Ķ˿ںţ�1-65535����", "����", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            AddService(filePath, port);
            textBox1.Clear();
            // ������˿ںţ������û������ֵ
        }

        private void numericUpDown2_ValueChanged(object sender, EventArgs e)
        {
            if (_disposed || isInitializing) return;

            // ����Ƿ��ǹ���Աģʽ
            if (!isAdminMode)
            {
                MessageBox.Show("����Ҫ�Թ���Աģʽ��¼�����޸�ɨ������", "Ȩ�޲���", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                // �ָ�ԭֵ���������ٴδ����¼�
                numericUpDown2.ValueChanged -= numericUpDown2_ValueChanged;
                numericUpDown2.Value = scanIntervalMinutes;
                numericUpDown2.ValueChanged += numericUpDown2_ValueChanged;
                return;
            }

            scanIntervalMinutes = (int)numericUpDown2.Value;
            UpdateScanIntervalInDb(scanIntervalMinutes); // �������ݿ��е�ɨ����
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

                // ����SQLite���ӳ�
                SQLiteConnection.ClearAllPools();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"����ɨ����ʱ��������: {ex.Message}");
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

                // �������ݿ��е�����״̬
                UpdateStartupStatusInDb(enable);
            }
            catch (Exception ex)
            {
                if (!_disposed)
                {
                    MessageBox.Show($"����������ʱ��������: {ex.Message}", "����", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

                // ����SQLite���ӳ�
                SQLiteConnection.ClearAllPools();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"����������״̬ʱ��������: {ex.Message}");
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
                Debug.WriteLine($"���������״̬ʱ��������: {ex.Message}");
                return false;
            }
        }

        private void checkBoxStartup_CheckedChanged(object sender, EventArgs e)
        {
            if (_disposed || isInitializing) return;

            // ����Ƿ��ǹ���Աģʽ
            if (!isAdminMode)
            {
                MessageBox.Show("����Ҫ�Թ���Աģʽ��¼�����޸����������á�", "Ȩ�޲���", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                // �ָ�ԭֵ���������ٴδ����¼�
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
                // ȡ�������첽����
                _cts.Cancel();

                // �ͷ���Դ
                Dispose(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"�رմ���ʱ��������: {ex.Message}");
            }

            base.OnFormClosing(e);
        }

        private async void listView1_MouseClick(object sender, MouseEventArgs e)
        {
            if (_disposed) return;

            var hitTest = listView1.HitTest(e.X, e.Y); // ��ȡ�����λ�õ���
            if (hitTest.Item != null && hitTest.SubItem != null) // ȷ���������Ч���������
            {
                int colIndex = hitTest.Item.SubItems.IndexOf(hitTest.SubItem); // �ҵ���������������
                var service = (MonitoredService)hitTest.Item.Tag; // ��ȡ���������ķ������

                if (colIndex == 5) // ���������ǲ�����
                {
                    if (e.X < hitTest.SubItem.Bounds.Left + (hitTest.SubItem.Bounds.Width / 2))
                    {
                        // ��������Ĳ��� - ����Ҫ����ԱȨ��
                        if (MessageBox.Show($"ȷ��Ҫ�������������\n�ļ�: {service.FilePath}\n�˿�: {service.Port}",
                                          "ȷ������", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        {
                            // ʹ���첽�����������񣬷�ֹUI����
                            await RestartServiceAsync(service);
                        }
                    }
                    else
                    {
                        // ����Ƿ��ǹ���Աģʽ
                        if (!isAdminMode)
                        {
                            MessageBox.Show("����Ҫ�Թ���Աģʽ��¼����ɾ������", "Ȩ�޲���", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        // ɾ������Ĳ���
                        if (MessageBox.Show($"ȷ��Ҫɾ������������\n�ļ�: {service.FilePath}\n�˿�: {service.Port}",
                                          "ȷ��ɾ��", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        {
                            RemoveService(service.Id); // ����ɾ������ķ���
                        }
                    }
                }
            }
        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            // ��������������߼������û�ѡ���б���ʱ�Ĳ���
        }

        // ��ȷʵ��IDisposableģʽ
        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // �ͷ��й���Դ
                    _cts?.Cancel();

                    // ֹͣ���ͷŶ�ʱ��
                    if (monitorTimer != null)
                    {
                        monitorTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        monitorTimer.Dispose();
                        monitorTimer = null;
                    }

                    if (countdownTimer != null)
                    {
                        countdownTimer.Stop();
                        countdownTimer.Tick -= CountdownTimer_Tick; // ����¼��������
                        countdownTimer.Dispose();
                        countdownTimer = null;
                    }

                    // ����¼�����
                    if (listView1 != null)
                    {
                        listView1.MouseClick -= listView1_MouseClick;
                        listView1.SelectedIndexChanged -= listView1_SelectedIndexChanged;
                    }

                    // ��ռ���
                    if (monitoredServices != null)
                    {
                        monitoredServices.Clear();
                        monitoredServices = null;
                    }

                    // ����SQLite���ӳ�
                    SQLiteConnection.ClearAllPools();

                    // �ͷ�ȡ������Դ
                    _cts?.Dispose();
                    _cts = null;
                }

                // �ͷŷ��й���Դ
                _disposed = true;

                // ������������
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false, false);
            }

            base.Dispose(disposing);
        }
    }


    // P/Invoke���ã����ڼ���Ӧ�ó���������С
    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        internal static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimumWorkingSetSize, IntPtr maximumWorkingSetSize);
    }
}
