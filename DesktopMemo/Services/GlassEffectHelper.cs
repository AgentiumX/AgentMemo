using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DesktopMemo.Services
{
    public static class GlassEffectHelper
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DWM_BLURBEHIND pBlurBehind);

        [DllImport("dwmapi.dll")]
        private static extern int DwmIsCompositionEnabled(out bool enabled);

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [StructLayout(LayoutKind.Sequential)]
        private struct DWM_BLURBEHIND
        {
            public uint dwFlags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fEnable;
            public IntPtr hRgnBlur;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fTransitionOnMaximized;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public AccentState AccentState;
            public uint AccentFlags;
            public uint GradientColor;
            public uint AnimationId;
        }

        private enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }

        private enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
            ACCENT_ENABLE_HOSTBACKDROP = 5,
            ACCENT_INVALID_STATE = 6
        }

        private const uint DWM_BB_ENABLE = 1;

        public static void ApplyGlassEffect(Window window)
        {
            try
            {
                var osVersion = Environment.OSVersion.Version;
                var hwnd = new WindowInteropHelper(window).Handle;

                if (osVersion.Major >= 10 && osVersion.Build >= 17763)
                {
                    // Windows 10 1809+ / Windows 11: Try Acrylic
                    if (!TryApplyAcrylic(hwnd))
                    {
                        // Fallback to DWM blur
                        TryApplyDwmBlur(hwnd);
                    }
                }
                else if (osVersion.Major >= 10 && osVersion.Build >= 10240)
                {
                    // Windows 10 (early): DWM blur
                    TryApplyDwmBlur(hwnd);
                }
                else
                {
                    // Windows 7/8: DWM blur
                    TryApplyDwmBlur(hwnd);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Glass effect error: {ex.Message}");
                ApplyFallbackEffect(window);
            }
        }

        private static bool TryApplyAcrylic(IntPtr hwnd)
        {
            try
            {
                var accent = new AccentPolicy
                {
                    AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                    AccentFlags = 2,
                    // AABBGGRR format: semi-transparent white tint
                    GradientColor = 0xCCFFFFFF
                };

                var accentPtr = Marshal.AllocHGlobal(Marshal.SizeOf(accent));
                Marshal.StructureToPtr(accent, accentPtr, false);

                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    Data = accentPtr,
                    SizeOfData = Marshal.SizeOf(accent)
                };

                var result = SetWindowCompositionAttribute(hwnd, ref data);
                Marshal.FreeHGlobal(accentPtr);
                return result != 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryApplyDwmBlur(IntPtr hwnd)
        {
            try
            {
                bool dwmEnabled;
                DwmIsCompositionEnabled(out dwmEnabled);
                if (!dwmEnabled) return false;

                var blurBehind = new DWM_BLURBEHIND
                {
                    dwFlags = DWM_BB_ENABLE,
                    fEnable = true,
                    hRgnBlur = IntPtr.Zero,
                    fTransitionOnMaximized = false
                };

                return DwmEnableBlurBehindWindow(hwnd, ref blurBehind) == 0;
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyFallbackEffect(Window window)
        {
            // Fallback: semi-transparent background
            window.Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
        }
    }
}
