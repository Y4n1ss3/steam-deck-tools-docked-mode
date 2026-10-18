using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ExternalHelpers
{
    public static class CursorHider
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetSystemCursor(IntPtr hcur, uint id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateCursor(IntPtr hInst, int xHotSpot, int yHotSpot, 
            int nWidth, int nHeight, byte[] pvANDPlane, byte[] pvXORPlane);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyCursor(IntPtr hCursor);

        [DllImport("user32.dll", EntryPoint = "CopyImage", SetLastError = true)]
        private static extern IntPtr CopyImage(IntPtr handle, uint un1, int n1, int n2, uint un2);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private const uint SPI_SETCURSORS = 0x0057;
        private const uint SPIF_SENDWININICHANGE = 0x02;
        private const uint IMAGE_CURSOR = 2;

        private static readonly uint[] SystemCursorIDs = new uint[]
        {
            32512, // OCR_NORMAL (Arrow)
            32513, // OCR_IBEAM (Text edit)
            32514, // OCR_WAIT (Wait/Hourglass)
            32515, // OCR_CROSS (Crosshair)
            32516, // OCR_UPARROW (Up arrow)
            32642, // OCR_SIZENWSE (Resize NW-SE)
            32643, // OCR_SIZENESW (Resize NE-SW)
            32644, // OCR_SIZEWE (Resize WE)
            32645, // OCR_SIZENS (Resize NS)
            32646, // OCR_SIZEALL (Move)
            32648, // OCR_NO (Slashed circle)
            32649, // OCR_HAND (Link / Hand)
            32650, // OCR_APPSTARTING (Working in background)
            32651  // OCR_HELP (Help)
        };

        private static readonly object _lock = new object();
        private static bool _isHidden = false;
        private static POINT _savedPoint;
        private static bool _hasSavedPoint = false;

        static CursorHider()
        {
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Restore();
        }

        public static bool IsHidden
        {
            get
            {
                lock (_lock)
                {
                    return _isHidden;
                }
            }
        }

        public static void Hide()
        {
            lock (_lock)
            {
                if (_isHidden)
                    return;

                try
                {
                    if (GetCursorPos(out POINT pt))
                    {
                        _savedPoint = pt;
                        _hasSavedPoint = true;
                    }

                    // 32x32 transparent monochrome cursor:
                    // AND plane = all 0xFF, XOR plane = all 0x00
                    byte[] andMask = new byte[128];
                    for (int i = 0; i < andMask.Length; i++)
                        andMask[i] = 0xFF;

                    byte[] xorMask = new byte[128];

                    IntPtr blankCursor = CreateCursor(IntPtr.Zero, 0, 0, 32, 32, andMask, xorMask);
                    if (blankCursor != IntPtr.Zero)
                    {
                        foreach (var id in SystemCursorIDs)
                        {
                            IntPtr copy = CopyImage(blankCursor, IMAGE_CURSOR, 0, 0, 0);
                            if (copy != IntPtr.Zero)
                            {
                                SetSystemCursor(copy, id);
                            }
                        }
                        DestroyCursor(blankCursor);
                    }

                    // Move cursor to bottom-right corner to avoid lingering hover events
                    SetCursorPos(32767, 32767);
                    _isHidden = true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"CursorHider.Hide error: {ex.Message}");
                }
            }
        }

        public static void Show()
        {
            lock (_lock)
            {
                if (!_isHidden)
                    return;

                try
                {
                    SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_SENDWININICHANGE);

                    if (_hasSavedPoint)
                    {
                        SetCursorPos(_savedPoint.X, _savedPoint.Y);
                        _hasSavedPoint = false;
                    }

                    _isHidden = false;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"CursorHider.Show error: {ex.Message}");
                }
            }
        }

        public static void Restore()
        {
            lock (_lock)
            {
                if (!_isHidden)
                    return;

                try
                {
                    SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_SENDWININICHANGE);
                    if (_hasSavedPoint)
                    {
                        SetCursorPos(_savedPoint.X, _savedPoint.Y);
                        _hasSavedPoint = false;
                    }
                }
                catch { }
                finally
                {
                    _isHidden = false;
                }
            }
        }
    }
}
