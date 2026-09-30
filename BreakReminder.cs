using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class BreakReminder : Form
{
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    static readonly string[] Tips =
    {
        "Посмотри в окно или на что-нибудь дальше 6 метров",
        "Закрой глаза и расслабь плечи",
        "Встань и потянись вверх",
        "Покрути головой медленно влево и вправо",
        "Выпей воды",
        "Поморгай быстро 10 раз, потом закрой глаза на пару секунд",
        "Пройдись по комнате",
        "Сведи и разведи лопатки 10 раз",
    };

    NumericUpDown shortEvery, shortLength, longEvery, longLength;
    CheckBox idleBox;
    Label nextLabel, statsLabel;
    Button pauseButton, nowButton;
    NotifyIcon tray;
    Timer timer;
    DateTime nextShort, nextLong, pausedUntil = DateTime.MinValue;
    int breaksTaken, breaksSkipped, tipIndex;
    readonly Random random = new Random();

    BreakReminder()
    {
        Text = "BreakReminder";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(360, 330);
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterScreen;

        var shortGroup = new GroupBox { Text = "Короткий перерыв для глаз", Location = new Point(12, 8), Size = new Size(336, 64) };
        Controls.Add(shortGroup);
        shortGroup.Controls.Add(new Label { Text = "Каждые", Location = new Point(10, 29), AutoSize = true });
        shortEvery = Num(shortGroup, 70, 26, 5, 120, 20);
        shortGroup.Controls.Add(new Label { Text = "мин, на", Location = new Point(136, 29), AutoSize = true });
        shortLength = Num(shortGroup, 196, 26, 10, 300, 20);
        shortGroup.Controls.Add(new Label { Text = "сек", Location = new Point(262, 29), AutoSize = true });

        var longGroup = new GroupBox { Text = "Длинный перерыв: встать и размяться", Location = new Point(12, 80), Size = new Size(336, 64) };
        Controls.Add(longGroup);
        longGroup.Controls.Add(new Label { Text = "Каждые", Location = new Point(10, 29), AutoSize = true });
        longEvery = Num(longGroup, 70, 26, 20, 240, 60);
        longGroup.Controls.Add(new Label { Text = "мин, на", Location = new Point(136, 29), AutoSize = true });
        longLength = Num(longGroup, 196, 26, 1, 30, 5);
        longGroup.Controls.Add(new Label { Text = "мин", Location = new Point(262, 29), AutoSize = true });

        idleBox = new CheckBox { Text = "Отошёл от компьютера — считать это перерывом", Checked = true, Location = new Point(14, 154), AutoSize = true };
        Controls.Add(idleBox);

        nextLabel = new Label { Location = new Point(12, 186), Size = new Size(336, 50), Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = Color.FromArgb(40, 110, 90) };
        Controls.Add(nextLabel);
        statsLabel = new Label { Location = new Point(12, 238), Size = new Size(336, 22), ForeColor = Color.Gray };
        Controls.Add(statsLabel);

        pauseButton = new Button { Text = "Пауза на час", Location = new Point(12, 272), Size = new Size(164, 40) };
        pauseButton.Click += (s, e) => TogglePause();
        Controls.Add(pauseButton);
        nowButton = new Button { Text = "Перерыв сейчас", Location = new Point(184, 272), Size = new Size(164, 40) };
        nowButton.Click += (s, e) => ShowBreak(false);
        Controls.Add(nowButton);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Показать окно", null, (s, e) => ShowWindow());
        menu.Items.Add("Перерыв сейчас", null, (s, e) => ShowBreak(false));
        menu.Items.Add("Пауза на час / продолжить", null, (s, e) => TogglePause());
        menu.Items.Add("Выход", null, (s, e) => Close());
        tray = new NotifyIcon { Icon = SystemIcons.Information, Visible = true, ContextMenuStrip = menu, Text = "BreakReminder" };
        tray.DoubleClick += (s, e) => ShowWindow();

        EventHandler reset = (s, e) => ResetTimers();
        shortEvery.ValueChanged += reset;
        longEvery.ValueChanged += reset;
        ResetTimers();

        timer = new Timer { Interval = 1000 };
        timer.Tick += (s, e) => Tick();
        timer.Start();
        Resize += (s, e) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        Tick();
    }

    static NumericUpDown Num(Control parent, int x, int y, int min, int max, int value)
    {
        var n = new NumericUpDown { Location = new Point(x, y), Width = 62, Minimum = min, Maximum = max, Value = value };
        parent.Controls.Add(n);
        return n;
    }

    void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    void ResetTimers()
    {
        nextShort = DateTime.Now.AddMinutes((double)shortEvery.Value);
        nextLong = DateTime.Now.AddMinutes((double)longEvery.Value);
    }

    void TogglePause()
    {
        if (pausedUntil > DateTime.Now) { pausedUntil = DateTime.MinValue; ResetTimers(); }
        else pausedUntil = DateTime.Now.AddHours(1);
        Tick();
    }

    static TimeSpan IdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
        GetLastInputInfo(ref info);
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
    }

    void Tick()
    {
        var now = DateTime.Now;
        if (pausedUntil > now)
        {
            pauseButton.Text = "Продолжить";
            nextLabel.Text = "Пауза до " + pausedUntil.ToString("HH:mm");
            nextLabel.ForeColor = Color.Gray;
            tray.Text = "BreakReminder: пауза";
            UpdateStats();
            return;
        }
        pauseButton.Text = "Пауза на час";

        // Being away counts as a break: restart the countdowns.
        var idle = IdleTime();
        if (idleBox.Checked && idle.TotalMinutes >= (double)longLength.Value) ResetTimers();
        else if (idleBox.Checked && idle.TotalSeconds >= Math.Max(120, (double)shortLength.Value)) nextShort = now.AddMinutes((double)shortEvery.Value);

        if (now >= nextLong) { ShowBreak(true); return; }
        if (now >= nextShort) { ShowBreak(false); return; }

        var toShort = nextShort - now;
        var toLong = nextLong - now;
        nextLabel.ForeColor = Color.FromArgb(40, 110, 90);
        nextLabel.Text = "Перерыв для глаз через " + Format(toShort) + "\nРазминка через " + Format(toLong);
        tray.Text = "BreakReminder: глаза через " + Format(toShort);
        UpdateStats();
    }

    void UpdateStats()
    {
        statsLabel.Text = "Сегодня перерывов: " + breaksTaken + (breaksSkipped > 0 ? ", пропущено: " + breaksSkipped : "");
    }

    static string Format(TimeSpan t)
    {
        if (t.TotalMinutes >= 1) return (int)Math.Ceiling(t.TotalMinutes) + " мин";
        return Math.Max(0, (int)t.TotalSeconds) + " сек";
    }

    void ShowBreak(bool isLong)
    {
        timer.Stop();
        int seconds = isLong ? (int)longLength.Value * 60 : (int)shortLength.Value;
        string tip = isLong ? Tips[2 + random.Next(Tips.Length - 2)] : Tips[tipIndex++ % 2];
        var form = new BreakForm(isLong ? "Время размяться" : "Отдых для глаз", tip, seconds);
        form.FormClosed += (s, e) =>
        {
            if (form.Completed) breaksTaken++; else breaksSkipped++;
            nextShort = DateTime.Now.AddMinutes((double)shortEvery.Value);
            if (isLong) nextLong = DateTime.Now.AddMinutes((double)longEvery.Value);
            else if (nextLong - DateTime.Now < TimeSpan.FromMinutes(2)) nextLong = DateTime.Now.AddMinutes(2);
            timer.Start();
            Tick();
        };
        form.Show();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        tray.Visible = false;
        tray.Dispose();
        base.OnFormClosing(e);
    }

    class BreakForm : Form
    {
        public bool Completed;
        readonly Label timeLabel;
        readonly Timer countdown;
        readonly DateTime end;

        public BreakForm(string title, string tip, int seconds)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen.Bounds;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(24, 38, 44);
            Opacity = 0.97;
            end = DateTime.Now.AddSeconds(seconds);

            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            for (int i = 0; i < 3; i++) panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            Controls.Add(panel);

            panel.Controls.Add(new Label(), 0, 0);
            panel.Controls.Add(Centered(title, 36, FontStyle.Bold, Color.White), 0, 1);
            panel.Controls.Add(Centered(tip, 18, FontStyle.Regular, Color.FromArgb(180, 210, 205)), 0, 2);
            timeLabel = Centered("", 64, FontStyle.Bold, Color.FromArgb(120, 220, 180));
            var bottom = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.None, FlowDirection = FlowDirection.TopDown };
            bottom.Controls.Add(timeLabel);
            var skip = new Button { Text = "Пропустить (Esc)", AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Color.Gray, Anchor = AnchorStyles.None, Font = new Font("Segoe UI", 11), Margin = new Padding(0, 20, 0, 0) };
            skip.FlatAppearance.BorderColor = Color.FromArgb(70, 90, 95);
            skip.Click += (s, e) => Close();
            bottom.Controls.Add(skip);
            panel.Controls.Add(bottom, 0, 3);

            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            countdown = new Timer { Interval = 250 };
            countdown.Tick += (s, e) => Update();
            countdown.Start();
            Update();
        }

        static Label Centered(string text, float size, FontStyle style, Color color)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.None, Font = new Font("Segoe UI", size, style), ForeColor = color, Margin = new Padding(0, 8, 0, 8) };
        }

        new void Update()
        {
            var left = end - DateTime.Now;
            if (left <= TimeSpan.Zero) { Completed = true; Close(); return; }
            timeLabel.Text = left.TotalSeconds >= 60 ? left.ToString(@"m\:ss") : ((int)Math.Ceiling(left.TotalSeconds)).ToString();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            countdown.Stop();
            base.OnFormClosed(e);
        }
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.Run(new BreakReminder());
    }
}
