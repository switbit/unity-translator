using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace UnityTranslator.Setup
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args != null && args.Length > 0 && args[0].StartsWith("/", StringComparison.Ordinal))
            {
                return Silent.Run(args);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly TextBox _pathBox;
        private readonly Button _browseButton;
        private readonly Label _statusLabel;
        private readonly CheckBox _launchBox;
        private readonly Button _installButton;
        private readonly Button _uninstallButton;
        private readonly Button _exitButton;
        private readonly ProgressBar _progress;
        private readonly TextBox _logBox;

        private GameInfo _info;

        public MainForm()
        {
            Text = "Универсальный переводчик Unity";
            ClientSize = new Size(720, 590);
            MinimumSize = new Size(736, 629);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            try
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
                // The embedded icon is optional; keep the default if it cannot be read.
            }
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            Label title = new Label();
            title.Text = "Универсальный переводчик Unity";
            title.Font = new Font("Segoe UI Semibold", 15f, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(14, 10);
            Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "Автоматически переводит текст игры на язык Windows. Установка в несколько кликов.";
            subtitle.ForeColor = Color.DimGray;
            subtitle.AutoSize = true;
            subtitle.Location = new Point(16, 44);
            Controls.Add(subtitle);

            Label version = new Label();
            version.Text = "v1.0";
            version.ForeColor = Color.Gray;
            version.AutoSize = true;
            version.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            version.Location = new Point(672, 18);
            Controls.Add(version);

            Label pathLabel = new Label();
            pathLabel.Text = "Папка игры (где находится .exe):";
            pathLabel.AutoSize = true;
            pathLabel.Location = new Point(16, 78);
            Controls.Add(pathLabel);

            _pathBox = new TextBox();
            _pathBox.Location = new Point(16, 100);
            _pathBox.Size = new Size(580, 24);
            _pathBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _pathBox.AllowDrop = true;
            _pathBox.DragEnter += OnDragEnter;
            _pathBox.DragDrop += OnDragDrop;
            _pathBox.Leave += delegate { Analyze(); };
            _pathBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    Analyze();
                    e.SuppressKeyPress = true;
                }
            };
            Controls.Add(_pathBox);

            _browseButton = new Button();
            _browseButton.Text = "Обзор...";
            _browseButton.Location = new Point(604, 98);
            _browseButton.Size = new Size(100, 27);
            _browseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _browseButton.Click += OnBrowseClick;
            Controls.Add(_browseButton);

            GroupBox statusGroup = new GroupBox();
            statusGroup.Text = "Состояние";
            statusGroup.Location = new Point(16, 136);
            statusGroup.Size = new Size(688, 166);
            statusGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(statusGroup);

            _statusLabel = new Label();
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.Padding = new Padding(10, 8, 10, 8);
            _statusLabel.Text = "Выберите папку игры или перетащите её в это окно.";
            _statusLabel.ForeColor = Color.DimGray;
            _statusLabel.AutoSize = false;
            _statusLabel.AllowDrop = true;
            _statusLabel.DragEnter += OnDragEnter;
            _statusLabel.DragDrop += OnDragDrop;
            statusGroup.Controls.Add(_statusLabel);

            _launchBox = new CheckBox();
            _launchBox.Text = "Запустить игру после установки";
            _launchBox.AutoSize = true;
            _launchBox.Checked = true;
            _launchBox.Location = new Point(16, 312);
            Controls.Add(_launchBox);

            _installButton = new Button();
            _installButton.Text = "Установить переводчик";
            _installButton.Location = new Point(16, 340);
            _installButton.Size = new Size(210, 40);
            _installButton.Enabled = false;
            _installButton.Click += OnInstallClick;
            Controls.Add(_installButton);

            _uninstallButton = new Button();
            _uninstallButton.Text = "Удалить переводчик";
            _uninstallButton.Location = new Point(236, 340);
            _uninstallButton.Size = new Size(180, 40);
            _uninstallButton.Enabled = false;
            _uninstallButton.Click += OnUninstallClick;
            Controls.Add(_uninstallButton);

            _exitButton = new Button();
            _exitButton.Text = "Выход";
            _exitButton.Location = new Point(584, 340);
            _exitButton.Size = new Size(120, 40);
            _exitButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _exitButton.Click += delegate { Close(); };
            Controls.Add(_exitButton);

            _progress = new ProgressBar();
            _progress.Location = new Point(16, 390);
            _progress.Size = new Size(688, 6);
            _progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.Visible = false;
            Controls.Add(_progress);

            GroupBox logGroup = new GroupBox();
            logGroup.Text = "Журнал";
            logGroup.Location = new Point(16, 404);
            logGroup.Size = new Size(688, 172);
            logGroup.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(logGroup);

            _logBox = new TextBox();
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = ScrollBars.Vertical;
            _logBox.Dock = DockStyle.Fill;
            _logBox.Font = new Font("Consolas", 9f);
            _logBox.BackColor = Color.White;
            _logBox.WordWrap = true;
            logGroup.Controls.Add(_logBox);

            AcceptButton = _installButton;
            CancelButton = _exitButton;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            GameInfo startup = GameInspector.Inspect(Application.StartupPath);
            if (startup.IsUnity)
            {
                _pathBox.Text = Application.StartupPath;
                Analyze();
            }
        }

        private void OnBrowseClick(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Выберите папку с игрой (там, где находится .exe)";
                dialog.ShowNewFolderButton = false;
                if (Directory.Exists(_pathBox.Text)) dialog.SelectedPath = _pathBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _pathBox.Text = dialog.SelectedPath;
                    Analyze();
                }
            }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;
            _pathBox.Text = files[0];
            Analyze();
        }

        private void Analyze()
        {
            if (string.IsNullOrEmpty(_pathBox.Text)) return;
            _info = GameInspector.Inspect(_pathBox.Text);
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            GameInfo info = _info;
            if (info == null || string.IsNullOrEmpty(info.GameDir))
            {
                SetStatus("Выберите папку игры или перетащите её в это окно.", Color.DimGray);
                _installButton.Enabled = false;
                _uninstallButton.Enabled = false;
                return;
            }

            if (!info.IsUnity)
            {
                SetStatus("Игра Unity не найдена.\r\n\r\nВыберите папку, где находится .exe игры. Рядом с ним должна быть папка <Имя>_Data или файл UnityPlayer.dll.",
                          Color.Firebrick);
                _installButton.Enabled = false;
                _uninstallButton.Enabled = false;
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Игра: " + Path.GetFileName(info.ExePath));
            sb.AppendLine("Unity: " + info.UnityVersion + "    Архитектура: " + ArchText(info.Architecture));
            if (info.IsIL2CPP)
                sb.AppendLine("Скрипты: IL2CPP — этот тип игр не поддерживается");
            else
                sb.AppendLine("Скрипты: Mono — поддерживается");
            sb.AppendLine("BepInEx: " + BepInExText(info));
            sb.AppendLine("Переводчик: " + (info.TranslatorInstalled ? "установлен" : "не установлен"));
            sb.AppendLine("TextMeshPro: " + (info.HasTmp ? "найден" : "не найден (только Text/TextMesh)"));

            bool bepArchMismatch = info.HasBepInEx5 &&
                                   !string.IsNullOrEmpty(info.BepInExArchitecture) &&
                                   info.BepInExArchitecture != "unknown" &&
                                   info.Architecture != "unknown" &&
                                   info.BepInExArchitecture != info.Architecture;
            if (bepArchMismatch)
                sb.AppendLine("Внимание: BepInEx установлен для " + info.BepInExArchitecture +
                              ", а игра — " + info.Architecture + ". Удалите папку BepInEx и повторите установку.");

            Color color = Color.DarkGreen;
            if (info.IsIL2CPP || info.HasBepInExOther || bepArchMismatch ||
                (info.Architecture != "x64" && info.Architecture != "x86"))
                color = Color.Firebrick;
            else if (info.HasLoaderConflict || info.IsRunning)
                color = Color.DarkOrange;

            if (info.IsRunning) sb.AppendLine("Игра сейчас запущена — закройте её перед установкой.");
            SetStatus(sb.ToString(), color);

            bool canInstall = !info.IsIL2CPP && !info.HasBepInExOther && !bepArchMismatch &&
                              (info.Architecture == "x64" || info.Architecture == "x86");
            _installButton.Enabled = canInstall;
            _uninstallButton.Enabled = info.TranslatorInstalled || HasTranslatorConfig(info);
        }

        private static bool HasTranslatorConfig(GameInfo info)
        {
            try
            {
                return File.Exists(Path.Combine(info.GameDir, "BepInEx", "config", "ru.unitytranslator.cfg")) ||
                       Directory.Exists(Path.Combine(info.GameDir, "BepInEx", "config", "UnityTranslator"));
            }
            catch { return false; }
        }

        private static string ArchText(string arch)
        {
            if (arch == "x64") return "x64";
            if (arch == "x86") return "x86 (32-бит)";
            if (arch == "arm64") return "ARM64 — не поддерживается";
            return "неизвестна";
        }

        private static string BepInExText(GameInfo info)
        {
            if (info.HasBepInEx5) return "установлен (BepInEx 5)";
            if (info.HasBepInExOther) return "обнаружена несовместимая версия BepInEx";
            if (info.HasLoaderConflict) return "не установлен; найден другой загрузчик (winhttp.dll/version.dll)";
            return "не установлен — будет установлен автоматически";
        }

        private void SetStatus(string text, Color color)
        {
            _statusLabel.Text = text;
            _statusLabel.ForeColor = color;
        }

        private void OnInstallClick(object sender, EventArgs e)
        {
            Analyze();
            GameInfo info = _info;
            if (info == null || !info.IsUnity)
            {
                MessageBox.Show(this, "Сначала выберите папку с Unity-игрой.", "Нет игры",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (info.IsIL2CPP)
            {
                MessageBox.Show(this, "Игра собрана на IL2CPP. Такой способ перевода поддерживает только игры на Mono.\r\n\r\nУстановка невозможна.",
                                "IL2CPP не поддерживается", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (info.Architecture != "x64" && info.Architecture != "x86")
            {
                MessageBox.Show(this, "Архитектура игры (" + info.Architecture + ") не поддерживается.\r\n" +
                                "Нужна игра x86 или x64.", "Неподдерживаемая архитектура",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (info.HasBepInExOther)
            {
                MessageBox.Show(this, "В папке игры обнаружена несовместимая версия BepInEx.\r\n" +
                                "Удалите папку BepInEx и winhttp.dll, затем запустите установщик снова.",
                                "Несовместимый BepInEx", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (info.IsRunning)
            {
                MessageBox.Show(this, "Игра запущена. Закройте её и повторите установку.",
                                "Игра запущена", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool allowConflict = false;
            if (info.HasLoaderConflict)
            {
                DialogResult result = MessageBox.Show(this,
                    "В папке игры уже есть winhttp.dll или version.dll (другой загрузчик модов).\r\n" +
                    "Установка BepInEx может конфликтовать с ним.\r\n\r\nПродолжить?",
                    "Возможен конфликт", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (result != DialogResult.Yes) return;
                allowConflict = true;
            }

            if (info.TranslatorInstalled)
            {
                DialogResult result = MessageBox.Show(this, "Переводчик уже установлен. Переустановить?",
                                                      "Подтверждение", MessageBoxButtons.YesNo,
                                                      MessageBoxIcon.Question);
                if (result != DialogResult.Yes) return;
            }

            SetBusy(true);
            AppendLog("--- Установка ---");
            try
            {
                Installer.Install(info, allowConflict, AppendLog);
                AppendLog("Готово. Перевод включится при следующем запуске игры.");
                if (_launchBox.Checked) LaunchGame(info);
            }
            catch (UnauthorizedAccessException ex)
            {
                AppendLog("Ошибка доступа: " + ex.Message);
                MessageBox.Show(this, "Нет прав на запись в папку игры.\r\n\r\n" +
                                "Запустите установщик от имени администратора.",
                                "Нет доступа", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                AppendLog("Ошибка: " + ex.Message);
                MessageBox.Show(this, "Не удалось установить переводчик:\r\n\r\n" + ex.Message,
                                "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                EmbeddedResources.Cleanup();
                SetBusy(false);
                Analyze();
            }
        }

        private void OnUninstallClick(object sender, EventArgs e)
        {
            Analyze();
            GameInfo info = _info;
            if (info == null || !info.IsUnity)
            {
                MessageBox.Show(this, "Сначала выберите папку с Unity-игрой.", "Нет игры",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (info.IsRunning)
            {
                MessageBox.Show(this, "Игра запущена. Закройте её перед удалением.",
                                "Игра запущена", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult result = MessageBox.Show(this,
                "Удалить переводчик, его настройки и кэш?\r\n\r\n" +
                "BepInEx и другие моды останутся на месте.",
                "Удаление переводчика", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return;

            SetBusy(true);
            AppendLog("--- Удаление ---");
            try
            {
                Installer.Uninstall(info, AppendLog);
                AppendLog("Готово.");
            }
            catch (UnauthorizedAccessException ex)
            {
                AppendLog("Ошибка доступа: " + ex.Message);
                MessageBox.Show(this, "Нет прав на запись в папку игры.\r\n\r\n" +
                                "Запустите установщик от имени администратора.",
                                "Нет доступа", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                AppendLog("Ошибка: " + ex.Message);
                MessageBox.Show(this, "Не удалось удалить переводчик:\r\n\r\n" + ex.Message,
                                "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                EmbeddedResources.Cleanup();
                SetBusy(false);
                Analyze();
            }
        }

        private void LaunchGame(GameInfo info)
        {
            try
            {
                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = info.ExePath;
                start.WorkingDirectory = info.GameDir;
                start.UseShellExecute = true;
                Process.Start(start);
                AppendLog("Игра запущена: " + Path.GetFileName(info.ExePath));
            }
            catch (Exception ex)
            {
                AppendLog("Не удалось запустить игру: " + ex.Message);
            }
        }

        private void SetBusy(bool busy)
        {
            _progress.Visible = busy;
            _installButton.Enabled = !busy;
            _uninstallButton.Enabled = !busy;
            _browseButton.Enabled = !busy;
            _pathBox.Enabled = !busy;
            _launchBox.Enabled = !busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        private void AppendLog(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLog), new object[] { message });
                return;
            }
            _logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }
    }

    internal sealed class GameInfo
    {
        public string GameDir;
        public string ExePath;
        public string DataDir;
        public string UnityVersion;
        public string Architecture;
        public string BepInExArchitecture;
        public bool IsUnity;
        public bool IsIL2CPP;
        public bool HasBepInEx5;
        public bool HasBepInExOther;
        public bool HasLoaderConflict;
        public bool HasTmp;
        public bool TranslatorInstalled;
        public bool IsRunning;
    }

    internal static class GameInspector
    {
        public static GameInfo Inspect(string path)
        {
            GameInfo info = new GameInfo();
            info.Architecture = "unknown";
            if (string.IsNullOrEmpty(path)) return info;
            path = path.Trim().Trim('"');
            if (path.Length == 0) return info;

            try
            {
                if (File.Exists(path))
                {
                    info.ExePath = path;
                    info.GameDir = Path.GetDirectoryName(path);
                }
                else if (Directory.Exists(path))
                {
                    info.GameDir = path;
                    info.ExePath = FindUnityExe(path, 2);
                }
                else
                {
                    return info;
                }

                if (string.IsNullOrEmpty(info.ExePath) || string.IsNullOrEmpty(info.GameDir))
                    return info;

                string dataDir = Path.Combine(info.GameDir,
                    Path.GetFileNameWithoutExtension(info.ExePath) + "_Data");
                if (!Directory.Exists(dataDir))
                {
                    string[] dirs = Directory.GetDirectories(info.GameDir, "*_Data");
                    for (int i = 0; i < dirs.Length; i++)
                    {
                        if (File.Exists(Path.Combine(dirs[i], "globalgamemanagers")))
                        {
                            dataDir = dirs[i];
                            break;
                        }
                    }
                }
                info.DataDir = Directory.Exists(dataDir) ? dataDir : null;

                info.IsUnity = File.Exists(Path.Combine(info.GameDir, "UnityPlayer.dll")) ||
                               (info.DataDir != null &&
                                File.Exists(Path.Combine(info.DataDir, "globalgamemanagers")));
                if (!info.IsUnity) return info;

                info.Architecture = GetPeArchitecture(info.ExePath);
                info.UnityVersion = ReadUnityVersion(info.DataDir) ?? "неизвестно";
                info.IsIL2CPP = File.Exists(Path.Combine(info.GameDir, "GameAssembly.dll")) ||
                                (info.DataDir != null &&
                                 Directory.Exists(Path.Combine(info.DataDir, "il2cpp_data")));

                string core = Path.Combine(info.GameDir, "BepInEx", "core");
                bool hasCoreDll = File.Exists(Path.Combine(core, "BepInEx.dll"));
                info.HasBepInEx5 = hasCoreDll &&
                    (File.Exists(Path.Combine(info.GameDir, "winhttp.dll")) ||
                     File.Exists(Path.Combine(info.GameDir, "version.dll")) ||
                     File.Exists(Path.Combine(info.GameDir, "doorstop_config.ini")));
                info.HasBepInExOther = !hasCoreDll && Directory.Exists(core);
                info.HasLoaderConflict = !info.HasBepInEx5 && !info.HasBepInExOther &&
                    (File.Exists(Path.Combine(info.GameDir, "winhttp.dll")) ||
                     File.Exists(Path.Combine(info.GameDir, "version.dll")));
                if (info.HasBepInEx5)
                {
                    string loader = Path.Combine(info.GameDir, "winhttp.dll");
                    if (!File.Exists(loader)) loader = Path.Combine(info.GameDir, "version.dll");
                    if (File.Exists(loader)) info.BepInExArchitecture = GetPeArchitecture(loader);
                }

                info.HasTmp = info.DataDir != null &&
                    File.Exists(Path.Combine(info.DataDir, "Managed", "Unity.TextMeshPro.dll"));
                info.TranslatorInstalled = File.Exists(Path.Combine(info.GameDir, "BepInEx", "plugins",
                    "UnityTranslator", "UnityTranslator.Plugin.dll"));

                try
                {
                    Process[] processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(info.ExePath));
                    info.IsRunning = processes != null && processes.Length > 0;
                }
                catch { }
            }
            catch { }

            return info;
        }

        private static string FindUnityExe(string directory, int depth)
        {
            string direct = SearchUnityExe(directory, depth);
            return direct;
        }

        private static string SearchUnityExe(string directory, int depth)
        {
            string[] files;
            try { files = Directory.GetFiles(directory, "*.exe"); }
            catch { return null; }

            string candidate = null;
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                string stem = Path.GetFileNameWithoutExtension(file);
                string lower = stem.ToLowerInvariant();
                if (lower.IndexOf("unins") >= 0 || lower.IndexOf("crash") >= 0 ||
                    lower.IndexOf("setup") >= 0 || lower.IndexOf("vcredist") >= 0 ||
                    lower.IndexOf("dxsetup") >= 0 || lower.IndexOf("install") >= 0)
                    continue;

                if (Directory.Exists(Path.Combine(directory, stem + "_Data")))
                    return file;
                if (candidate == null && File.Exists(Path.Combine(directory, "UnityPlayer.dll")))
                    candidate = file;
            }

            if (candidate != null) return candidate;
            if (depth <= 0) return null;

            string[] subdirs;
            try { subdirs = Directory.GetDirectories(directory); }
            catch { return null; }

            for (int i = 0; i < subdirs.Length; i++)
            {
                string name = Path.GetFileName(subdirs[i]);
                if (name.EndsWith("_Data", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("BepInEx", StringComparison.OrdinalIgnoreCase)) continue;
                string found = SearchUnityExe(subdirs[i], depth - 1);
                if (found != null) return found;
            }

            return null;
        }

        private static string GetPeArchitecture(string exePath)
        {
            try
            {
                using (FileStream stream = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    if (stream.Length < 0x40) return "unknown";
                    stream.Seek(0x3C, SeekOrigin.Begin);
                    int peOffset = reader.ReadInt32();
                    if (peOffset <= 0 || peOffset + 6 > stream.Length) return "unknown";
                    stream.Seek(peOffset, SeekOrigin.Begin);
                    if (reader.ReadUInt32() != 0x00004550) return "unknown";
                    ushort machine = reader.ReadUInt16();
                    if (machine == 0x8664) return "x64";
                    if (machine == 0x014C) return "x86";
                    if (machine == 0xAA64) return "arm64";
                    return "unknown";
                }
            }
            catch { return "unknown"; }
        }

        private static string ReadUnityVersion(string dataDir)
        {
            if (string.IsNullOrEmpty(dataDir)) return null;
            string file = Path.Combine(dataDir, "globalgamemanagers");
            try
            {
                if (!File.Exists(file)) return null;
                byte[] buffer = new byte[2048];
                int read;
                using (FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    read = stream.Read(buffer, 0, buffer.Length);
                }
                string text = Encoding.ASCII.GetString(buffer, 0, read);
                Match match = Regex.Match(text, @"(\d+\.\d+\.\d+[abfp]\d+)");
                return match.Success ? match.Groups[1].Value : null;
            }
            catch { return null; }
        }
    }

    internal static class Installer
    {
        public static void Install(GameInfo info, bool allowConflict, Action<string> log)
        {
            if (info == null || !info.IsUnity)
                throw new InvalidOperationException("Папка Unity-игры не найдена.");
            if (info.IsIL2CPP)
                throw new InvalidOperationException("Игра использует IL2CPP. Поддерживаются только игры на Mono.");
            if (info.Architecture != "x64" && info.Architecture != "x86")
                throw new InvalidOperationException("Архитектура игры не поддерживается: " + info.Architecture);
            if (info.HasBepInExOther)
                throw new InvalidOperationException("Обнаружена несовместимая версия BepInEx. Удалите папку BepInEx и повторите установку.");
            if (info.HasBepInEx5 &&
                !string.IsNullOrEmpty(info.BepInExArchitecture) &&
                info.BepInExArchitecture != "unknown" &&
                info.BepInExArchitecture != info.Architecture)
                throw new InvalidOperationException("BepInEx установлен для " + info.BepInExArchitecture +
                    ", а игра — " + info.Architecture + ". Удалите папку BepInEx и повторите установку.");
            if (info.HasLoaderConflict && !allowConflict)
                throw new InvalidOperationException("Найден другой загрузчик модов (winhttp.dll/version.dll).");
            if (info.IsRunning)
                throw new InvalidOperationException("Игра запущена. Закройте её перед установкой.");

            if (!info.HasBepInEx5)
            {
                log("Установка BepInEx 5.4.23.6 (" + info.Architecture + ")...");
                string resource = info.Architecture == "x64" ? "BepInEx_x64.zip" : "BepInEx_x86.zip";
                string zip = EmbeddedResources.Extract(resource, resource);
                ExtractZip(zip, info.GameDir);
                log("BepInEx установлен. Файлы ядра: BepInEx\\core.");
            }
            else
            {
                log("BepInEx 5 уже установлен — файлы ядра не изменяются.");
            }

            string pluginSource = EmbeddedResources.Extract("UnityTranslator.Plugin.dll", "UnityTranslator.Plugin.dll");
            string pluginDir = Path.Combine(info.GameDir, "BepInEx", "plugins", "UnityTranslator");
            Directory.CreateDirectory(pluginDir);
            string pluginTarget = Path.Combine(pluginDir, "UnityTranslator.Plugin.dll");
            File.Copy(pluginSource, pluginTarget, true);
            File.WriteAllText(Path.Combine(pluginDir, "README.txt"), PluginReadme, new UTF8Encoding(false));
            log("Переводчик установлен: " + pluginTarget);

            log("Целевой язык определяется автоматически из языка Windows.");
            log("Настройки: BepInEx\\config\\ru.unitytranslator.cfg");
        }

        public static void Uninstall(GameInfo info, Action<string> log)
        {
            if (info == null || string.IsNullOrEmpty(info.GameDir))
                throw new InvalidOperationException("Папка игры не найдена.");
            if (info.IsRunning)
                throw new InvalidOperationException("Игра запущена. Закройте её перед удалением.");

            string pluginDir = Path.Combine(info.GameDir, "BepInEx", "plugins", "UnityTranslator");
            if (Directory.Exists(pluginDir))
            {
                Directory.Delete(pluginDir, true);
                log("Удалена папка переводчика: " + pluginDir);
            }
            else
            {
                log("Папка переводчика не найдена — возможно, он уже удалён.");
            }

            string configDir = Path.Combine(info.GameDir, "BepInEx", "config", "UnityTranslator");
            if (Directory.Exists(configDir))
            {
                Directory.Delete(configDir, true);
                log("Удалён кэш переводов.");
            }

            string configFile = Path.Combine(info.GameDir, "BepInEx", "config", "ru.unitytranslator.cfg");
            if (File.Exists(configFile))
            {
                File.Delete(configFile);
                log("Удалён файл настроек переводчика.");
            }

            log("BepInEx и другие моды не затрагивались.");
        }

        private static void ExtractZip(string zipPath, string destination)
        {
            string root = Path.GetFullPath(destination);
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                root += Path.DirectorySeparatorChar;

            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                for (int i = 0; i < archive.Entries.Count; i++)
                {
                    ZipArchiveEntry entry = archive.Entries[i];
                    string target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Некорректный путь в архиве: " + entry.FullName);

                    if (entry.Name.Length == 0)
                    {
                        Directory.CreateDirectory(target);
                    }
                    else
                    {
                        string parent = Path.GetDirectoryName(target);
                        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                        entry.ExtractToFile(target, true);
                    }
                }
            }
        }

        private const string PluginReadme =
            "UNIVERSAL UNITY TRANSLATOR\r\n" +
            "==========================\r\n\r\n" +
            "Перевод включается автоматически при запуске игры.\r\n" +
            "Целевой язык = язык интерфейса Windows.\r\n\r\n" +
            "Настройки:\r\n" +
            "  BepInEx\\config\\ru.unitytranslator.cfg\r\n\r\n" +
            "Кэш переводов:\r\n" +
            "  BepInEx\\config\\UnityTranslator\\cache_<язык>.tsv\r\n\r\n" +
            "Если текст не переводится, включите LogTranslations в конфиге\r\n" +
            "и проверьте BepInEx\\LogOutput.log.\r\n\r\n" +
            "Поддерживаются: TextMeshPro, uGUI Text, TextMesh, игры на Mono.\r\n" +
            "Не поддерживаются: IL2CPP, UI Toolkit, NGUI, FairyGUI, текст на текстурах.\r\n";
    }

    internal static class EmbeddedResources
    {
        private static string TempDir
        {
            get { return Path.Combine(Path.GetTempPath(), "UnityTranslatorSetup"); }
        }

        public static string Extract(string resourceName, string fileName)
        {
            string dir = TempDir;
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, fileName);
            using (Stream stream = Open(resourceName))
            using (FileStream file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.CopyTo(file);
            }
            return target;
        }

        private static Stream Open(string name)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            Stream stream = assembly.GetManifestResourceStream(name);
            if (stream != null) return stream;

            string[] names = assembly.GetManifestResourceNames();
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] == name ||
                    names[i].EndsWith("." + name, StringComparison.OrdinalIgnoreCase))
                {
                    stream = assembly.GetManifestResourceStream(names[i]);
                    if (stream != null) return stream;
                }
            }

            throw new InvalidOperationException("Встроенный ресурс не найден: " + name);
        }

        public static void Cleanup()
        {
            try
            {
                if (Directory.Exists(TempDir)) Directory.Delete(TempDir, true);
            }
            catch { }
        }
    }

    internal static class Silent
    {
        public static int Run(string[] args)
        {
            string command = args[0].ToLowerInvariant();
            string path = args.Length > 1 ? args[1] : null;
            string logPath = Path.Combine(Path.GetTempPath(), "UnityTranslatorSetup.log");
            StringBuilder log = new StringBuilder();
            int exitCode = 1;

            try
            {
                GameInfo info = GameInspector.Inspect(path);
                log.AppendLine("Command: " + command);
                log.AppendLine("Input: " + (path ?? "(none)"));
                log.AppendLine("IsUnity: " + info.IsUnity);
                log.AppendLine("UnityVersion: " + info.UnityVersion);
                log.AppendLine("Architecture: " + info.Architecture);
                log.AppendLine("IL2CPP: " + info.IsIL2CPP);
                log.AppendLine("BepInEx5: " + info.HasBepInEx5);
                log.AppendLine("BepInExOther: " + info.HasBepInExOther);
                log.AppendLine("LoaderConflict: " + info.HasLoaderConflict);
                log.AppendLine("TMP: " + info.HasTmp);
                log.AppendLine("TranslatorInstalled: " + info.TranslatorInstalled);
                log.AppendLine("Running: " + info.IsRunning);
                log.AppendLine("GameDir: " + info.GameDir);
                log.AppendLine("ExePath: " + info.ExePath);
                log.AppendLine();

                if (command == "/check")
                {
                    exitCode = info.IsUnity ? 0 : 2;
                }
                else if (command == "/install")
                {
                    if (!info.IsUnity) throw new InvalidOperationException("Unity game not found.");
                    Installer.Install(info, true, delegate(string message) { log.AppendLine(message); });
                    GameInfo after = GameInspector.Inspect(path);
                    log.AppendLine("Plugin after install: " + after.TranslatorInstalled);
                    log.AppendLine("BepInEx after install: " + after.HasBepInEx5);
                    exitCode = after.TranslatorInstalled ? 0 : 3;
                }
                else if (command == "/uninstall")
                {
                    Installer.Uninstall(info, delegate(string message) { log.AppendLine(message); });
                    GameInfo after = GameInspector.Inspect(path);
                    log.AppendLine("Plugin after uninstall: " + after.TranslatorInstalled);
                    exitCode = after.TranslatorInstalled ? 3 : 0;
                }
                else
                {
                    log.AppendLine("Unknown command: " + command);
                    exitCode = 64;
                }
            }
            catch (Exception ex)
            {
                log.AppendLine("ERROR: " + ex);
                exitCode = 1;
            }

            try
            {
                File.WriteAllText(logPath, log.ToString(), new UTF8Encoding(false));
            }
            catch { }

            EmbeddedResources.Cleanup();
            return exitCode;
        }
    }
}
