using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace BlindCover
{
    // 바탕화면 맨 위에 매달린 블라인드 줄 + 키링.
    // 아래로 당긴 만큼 방금 쓰던 창에 블라인드가 내려오고, 위로 올린 만큼 올라감. 옆으로 끌면 옮겨짐
    class BlindWidget : Window
    {
        public static readonly double[] KeyScales = { 0.8, 1.0, 1.3, 1.7, 2.2, 2.8 };
        public static readonly string[] KeyScaleNames = { "작게", "보통", "크게", "아주 크게", "더 크게", "왕 크게" };
        public static readonly double[] CordLengths = { 45, 80, 130, 190 };
        public static readonly string[] CordLengthNames = { "짧게", "보통", "길게", "아주 길게" };

        const double AY = 7, BaseKey = 36, PullRoom = 150;
        const double MaxSwing = 40 * Math.PI / 180;   // 줄이 기울 수 있는 최대 각도
        double keyS = BaseKey, rest = 80, ww = 180, wh = 300, ax = 90;

        readonly Controller ctl;
        readonly Canvas art = new Canvas { Background = Brushes.Transparent };
        Line cordHit, cordOuter, cordInner;
        Keyring key;
        HintBubble bubble;
        bool bubbleShown;
        BitmapSource character;
        readonly MenuItem sizeMenu = new MenuItem { Header = "키링 크기" };
        readonly MenuItem cordMenu = new MenuItem { Header = "줄 길이" };
        readonly MenuItem hintItem = new MenuItem { Header = "말풍선 안내", IsChecked = true };
        public void SetHintsChecked(bool on) { hintItem.IsChecked = on; if (!on) ShowBubble(false); }
        readonly MenuItem startupItem = new MenuItem { Header = "컴퓨터 켤 때 자동 실행" };
        public void SetAutoStartChecked(bool on) { startupItem.IsChecked = on; }
        readonly MenuItem colorMenu = new MenuItem { Header = "줄 색" };
        readonly MenuItem resetItem = new MenuItem { Header = "기본 키링으로 되돌리기", Visibility = Visibility.Collapsed };

        readonly Spring kx = new Spring(90, 80, 0.12), ky = new Spring(87, 340, 0.3);
        bool on, rendering;
        TimeSpan lastTime;

        enum Drag { None, Cord, Slide }
        Drag drag;
        Point downPt, grab;
        double slideOffset, startKy;
        bool moved, pulling;
        double lastMoveX, lastMoveY;

        public BlindWidget(Controller ctl)
        {
            this.ctl = ctl;
            Title = Program.AppName;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;   // 떠도 지금 쓰던 창의 포커스를 가져가지 않게
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Content = art;
            AllowDrop = true;

            // 우클릭 메뉴: 꾸미기 ▸ / 말풍선 안내 / 사용법 / 종료
            var menu = new ContextMenu();
            for (int i = 0; i < KeyScales.Length; i++)
            {
                double v = KeyScales[i];
                sizeMenu.Items.Add(Item(KeyScaleNames[i], delegate { ctl.SetWidgetSize(v, rest); }));
            }
            for (int i = 0; i < CordLengths.Length; i++)
            {
                double v = CordLengths[i];
                cordMenu.Items.Add(Item(CordLengthNames[i], delegate { ctl.SetWidgetSize(keyS / BaseKey, v); }));
            }
            resetItem.Click += delegate { ctl.ResetCharacter(); };
            var decorate = new MenuItem { Header = "꾸미기" };
            for (int i = 0; i < Art.CordColorNames.Length; i++)
            {
                int n = i;
                var mi = Item(Art.CordColorNames[i], delegate { ctl.SetCordColor(n); });
                mi.Icon = new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, Fill = Art.CordOuterOf(i), Stroke = Brushes.White, StrokeThickness = 1 };
                colorMenu.Items.Add(mi);
            }
            decorate.Items.Add(sizeMenu);
            decorate.Items.Add(cordMenu);
            decorate.Items.Add(colorMenu);
            decorate.Items.Add(new Separator());
            decorate.Items.Add(Item("키링 캐릭터 바꾸기...", delegate { PickCharacter(); }));
            decorate.Items.Add(resetItem);   // 내 캐릭터를 넣었을 때만 보임
            menu.Items.Add(decorate);
            // 설정 ▸ 말풍선 안내 / 컴퓨터 켤 때 자동 실행
            var settingsMenu = new MenuItem { Header = "설정" };
            hintItem.Click += delegate { ctl.SetHints(!hintItem.IsChecked); };
            startupItem.Click += delegate { ctl.SetAutoStart(!startupItem.IsChecked); };
            settingsMenu.Items.Add(hintItem);
            settingsMenu.Items.Add(startupItem);
            menu.Items.Add(settingsMenu);
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("사용법", delegate { ctl.ShowGuide(); }));
            menu.Items.Add(Item("종료", delegate { ctl.Exit(); }));
            ContextMenu = menu;

            SetSize(1, 80);
            // 눌러도 시험 문제 창의 포커스를 빼앗지 않게
            SourceInitialized += delegate { Native.MakeToolWindow(this, true); };
        }

        static MenuItem Item(string header, RoutedEventHandler onClick)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += onClick;
            return mi;
        }

        // 키링 크기와 줄 길이 바꾸기 (줄이 매달린 가로 위치는 그대로)
        public void SetSize(double keyScale, double cordLen)
        {
            double oldCenter = Left + ww / 2;
            keyS = BaseKey * keyScale;
            rest = cordLen;
            // 키링이 최대로 흔들려도(줄 각도 40도) 창 밖으로 안 나가게 넉넉히
            ww = Math.Max(190, 2 * (Math.Tan(MaxSwing) * rest + keyS * 1.35));
            ax = ww / 2;
            wh = AY + rest + keyS * 1.3 + Math.Max(PullRoom, keyS * 2.5);
            Width = ww; Height = wh;
            art.Width = ww; art.Height = wh;
            if (!double.IsNaN(oldCenter)) Left = oldCenter - ww / 2;
            kx.Snap(ax); ky.Snap(AY + rest);
            for (int i = 0; i < KeyScales.Length; i++) ((MenuItem)sizeMenu.Items[i]).IsChecked = Math.Abs(KeyScales[i] - keyScale) < 0.01;
            for (int i = 0; i < CordLengths.Length; i++) ((MenuItem)cordMenu.Items[i]).IsChecked = Math.Abs(CordLengths[i] - cordLen) < 0.5;
            BuildArt();
        }

        void BuildArt()
        {
            art.Children.Clear();
            // 화면 위쪽 끝에 붙은 작은 고정 집게
            double cw = 22;   // 집게는 키링을 키워도 그대로
            var clip = new Rectangle
            {
                Width = cw, Height = 11, RadiusX = 4, RadiusY = 4, Fill = Art.HeadRail, Stroke = Art.RailEdge, StrokeThickness = 1,
                Effect = new DropShadowEffect { BlurRadius = 5, ShadowDepth = 1.5, Direction = 270, Opacity = 0.3, Color = Color.FromRgb(0x5A, 0x48, 0x38) }
            };
            Canvas.SetLeft(clip, ax - cw / 2); Canvas.SetTop(clip, -3);
            double th = 3.8;   // 줄 굵기도 키링을 키워도 그대로
            // 줄은 가늘어서 잡기 어려우니 눈에 안 보이는 굵은 줄을 겹쳐 둠
            cordHit = new Line { Stroke = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)), StrokeThickness = 16, X1 = ax, Y1 = AY };
            cordOuter = new Line { Stroke = Art.CordOuter, StrokeThickness = th, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, X1 = ax, Y1 = AY, IsHitTestVisible = false };
            cordInner = new Line { Stroke = Art.CordInner, StrokeThickness = th * 0.34, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, X1 = ax, Y1 = AY, IsHitTestVisible = false };
            key = new Keyring(keyS, character, Keyring.FixedRing) { Effect = new DropShadowEffect { BlurRadius = 5, ShadowDepth = 2, Direction = 280, Opacity = 0.28 } };
            bubble = new HintBubble(12.5, Dock.Top);
            bubbleShown = false;
            UpdateBubbleText();
            art.Children.Add(cordHit);
            art.Children.Add(cordOuter);
            art.Children.Add(cordInner);
            art.Children.Add(clip);
            art.Children.Add(key);
            art.Children.Add(bubble);
            Apply();
        }

        void UpdateBubbleText()
        {
            if (bubble != null) bubble.Text = on ? "당기면 내려가요 ▼\n올리면 올라가요 ▲\n톡 누르면 끝까지 가려요" : "당기면 내려와요 ▼\n톡 누르면 끝까지 가려요";
        }

        // 블라인드가 나와 있는지 (말풍선 안내만 달라짐)
        public void SetOn(bool covering)
        {
            if (on == covering) return;
            on = covering;
            UpdateBubbleText();
        }

        // 줄 색 바꾸기 (메뉴 체크 + 바로 반영)
        public void SetCordColor(int index)
        {
            for (int i = 0; i < colorMenu.Items.Count; i++) ((MenuItem)colorMenu.Items[i]).IsChecked = i == index;
            if (cordOuter != null) { cordOuter.Stroke = Art.CordOuter; cordInner.Stroke = Art.CordInner; }
        }

        public void SetCharacter(BitmapSource bmp)
        {
            character = bmp;
            resetItem.Visibility = bmp != null ? Visibility.Visible : Visibility.Collapsed;
            BuildArt();
        }

        void Apply()
        {
            double x = kx.X, y = ky.X;
            cordHit.X2 = cordOuter.X2 = cordInner.X2 = x;
            cordHit.Y2 = cordOuter.Y2 = cordInner.Y2 = y + 1.5;
            Canvas.SetLeft(key, x - keyS / 2); Canvas.SetTop(key, y);
            key.Angle = -Math.Atan2(x - ax, Math.Max(1, y - AY)) * 180 / Math.PI;
            if (bubbleShown) PlaceBubble();
        }

        // 말풍선은 키링 바로 아래에
        void PlaceBubble()
        {
            var sz = bubble.Measured;
            Canvas.SetLeft(bubble, Layout.Clamp(kx.X - sz.Width / 2, 1, ww - sz.Width - 1));
            Canvas.SetTop(bubble, ky.X + keyS * 1.12);
        }

        // ── 키링이 블라인드로 옮겨 가기 / 돌아오기 ──
        // 블라인드가 화면에 보이는 동안: 줄이 위로 감기며 사라짐 (블라인드 쪽 키링이 대신 내려옴)
        // 블라인드가 안 보이게 되면: 집게에서 줄이 다시 쭉 풀려 내려옴
        bool away;
        public bool Away { get { return away; } }
        public double HangBottom { get { return AY + rest + keyS * 1.35; } }   // 키링 아래끝까지의 높이
        public void Wiggle() { kx.V += 120; StartRendering(); }

        public void SetAway(bool a)
        {
            if (a == away) return;
            if (a && drag != Drag.None) return;   // 줄을 잡고 있는 동안엔 그대로 (놓은 뒤 다음 점검 때 사라짐)
            away = a;
            ShowBubble(false);
            art.IsHitTestVisible = !away;         // 사라진 동안엔 클릭이 뒤 화면으로 그대로 통과
            if (away)
            {
                var fade = new DoubleAnimation(0, TimeSpan.FromSeconds(0.22)) { BeginTime = TimeSpan.FromSeconds(0.14) };
                art.BeginAnimation(OpacityProperty, fade);
                kx.V += 40;
            }
            else
            {
                art.BeginAnimation(OpacityProperty, null);
                art.Opacity = 1;
                ky.Snap(AY + 4);                  // 집게에서 쭉 풀려 내려오며 통통
                kx.Snap(ax); kx.V += 70;
            }
            StartRendering();
        }

        // 블라인드 쪽 키링을 우클릭했을 때도 같은 메뉴
        public void ShowMenuFor(UIElement target)
        {
            var m = ContextMenu;
            m.PlacementTarget = target;
            m.Placement = PlacementMode.MousePoint;
            m.IsOpen = true;
        }

        // ── 가릴 창이 없을 때: 키링이 고개를 절레절레 + 말풍선으로 알려줌 ──
        // (말풍선 안내를 꺼 둬도 이건 뜸. 안 그러면 아무 반응이 없어 고장 난 줄 앎)
        System.Windows.Threading.DispatcherTimer noticeTimer;
        int noticeStep;
        bool noticing;

        public void NoTarget()
        {
            if (noticing) return;              // 당기는 동안 여러 번 불려도 한 번만
            noticing = true;
            noticeStep = 0;
            bubble.Text = "가릴 창을 먼저 클릭해 주세요!\n그다음 키링을 당기면 돼요";
            bubbleShown = true;
            PlaceBubble();
            bubble.Show(true, 0);
            if (noticeTimer == null)
            {
                noticeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(0.14) };
                noticeTimer.Tick += delegate { NoticeTick(); };
            }
            noticeTimer.Start();
            NoticeTick();
        }

        void NoticeTick()
        {
            // 처음 0.4초 동안 좌우로 세 번 툭툭 (절레절레), 3초 뒤 말풍선 닫기
            if (noticeStep < 3 && drag == Drag.None) { kx.V = (noticeStep % 2 == 0 ? 1 : -1) * 260; StartRendering(); }
            noticeStep++;
            if (noticeStep * 0.14 >= 3.0)
            {
                noticeTimer.Stop();
                noticing = false;
                bubbleShown = false;
                bubble.Show(false);
                UpdateBubbleText();
            }
        }

        void ShowBubble(bool show)
        {
            if (noticing) return;                              // 안내 말풍선이 떠 있는 동안엔 그대로
            if (show && ctl != null && !ctl.HintsOn) return;   // 말풍선 안내를 끈 경우
            if (bubble == null || bubbleShown == show) return;
            bubbleShown = show;
            if (show) PlaceBubble();
            bubble.Show(show);
        }

        void StartRendering()
        {
            if (rendering) return;
            rendering = true;
            lastTime = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRendering;
        }

        void OnRendering(object sender, EventArgs e)
        {
            var now = ((RenderingEventArgs)e).RenderingTime;
            if (now == lastTime) return;
            double dt = lastTime == TimeSpan.Zero ? 1 / 60.0 : (now - lastTime).TotalSeconds;
            lastTime = now;
            if (dt > 0.05) dt = 0.05;
            // 블라인드로 옮겨 간 동안에는 줄이 집게 속으로 쏙 감겨 올라감
            if (drag == Drag.None) ky.Target = AY + (away ? 2 : rest);
            if (drag != Drag.Cord) { kx.Step(dt); ky.Step(dt); LimitSwing(); }
            Apply();
            if (drag == Drag.None && kx.Resting(0.2) && ky.Resting(0.2))
            {
                rendering = false;
                CompositionTarget.Rendering -= OnRendering;
            }
        }

        // 세게 흔들어도 줄이 너무 기울지 않고, 끝에 닿으면 통 튕겨 돌아옴
        double MaxDx(double y)
        {
            return Math.Max(0, Math.Min(Math.Tan(MaxSwing) * Math.Max(0, y - AY), ww / 2 - keyS * 1.3));
        }

        void LimitSwing()
        {
            double m = MaxDx(ky.X);
            if (kx.X > ax + m) { kx.X = ax + m; kx.V = -Math.Abs(kx.V) * 0.45; }
            else if (kx.X < ax - m) { kx.X = ax - m; kx.V = Math.Abs(kx.V) * 0.45; }
        }

        void PickCharacter()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "키링으로 달 캐릭터 그림 고르기 (배경이 투명한 PNG가 가장 예뻐요)",
                Filter = "그림 파일|*.png;*.jpg;*.jpeg;*.bmp;*.gif|모든 파일|*.*"
            };
            if (dlg.ShowDialog(this) == true) ctl.SetCustomCharacter(dlg.FileName);
        }

        bool OnCord(Point p)
        {
            // 키링 또는 줄 근처
            if (p.X > kx.X - keyS * 0.7 && p.X < kx.X + keyS * 0.7 && p.Y > ky.X - 6 && p.Y < ky.X + keyS * 1.3) return true;
            if (p.Y < AY - 4 || p.Y > ky.X) return false;
            double t = (p.Y - AY) / Math.Max(1, ky.X - AY);
            return Math.Abs(p.X - (ax + (kx.X - ax) * t)) < 10;
        }

        // 마우스를 올리면 키링이 살랑 + 사용법 말풍선
        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            if (drag == Drag.None) { kx.V += 45; StartRendering(); }
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (drag == Drag.None) ShowBubble(false);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point p = e.GetPosition(this);
            if (drag == Drag.None)
            {
                bool over = !away && OnCord(p);
                Cursor = over ? Cursors.Hand : null;
                ShowBubble(over);
                return;
            }
            if (e.LeftButton != MouseButtonState.Pressed) return;
            Vector d = p - downPt;
            if (!moved && Math.Abs(d.X) + Math.Abs(d.Y) < 4) return;
            if (!moved)
            {
                moved = true;
                ShowBubble(false);
                // 옆으로 끌면 옮기기, 위아래로 끌면 줄 당기기
                if (Math.Abs(d.X) > Math.Abs(d.Y) * 1.3)
                {
                    drag = Drag.Slide;
                    slideOffset = p.X;
                    kx.Target = ax; ky.Target = AY + rest;
                    Cursor = Cursors.SizeWE;
                }
                else key.Pulling = true;
            }
            if (drag == Drag.Slide)
            {
                double dx = p.X - slideOffset;
                kx.V = Layout.Clamp(kx.V - dx * 6, -600, 600);   // 옮기는 반대쪽으로 키링이 대롱대롱
                Left += dx;
                StartRendering();
                return;
            }
            double ny = Layout.Clamp(p.Y - grab.Y, AY + 14, wh - keyS * 1.3);
            double nx = Layout.Clamp(p.X - grab.X, ax - MaxDx(ny), ax + MaxDx(ny));
            lastMoveX = nx - kx.X; lastMoveY = ny - ky.X;
            kx.Snap(nx); ky.Snap(ny);
            Apply();

            // 당긴 만큼 큰 블라인드가 움직임 (블라인드가 없으면 아래로 당길 때 나타남)
            double pull = ny - startKy;
            pull = Math.Abs(pull) < 5 ? 0 : pull - Math.Sign(pull) * 5;
            if (pull == 0) return;
            if (!pulling)
            {
                pulling = ctl.PullStart(pull > 0);
                if (!pulling) return;
            }
            double room = pull > 0 ? (wh - keyS * 1.3) - startKy : startKy - (AY + 14);
            ctl.PullMove(pull / Math.Max(30, room) * BlindWindow.PullGain);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (away) return;
            Point p = e.GetPosition(this);
            if (!OnCord(p)) return;
            downPt = p;
            moved = false;
            pulling = false;
            lastMoveX = lastMoveY = 0;
            drag = Drag.Cord;
            // 줄 어디를 잡아도 키링을 잡은 것처럼
            grab = new Point(p.X - kx.X, Math.Min(p.Y - ky.X, keyS * 0.5));
            startKy = ky.X;
            CaptureMouse();
            StartRendering();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            FinishDrag();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            FinishDrag();
        }

        void FinishDrag()
        {
            var mode = drag;
            if (mode == Drag.None) return;
            drag = Drag.None;
            if (IsMouseCaptured) ReleaseMouseCapture();
            Cursor = Cursors.Hand;
            key.Pulling = false;
            kx.Target = ax; ky.Target = AY + rest;
            if (mode == Drag.Cord)
            {
                // 놓으면 키링만 통통 튀며 제자리로 (블라인드는 그 자리)
                kx.V = lastMoveX * 40; ky.V = lastMoveY * 40;
                if (pulling) ctl.PullEnd();
                else if (!moved) { kx.V += 120; ShowBubble(false); ctl.CoverAll(); }   // 톡 누르면 끝까지 쭉
                pulling = false;
            }
            else if (mode == Drag.Slide) ctl.WidgetMoved();
            StartRendering();
        }

        protected override void OnDrop(DragEventArgs e)
        {
            base.OnDrop(e);
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0) ctl.SetCustomCharacter(files[0]);
        }

#if DEV
        // 개발용: 위젯을 그림 파일로 저장 (크기/줄 길이 몇 가지 + 말풍선)
        public static void SavePreview(string path, BitmapSource character)
        {
            var group = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xF0, 0xDA)) };
            var sets = new[] { new[] { 0.8, 80, 0, 0 }, new[] { 1.0, 80, 0, 0 }, new[] { 1.7, 80, 0, 0 }, new[] { 2.2, 80, 0, 1 }, new[] { 2.8, 80, 0, 3 }, new[] { 1.0, 80, 2, 0 } };
            foreach (var st in sets)
            {
                Art.CordColor = (int)st[3];
                var w = new BlindWidget(null);
                w.SetCharacter(character);
                w.SetSize(st[0], st[1]);
                if (st[2] > 1) w.bubble.Text = "가릴 창을 먼저 클릭해 주세요!\n그다음 키링을 당기면 돼요";
                if (st[2] > 0) { w.bubble.BeginAnimation(OpacityProperty, null); w.bubble.Opacity = 1; w.bubbleShown = true; }
                w.Apply();
                w.Content = null;
                group.Children.Add(w.art);
            }
            group.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            group.Arrange(new Rect(group.DesiredSize));
            group.UpdateLayout();
            var rtb = new RenderTargetBitmap((int)(group.ActualWidth * 2), (int)(group.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
            rtb.Render(group);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = System.IO.File.Create(path)) enc.Save(fs);
        }
#endif
    }
}
