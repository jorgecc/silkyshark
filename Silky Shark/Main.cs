using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Timers;
using Timer = System.Timers.Timer;

// ReSharper disable NotAccessedField.Local
// ReSharper disable LocalizableElement
#pragma warning disable 649

namespace Silky_Shark
{
    public partial class Main : Form
    {
        private readonly Config config;
        private Settings settings;
        private readonly Overlay overlay = new Overlay();
        private readonly List<Point> linePoints = new List<Point>();
        private readonly List<Point> smoothPoints = new List<Point>();
        private readonly Timer lineSmoothingTimer = new Timer();
        private readonly Timer lineProcessingTimer = new Timer();
        private readonly int virtualWidth = GetSystemMetrics(78);
        private readonly int virtualHeight = GetSystemMetrics(79);
        private readonly int virtualLeft = GetSystemMetrics(76);
        private readonly int virtualTop = GetSystemMetrics(77);
        public bool smoothingOn;
        private bool isDrawing;
        private bool mouseMoving;
        public bool tabletMode;
        public readonly Hotkey[] hotKeyHandling = new Hotkey[6];
        private Point position = new Point(0, 0);
        private Point lastPosition = new Point(0, 0);

        public Main()
        {
            InitializeComponent();

            // Initialize the config file
            config = new Config(this, overlay);

            // Overlay setup
            overlay.Show();
            overlay.TopMost = true;
            overlay.Bounds = Screen.AllScreens[0].Bounds;
            button_colorDialog.BackColor = overlay.cursorColor;

            // Attempt to load the config file, if any
            config.LoadConfig();

            // Low level mouse hook (MouseHook.cs)
            MouseHook.Start();
            MouseHook.MouseDownHooked += MouseDownHandler;
            MouseHook.MouseUpHooked += MouseUpHandler;
            MouseHook.MouseMoveHooked += MouseMoveHandler;

            // Mouse smoothing updater
            lineSmoothingTimer.Elapsed += LineSmoothingUpdate;
            lineSmoothingTimer.Interval = 5;

            // Line processing updater
            lineProcessingTimer.Elapsed += LineProcessingUpdate;
            lineProcessingTimer.Interval = config.smoothingStrength;

            // Register a raw input listener
            var size = Marshal.SizeOf(typeof(RawInputDevice));
            var devices = new RawInputDevice[1];
            devices[0].UsagePage = 1;
            devices[0].Usage = 2;
            devices[0].Flags = 0x00000100;
            devices[0].Target = Handle;
            RegisterRawInputDevices(devices, 1, size);
        }

        // Hotkey handling
        public void RegisterHotkey(int id, Hotkey.KeyModifiers modifiers, Keys key)
        {
            try
            {
                hotKeyHandling[id].Dispose();
            }
            catch
            {
                // No hotkey to dispose?
            }

            try
            {
                hotKeyHandling[id] = new Hotkey(Handle, id, modifiers, key);
                switch (id)
                {
                    case 0:
                        hotKeyHandling[0].HotKeyPressed += Hotkey_SmoothOnOff;
                        break;
                    case 1:
                        hotKeyHandling[1].HotKeyPressed += Hotkey_OverlayOnOff;
                        break;
                    case 2:
                        hotKeyHandling[2].HotKeyPressed += Hotkey_ToggleDisplay;
                        break;
                    case 3:
                        hotKeyHandling[3].HotKeyPressed += Hotkey_TabletMode;
                        break;
                    case 4:
                        hotKeyHandling[4].HotKeyPressed += Hotkey_StrengthUp;
                        break;
                    case 5:
                        hotKeyHandling[5].HotKeyPressed += Hotkey_StrengthDown;
                        break;
                }
            }
            catch
            {
                // Hotkey registration failed
            }
        }

        private void Hotkey_StrengthDown(object sender, EventArgs e)
        {
            if (Application.OpenForms.OfType<Settings>().Count() != 1)
            {
                var s = int.Parse(textBox_smoothingStrength.Text) - 10;
                s = (int) Math.Round(s / 10.0) * 10;
                textBox_smoothingStrength.Text = s.ToString();
            }
        }

        private void Hotkey_StrengthUp(object sender, EventArgs e)
        {
            if (Application.OpenForms.OfType<Settings>().Count() != 1)
            {
                var s = int.Parse(textBox_smoothingStrength.Text) + 10;
                s = (int) Math.Round(s / 10.0) * 10;
                textBox_smoothingStrength.Text = s.ToString();
            }
        }

        private void Hotkey_TabletMode(object sender, EventArgs e)
        {
            if (Application.OpenForms.OfType<Settings>().Count() != 1)
            {
                if (config.disableAutoDetection)
                {
                    checkBox_tabletMode.Checked = !checkBox_tabletMode.Checked;
                    tabletMode = checkBox_tabletMode.Checked;
                }
            }
        }

        private void Hotkey_ToggleDisplay(object sender, EventArgs e)
        {
            if (Application.OpenForms.OfType<Settings>().Count() != 1)
            {
                button_toggleDisplay.PerformClick();
            }
        }

        private void Hotkey_OverlayOnOff(object sender, EventArgs e)
        {
            if (Application.OpenForms.OfType<Settings>().Count() != 1)
            {
                if (!config.disableOverlay)
                {
                    overlay.Hide();
                    config.disableOverlay = true;
                }
                else
                {
                    overlay.Show();
                    config.disableOverlay = false;
                }
            }
        }

        private void Hotkey_SmoothOnOff(object sender, EventArgs e)
        {
            if (Application.OpenForms.OfType<Settings>().Count() != 1)
            {
                button_smoothOnOff.PerformClick();
            }
        }

        // Reading global raw input
        private void VirtualCursorUpdate(ref Message m)
        {
            var RidInput = 0x10000003;
            var headerSize = Marshal.SizeOf(typeof(RawInputHeader));
            var size = Marshal.SizeOf(typeof(RawInput));
            GetRawInputData(m.LParam, RidInput, out var input, ref size, headerSize);
            var mouse = input.Mouse;

            if (!config.disableAutoDetection)
            {
                if (mouse.LastX > config.tolerance || mouse.LastY > config.tolerance)
                {
                    checkBox_tabletMode.Checked = true;
                    tabletMode = true;
                }
                else
                {
                    checkBox_tabletMode.Checked = false;
                    tabletMode = false;
                }
            }

            if (isDrawing)
            {
                if (tabletMode)
                {
                    var offset = new Point(0, 0);
                    if (config.tabletOffsetOverride) offset = config.tabletOffset;
                    var tabletX = mouse.LastX * virtualWidth / 65536;
                    var tabletY = mouse.LastY * virtualHeight / 65536;
                    var p = new Point(tabletX + offset.X + virtualLeft, tabletY + offset.Y + virtualTop);
                    position = p;
                    overlay.cursorPos = p;
                    overlay.Invalidate();
                }
                else
                {
                    var p = new Point(position.X + mouse.LastX, position.Y + mouse.LastY);
                    //if (p.X < virtualLeft) p.X = virtualLeft;
                    //if (p.X > virtualWidth) p.X = virtualWidth;
                    //if (p.Y < virtualTop) p.Y = virtualTop;
                    //if (p.Y > virtualHeight) p.Y = virtualHeight;
                    position = p;
                    overlay.cursorPos = p;
                    overlay.Invalidate();
                }
            }
        }

        // Line processing (interlopation)
        private void LineProcessingUpdate(object sender, ElapsedEventArgs e)
        {
            try
            {
                // B-Spline smoothing
                if (linePoints.Count > 3)
                {
                    int i;
                    int splineX;
                    int splineY;
                    var a = new double[5];
                    var b = new double[5];
                    var p1 = linePoints[0];
                    var p2 = linePoints[1];
                    var p3 = linePoints[2];
                    var p4 = linePoints[3];

                    a[0] = (-p1.X + 3 * p2.X - 3 * p3.X + p4.X) / 6.0;
                    a[1] = (3 * p1.X - 6 * p2.X + 3 * p3.X) / 6.0;
                    a[2] = (-3 * p1.X + 3 * p3.X) / 6.0;
                    a[3] = (p1.X + 4 * p2.X + p3.X) / 6.0;
                    b[0] = (-p1.Y + 3 * p2.Y - 3 * p3.Y + p4.Y) / 6.0;
                    b[1] = (3 * p1.Y - 6 * p2.Y + 3 * p3.Y) / 6.0;
                    b[2] = (-3 * p1.Y + 3 * p3.Y) / 6.0;
                    b[3] = (p1.Y + 4 * p2.Y + p3.Y) / 6.0;

                    smoothPoints.Add(new Point((int) a[3], (int) b[3]));

                    for (i = 1; i <= config.smoothingInterpolation - 1; i++)
                    {
                        var t = Convert.ToSingle(i) / Convert.ToSingle(config.smoothingInterpolation);
                        splineX = (int) ((a[2] + t * (a[1] + t * a[0])) * t + a[3]);
                        splineY = (int) ((b[2] + t * (b[1] + t * b[0])) * t + b[3]);
                        if (smoothPoints.Last() != new Point(splineX, splineY))
                        {
                            smoothPoints.Add(new Point(splineX, splineY));
                        }
                    }

                    linePoints.RemoveAt(0);
                }
                else if (MouseHook.GetCursorPosition() != position && isDrawing)
                {
                    if (config.disableCatchUp)
                    {
                        if (mouseMoving)
                        {
                            linePoints.Add(position);
                        }
                    }
                    else
                    {
                        linePoints.Add(position);
                    }
                }
            }
            catch
            {
                // Fail processing gracefully
            }
        }

        // Line smoothing
        private void LineSmoothingUpdate(object sender, ElapsedEventArgs e)
        {
            var guidePos = position;
            if (lastPosition == guidePos)
            {
                mouseMoving = false;
            }
            else
            {
                mouseMoving = true;
            }

            lastPosition = guidePos;

            try
            {
                // Begin smoothing only if we have points to work with and if drawing
                if (smoothPoints.Count > 0 && isDrawing)
                {
                    if (config.disableCatchUp)
                    {
                        if (mouseMoving)
                        {
                            MouseHook.SetCursorPos(smoothPoints[0].X, smoothPoints[0].Y);
                            smoothPoints.RemoveAt(0);
                        }
                    }
                    else
                    {
                        MouseHook.SetCursorPos(smoothPoints[0].X, smoothPoints[0].Y);
                        smoothPoints.RemoveAt(0);
                    }
                }
            }
            catch
            {
                // Fail smoothing gracefully
            }

            if (!isDrawing)
            {
                smoothPoints.Clear();
                lineSmoothingTimer.Stop();
                if (!config.snapToCursor) MouseHook.SetCursorPos(guidePos.X, guidePos.Y);
                MouseHook.moveEnabled = true;
                MouseHook.downEnabled = true;
            }
        }

        // Mouse event handling
        private void MouseDownHandler(object sender, EventArgs e)
        {
            if (smoothingOn)
            {
                if (config.smoothOnDraw && !isDrawing)
                {
                    linePoints.Clear();
                    smoothPoints.Clear();
                    MouseHook.moveEnabled = false;
                    var p = MouseHook.GetCursorPosition();
                    smoothPoints.Add(p);
                    linePoints.Add(p);
                    linePoints.Add(p);
                    linePoints.Add(p);
                    position = p;
                    isDrawing = true;
                    lineProcessingTimer.Start();
                    lineSmoothingTimer.Start();
                }
            }
        }

        private void MouseUpHandler(object sender, EventArgs e)
        {
            if (smoothingOn)
            {
                if (config.smoothOnDraw && isDrawing)
                {
                    MouseHook.downEnabled = false;
                    isDrawing = false;
                    lineProcessingTimer.Stop();
                    linePoints.Clear();
                    if (!config.snapToCursor)
                    {
                        var guidePos = overlay.cursorPos;
                        MouseHook.SetCursorPos(guidePos.X, guidePos.Y);
                    }
                    else
                    {
                        overlay.cursorPos = MouseHook.GetCursorPosition();
                    }
                }
            }
        }

        private void MouseMoveHandler(object sender, EventArgs e)
        {
            if (!smoothingOn)
            {
                overlay.cursorPos = MouseHook.GetCursorPosition();
                overlay.Invalidate();
            }

            if (config.smoothOnDraw && !isDrawing && MouseHook.moveEnabled)
            {
                overlay.cursorPos = MouseHook.GetCursorPosition();
                overlay.Invalidate();
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_INPUT = 0xFF;
            const int WM_SYSCOMMAND = 0x0112;
            const int SC_MOVE = 0xF010;

            switch (m.Msg)
            {
                case WM_SYSCOMMAND:
                    var command = m.WParam.ToInt32() & 0xfff0;
                    if (command == SC_MOVE)
                    {
                        m.Result = (IntPtr) 0x2;
                    }

                    break;
            }

            if (m.Msg == WM_INPUT && smoothingOn)
            {
                VirtualCursorUpdate(ref m);
            }
            else
            {
                base.WndProc(ref m);
            }
        }

        // Interface handling
        private void Button_SmoothOnOff_Click(object sender, EventArgs e)
        {
            if (smoothingOn)
            {
                // Off
                button_smoothOnOff.BackColor = Color.Gainsboro;
                MouseHook.moveEnabled = true;
                MouseHook.downEnabled = true;
                smoothingOn = false;
                isDrawing = false;
                lineSmoothingTimer.Stop();
                lineProcessingTimer.Stop();
                try
                {
                    settings.checkBox_disableAutoDetection.Enabled = true;
                }
                catch
                {
                    // Fail gracefully
                }

                if (config.disableAutoDetection)
                {
                    checkBox_tabletMode.Enabled = true;
                }
            }
            else
            {
                // On
                button_smoothOnOff.BackColor = Color.Azure;
                linePoints.Clear();
                smoothPoints.Clear();
                position = MouseHook.GetCursorPosition();
                smoothPoints.Add(position);
                if (config.smoothOnDraw)
                {
                    MouseHook.moveEnabled = true;
                    isDrawing = false;
                }
                else
                {
                    MouseHook.moveEnabled = false;
                    isDrawing = true;
                    lineProcessingTimer.Start();
                    lineSmoothingTimer.Start();
                }

                smoothingOn = true;
                try
                {
                    settings.checkBox_disableAutoDetection.Enabled = false;
                }
                catch
                {
                    // Fail gracefully
                }

                if (config.disableAutoDetection)
                {
                    checkBox_tabletMode.Enabled = false;
                }
            }
        }

        private void TrackBar_smoothStrength_Scroll(object sender, EventArgs e)
        {
            config.smoothingStrength = trackBar_smoothingStrength.Value;
            lineProcessingTimer.Interval = config.smoothingStrength;
            textBox_smoothingStrength.Text = config.smoothingStrength.ToString();
            if (!config.manualInterpolation)
            {
                config.smoothingInterpolation = (int) Math.Round(config.smoothingStrength * 0.15);
                trackBar_smoothingInterpolation.Value = config.smoothingInterpolation;
                textBox_smoothingInterpolation.Text = config.smoothingInterpolation.ToString();
            }
        }

        private void textBox_smoothingStrength_TextChanged(object sender, EventArgs e)
        {
            try
            {
                if (int.Parse(textBox_smoothingStrength.Text) < 1)
                {
                    config.smoothingStrength = 1;
                }
                else if (int.Parse(textBox_smoothingStrength.Text) > 100)
                {
                    config.smoothingStrength = 100;
                }
                else
                {
                    config.smoothingStrength = int.Parse(textBox_smoothingStrength.Text);
                }
            }
            catch
            {
                config.smoothingStrength = 1;
            }

            lineProcessingTimer.Interval = config.smoothingStrength;
            trackBar_smoothingStrength.Value = config.smoothingStrength;
            textBox_smoothingStrength.Text = config.smoothingStrength.ToString();
            if (!config.manualInterpolation)
            {
                config.smoothingInterpolation = (int) Math.Round(config.smoothingStrength * 0.15);
                trackBar_smoothingInterpolation.Value = config.smoothingInterpolation;
                textBox_smoothingInterpolation.Text = config.smoothingInterpolation.ToString();
            }
        }

        private void trackBar_smoothingInterpolation_Scroll(object sender, EventArgs e)
        {
            config.smoothingInterpolation = trackBar_smoothingInterpolation.Value;
            trackBar_smoothingInterpolation.Value = config.smoothingInterpolation;
            textBox_smoothingInterpolation.Text = config.smoothingInterpolation.ToString();
        }

        private void textBox_smoothingInterpolation_TextChanged(object sender, EventArgs e)
        {
            try
            {
                if (int.Parse(textBox_smoothingInterpolation.Text) < 0)
                {
                    config.smoothingInterpolation = 0;
                }
                else if (int.Parse(textBox_smoothingInterpolation.Text) > 20)
                {
                    config.smoothingInterpolation = 20;
                }
                else
                {
                    config.smoothingInterpolation = int.Parse(textBox_smoothingInterpolation.Text);
                }
            }
            catch
            {
                config.smoothingInterpolation = 0;
            }

            trackBar_smoothingInterpolation.Value = config.smoothingInterpolation;
            textBox_smoothingInterpolation.Text = config.smoothingInterpolation.ToString();
        }

        private void CheckBox_manualInterpolation_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox_manualInterpolation.Checked)
            {
                config.manualInterpolation = true;
                trackBar_smoothingInterpolation.Enabled = true;
                textBox_smoothingInterpolation.Enabled = true;
            }
            else
            {
                config.manualInterpolation = false;
                trackBar_smoothingInterpolation.Enabled = false;
                textBox_smoothingInterpolation.Enabled = false;
                config.smoothingInterpolation = (int) Math.Round(config.smoothingStrength * 0.15);
                trackBar_smoothingInterpolation.Value = config.smoothingInterpolation;
                textBox_smoothingInterpolation.Text = config.smoothingInterpolation.ToString();
            }
        }

        private void checkBox_stayOnTop_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox_stayOnTop.Checked)
            {
                TopMost = true;
                overlay.TopMost = true;
                config.stayOnTop = true;
            }
            else
            {
                TopMost = false;
                config.stayOnTop = false;
            }
        }

        private void checkBox_smoothOnDraw_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox_smoothOnDraw.Checked)
            {
                if (smoothingOn)
                {
                    isDrawing = false;
                    MouseHook.moveEnabled = true;
                    linePoints.Clear();
                    smoothPoints.Clear();
                    lineProcessingTimer.Stop();
                    lineSmoothingTimer.Stop();
                }

                config.smoothOnDraw = true;
            }
            else
            {
                if (smoothingOn)
                {
                    isDrawing = true;
                    MouseHook.moveEnabled = false;
                    MouseHook.downEnabled = true;
                    lineProcessingTimer.Start();
                    lineSmoothingTimer.Start();
                }

                config.smoothOnDraw = false;
            }
        }

        private void checkBox_tabletMode_CheckedChanged(object sender, EventArgs e)
        {
            tabletMode = checkBox_tabletMode.Checked;
        }

        private void Button_toggleScreen_Click(object sender, EventArgs e)
        {
            config.overlayScreen++;
            if (config.overlayScreen > Screen.AllScreens.Length - 1)
            {
                config.overlayScreen = 0;
            }

            overlay.Bounds = Screen.AllScreens[config.overlayScreen].Bounds;
            overlay.Invalidate();
        }

        private void Button_colorDialog_Click(object sender, EventArgs e)
        {
            var result = colorDialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                if (colorDialog.Color == Color.White) colorDialog.Color = Color.FromArgb(255, 255, 254);
                button_colorDialog.BackColor = colorDialog.Color;
                overlay.cursorColor = colorDialog.Color;
                overlay.Invalidate();
                try
                {
                    settings.panel_cursorPanel.Invalidate();
                    settings.button_mainColor.BackColor = colorDialog.Color;
                }
                catch
                {
                    // Fail setting color in settings gracefully
                }
            }
        }

        // Menu handling
        private void ToolStrip_Settings_Click(object sender, EventArgs e)
        {
            if (Application.OpenForms.OfType<Settings>().Count() != 1)
            {
                settings = new Settings(this, config, overlay) {Owner = this, MinimizeBox = false, MaximizeBox = false};
                ToolStripMenuItem_restoreDefaults.Enabled = false;
                ToolStripMenuItem_saveConfig.Enabled = false;
                settings.Show();
            }
        }

        private void ToolStripMenuItem_saveConfig_Click(object sender, EventArgs e)
        {
            config.SaveConfig();
            MessageBox.Show("Configuration settings saved to: Silky Shark.config", "Silky Shark", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void ToolStripMenuItem_restoreDefaults_Click(object sender, EventArgs e)
        {
            config.LoadConfig(true);
            MessageBox.Show("Default settings restored.", "Silky Shark", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void ToolStripMenuItem_exit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void ToolStripMenuItem_help_Click(object sender, EventArgs e)
        {
            if (Application.OpenForms.OfType<Help>().Count() != 1)
            {
                var help = new Help {Owner = this, MinimizeBox = false, MaximizeBox = false};
                help.Show();
            }
        }

        private void ToolStripMenuItem_about_Click(object sender, EventArgs e)
        {
            if (Application.OpenForms.OfType<About>().Count() != 1)
            {
                var about = new About {Owner = this, MinimizeBox = false, MaximizeBox = false};
                about.Show();
            }
        }

        // Raw input hook
        private struct RawInputDevice
        {
            public short UsagePage;
            public short Usage;
            public int Flags;
            public IntPtr Target;
        }

        private struct RawInputHeader
        {
            public int Type;
            public int Size;
            public IntPtr Device;
            public IntPtr WParam;
        }

        private struct RawInput
        {
            public RawInputHeader Header;
            public RawMouse Mouse;
        }

        private struct RawMouse
        {
            public short Flags;
            public short ButtonFlags;
            public short ButtonData;
            public int RawButtons;
            public int LastX;
            public int LastY;
            public int Extra;
        }

        //Dll importing
        [DllImport("user32.dll")]
        private static extern int RegisterRawInputDevices(RawInputDevice[] devices, int number, int size);

        [DllImport("user32.dll")]
        private static extern int GetRawInputData(IntPtr rawInput, int command, out RawInput data, ref int size,
            int headerSize);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
    }
}