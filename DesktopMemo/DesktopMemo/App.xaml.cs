using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using DesktopMemo.Models;
using DesktopMemo.Services;
using DesktopMemo.Views;
using WpfApp = System.Windows.Application;

namespace DesktopMemo
{
    public partial class App : System.Windows.Application
    {
        private NotifyIcon _trayIcon;
        private ApiServer _apiServer;
        private readonly Dictionary<string, NoteWindow> _noteWindows = new Dictionary<string, NoteWindow>();
        private readonly object _windowLock = new object();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Check for single instance
            bool createdNew;
            var mutex = new Mutex(true, "DesktopMemo_SingleInstance", out createdNew);
            if (!createdNew)
            {
                System.Windows.MessageBox.Show("DesktopMemo 已在运行中。", "DesktopMemo",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // Load data
            var store = NoteStore.Instance;

            // Setup event handlers
            store.NoteAdded += OnNoteAdded;
            store.NoteUpdated += OnNoteUpdated;
            store.NoteDeleted += OnNoteDeleted;

            // Initialize tray icon
            InitializeTray();

            // Restore existing notes
            foreach (var note in store.GetAllNotes())
            {
                // Always show all notes on startup (close = hide, not delete)
                note.Visible = true;
                CreateNoteWindow(note);
            }

            // If no notes exist, create a default one
            if (!store.GetAllNotes().Any())
            {
                AddNewNote();
            }

            // Start API server
            _apiServer = new ApiServer(store.Settings.ApiPort, SynchronizationContext.Current);
            _apiServer.Start();
        }

        private void InitializeTray()
        {
            _trayIcon = new NotifyIcon
            {
                Text = "DesktopMemo 桌面便笺",
                Icon = CreateDefaultIcon(),
                Visible = true
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("📝 新建便笺", null, (s, e) => AddNewNote());
            contextMenu.Items.Add("👁 显示全部便笺", null, (s, e) => ShowAllNotes());
            contextMenu.Items.Add("🙈 隐藏全部便笺", null, (s, e) => HideAllNotes());
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("❌ 退出", null, (s, e) => Shutdown());

            _trayIcon.ContextMenuStrip = contextMenu;
            _trayIcon.DoubleClick += (s, e) => AddNewNote();
        }

        private static Icon CreateDefaultIcon()
        {
            // Try to load from resources, fallback to creating a simple icon
            try
            {
                var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "tray.ico");
                if (File.Exists(iconPath))
                    return new Icon(iconPath);
            }
            catch { }

            // Create a simple colored icon programmatically
            using (var bmp = new Bitmap(16, 16))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    using (var brush = new SolidBrush(Color.FromArgb(255, 220, 160)))
                    {
                        g.FillRoundedRectangle(brush, new Rectangle(1, 1, 14, 14), 3);
                    }
                    using (var pen = new Pen(Color.FromArgb(200, 160, 80), 1))
                    {
                        g.DrawRoundedRectangle(pen, new Rectangle(1, 1, 13, 13), 3);
                    }
                    // Draw lines to represent text
                    using (var pen = new Pen(Color.FromArgb(180, 140, 60), 1))
                    {
                        g.DrawLine(pen, 4, 5, 12, 5);
                        g.DrawLine(pen, 4, 8, 11, 8);
                        g.DrawLine(pen, 4, 11, 10, 11);
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        private void AddNewNote()
        {
            var note = new Note
            {
                Color = NoteStore.Instance.Settings.DefaultColor,
                Width = 280,
                Height = 320
            };

            var pos = AutoPositioner.FindAvailablePosition(note.Width, note.Height);
            note.X = pos.X;
            note.Y = pos.Y;

            NoteStore.Instance.AddNote(note);
        }

        private void OnNoteAdded(Note note)
        {
            Dispatcher.Invoke(() =>
            {
                CreateNoteWindow(note);

                // Auto-enter edit mode for new notes
                if (_noteWindows.TryGetValue(note.Id, out var window))
                {
                    window.Activate();
                    // Trigger edit mode after window is loaded
                    window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        window.ToggleEditMode();
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            });
        }

        private void OnNoteUpdated(Note note)
        {
            Dispatcher.Invoke(() =>
            {
                if (_noteWindows.TryGetValue(note.Id, out var window))
                {
                    window.UpdateFromNote(note);
                }
            });
        }

        private void OnNoteDeleted(string noteId)
        {
            Dispatcher.Invoke(() =>
            {
                if (_noteWindows.TryGetValue(noteId, out var window))
                {
                    window.Closing -= null; // Prevent recursive
                    window.Close();
                    _noteWindows.Remove(noteId);
                }
            });
        }

        private void CreateNoteWindow(Note note)
        {
            lock (_windowLock)
            {
                if (_noteWindows.ContainsKey(note.Id)) return;

                var window = new NoteWindow(note);
                window.Closed += (s, e) =>
                {
                    lock (_windowLock)
                    {
                        _noteWindows.Remove(note.Id);
                    }
                };
                _noteWindows[note.Id] = window;
                window.Show();
            }
        }

        private void ShowAllNotes()
        {
            foreach (var window in _noteWindows.Values)
            {
                window.Visibility = Visibility.Visible;
                window.Show();
            }
        }

        private void HideAllNotes()
        {
            foreach (var window in _noteWindows.Values)
            {
                window.Hide();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _apiServer?.Dispose();
            _trayIcon?.Dispose();
            NoteStore.Instance.Save();
            base.OnExit(e);
        }
    }

    // Extension method for rounded rectangles in GDI+
    internal static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle rect, int radius)
        {
            using (var path = CreateRoundedRectPath(rect, radius))
            {
                g.FillPath(brush, path);
            }
        }

        public static void DrawRoundedRectangle(this Graphics g, Pen pen, Rectangle rect, int radius)
        {
            using (var path = CreateRoundedRectPath(rect, radius))
            {
                g.DrawPath(pen, path);
            }
        }

        private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectPath(Rectangle rect, int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            var d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
