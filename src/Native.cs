using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace BlindCover
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT r, int size);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr v);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr v);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);

        const int GWLP_HWNDPARENT = -8;
        const uint GW_OWNER = 4, GW_HWNDPREV = 3;

        // 블라인드를 가린 창에 딸린 창으로 만듦 → 가린 창 바로 위에만 붙어 있고,
        // 다른 창을 앞으로 가져오면 가린 창과 함께 그 뒤로 감. 성공하면 true
        public static bool SetOwner(Window w, IntPtr owner)
        {
            IntPtr h = new WindowInteropHelper(w).EnsureHandle();
            if (IntPtr.Size == 8) SetWindowLongPtr64(h, GWLP_HWNDPARENT, owner);
            else SetWindowLong32(h, GWLP_HWNDPARENT, owner);
            return GetWindow(h, GW_OWNER) == owner;
        }

        // 가린 창 바로 위에 놓기 (포커스는 빼앗지 않음)
        public static void PlaceAbove(Window w, IntPtr target)
        {
            IntPtr h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            IntPtr above = GetWindow(target, GW_HWNDPREV);   // 가린 창 바로 위에 있는 창
            if (above == h) return;                            // 이미 제자리
            // above 아래 = 가린 창 바로 위. 가린 창이 맨 위면 맨 위(항상 위 창들은 빼고)로.
            // 바로 위가 '항상 위' 창이면 그 밑에 끼우는 순간 블라인드도 항상 위가 돼 버리니 맨 위로만
            bool aboveTopmost = above != IntPtr.Zero && (GetWindowLong(above, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
            bool meTopmost = (GetWindowLong(h, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
            IntPtr after = above == IntPtr.Zero || (aboveTopmost && !meTopmost) ? IntPtr.Zero : above;
            SetWindowPos(h, after, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        // 창을 '항상 위' 창들 중에서도 맨 앞으로 (포커스는 빼앗지 않음)
        public static void BringToTop(Window w)
        {
            IntPtr h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // HWND_TOPMOST, NOSIZE|NOMOVE|NOACTIVATE
        }

        const int GWL_EXSTYLE = -20;
        const int WS_EX_TOOLWINDOW = 0x80;
        const int WS_EX_NOACTIVATE = 0x08000000;
        const int WS_EX_TOPMOST = 0x8;
        const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        static readonly uint myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        // Alt+Tab 목록에 안 나오고, 눌러도 다른 창의 포커스를 빼앗지 않게
        public static void MakeToolWindow(Window w, bool noActivate)
        {
            IntPtr h = new WindowInteropHelper(w).Handle;
            int ex = GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TOOLWINDOW;
            if (noActivate) ex |= WS_EX_NOACTIVATE;
            SetWindowLong(h, GWL_EXSTYLE, ex);
        }

        // 가릴 만한 일반 창인지 (내 창, 바탕화면, 작업표시줄 제외)
        public static bool IsCoverable(IntPtr h)
        {
            if (h == IntPtr.Zero || !IsWindow(h) || !IsWindowVisible(h)) return false;
            h = GetAncestor(h, 2); // GA_ROOT
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid == myPid) return false;
            var sb = new StringBuilder(64);
            GetClassName(h, sb, 64);
            string c = sb.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd"
                || c == "Windows.UI.Core.CoreWindow" || c == "NotifyIconOverflowWindow") return false;
            // 화면에 안 보이는 창은 제외:
            //  - 크기가 0이거나 아주 작은 창 (예: 옛날 프로그램이 숨겨 두는 본체 창. 컴시간알리미 등)
            //  - 윈도우가 숨겨 둔 앱 창 (DWM cloaked)
            RECT r;
            if (!GetWindowRect(h, out r) || r.Right - r.Left < MinCoverSize || r.Bottom - r.Top < MinCoverSize) return false;
            int cloaked;
            if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
            return true;
        }

        const int MinCoverSize = 100;   // 이보다 작은 창은 가릴 대상이 아님 (픽셀)
        const int DWMWA_CLOAKED = 14;
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int v, int size);

        public static IntPtr Root(IntPtr h) { return GetAncestor(h, 2); }

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        // 그 화면 위치에서 실제로 보이는(맨 위) 창
        public static IntPtr WindowAt(int x, int y)
        {
            IntPtr h = WindowFromPoint(new POINT { X = x, Y = y });
            return h == IntPtr.Zero ? h : GetAncestor(h, 2);
        }
        public static IntPtr Handle(Window w) { return new WindowInteropHelper(w).Handle; }
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        public static bool IsMine(IntPtr h)
        {
            if (h == IntPtr.Zero) return false;
            uint pid; GetWindowThreadProcessId(h, out pid);
            return pid == myPid;
        }
        // 포커스가 우리 창으로 넘어왔으면 원래 쓰던 창으로 돌려줌
        public static void GiveFocusBack(IntPtr original)
        {
            if (original == IntPtr.Zero || !IsWindow(original) || IsMine(original)) return;
            if (IsMine(GetForegroundWindow())) SetForegroundWindow(original);
        }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);

        // 창이 모니터 화면 전체를 덮고 있는지 (작업 표시줄까지 가린 전체 화면)
        public static bool IsFullscreen(IntPtr h)
        {
            RECT r;
            if (!GetWindowRect(h, out r)) return false;
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (!GetMonitorInfo(MonitorFromWindow(h, 2), ref mi)) return false;
            var m = mi.rcMonitor;
            return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
        }

        public static bool SameMonitor(IntPtr a, IntPtr b) { return MonitorFromWindow(a, 2) == MonitorFromWindow(b, 2); }

        public static Point CursorPos()
        {
            POINT p;
            GetCursorPos(out p);
            return new Point(p.X, p.Y);
        }

        public static bool IsTopmost(IntPtr h) { return (GetWindowLong(h, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0; }

#if DEV
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
        // 개발용 점검 도우미 (개발판에만)
        public static void Close(IntPtr h) { PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); }
        public static bool IsJustAbove(IntPtr me, IntPtr target) { return GetWindow(target, GW_HWNDPREV) == me; }
        public static IntPtr Prev(IntPtr h) { return GetWindow(h, GW_HWNDPREV); }
        public static bool GetWindowRectPublic(IntPtr h, out RECT r) { return GetWindowRect(h, out r); }
        public static string Describe(IntPtr h)
        {
            var sb = new StringBuilder(64); GetClassName(h, sb, 64);
            uint pid; GetWindowThreadProcessId(h, out pid);
            return h + ":" + sb + ":pid" + pid + (pid == myPid ? "(me)" : "");
        }
#endif

        // 창의 실제 보이는 테두리 (이 프로그램 기준 좌표).
        // 화면 확대 비율이 다른 모니터에 있는 창도 위치가 어긋나지 않게:
        //  - GetWindowRect는 이 프로그램 기준으로 맞춰진 값(확대 비율 보정됨)을 주지만 보이지 않는 테두리까지 포함
        //  - DWM 테두리는 보이는 부분만 정확히 주지만 실제 화면 픽셀 기준
        //  → 실제 픽셀 기준의 창 전체 크기를 한 번 더 구해서, 보이지 않는 테두리 두께만 비율대로 빼 줌
        public static bool GetBounds(IntPtr h, out RECT r)
        {
            RECT log;
            if (!GetWindowRect(h, out log)) { r = log; return false; }
            RECT dwm;
            if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out dwm, Marshal.SizeOf(typeof(RECT))) != 0) { r = log; return true; }
            RECT phys;
            if (!PhysicalWindowRect(h, out phys)) { r = dwm; return true; }   // 옛날 윈도우: 예전 방식 그대로
            double sx = (log.Right - log.Left) / (double)Math.Max(1, phys.Right - phys.Left);
            double sy = (log.Bottom - log.Top) / (double)Math.Max(1, phys.Bottom - phys.Top);
            r.Left = log.Left + (int)Math.Round((dwm.Left - phys.Left) * sx);
            r.Top = log.Top + (int)Math.Round((dwm.Top - phys.Top) * sy);
            r.Right = log.Right - (int)Math.Round((phys.Right - dwm.Right) * sx);
            r.Bottom = log.Bottom - (int)Math.Round((phys.Bottom - dwm.Bottom) * sy);
            return true;
        }

        // 확대 비율 보정 없이 실제 화면 픽셀 기준의 창 크기 (윈도우 10 1607 이상)
        static bool PhysicalWindowRect(IntPtr h, out RECT r)
        {
            r = new RECT();
            IntPtr old;
            try { old = SetThreadDpiAwarenessContext(new IntPtr(-4)); }   // 모니터별 확대 비율 인식 (V2)
            catch (EntryPointNotFoundException) { return false; }
            if (old == IntPtr.Zero) return false;
            try { return GetWindowRect(h, out r); }
            finally { SetThreadDpiAwarenessContext(old); }
        }
    }
}
