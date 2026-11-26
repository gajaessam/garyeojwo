using System;
using System.Globalization;
using System.IO;

namespace BlindCover
{
    class Settings
    {
        public double WidgetX = double.NaN, WidgetY = double.NaN;
        public bool UseCustomCharacter;
        public bool SeenGuide;
        public int CordColor = 0;                      // 줄 색 번호 (0 = 분홍)
        public bool ShowHints = true;                  // 키링에 마우스를 올리면 사용법 말풍선
        public double KeyScale = 1, CordLength = 80;   // 화면 위 키링 크기, 줄 길이

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Garyeojwo"); }
        }
        // 예전 이름(블라인드)으로 쓰던 설정이 있으면 한 번만 옮겨 옴
        static string OldDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gaerimpan_Blind"); }
        }

        static void MigrateOld()
        {
            try
            {
                if (Directory.Exists(Dir) || !Directory.Exists(OldDir)) return;
                Directory.CreateDirectory(Dir);
                foreach (var name in new[] { "settings.txt", "keyring.png" })
                {
                    string src = Path.Combine(OldDir, name);
                    if (File.Exists(src)) File.Copy(src, Path.Combine(Dir, name));
                }
            }
            catch { }
        }

        // 오류 기록은 너무 커지지 않게 (200KB 넘으면 예전 기록은 error.old.log로 넘기고 새로 시작)
        public static void LogError(string text)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string log = Path.Combine(Dir, "error.log");
                if (File.Exists(log) && new FileInfo(log).Length > 200 * 1024)
                {
                    string old = Path.Combine(Dir, "error.old.log");
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(log, old);
                }
                File.AppendAllText(log, text);
            }
            catch { }
        }
        // 키링 캐릭터 그림
        public static string CharacterPath { get { return Path.Combine(Dir, "keyring.png"); } }
        static string FilePath { get { return Path.Combine(Dir, "settings.txt"); } }

        public static Settings Load()
        {
            var s = new Settings();
            MigrateOld();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    double d;
                    if (k == "widgetX" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.WidgetX = d;
                    else if (k == "widgetY" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.WidgetY = d;
                    else if (k == "customCharacter") s.UseCustomCharacter = v == "1";
                    else if (k == "seenGuide") s.SeenGuide = v == "1";
                    else if (k == "hints") s.ShowHints = v != "0";
                    else if (k == "cordColor") { int n; if (int.TryParse(v, out n)) s.CordColor = n; }
                    else if (k == "keyScale" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.KeyScale = d;
                    else if (k == "cordLength" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.CordLength = d;
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllLines(FilePath, new[]
                {
                    "widgetX=" + WidgetX.ToString(CultureInfo.InvariantCulture),
                    "widgetY=" + WidgetY.ToString(CultureInfo.InvariantCulture),
                    "customCharacter=" + (UseCustomCharacter ? "1" : "0"),
                    "seenGuide=" + (SeenGuide ? "1" : "0"),
                    "hints=" + (ShowHints ? "1" : "0"),
                    "cordColor=" + CordColor,
                    "keyScale=" + KeyScale.ToString(CultureInfo.InvariantCulture),
                    "cordLength=" + CordLength.ToString(CultureInfo.InvariantCulture),
                });
            }
            catch { }
        }
    }
}
