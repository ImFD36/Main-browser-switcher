using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using Microsoft.Win32;

public class Program {
    [STAThread]
    public static void Main() {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

public class BrowserInfo {
    public string DisplayName { get; set; }
    public string ExePath { get; set; }
    public string RegisteredAppName { get; set; }
    public Image BrowserBitmap { get; set; }
    public bool IsCurrentDefault { get; set; }
}

public class AnimatedBrowserListView : Control {
    public class CardState {
        public BrowserInfo Browser;
        public float Progress;
        public float TargetY;
        public Rectangle Bounds;
        public Rectangle ButtonBounds;
        public bool ButtonHovered;
        public bool CardHovered;
    }

    private List<CardState> cards = new List<CardState>();
    public Action<BrowserInfo> OnSwapClicked;

    private Stopwatch animStopwatch = new Stopwatch();
    private System.Windows.Forms.Timer frameTimer;
    private const float CARD_DURATION_SEC = 0.55f;
    private const float STAGGER_INTERVAL_SEC = 0.08f;
    private bool isAnimating = false;

    // Smooth Scroll System
    private float scrollOffset = 0f;
    private float targetScroll = 0f;
    private float maxScroll = 0f;
    private bool isDraggingScrollbar = false;
    private float dragStartY = 0f;
    private float dragStartScroll = 0f;

    public AnimatedBrowserListView() {
        this.SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw, true);

        this.BackColor = Color.FromArgb(16, 18, 22);

        frameTimer = new System.Windows.Forms.Timer();
        frameTimer.Interval = 14;
        frameTimer.Tick += OnFrameTick;
    }

    public void SetBrowsers(List<BrowserInfo> browserList, bool animated) {
        cards.Clear();
        int width = this.Width - 30;
        if (width < 300) width = 300;
        int y = 14;

        for (int i = 0; i < browserList.Count; i++) {
            CardState state = new CardState {
                Browser = browserList[i],
                Progress = animated ? 0f : 1f,
                TargetY = y,
                Bounds = new Rectangle(15, y, width, 92),
                ButtonBounds = new Rectangle(15 + width - 128, y + 28, 114, 34)
            };
            cards.Add(state);
            y += 92 + 12;
        }

        UpdateScrollLimits();

        if (animated) {
            isAnimating = true;
            animStopwatch.Reset();
            animStopwatch.Start();
            frameTimer.Start();
        } else {
            isAnimating = false;
            Invalidate();
        }
    }

    private void UpdateScrollLimits() {
        int totalContentHeight = 14 + (cards.Count * (92 + 12)) + 14;
        maxScroll = Math.Max(0, totalContentHeight - this.Height);
        if (targetScroll > maxScroll) targetScroll = maxScroll;
        if (targetScroll < 0) targetScroll = 0;
    }

    protected override void OnMouseWheel(MouseEventArgs e) {
        base.OnMouseWheel(e);
        if (maxScroll <= 0) return;

        targetScroll -= (e.Delta / 120f) * 70f;
        if (targetScroll < 0) targetScroll = 0;
        if (targetScroll > maxScroll) targetScroll = maxScroll;

        if (!frameTimer.Enabled) frameTimer.Start();
    }

    private void OnFrameTick(object sender, EventArgs e) {
        bool needInvalidate = false;

        if (Math.Abs(targetScroll - scrollOffset) > 0.5f) {
            scrollOffset += (targetScroll - scrollOffset) * 0.28f;
            needInvalidate = true;
        } else {
            scrollOffset = targetScroll;
        }

        if (isAnimating) {
            float elapsed = (float)animStopwatch.Elapsed.TotalSeconds;
            bool allFinished = true;

            for (int i = 0; i < cards.Count; i++) {
                var card = cards[i];
                float cardStart = i * STAGGER_INTERVAL_SEC;

                if (elapsed < cardStart) {
                    card.Progress = 0f;
                    allFinished = false;
                } else {
                    float localTime = (elapsed - cardStart) / CARD_DURATION_SEC;
                    if (localTime >= 1.0f) {
                        card.Progress = 1.0f;
                    } else {
                        allFinished = false;
                        float t = 1.0f - (float)Math.Pow(1.0 - localTime, 5);
                        card.Progress = t;
                    }
                }
            }

            needInvalidate = true;

            if (allFinished) {
                isAnimating = false;
                animStopwatch.Stop();
            }
        }

        if (needInvalidate) {
            Invalidate();
        } else if (!isAnimating && Math.Abs(targetScroll - scrollOffset) <= 0.5f) {
            frameTimer.Stop();
        }
    }

    protected override void OnResize(EventArgs e) {
        base.OnResize(e);
        int width = this.Width - (maxScroll > 0 ? 36 : 30);
        if (width < 280) width = 280;

        for (int i = 0; i < cards.Count; i++) {
            cards[i].Bounds = new Rectangle(15, (int)cards[i].TargetY, width, 92);
            cards[i].ButtonBounds = new Rectangle(15 + width - 128, (int)cards[i].TargetY + 28, 114, 34);
        }
        UpdateScrollLimits();
        Invalidate();
    }

    private Rectangle GetScrollThumbRect() {
        if (maxScroll <= 0) return Rectangle.Empty;

        int trackHeight = this.Height - 20;
        int totalContent = 14 + (cards.Count * 104) + 14;
        int thumbHeight = Math.Max(35, (int)((float)this.Height / totalContent * trackHeight));

        float ratio = (maxScroll > 0) ? (scrollOffset / maxScroll) : 0f;
        int thumbY = 10 + (int)(ratio * (trackHeight - thumbHeight));

        return new Rectangle(this.Width - 10, thumbY, 6, thumbHeight);
    }

    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && maxScroll > 0) {
            Rectangle thumb = GetScrollThumbRect();
            if (thumb.Contains(e.Location)) {
                isDraggingScrollbar = true;
                dragStartY = e.Y;
                dragStartScroll = targetScroll;
                return;
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e) {
        base.OnMouseUp(e);
        isDraggingScrollbar = false;
    }

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);

        if (isDraggingScrollbar && maxScroll > 0) {
            int trackHeight = this.Height - 20;
            int totalContent = 14 + (cards.Count * 104) + 14;
            int thumbHeight = Math.Max(35, (int)((float)this.Height / totalContent * trackHeight));
            int availableTrack = trackHeight - thumbHeight;

            if (availableTrack > 0) {
                float deltaY = e.Y - dragStartY;
                float scrollDelta = (deltaY / availableTrack) * maxScroll;
                targetScroll = dragStartScroll + scrollDelta;
                if (targetScroll < 0) targetScroll = 0;
                if (targetScroll > maxScroll) targetScroll = maxScroll;
                scrollOffset = targetScroll;
                Invalidate();
            }
            return;
        }

        bool needRepaint = false;
        Cursor newCursor = Cursors.Default;
        Point virtualMouse = new Point(e.X, (int)(e.Y + scrollOffset));

        for (int i = 0; i < cards.Count; i++) {
            var c = cards[i];
            bool overCard = c.Bounds.Contains(virtualMouse);
            bool overBtn = c.ButtonBounds.Contains(virtualMouse) && !c.Browser.IsCurrentDefault;

            if (c.CardHovered != overCard || c.ButtonHovered != overBtn) {
                c.CardHovered = overCard;
                c.ButtonHovered = overBtn;
                needRepaint = true;
            }

            if (overBtn) newCursor = Cursors.Hand;
        }

        if (this.Cursor != newCursor) this.Cursor = newCursor;
        if (needRepaint) Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) {
        base.OnMouseLeave(e);
        for (int i = 0; i < cards.Count; i++) {
            cards[i].CardHovered = false;
            cards[i].ButtonHovered = false;
        }
        this.Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e) {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left || isDraggingScrollbar) return;

        Point virtualMouse = new Point(e.X, (int)(e.Y + scrollOffset));
        for (int i = 0; i < cards.Count; i++) {
            var c = cards[i];
            if (c.ButtonBounds.Contains(virtualMouse)) {
                if (!c.Browser.IsCurrentDefault && OnSwapClicked != null) {
                    OnSwapClicked(c.Browser);
                }
                break;
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (Font fontTitle = new Font("Segoe UI Semibold", 11.5f, FontStyle.Bold))
        using (Font fontSub = new Font("Segoe UI", 8.2f, FontStyle.Regular))
        using (Font fontBadge = new Font("Segoe UI Semibold", 8.2f, FontStyle.Bold))
        using (Font fontBtn = new Font("Segoe UI Semibold", 9.2f, FontStyle.Bold))
        using (StringFormat sfBtn = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) {

            for (int i = 0; i < cards.Count; i++) {
                var c = cards[i];
                if (c.Progress <= 0.001f && isAnimating) continue;

                float p = c.Progress;

                int slideOffsetY = (int)((1.0f - p) * 36.0f);
                int slideOffsetX = (int)((1.0f - p) * 20.0f);

                int cardX = c.Bounds.X + slideOffsetX;
                int cardY = (int)(c.Bounds.Y - scrollOffset + slideOffsetY);
                int cardW = c.Bounds.Width - slideOffsetX;
                int cardH = c.Bounds.Height;

                if (cardY + cardH < -20 || cardY > this.Height + 20) continue;

                Rectangle drawBounds = new Rectangle(cardX, cardY, cardW, cardH);
                Rectangle drawBtn = new Rectangle(cardX + cardW - 128, cardY + 28, 114, 34);

                int alpha = (int)(p * 255.0f);
                if (alpha > 255) alpha = 255;
                if (alpha < 0) alpha = 0;

                Color bgColor = c.Browser.IsCurrentDefault 
                    ? Color.FromArgb(alpha, 24, 42, 60) 
                    : (c.CardHovered ? Color.FromArgb(alpha, 34, 38, 48) : Color.FromArgb(alpha, 26, 28, 36));

                using (SolidBrush bgBrush = new SolidBrush(bgColor)) {
                    FillRoundedRectangle(g, bgBrush, drawBounds, 6);
                }

                Color borderColor = c.Browser.IsCurrentDefault 
                    ? Color.FromArgb(alpha, 0, 160, 255) 
                    : (c.CardHovered ? Color.FromArgb(alpha, 75, 85, 105) : Color.FromArgb(alpha, 42, 46, 58));

                using (Pen borderPen = new Pen(borderColor, c.Browser.IsCurrentDefault ? 2f : 1f)) {
                    DrawRoundedRectangle(g, borderPen, drawBounds, 6);
                }

                if (c.Browser.BrowserBitmap != null) {
                    g.DrawImage(c.Browser.BrowserBitmap, new Rectangle(cardX + 16, cardY + 23, 46, 46));
                }

                using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255))) {
                    g.DrawString(c.Browser.DisplayName, fontTitle, textBrush, cardX + 74, cardY + 13);
                }

                using (SolidBrush subBrush = new SolidBrush(Color.FromArgb(alpha, 140, 145, 160))) {
                    Rectangle textRect = new Rectangle(cardX + 76, cardY + 38, cardW - 215, 18);
                    TextRenderer.DrawText(g, c.Browser.ExePath, fontSub, textRect, Color.FromArgb(alpha, 140, 145, 160), 
                        TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
                }

                if (c.Browser.IsCurrentDefault) {
                    using (SolidBrush badgeBrush = new SolidBrush(Color.FromArgb(alpha, 70, 220, 135))) {
                        g.DrawString("[*] CURRENT MAIN BROWSER", fontBadge, badgeBrush, cardX + 76, cardY + 61);
                    }
                } else {
                    using (SolidBrush badgeBrush = new SolidBrush(Color.FromArgb(alpha, 115, 120, 135))) {
                        g.DrawString("Ready to set as default", fontBadge, badgeBrush, cardX + 76, cardY + 61);
                    }
                }

                if (c.Browser.IsCurrentDefault) {
                    using (SolidBrush btnBrush = new SolidBrush(Color.FromArgb(alpha, 18, 68, 40))) {
                        FillRoundedRectangle(g, btnBrush, drawBtn, 5);
                    }
                    using (SolidBrush btnText = new SolidBrush(Color.FromArgb(alpha, 120, 235, 150))) {
                        g.DrawString("Current [OK]", fontBtn, btnText, drawBtn, sfBtn);
                    }
                } else {
                    Color btnBg = c.ButtonHovered ? Color.FromArgb(alpha, 28, 140, 255) : Color.FromArgb(alpha, 0, 122, 237);
                    using (SolidBrush btnBrush = new SolidBrush(btnBg)) {
                        FillRoundedRectangle(g, btnBrush, drawBtn, 5);
                    }
                    using (SolidBrush btnText = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255))) {
                        g.DrawString("Swap to This", fontBtn, btnText, drawBtn, sfBtn);
                    }
                }
            }

            if (maxScroll > 0) {
                Rectangle thumb = GetScrollThumbRect();
                Color thumbColor = isDraggingScrollbar ? Color.FromArgb(140, 150, 175) : Color.FromArgb(60, 66, 82);
                using (SolidBrush thumbBrush = new SolidBrush(thumbColor)) {
                    FillRoundedRectangle(g, thumbBrush, thumb, 3);
                }
            }
        }
    }

    private void FillRoundedRectangle(Graphics g, Brush brush, Rectangle r, int radius) {
        using (GraphicsPath path = MakeRoundedPath(r, radius)) {
            g.FillPath(brush, path);
        }
    }

    private void DrawRoundedRectangle(Graphics g, Pen pen, Rectangle r, int radius) {
        using (GraphicsPath path = MakeRoundedPath(r, radius)) {
            g.DrawPath(pen, path);
        }
    }

    private GraphicsPath MakeRoundedPath(Rectangle r, int radius) {
        GraphicsPath path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

public class MainForm : Form {
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private AnimatedBrowserListView listView;
    private Label statusLabel;
    private Button btnRefresh;
    private Button btnSettings;
    private CheckBox chkBlockTaskbarPrompt;
    private List<BrowserInfo> browsers = new List<BrowserInfo>();

    private System.Windows.Forms.Timer windowFadeTimer;
    private System.Windows.Forms.Timer autoRefreshTimer;

    public MainForm() {
        this.Text = "Main Browser Swapper";
        this.Size = new Size(560, 680);
        this.MinimumSize = new Size(500, 540);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.FromArgb(16, 18, 22);
        this.ForeColor = Color.White;
        this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        this.Opacity = 0.0;

        try {
            string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (!File.Exists(icoPath)) {
                icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"assets\app.ico");
            }
            if (File.Exists(icoPath)) {
                this.Icon = new Icon(icoPath);
            }
        } catch {}

        InitializeComponents();
        LoadUserPreferences();
        ApplyTaskbarSuppression();

        this.HandleCreated += delegate {
            EnableDarkTitleBar();
        };

        this.Load += delegate {
            EnableDarkTitleBar();
            StartSmoothEntrance();
        };

        this.Activated += delegate {
            RefreshBrowserData(false);
        };

        autoRefreshTimer = new System.Windows.Forms.Timer();
        autoRefreshTimer.Interval = 3000;
        autoRefreshTimer.Tick += delegate {
            if (this.ContainsFocus) {
                RefreshBrowserData(false);
            }
        };
        autoRefreshTimer.Start();
    }

    private void EnableDarkTitleBar() {
        try {
            int trueValue = 1;
            int res = DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref trueValue, sizeof(int));
            if (res != 0) {
                DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref trueValue, sizeof(int));
            }
        } catch {}
    }

    private void LoadUserPreferences() {
        try {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\MainBrowserSwitcher")) {
                if (key != null) {
                    object val = key.GetValue("AutoDisableTaskbar");
                    if (val != null) {
                        chkBlockTaskbarPrompt.Checked = Convert.ToInt32(val) != 0;
                        return;
                    }
                }
            }
        } catch {}
        // Default to TRUE so user never has to turn it on manually
        chkBlockTaskbarPrompt.Checked = true;
    }

    private void SaveUserPreferences() {
        try {
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\MainBrowserSwitcher")) {
                if (key != null) {
                    key.SetValue("AutoDisableTaskbar", chkBlockTaskbarPrompt.Checked ? 1 : 0, RegistryValueKind.DWord);
                }
            }
        } catch {}
    }

    private void InitializeComponents() {
        Panel headerPanel = new Panel();
        headerPanel.Dock = DockStyle.Top;
        headerPanel.Height = 85;
        headerPanel.BackColor = Color.FromArgb(12, 14, 18);
        headerPanel.Padding = new Padding(20, 15, 20, 10);

        Label title = new Label();
        title.Text = "Main Browser Swapper";
        title.Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold);
        title.ForeColor = Color.White;
        title.AutoSize = true;
        title.Location = new Point(18, 14);

        Label subtitle = new Label();
        subtitle.Text = "Auto-detects installed browsers. Click 'Swap to This' to change your main browser.";
        subtitle.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
        subtitle.ForeColor = Color.FromArgb(160, 165, 180);
        subtitle.AutoSize = true;
        subtitle.Location = new Point(20, 48);

        headerPanel.Controls.Add(title);
        headerPanel.Controls.Add(subtitle);

        Panel bottomPanel = new Panel();
        bottomPanel.Dock = DockStyle.Bottom;
        bottomPanel.Height = 92;
        bottomPanel.BackColor = Color.FromArgb(12, 14, 18);
        bottomPanel.Padding = new Padding(15, 10, 15, 10);

        chkBlockTaskbarPrompt = new CheckBox();
        chkBlockTaskbarPrompt.Text = "Auto-disable 'Add to taskbar' & auto-set default";
        chkBlockTaskbarPrompt.Checked = true;
        chkBlockTaskbarPrompt.ForeColor = Color.FromArgb(135, 195, 255);
        chkBlockTaskbarPrompt.Font = new Font("Segoe UI Semibold", 8.8f, FontStyle.Bold);
        chkBlockTaskbarPrompt.AutoSize = true;
        chkBlockTaskbarPrompt.Location = new Point(20, 12);
        chkBlockTaskbarPrompt.Cursor = Cursors.Hand;
        chkBlockTaskbarPrompt.CheckedChanged += delegate {
            SaveUserPreferences();
            if (chkBlockTaskbarPrompt.Checked) {
                ApplyTaskbarSuppression();
                statusLabel.Text = "Auto-disable taskbar active.";
            } else {
                statusLabel.Text = "Auto-disable taskbar disabled.";
            }
        };

        statusLabel = new Label();
        statusLabel.Text = "Detecting browsers...";
        statusLabel.ForeColor = Color.FromArgb(140, 145, 160);
        statusLabel.AutoSize = false;
        statusLabel.Location = new Point(18, 44);
        statusLabel.Size = new Size(260, 36);
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;

        btnRefresh = new Button();
        btnRefresh.Text = "Refresh";
        btnRefresh.Size = new Size(95, 34);
        btnRefresh.Location = new Point(310, 45);
        btnRefresh.FlatStyle = FlatStyle.Flat;
        btnRefresh.BackColor = Color.FromArgb(36, 40, 50);
        btnRefresh.ForeColor = Color.White;
        btnRefresh.FlatAppearance.BorderSize = 0;
        btnRefresh.Cursor = Cursors.Hand;
        btnRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnRefresh.Click += delegate { RefreshBrowserData(true); };

        btnSettings = new Button();
        btnSettings.Text = "Windows Settings";
        btnSettings.Size = new Size(130, 34);
        btnSettings.Location = new Point(415, 45);
        btnSettings.FlatStyle = FlatStyle.Flat;
        btnSettings.BackColor = Color.FromArgb(0, 120, 215);
        btnSettings.ForeColor = Color.White;
        btnSettings.FlatAppearance.BorderSize = 0;
        btnSettings.Cursor = Cursors.Hand;
        btnSettings.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnSettings.Click += delegate { OpenDefaultAppsSettings(null); };

        bottomPanel.Controls.Add(chkBlockTaskbarPrompt);
        bottomPanel.Controls.Add(statusLabel);
        bottomPanel.Controls.Add(btnRefresh);
        bottomPanel.Controls.Add(btnSettings);

        listView = new AnimatedBrowserListView();
        listView.Dock = DockStyle.Fill;
        listView.OnSwapClicked = SetAsDefault;

        this.Controls.Add(listView);
        this.Controls.Add(bottomPanel);
        this.Controls.Add(headerPanel);
    }

    private void ApplyTaskbarSuppression() {
        try {
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Google\Chrome")) {
                if (key != null) key.SetValue("PromoteBrowserForTaskbarPinning", 0, RegistryValueKind.DWord);
            }
        } catch {}

        try {
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Microsoft\Edge")) {
                if (key != null) {
                    key.SetValue("PromoteBrowserForTaskbarPinning", 0, RegistryValueKind.DWord);
                    key.SetValue("PinToTaskbarEnabled", 0, RegistryValueKind.DWord);
                    key.SetValue("WebWidgetIsEnabled", 0, RegistryValueKind.DWord);
                }
            }
        } catch {}

        try {
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager")) {
                if (key != null) {
                    key.SetValue("SubscribedContent-338389Enabled", 0, RegistryValueKind.DWord);
                    key.SetValue("SubscribedContent-310093Enabled", 0, RegistryValueKind.DWord);
                    key.SetValue("SoftLandingEnabled", 0, RegistryValueKind.DWord);
                }
            }
        } catch {}
    }

    private void TriggerDirectClick(AutomationElement element) {
        try {
            object pattern;
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out pattern)) {
                ((InvokePattern)pattern).Invoke();
                return;
            }
        } catch {}

        try {
            System.Windows.Rect rect = element.Current.BoundingRectangle;
            if (rect.Width > 0 && rect.Height > 0) {
                int cx = (int)(rect.Left + rect.Width / 2);
                int cy = (int)(rect.Top + rect.Height / 2);
                SetCursorPos(cx, cy);
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                Thread.Sleep(30);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
            }
        } catch {}
    }

    private void StartAutoClickSequence(string browserExePath) {
        ThreadPool.QueueUserWorkItem(delegate {
            int retries = 50;
            bool taskbarHandled = false;
            bool setDefaultHandled = false;

            while (retries-- > 0) {
                try {
                    AutomationElement settingsWin = null;
                    var condWin = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window);
                    var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, condWin);
                    foreach (AutomationElement win in windows) {
                        try {
                            string procName = "";
                            int pid = win.Current.ProcessId;
                            try { procName = Process.GetProcessById(pid).ProcessName; } catch {}
                            if (procName.Equals("SystemSettings", StringComparison.OrdinalIgnoreCase)) {
                                settingsWin = win;
                                break;
                            }
                        } catch {}
                    }

                    AutomationElement searchRoot = settingsWin ?? AutomationElement.RootElement;

                    if (!taskbarHandled) {
                        var condCb = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox);
                        var checkboxes = searchRoot.FindAll(TreeScope.Descendants, condCb);
                        foreach (AutomationElement cb in checkboxes) {
                            try {
                                string name = cb.Current.Name ?? string.Empty;
                                if (name.IndexOf("barra applicazioni", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    name.IndexOf("taskbar", StringComparison.OrdinalIgnoreCase) >= 0) {
                                    
                                    object pattern;
                                    if (cb.TryGetCurrentPattern(TogglePattern.Pattern, out pattern)) {
                                        TogglePattern tp = (TogglePattern)pattern;
                                        if (tp.Current.ToggleState == ToggleState.On) {
                                            tp.Toggle();
                                        }
                                        taskbarHandled = true;
                                    } else {
                                        TriggerDirectClick(cb);
                                        taskbarHandled = true;
                                    }
                                }
                            } catch {}
                        }
                    }

                    var condBtn = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                    var buttons = searchRoot.FindAll(TreeScope.Descendants, condBtn);
                    foreach (AutomationElement btn in buttons) {
                        try {
                            string name = btn.Current.Name ?? string.Empty;
                            string autoId = btn.Current.AutomationId ?? string.Empty;

                            if (name.IndexOf("Imposta predefinito", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Set default", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                autoId.IndexOf("Default", StringComparison.OrdinalIgnoreCase) >= 0) {
                                
                                TriggerDirectClick(btn);
                                setDefaultHandled = true;
                            }
                        } catch {}
                    }

                    if (setDefaultHandled && (taskbarHandled || retries < 25)) {
                        break;
                    }
                } catch {}
                Thread.Sleep(200);
            }

            Thread.Sleep(1200);
            CleanTaskbarPin(browserExePath);
        });
    }

    private void CleanTaskbarPin(string exePath) {
        try {
            string taskbarPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

            if (!Directory.Exists(taskbarPath) || string.IsNullOrEmpty(exePath)) return;

            string exeName = Path.GetFileNameWithoutExtension(exePath).ToLower();
            string[] links = Directory.GetFiles(taskbarPath, "*.lnk");

            foreach (string link in links) {
                string linkName = Path.GetFileNameWithoutExtension(link).ToLower();
                if (linkName.Contains(exeName) || 
                   (exeName.Contains("chrome") && linkName.Contains("chrome")) ||
                   (exeName.Contains("firefox") && linkName.Contains("firefox")) ||
                   (exeName.Contains("edge") && linkName.Contains("edge"))) {
                    try {
                        DateTime lastWrite = File.GetLastWriteTime(link);
                        if ((DateTime.Now - lastWrite).TotalSeconds < 30) {
                            File.Delete(link);
                        }
                    } catch {}
                }
            }
        } catch {}
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint AssocQueryString(
        uint flags,
        int str,
        string pszAssoc,
        string pszExtra,
        [Out] System.Text.StringBuilder pszOut,
        ref uint pcchOut);

    private string GetCurrentDefaultBrowserExe() {
        try {
            uint len = 260;
            System.Text.StringBuilder sb = new System.Text.StringBuilder(260);
            uint hr = AssocQueryString(0, 2, ".html", "open", sb, ref len);
            if (hr == 0 && sb.Length > 0) return sb.ToString();
        } catch {}
        return string.Empty;
    }

    private List<BrowserInfo> QueryBrowsers() {
        string currentDefaultExe = GetCurrentDefaultBrowserExe();
        var dict = new Dictionary<string, BrowserInfo>(StringComparer.OrdinalIgnoreCase);

        string[] roots = new string[] {
            @"SOFTWARE\Clients\StartMenuInternet",
            @"SOFTWARE\WOW6432Node\Clients\StartMenuInternet"
        };
        RegistryKey[] hives = new RegistryKey[] { Registry.CurrentUser, Registry.LocalMachine };

        foreach (var hive in hives) {
            foreach (var root in roots) {
                try {
                    using (var key = hive.OpenSubKey(root)) {
                        if (key == null) continue;
                        foreach (var name in key.GetSubKeyNames()) {
                            try {
                                using (var sub = key.OpenSubKey(name)) {
                                    if (sub == null) continue;
                                    string disp = sub.GetValue(null) as string;
                                    string cmd = null;
                                    using (var cmdKey = sub.OpenSubKey(@"shell\open\command")) {
                                        if (cmdKey != null) cmd = cmdKey.GetValue(null) as string;
                                    }
                                    if (string.IsNullOrEmpty(cmd)) continue;

                                    string exe = cmd.Trim();
                                    if (exe.StartsWith("\"")) {
                                        int nextQuote = exe.IndexOf('\"', 1);
                                        if (nextQuote > 1) exe = exe.Substring(1, nextQuote - 1);
                                    } else {
                                        int space = exe.IndexOf(' ');
                                        if (space > 0) exe = exe.Substring(0, space);
                                    }

                                    if (!File.Exists(exe)) continue;
                                    if (exe.IndexOf("iexplore.exe", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                                    string regApp = name;
                                    try {
                                        using (var cap = sub.OpenSubKey("Capabilities")) {
                                            if (cap != null) {
                                                string appName = cap.GetValue("ApplicationName") as string;
                                                if (!string.IsNullOrEmpty(appName)) disp = appName;
                                            }
                                        }
                                    } catch {}

                                    if (!dict.ContainsKey(exe)) {
                                        dict[exe] = new BrowserInfo {
                                            DisplayName = CleanBrowserName(disp),
                                            ExePath = exe,
                                            RegisteredAppName = regApp
                                        };
                                    }
                                }
                            } catch {}
                        }
                    }
                } catch {}
            }
        }

        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] common = new string[] {
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files (x86)\Microsoft\EdgeCore\157.0.4341.46\msedge.exe",
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files\Mozilla Firefox\firefox.exe",
            @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe",
            @"C:\Program Files\Opera\launcher.exe",
            @"C:\Program Files\Opera GX\launcher.exe",
            @"C:\Program Files\Vivaldi\Application\vivaldi.exe",
            Path.Combine(localApp, @"Programs\Opera GX\launcher.exe"),
            Path.Combine(localApp, @"Programs\Opera\launcher.exe"),
            Path.Combine(localApp, @"BraveSoftware\Brave-Browser\Application\brave.exe")
        };

        foreach (var path in common) {
            try {
                if (File.Exists(path) && !dict.ContainsKey(path)) {
                    string name = Path.GetFileNameWithoutExtension(path);
                    string regName = "Microsoft Edge";
                    if (path.IndexOf("Edge", StringComparison.OrdinalIgnoreCase) >= 0) {
                        name = "Microsoft Edge";
                        regName = "Microsoft Edge";
                    } else if (path.IndexOf("Chrome", StringComparison.OrdinalIgnoreCase) >= 0) {
                        name = "Google Chrome";
                        regName = "Google Chrome";
                    } else if (path.IndexOf("Firefox", StringComparison.OrdinalIgnoreCase) >= 0) {
                        name = "Mozilla Firefox";
                        regName = "Firefox";
                    } else if (path.IndexOf("Opera GX", StringComparison.OrdinalIgnoreCase) >= 0) {
                        name = "Opera GX";
                        regName = "Opera GX";
                    } else if (path.IndexOf("Opera", StringComparison.OrdinalIgnoreCase) >= 0) {
                        name = "Opera";
                        regName = "Opera";
                    } else if (path.IndexOf("Brave", StringComparison.OrdinalIgnoreCase) >= 0) {
                        name = "Brave Browser";
                        regName = "Brave";
                    } else if (path.IndexOf("Vivaldi", StringComparison.OrdinalIgnoreCase) >= 0) {
                        name = "Vivaldi";
                        regName = "Vivaldi";
                    }

                    dict[path] = new BrowserInfo {
                        DisplayName = name,
                        ExePath = path,
                        RegisteredAppName = regName
                    };
                }
            } catch {}
        }

        var result = new List<BrowserInfo>();
        foreach (var b in dict.Values) {
            try {
                using (Icon ico = Icon.ExtractAssociatedIcon(b.ExePath)) {
                    if (ico != null) {
                        b.BrowserBitmap = new Bitmap(ico.ToBitmap());
                    }
                }
            } catch {}

            if (!string.IsNullOrEmpty(currentDefaultExe) &&
                string.Equals(b.ExePath, currentDefaultExe, StringComparison.OrdinalIgnoreCase)) {
                b.IsCurrentDefault = true;
            }
            result.Add(b);
        }

        result.Sort(delegate(BrowserInfo x, BrowserInfo y) {
            if (x.IsCurrentDefault && !y.IsCurrentDefault) return -1;
            if (!x.IsCurrentDefault && y.IsCurrentDefault) return 1;
            return string.Compare(x.DisplayName, y.DisplayName, StringComparison.OrdinalIgnoreCase);
        });

        return result;
    }

    private string CleanBrowserName(string raw) {
        if (string.IsNullOrEmpty(raw)) return "Browser";
        if (raw.StartsWith("Firefox", StringComparison.OrdinalIgnoreCase)) return "Mozilla Firefox";
        if (raw.StartsWith("Google Chrome", StringComparison.OrdinalIgnoreCase)) return "Google Chrome";
        if (raw.StartsWith("Norton", StringComparison.OrdinalIgnoreCase)) return "Norton Private Browser";
        if (raw.IndexOf("Edge", StringComparison.OrdinalIgnoreCase) >= 0) return "Microsoft Edge";
        return raw;
    }

    private void StartSmoothEntrance() {
        browsers = QueryBrowsers();
        listView.SetBrowsers(browsers, true);
        statusLabel.Text = browsers.Count + " browser(s) detected.";

        windowFadeTimer = new System.Windows.Forms.Timer();
        windowFadeTimer.Interval = 15;
        windowFadeTimer.Tick += delegate {
            if (this.Opacity < 1.0) {
                this.Opacity = Math.Min(1.0, this.Opacity + 0.07);
            } else {
                windowFadeTimer.Stop();
                windowFadeTimer.Dispose();
                windowFadeTimer = null;
            }
        };
        windowFadeTimer.Start();
    }

    private void RefreshBrowserData(bool reanimate) {
        var updated = QueryBrowsers();
        bool changed = updated.Count != browsers.Count;
        if (!changed) {
            for (int i = 0; i < updated.Count; i++) {
                if (updated[i].IsCurrentDefault != browsers[i].IsCurrentDefault ||
                    updated[i].ExePath != browsers[i].ExePath) {
                    changed = true;
                    break;
                }
            }
        }

        if (changed || reanimate) {
            browsers = updated;
            listView.SetBrowsers(browsers, reanimate);
        }
        statusLabel.Text = browsers.Count + " browser(s) detected.";
    }

    private void SetAsDefault(BrowserInfo browser) {
        statusLabel.Text = "Swapping to " + browser.DisplayName + "...";

        if (chkBlockTaskbarPrompt.Checked) {
            ApplyTaskbarSuppression();
            StartAutoClickSequence(browser.ExePath);
        }

        string exeLower = browser.ExePath.ToLower();
        try {
            if (exeLower.Contains("chrome.exe")) {
                Process.Start(browser.ExePath, "--make-default-browser");
            } else if (exeLower.Contains("firefox.exe")) {
                Process.Start(browser.ExePath, "-setDefaultBrowser");
            } else if (exeLower.Contains("msedge.exe")) {
                Process.Start(browser.ExePath, "--make-default-browser");
            } else if (exeLower.Contains("brave.exe")) {
                Process.Start(browser.ExePath, "--make-default-browser");
            }
        } catch {}

        OpenDefaultAppsSettings(browser.RegisteredAppName);
        statusLabel.Text = "Applying default for " + browser.DisplayName + "...";
    }

    private void OpenDefaultAppsSettings(string appName) {
        try {
            if (!string.IsNullOrEmpty(appName)) {
                string uri = "ms-settings:defaultapps?registeredAppMachine=" + Uri.EscapeDataString(appName);
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            } else {
                Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
            }
        } catch {
            try {
                Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
            } catch {}
        }
    }
}
