using System;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace BlindCover
{
    static class Program
    {
        public const string AppName = "가려줘";
        public const string Version = "1.0.0";
        public const string Credit = "제작: 가정재연쌤 (@gajae_ssam)";

        [STAThread]
        static void Main(string[] args)
        {
#if DEV
            // ── 개발용 기능 (build-dev.bat으로 만든 개발판에만 들어감) ──
            // 미리보기: --preview 저장경로 [내린비율 0~1] [hint]
            if (args.Length >= 2 && args[0] == "--preview")
            {
                var papp = new Application();
                var pctl = new Controller(papp);
                pctl.LoadCharacterForPreview(args.Length >= 5 && args[4] == "custom");
                double frac = 1;
                if (args.Length >= 3) double.TryParse(args[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out frac);
                new BlindWindow(pctl).SavePreview(args[1], new Rect(200, 300, 1000, 560), frac, 10, args.Length >= 4 && args[3] == "hint");
                return;
            }
            if (args.Length >= 2 && args[0] == "--icon")
            {
                var iapp = new Application();
                var ictl = new Controller(iapp);
                ictl.LoadCharacterForPreview(args.Length >= 3 && args[2] == "custom");
                BlindWidget.SavePreview(args[1], ictl.Keyring);
                return;
            }
            // --coverable 창핸들 기록파일  (그 창을 가릴 대상으로 보는지)
            if (args.Length >= 3 && args[0] == "--coverable")
            {
                System.IO.File.WriteAllText(args[2], Native.IsCoverable(new IntPtr(long.Parse(args[1]))).ToString());
                return;
            }
            // --fullscreen 창핸들 기록파일  (그 창을 전체 화면으로 보는지)
            if (args.Length >= 3 && args[0] == "--fullscreen")
            {
                System.IO.File.WriteAllText(args[2], Native.IsFullscreen(new IntPtr(long.Parse(args[1]))).ToString());
                return;
            }
            // 자동 점검: --selftest 창핸들 기록파일
            if (args.Length >= 3 && args[0] == "--selftest")
            {
                var sapp = new Application();
                sapp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var sctl = new Controller(sapp);
                sapp.Startup += delegate { sctl.SelfTest(new IntPtr(long.Parse(args[1])), args[2]); };
                sapp.Run();
                return;
            }
#endif
            bool isNew;
            var mutex = new Mutex(true, "Garyeojwo_SingleInstance", out isNew);
            if (!isNew)
            {
                MessageBox.Show("가려줘가 이미 실행 중이에요.\n화면 위에 매달린 키링을 찾아보세요!", Program.AppName);
                return;
            }
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var ctl = new Controller(app);
            app.Startup += delegate { ctl.Start(); };
            app.SessionEnding += delegate { ctl.Exit(); };
            // 예상 못 한 오류가 나도 꺼지지 않고 기록만 남김
            app.DispatcherUnhandledException += (s, e) =>
            {
                Settings.LogError(DateTime.Now + "\r\n" + e.Exception + "\r\n\r\n");
                e.Handled = true;
            };
            app.Run();
            GC.KeepAlive(mutex);
        }
    }

    class Controller
    {
        readonly Application app;
        Settings settings;
        BlindWidget widget;
        BlindWindow blind;
        readonly DispatcherTimer tracker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        IntPtr lastTarget, coverTarget;
        Native.RECT lastRect;
        bool exiting, ownedByTarget;

        public BitmapSource Keyring { get; private set; }
        public double DpiScale { get; private set; }
        public bool Covering { get { return coverTarget != IntPtr.Zero; } }

        public Controller(Application app)
        {
            this.app = app;
            DpiScale = 1.0;
        }

        public void Start()
        {
            settings = Settings.Load();
            Art.CordColor = settings.CordColor;
            if (settings.UseCustomCharacter) Keyring = CharacterImage.LoadSaved();

            widget = new BlindWidget(this);
            widget.SetCharacter(Keyring);
            widget.SetSize(settings.KeyScale, settings.CordLength);
            widget.SetHintsChecked(settings.ShowHints);
            widget.SetAutoStartChecked(AutoStart.Refresh());   // 프로그램을 옮겼으면 자동 실행 경로도 새 위치로
            widget.SetCordColor(settings.CordColor);
            widget.Left = -10000; widget.Top = -10000;
            widget.Show();
            var src = PresentationSource.FromVisual(widget);
            if (src != null && src.CompositionTarget != null) DpiScale = src.CompositionTarget.TransformToDevice.M11;

            // 줄은 바탕화면 맨 위에 매달려 있음 (가로 위치만 기억)
            double x = settings.WidgetX, y = settings.WidgetY;
            if (double.IsNaN(x) || double.IsNaN(y) || !OnAnyScreen(x, y))
            {
                var wa = SystemParameters.WorkArea;
                x = wa.Right - widget.Width - 160;
                y = wa.Top;
            }
            widget.Left = x; widget.Top = y;
            SnapWidgetToTop();

            blind = CreateBlind();
            tracker.Tick += delegate { Track(); };
            tracker.Start();
            CreateTray();

            if (!settings.SeenGuide)
            {
                ShowGuide();
                settings.SeenGuide = true;
                settings.Save();
            }
        }

        // 블라인드는 가린 창에 딸린 창이라, 가린 창이 갑자기 닫히면 윈도우가 블라인드 창까지 같이 없앨 수 있음
        // → 그럴 땐 조용히 새로 만들어 둠
        BlindWindow CreateBlind()
        {
            var b = new BlindWindow(this);
            b.Closed += delegate
            {
                if (exiting || blind != b) return;
                coverTarget = IntPtr.Zero;
                ownedByTarget = false;
                if (widget != null) widget.SetOn(false);
                app.Dispatcher.BeginInvoke(new Action(delegate { if (!exiting) blind = CreateBlind(); }));
            };
            b.WarmUp();
            return b;
        }

        // 블라인드를 가린 창에 딸린 창으로 연결 (안 되면 예전처럼 항상 위에)
#if DEV
        IntPtr forcedTarget;   // 자동 점검에서 가릴 창을 직접 정함
#endif
        void AttachTo(IntPtr target)
        {
            // 가린 창이 '항상 위' 창(시험용 브라우저 등)이면 블라인드도 항상 위여야 그 창 위에 올라감
            blind.Topmost = Native.IsTopmost(target);
            ownedByTarget = Native.SetOwner(blind, target);
            if (!ownedByTarget) blind.Topmost = true;
        }

        void Detach()
        {
            if (blind == null || !ownedByTarget) return;
            try { Native.SetOwner(blind, IntPtr.Zero); } catch { }
            ownedByTarget = false;
        }

        bool OnAnyScreen(double x, double y)
        {
            foreach (var s in WinForms.Screen.AllScreens)
            {
                var b = s.WorkingArea;
                var r = new Rect(b.X / DpiScale, b.Y / DpiScale, b.Width / DpiScale, b.Height / DpiScale);
                if (r.Contains(new Point(x + 10, y + 10))) return true;
            }
            return false;
        }

        public void ShowGuide()
        {
            MessageBox.Show(
                Program.AppName + " 사용법\n\n" +
                "🐻 키링을\n" +
                "   클릭  →  다 가려요\n" +
                "   아래로 당기기  →  내려와요\n" +
                "   위로 올리기  →  올라가요\n\n" +
                "가릴 창을 먼저 클릭하세요.\n" +
                "키링을 옆으로 드래그해서 옮길 수 있어요.\n" +
                "우클릭하면 메뉴가 나와요.\n\n" +
                "버전 " + Program.Version + " · " + Program.Credit,
                Program.AppName + "  " + Program.Version, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        Layout MakeLayout(IntPtr h, out Native.RECT r)
        {
            if (!Native.GetBounds(h, out r)) return null;
            double s = DpiScale;
            var target = new Rect(r.Left / s, r.Top / s, Math.Max(40, (r.Right - r.Left) / s), Math.Max(40, (r.Bottom - r.Top) / s));
            var wa = WinForms.Screen.FromHandle(h).WorkingArea;
            var work = new Rect(wa.X / s, wa.Y / s, wa.Width / s, wa.Height / s);
            return new Layout(target, work);
        }

        static bool SameRect(Native.RECT a, Native.RECT b)
        {
            return a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
        }

        void Track()
        {
            TrackCover();
            UpdateWidgetAway();
        }

        // 블라인드 쪽 키링이 화면에 실제로 보이면 화면 위 키링은 블라인드로 옮겨 간 것처럼 숨고,
        // 가린 창이 최소화되거나 다른 창 뒤로 가서 안 보이면 다시 나타남
        void UpdateWidgetAway()
        {
            if (widget == null) return;
            bool away = false;
            Point k;
            if (Covering && blind != null && blind.KeyCenterScreen(out k))
            {
                IntPtr top = Native.WindowAt((int)Math.Round(k.X * DpiScale), (int)Math.Round(k.Y * DpiScale));
                away = top != IntPtr.Zero && top == Native.Handle(blind);
            }
            // 전체 화면 프로그램(PPT 슬라이드쇼, 전체 화면 영상 등)이 앞에 있으면 키링도 숨김.
            // 단, 마우스를 키링 자리(화면 위쪽)에 가져가면 다시 나타나서 쓸 수 있음
            if (!away && FullscreenOnWidgetScreen() && !MouseNearWidget()) away = true;
            widget.SetAway(away);
        }

        bool FullscreenOnWidgetScreen()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (!Native.IsCoverable(fg)) return false;
            fg = Native.Root(fg);
            return Native.IsFullscreen(fg) && Native.SameMonitor(fg, Native.Handle(widget));
        }

        bool MouseNearWidget()
        {
            Point m = Native.CursorPos();
            double x = m.X / DpiScale, y = m.Y / DpiScale;
            return x >= widget.Left - 10 && x <= widget.Left + widget.Width + 10 && y >= widget.Top - 10 && y <= widget.Top + widget.HangBottom + 20;
        }

        // ── 작업 표시줄 오른쪽 아이콘 (키링을 잃어버렸을 때 되찾기 / 사용법 / 종료) ──
        WinForms.NotifyIcon tray;

        void CreateTray()
        {
            try
            {
                var menu = new WinForms.ContextMenuStrip();
                var title = new WinForms.ToolStripMenuItem(Program.AppName + "  버전 " + Program.Version) { Enabled = false };
                menu.Items.Add(title);
                menu.Items.Add(new WinForms.ToolStripSeparator());
                menu.Items.Add("키링 위치 처음으로", null, delegate { ResetWidgetPosition(); });
                menu.Items.Add("사용법", null, delegate { ShowGuide(); });
                menu.Items.Add(new WinForms.ToolStripSeparator());
                menu.Items.Add("종료", null, delegate { Exit(); });
                tray = new WinForms.NotifyIcon
                {
                    Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location),
                    Text = Program.AppName + " (더블클릭: 키링 위치 처음으로)",
                    ContextMenuStrip = menu,
                    Visible = true
                };
                tray.DoubleClick += delegate { ResetWidgetPosition(); };
            }
            catch { tray = null; }
        }

        // 키링을 주 모니터 위쪽 오른편, 처음 자리로 되돌림
        public void ResetWidgetPosition()
        {
            var wa = SystemParameters.WorkArea;
            widget.Left = wa.Right - widget.Width - 160;
            widget.Top = wa.Top;
            SnapWidgetToTop();
            widget.SetAway(false);
            widget.Wiggle();
            settings.WidgetX = widget.Left; settings.WidgetY = widget.Top;
            settings.Save();
        }

        public void SetCordColor(int index)
        {
            Art.CordColor = index;
            settings.CordColor = index;
            settings.Save();
            widget.SetCordColor(index);
            if (blind != null) blind.RefreshCord();
        }

        public void SetAutoStart(bool on)
        {
            bool ok = AutoStart.Set(on);
            widget.SetAutoStartChecked(AutoStart.IsOn());
            if (!ok) MessageBox.Show("자동 실행 설정을 바꾸지 못했어요.\n회사나 학교 관리 정책으로 막혀 있을 수 있어요.", Program.AppName);
        }

        public bool HintsOn { get { return settings == null || settings.ShowHints; } }

        public void SetHints(bool on)
        {
            settings.ShowHints = on;
            settings.Save();
            widget.SetHintsChecked(on);
            if (!on && blind != null) blind.HideHint();
        }

        public void ShowMenu(UIElement target) { if (widget != null) widget.ShowMenuFor(target); }

        // 방금 쓰던 창을 기억하고, 가린 창이 움직이면 블라인드도 따라가게
        void TrackCover()
        {
            if (exiting) return;
            IntPtr fg = Native.GetForegroundWindow();
            if (Native.IsCoverable(fg)) fg = Native.Root(fg); else fg = IntPtr.Zero;

            if (!Covering)
            {
                if (fg != IntPtr.Zero) lastTarget = fg;
                return;
            }

            if (!Native.IsWindow(coverTarget) || !Native.IsWindowVisible(coverTarget))
            {
                blind.HideNow();
                EndCover();
                return;
            }
            if (Native.IsIconic(coverTarget))
            {
                if (blind.IsVisible) blind.Visibility = Visibility.Hidden;
                return;
            }
            if (blind.Rolling) return;
            Native.RECT r;
            var layout = MakeLayout(coverTarget, out r);
            if (layout == null) return;
            if (blind.Visibility != Visibility.Visible) blind.Visibility = Visibility.Visible;
            if (ownedByTarget)
            {
                bool tt = Native.IsTopmost(coverTarget);         // 가린 창이 도중에 항상 위로 바뀌어도 따라감
                if (blind.Topmost != tt) blind.Topmost = tt;
                Native.PlaceAbove(blind, coverTarget);           // 가린 창 바로 위 자리 지키기
            }
            Native.BringToTop(widget);   // 화면 위 줄은 항상 블라인드보다 위에
            if (!SameRect(r, lastRect))
            {
                lastRect = r;
                blind.Track(layout);
            }
        }

        public void Toggle()
        {
            if (Covering && !blind.Rolling) Uncover(); else Cover();
        }

        public void Cover() { Cover(true); }

        // drop: 끝까지 쭉 내림 / 아니면 위 레일만 걸고 시작 (줄을 당긴 만큼 내려옴)
        bool Cover(bool drop)
        {
            if (exiting) return false;
            if (Covering && !blind.Rolling) return true;
            IntPtr target = coverTarget;
#if DEV
            if (target == IntPtr.Zero) target = forcedTarget;
#endif
            if (target == IntPtr.Zero)
            {
                IntPtr fg = Native.GetForegroundWindow();
                target = Native.IsCoverable(fg) ? Native.Root(fg) : lastTarget;
            }
            if (target == IntPtr.Zero || !Native.IsWindow(target) || !Native.IsWindowVisible(target) || Native.IsIconic(target))
            {
                widget.NoTarget();   // 키링이 고개를 절레절레 + 말풍선으로 안내
                return false;
            }
            Native.RECT r;
            var layout = MakeLayout(target, out r);
            if (layout == null) return false;
            IntPtr fgBefore = Native.GetForegroundWindow();
            if (coverTarget != target) AttachTo(target);
            coverTarget = target;
            lastRect = r;
            blind.Cover(layout, drop);
            if (ownedByTarget) Native.PlaceAbove(blind, target);
            Native.GiveFocusBack(fgBefore);   // 블라인드를 붙이는 과정에서 포커스가 넘어왔으면 시험 창으로 되돌림
            widget.SetOn(true);
            Native.BringToTop(widget);
            return true;
        }

        // ── 화면 위 줄을 당길 때 ──
        public bool PullStart(bool down)
        {
            if (exiting) return false;
            if (!Covering)
            {
                if (!down) return false;            // 블라인드가 없을 땐 아래로 당길 때만 나타남
                if (!Cover(false)) return false;
            }
            widget.SetOn(true);
            blind.BeginPull();
            return true;
        }

        // 키링을 톡: 블라인드가 없으면 나타나서, 있으면 지금 높이에서 끝까지 쭉 내려옴
        public void CoverAll()
        {
            if (exiting) return;
            if (!Covering || blind.Rolling) { Cover(true); return; }
            widget.SetOn(true);
            blind.DropAll();
        }

        public void PullMove(double frac) { if (Covering) blind.Pull(frac); }
        public void PullEnd() { if (Covering) blind.EndPull(); }

        public void SetWidgetSize(double keyScale, double cordLength)
        {
            widget.SetSize(keyScale, cordLength);
            SnapWidgetToTop();
            settings.KeyScale = keyScale; settings.CordLength = cordLength;
            settings.WidgetX = widget.Left; settings.WidgetY = widget.Top;
            settings.Save();
        }

        public void Uncover()
        {
            if (!Covering) return;
            widget.SetOn(false);
            widget.SetAway(false);   // 걷기 시작하면 키링이 바로 화면 위로 돌아옴
            blind.RollUp(EndCover);
        }

        void EndCover()
        {
            coverTarget = IntPtr.Zero;
            Detach();
            if (widget != null) widget.SetOn(false);
        }

        // 줄을 옆으로 옮긴 뒤: 그 모니터의 맨 위에 다시 매달고 위치 기억
        public void WidgetMoved()
        {
            SnapWidgetToTop();
            settings.WidgetX = widget.Left; settings.WidgetY = widget.Top;
            settings.Save();
        }

        void SnapWidgetToTop()
        {
            double s = DpiScale;
            var center = new System.Drawing.Point((int)((widget.Left + widget.Width / 2) * s), (int)((widget.Top + 20) * s));
            var b = WinForms.Screen.FromPoint(center).WorkingArea;
            double left = b.X / s, top = b.Y / s, right = (b.X + b.Width) / s;
            widget.Left = Math.Max(left - widget.Width * 0.3, Math.Min(right - widget.Width * 0.7, widget.Left));
            widget.Top = top;
        }

        public void SetCustomCharacter(string path)
        {
            try
            {
                var bmp = CharacterImage.Process(path);
                CharacterImage.Save(bmp);
                Keyring = bmp;
                widget.SetCharacter(bmp);
                settings.UseCustomCharacter = true;
                settings.Save();
                MessageBox.Show("키링을 내 캐릭터로 바꿨어요!\n다음에 블라인드를 내릴 때부터 큰 블라인드에도 달려요.", Program.AppName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("그림을 불러오지 못했어요.\n" + ex.Message, Program.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public void LoadCharacterForPreview(bool custom)
        {
            Keyring = custom ? CharacterImage.LoadSaved() : null;
        }

        public void ResetCharacter()
        {
            Keyring = null;
            widget.SetCharacter(null);
            settings.UseCustomCharacter = false;
            settings.Save();
        }

#if DEV
        // ── 개발용 자동 점검 ──
        public void SelfTest(IntPtr target, string logPath)
        {
            var log = new System.Text.StringBuilder();
            Action<string> W = s => log.AppendLine(s);
            settings = new Settings { SeenGuide = true };
            widget = new BlindWidget(this);
            widget.Left = -10000; widget.Top = -10000;
            widget.Show();
            var src = PresentationSource.FromVisual(widget);
            if (src != null && src.CompositionTarget != null) DpiScale = src.CompositionTarget.TransformToDevice.M11;
            blind = CreateBlind();
            lastTarget = target;
            W("dpi=" + DpiScale);
            int step = 0;
            BlindWindow first = null;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            t.Tick += delegate
            {
                try
                {
                    step++;
                    if (step == 1)
                    {
                        Native.RECT r; Native.GetBounds(target, out r);
                        W("target=" + r.Left + "," + r.Top + "," + r.Right + "," + r.Bottom);
                        Native.RECT lr; var lay = MakeLayout(target, out lr);
                        W("layout=" + lay.WindowRect);
                        forcedTarget = target;
                        W("fgBefore=" + Native.Describe(Native.GetForegroundWindow()) + " blind=" + Native.Handle(blind) + " widget=" + Native.Handle(widget));
                        bool ok = Cover(true);
                        W("fgAfterCover=" + Native.Describe(Native.GetForegroundWindow()));
                        first = blind;
                        W("cover=" + ok + " owned=" + ownedByTarget + " topmost=" + blind.Topmost);
                        W("blindDip(after cover)=" + blind.Left + "," + blind.Top + "," + blind.Width + "," + blind.Height);
                        Native.RECT wr; Native.GetWindowRectPublic(new System.Windows.Interop.WindowInteropHelper(blind).Handle, out wr);
                        W("blindWin32=" + wr.Left + "," + wr.Top + "," + wr.Right + "," + wr.Bottom);
                    }
                    else if (step == 3)
                    {
                        Track();
                        var h = new System.Windows.Interop.WindowInteropHelper(blind).Handle;
                        W("blindPx=" + Math.Round(blind.Left * DpiScale) + "," + Math.Round(blind.Top * DpiScale) + "," + Math.Round((blind.Left + blind.Width) * DpiScale) + "," + Math.Round((blind.Top + blind.Height) * DpiScale));
                        UpdateWidgetAway();
                        W("widgetAway(blind visible)=" + widget.Away);
                        Point kc; blind.KeyCenterScreen(out kc);
                        W("keyPt=" + kc + " windowThere=" + Native.Describe(Native.WindowAt((int)kc.X, (int)kc.Y)) + " fg=" + Native.Describe(Native.GetForegroundWindow()));
                        W("justAboveTarget=" + Native.IsJustAbove(h, target) + " visible=" + blind.IsVisible + " prev=" + Native.Describe(Native.Prev(target)));
                        Native.RECT wr2; Native.GetWindowRectPublic(h, out wr2);
                        W("blindWin32=" + wr2.Left + "," + wr2.Top + "," + wr2.Right + "," + wr2.Bottom);
                    }
                    else if (step == 4)
                    {
                        Native.Close(target);   // 가린 창이 갑자기 닫히면?
                    }
                    else if (step == 6)
                    {
                        Track();
                        W("targetAlive=" + Native.IsWindow(target));
                        W("widgetAway(after close)=" + widget.Away);
                        W("afterClose covering=" + Covering + " recreated=" + (blind != first) + " blindAlive=" + (blind != null));
                        W("done");
                        System.IO.File.WriteAllText(logPath, log.ToString());
                        t.Stop();
                        Exit();
                    }
                }
                catch (Exception ex)
                {
                    W("ERROR " + ex);
                    System.IO.File.WriteAllText(logPath, log.ToString());
                    t.Stop();
                    Exit();
                }
            };
            t.Start();
        }

#endif

        public void Exit()
        {
            if (exiting) return;
            exiting = true;
            tracker.Stop();
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            if (blind != null) blind.Close();
            if (widget != null) widget.Close();
            app.Shutdown();
        }
    }
}
