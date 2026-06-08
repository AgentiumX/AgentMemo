using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;

namespace DesktopMemo.Services
{
    public static class AutoPositioner
    {
        [DllImport("user32.dll")]
        private static extern bool SystemParametersInfo(uint uAction, uint uParam, ref RECT lpvParam, uint fuWinIni);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        private const uint SPI_GETWORKAREA = 48;
        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;

        public static Point FindAvailablePosition(double noteWidth = 280, double noteHeight = 320)
        {
            var workArea = GetWorkArea();
            var existingNotes = NoteStore.Instance.GetAllNotes()
                .Where(n => n.Visible)
                .Select(n => new Rect(n.X, n.Y, n.Width, n.Height))
                .ToList();

            double padding = 20;

            // Strategy 1: Scan from top-right corner (desktop icons are typically top-left)
            double startX = workArea.Right - noteWidth - padding;
            double startY = workArea.Top + padding;

            // First try the right side of the screen
            for (double x = startX; x >= workArea.Left; x -= (noteWidth + padding))
            {
                for (double y = startY; y + noteHeight <= workArea.Bottom; y += (noteHeight + padding))
                {
                    var candidate = new Rect(x, y, noteWidth, noteHeight);
                    if (!existingNotes.Any(r => r.IntersectsWith(candidate)))
                    {
                        return new Point(x, y);
                    }
                }
            }

            // Strategy 2: Fine-grained scan from top-left
            for (double x = workArea.Left + padding; x + noteWidth <= workArea.Right; x += (noteWidth + padding))
            {
                for (double y = workArea.Top + padding; y + noteHeight <= workArea.Bottom; y += (noteHeight + padding))
                {
                    var candidate = new Rect(x, y, noteWidth, noteHeight);
                    if (!existingNotes.Any(r => r.IntersectsWith(candidate)))
                    {
                        return new Point(x, y);
                    }
                }
            }

            // Strategy 3: Offset from the last note
            if (existingNotes.Any())
            {
                var last = existingNotes.Last();
                double newX = last.Right + padding;
                double newY = last.Top;

                if (newX + noteWidth > workArea.Right)
                {
                    newX = workArea.Left + padding;
                    newY = last.Bottom + padding;
                }

                if (newY + noteHeight <= workArea.Bottom)
                    return new Point(newX, newY);
            }

            // Fallback: cascade from top-left
            return new Point(
                workArea.Left + padding + (existingNotes.Count * 30) % (int)(workArea.Width - noteWidth),
                workArea.Top + padding + (existingNotes.Count * 30) % (int)(workArea.Height - noteHeight));
        }

        private static Rect GetWorkArea()
        {
            var rect = new RECT();
            if (SystemParametersInfo(SPI_GETWORKAREA, 0, ref rect, 0))
            {
                return new Rect(rect.Left, rect.Top,
                    rect.Right - rect.Left, rect.Bottom - rect.Top);
            }

            // Fallback: primary screen
            var screen = Screen.PrimaryScreen.WorkingArea;
            return new Rect(screen.Left, screen.Top, screen.Width, screen.Height);
        }
    }
}
