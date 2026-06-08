using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopMemo.Models;
using DesktopMemo.Services;
using DesktopMemo.Views.Converters;

namespace DesktopMemo.Views
{
    public partial class NoteWindow : Window
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int HTCAPTION = 2;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        private const int GWL_EXSTYLE = -20;
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        public Note Note { get; private set; }
        private bool _isEditMode;
        private bool _isResizing;
        private Point _resizeStartPoint;
        private double _resizeStartWidth;
        private double _resizeStartHeight;
        private Timer _autoSaveTimer;

        // Color theme map (view mode: semi-transparent for glass effect)
        private static readonly System.Collections.Generic.Dictionary<string, Color> ColorMap =
            new System.Collections.Generic.Dictionary<string, Color>
            {
                { "yellow", Color.FromArgb(160, 255, 243, 176) },
                { "blue",   Color.FromArgb(160, 187, 222, 251) },
                { "green",  Color.FromArgb(160, 200, 230, 201) },
                { "pink",   Color.FromArgb(160, 248, 187, 208) },
                { "purple", Color.FromArgb(160, 206, 147, 216) },
                { "gray",   Color.FromArgb(160, 207, 216, 220) }
            };

        // Edit mode colors (fully opaque for readability)
        private static readonly System.Collections.Generic.Dictionary<string, Color> EditColorMap =
            new System.Collections.Generic.Dictionary<string, Color>
            {
                { "yellow", Color.FromArgb(240, 255, 243, 176) },
                { "blue",   Color.FromArgb(240, 187, 222, 251) },
                { "green",  Color.FromArgb(240, 200, 230, 201) },
                { "pink",   Color.FromArgb(240, 248, 187, 208) },
                { "purple", Color.FromArgb(240, 206, 147, 216) },
                { "gray",   Color.FromArgb(240, 207, 216, 220) }
            };

        public NoteWindow(Note note)
        {
            InitializeComponent();
            Note = note;

            Loaded += OnLoaded;
            LocationChanged += OnLocationChanged;
            SizeChanged += OnSizeChanged;
            Closing += OnClosing;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Set as tool window (no Alt+Tab, no taskbar)
            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);

            // Apply glass effect
            GlassEffectHelper.ApplyGlassEffect(this);

            // Apply note data to UI
            ApplyNoteData();

            // Register keyboard shortcuts
            InputBindings.Add(new KeyBinding(
                new RelayCommand(_ => ToggleEditMode()),
                Key.E, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(
                new RelayCommand(_ => DeleteNoteWithConfirmation()),
                Key.Delete, ModifierKeys.Control));
        }

        private void ApplyNoteData()
        {
            // Position and size
            Left = Note.X;
            Top = Note.Y;
            Width = Note.Width;
            Height = Note.Height;
            Topmost = Note.AlwaysOnTop;

            // Color
            ApplyColor(Note.Color);

            // Title
            TitleDisplay.Text = Note.Title;
            TitleEdit.Text = Note.Title;

            // Content
            RenderMarkdown();
            ContentEdit.Text = Note.Content;

            // Pin button
            PinButton.Content = Note.AlwaysOnTop ? "📍" : "📌";
        }

        private void ApplyColor(string colorName)
        {
            var map = _isEditMode ? EditColorMap : ColorMap;
            if (map.TryGetValue(colorName, out var color))
            {
                GlassBackground.Background = new SolidColorBrush(color);
            }

            // Update readability overlay
            ReadabilityOverlay.Background = _isEditMode
                ? new SolidColorBrush(Color.FromArgb(220, 255, 255, 255))  // opaque in edit mode
                : new SolidColorBrush(Color.FromArgb(112, 255, 255, 255)); // semi-transparent in view mode

            // Highlight selected color dot
            foreach (var child in ColorPanel.Children)
            {
                if (child is System.Windows.Shapes.Ellipse dot)
                {
                    dot.Stroke = (string)dot.Tag == colorName
                        ? new SolidColorBrush(Color.FromRgb(80, 80, 80))
                        : Brushes.Transparent;
                }
            }
        }

        private void RenderMarkdown()
        {
            ContentPanel.Children.Clear();
            var elements = MarkdownRenderer.RenderToElements(
                Note.Content,
                new SolidColorBrush(Color.FromRgb(51, 51, 51)),
                14);
            foreach (var el in elements)
                ContentPanel.Children.Add(el);
        }

        #region Edit Mode

        public void ToggleEditMode()
        {
            _isEditMode = !_isEditMode;
            SetEditMode(_isEditMode);
        }

        private void SetEditMode(bool editMode)
        {
            _isEditMode = editMode;

            if (editMode)
            {
                // Enter edit mode
                TitleDisplay.Visibility = Visibility.Collapsed;
                TitleEdit.Visibility = Visibility.Visible;
                TitleEdit.Text = Note.Title;
                TitleEdit.Focus();
                TitleEdit.SelectAll();

                ContentDisplay.Visibility = Visibility.Collapsed;
                ContentEdit.Visibility = Visibility.Visible;
                ContentEdit.Text = Note.Content;

                ColorPanel.Visibility = Visibility.Visible;
                ResizeHandle.Visibility = Visibility.Visible;
                EditIndicator.Visibility = Visibility.Visible;

                // Switch to opaque colors for readability while editing
                ApplyColor(Note.Color);

                // Allow resize
                ResizeMode = ResizeMode.CanResizeWithGrip;
            }
            else
            {
                // Cancel pending auto-save timer
                _autoSaveTimer?.Dispose();
                _autoSaveTimer = null;

                // Exit edit mode - save changes from editor
                Note.Title = TitleEdit.Text;
                Note.Content = ContentEdit.Text;
                Note.UpdatedAt = DateTime.Now;

                TitleDisplay.Text = Note.Title;
                TitleDisplay.Visibility = Visibility.Visible;
                TitleEdit.Visibility = Visibility.Collapsed;

                RenderMarkdown();
                ContentDisplay.Visibility = Visibility.Visible;
                ContentEdit.Visibility = Visibility.Collapsed;

                ColorPanel.Visibility = Visibility.Collapsed;
                ResizeHandle.Visibility = Visibility.Collapsed;
                EditIndicator.Visibility = Visibility.Collapsed;

                // Switch back to transparent glass colors
                ApplyColor(Note.Color);

                ResizeMode = ResizeMode.NoResize;

                // Save position and size
                Note.X = Left;
                Note.Y = Top;
                Note.Width = Width;
                Note.Height = Height;

                NoteStore.Instance.Save();
            }
        }

        private void ContentArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleEditMode();
                e.Handled = true;
            }
        }

        private void ContentEdit_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ScheduleAutoSave();
        }

        private void ScheduleAutoSave()
        {
            _autoSaveTimer?.Dispose();
            _autoSaveTimer = new Timer(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (_isEditMode)
                    {
                        Note.Title = TitleEdit.Text;
                        Note.Content = ContentEdit.Text;
                        Note.UpdatedAt = DateTime.Now;
                        NoteStore.Instance.Save();
                    }
                });
            }, null, 1000, Timeout.Infinite);
        }

        #endregion

        #region Drag

        private void DragHandle_MouseEnter(object sender, MouseEventArgs e)
        {
            DragIndicator.Visibility = Visibility.Visible;
            DragIndicator.Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));
        }

        private void DragHandle_MouseLeave(object sender, MouseEventArgs e)
        {
            DragIndicator.Background = new SolidColorBrush(Color.FromArgb(48, 0, 0, 0));
            if (!_isEditMode)
                DragIndicator.Visibility = Visibility.Visible; // Keep subtle indicator
        }

        private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleEditMode();
                return;
            }

            DragIndicator.Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));

            DragMove();

            DragIndicator.Background = new SolidColorBrush(Color.FromArgb(48, 0, 0, 0));
        }

        private void OnLocationChanged(object sender, EventArgs e)
        {
            if (!_isEditMode)
            {
                Note.X = Left;
                Note.Y = Top;
            }
        }

        #endregion

        #region Resize

        private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isResizing = true;
            _resizeStartPoint = e.GetPosition(this);
            _resizeStartWidth = Width;
            _resizeStartHeight = Height;

            Mouse.Capture(ResizeHandle);
            ResizeHandle.MouseMove += ResizeHandle_MouseMove;
            ResizeHandle.MouseLeftButtonUp += ResizeHandle_MouseLeftButtonUp;
            e.Handled = true;
        }

        private void ResizeHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isResizing) return;

            var currentPos = e.GetPosition(this);
            var deltaX = currentPos.X - _resizeStartPoint.X;
            var deltaY = currentPos.Y - _resizeStartPoint.Y;

            Width = Math.Max(MinWidth, _resizeStartWidth + deltaX);
            Height = Math.Max(MinHeight, _resizeStartHeight + deltaY);
        }

        private void ResizeHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isResizing = false;
            Mouse.Capture(null);
            ResizeHandle.MouseMove -= ResizeHandle_MouseMove;
            ResizeHandle.MouseLeftButtonUp -= ResizeHandle_MouseLeftButtonUp;

            Note.Width = Width;
            Note.Height = Height;
            NoteStore.Instance.Save();
            e.Handled = true;
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isEditMode)
            {
                Note.Width = Width;
                Note.Height = Height;
            }
        }

        #endregion

        #region Toolbar Actions

        private void ColorDot_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Shapes.Ellipse dot && dot.Tag is string colorName)
            {
                Note.Color = colorName;
                ApplyColor(colorName);
                NoteStore.Instance.Save();
            }
            e.Handled = true;
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            Note.AlwaysOnTop = !Note.AlwaysOnTop;
            Topmost = Note.AlwaysOnTop;
            PinButton.Content = Note.AlwaysOnTop ? "📍" : "📌";
            NoteStore.Instance.Save();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isEditMode)
            {
                SetEditMode(false);
                return;
            }

            // Hide the note (do not delete data)
            Hide();
            Note.Visible = false;
            NoteStore.Instance.Save();
        }

        private void DeleteNoteWithConfirmation()
        {
            var result = System.Windows.MessageBox.Show(
                $"确定要删除便笺「{Note.Title}」吗？\n此操作不可恢复。",
                "删除便笺",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                NoteStore.Instance.DeleteNote(Note.Id);
            }
        }

        #endregion

        #region Window Lifecycle

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _autoSaveTimer?.Dispose();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.Escape && _isEditMode)
            {
                SetEditMode(false);
                e.Handled = true;
            }
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);

            // Exit edit mode when clicking outside
            if (_isEditMode)
            {
                SetEditMode(false);
            }

            // Keep window in desktop layer (below normal windows)
            if (!Note.AlwaysOnTop)
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                // HWND_BOTTOM keeps notes below all normal windows (desktop layer)
                SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            if (!Note.AlwaysOnTop)
            {
                // When clicking a note, bring it above other notes but keep below normal windows
                // This is handled naturally by Windows - clicking brings it forward
            }
        }

        #endregion

        #region Update from external source (API)

        public void UpdateFromNote(Note updated)
        {
            Dispatcher.Invoke(() =>
            {
                // Cancel any pending auto-save to prevent overwriting API changes
                _autoSaveTimer?.Dispose();
                _autoSaveTimer = null;

                // Update the note reference and sync edit controls
                Note = updated;
                TitleEdit.Text = updated.Title;
                ContentEdit.Text = updated.Content;

                // Exit edit mode (will save the updated data, not stale edit content)
                if (_isEditMode) SetEditMode(false);

                ApplyNoteData();
            });
        }

        #endregion
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object parameter) => _execute(parameter);
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}
