using System;
using Microsoft.Win32;

namespace BlindCover
{
    // 컴퓨터 켤 때 자동 실행 (지금 로그인한 사용자 것만. 관리자 권한 필요 없음)
    // 윈도우의 "시작프로그램" 목록(HKCU\...\Run)에 이 프로그램 경로를 올리거나 지움
    static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "Garyeojwo";

        static string ExePath { get { return "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\""; } }

        public static bool IsOn()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(ValueName) != null;
            }
            catch { return false; }
        }

        public static bool Set(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(ValueName, ExePath);
                    else if (k.GetValue(ValueName) != null) k.DeleteValue(ValueName);
                }
                return true;
            }
            catch { return false; }
        }

        // 켜져 있는데 프로그램을 다른 곳으로 옮겼으면 새 위치로 고쳐 둠. 켜져 있는지 돌려줌
        public static bool Refresh()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return false;
                    var v = k.GetValue(ValueName) as string;
                    if (v == null) return false;
                    if (!string.Equals(v, ExePath, StringComparison.OrdinalIgnoreCase)) k.SetValue(ValueName, ExePath);
                    return true;
                }
            }
            catch { return IsOn(); }
        }
    }
}