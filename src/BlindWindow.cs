using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace BlindCover
{
    // 가릴 창 위에 딱 맞게 내려오는 블라인드 (살 + 위아래 레일 + 키링 줄, 스프링 움직임).
    // 배경이 투명한 창이라 블라인드가 올라간 자리는 뒤의 실제 화면이 보이고 클릭도 됨.
    // 조작은 키링 하나: 아래로 당긴 만큼 내려오고, 위로 올린 만큼 올라감. 놓으면 그 자리에 멈춤
    class BlindWindow : Window
    {
        // 키링을 끝까지 한 번 당기면 블라인드 전체 높이의 몇 배만큼 움직이는지
        public const double PullGain = 0.6;

        readonly Controller ctl;
        readonly Canvas stage = new Canvas();
        Layout L;
        Rectangle slats, tapeL, tapeR, bottomRail, headRail, headShade;
        DrawingBrush slatBrush;
        Line cordOuter, cordInner;
        Keyring key;
        HintBubble bubble;
        bool bubbleShown;

        Spring bottom;     // 하단 바 아래끝 위치
        Spring kx, ky;     // 키링 위치 (줄 끝)
        bool dropping, rolling, rendering;
        TimeSpan lastTime;
        Action rollDone;

        bool dragging, moved;
        Point downPt, grab;
        double pullStart, startKy, lastMoveX, lastMoveY;

        public BlindWindow(Controller ctl)
        {
            this.ctl = ctl;
            Title = Program.AppName;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = false;   // 항상 위가 아니라, 가린 창에 딸려서 그 창 바로 위에만 있음 (Controller가 연결)
            WindowStartupLocation = WindowStartupLocation.Manual;
            Content = stage;
            SourceInitialized += delegate { Native.MakeToolWindow(this, true); };
        }

        public bool Rolling { get { return rolling; } }

        // 처음 누를 때 늦지 않도록 화면 밖에서 한 번 그려 둠
        public void WarmUp()
        {
            L = new Layout(new Rect(-30000, -30000, 800, 500), new Rect(-30200, -30400, 1400, 1200));
            Place();
            Build(1);
            Show();
            UpdateLayout();
            HideNow();
        }

#if DEV
        // 개발용: 화면에 띄우지 않고 블라인드 모습을 그림 파일로 저장 (frac: 0 = 다 올림, 1 = 다 내림)
        public void SavePreview(string path, Rect target, double frac, double swing, bool hint)
        {
            L = new Layout(target, new Rect(0, 0, 1920, 1040));
            Place();
            Build(frac);
            StopRendering();
            kx.Snap(L.AnchorX + swing); ky.Snap(L.AnchorY + L.Rest);
            Apply();
            if (hint) { bubble.BeginAnimation(OpacityProperty, null); bubble.Opacity = 1; PlaceBubble(); }
            var bg = new Rectangle { Width = L.Target.Width, Height = L.Target.Height, Fill = new SolidColorBrush(Color.FromRgb(0x5B, 0x8D, 0xD6)) };
            Canvas.SetLeft(bg, L.Target.X); Canvas.SetTop(bg, L.Target.Y);
            stage.Children.Insert(0, bg);
            stage.Measure(new Size(L.W, L.H));
            stage.Arrange(new Rect(0, 0, L.W, L.H));
            stage.UpdateLayout();
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)L.W, (int)L.H, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(stage);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using (var fs = System.IO.File.Create(path)) enc.Save(fs);
        }

#endif

        // ── 나타나기: 위 레일만 걸린 채로 시작 (drop이면 끝까지 쭉 내려옴) ──
        public void Cover(Layout layout, bool drop)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            rolling = false;
            rollDone = null;
            L = layout;
            Place();
            Build(0);
            if (drop) LetDown();
            ky.X = L.AnchorY + 6;     // 화면 위 키링이 옮겨 오듯, 위 레일에서 줄이 쭉 풀려 내려옴
            kx.V = L.KeySize * 2.5;   // 줄이 살랑
            if (!IsVisible) Show();
        }

        // 가린 창이 움직이거나 크기가 바뀌면 따라감
        public void Track(Layout layout)
        {
            if (rolling || L == null) return;
            bool same = layout.SameSize(L);
            double frac = Frac();
            bool wasDropping = dropping;
            L = layout;
            Place();
            if (!same)
            {
                Build(frac);
                if (wasDropping) LetDown();
            }
        }

        double Range { get { return Math.Max(1, L.Full - L.MinB); } }

        double Frac()
        {
            if (L == null || bottom == null) return 1;
            if (dropping) return 1;
            return Layout.Clamp((bottom.Target - L.MinB) / Range, 0, 1);
        }

        void Place()
        {
            Left = L.WindowRect.X; Top = L.WindowRect.Y;
            Width = L.W; Height = L.H;
            stage.Width = L.W; stage.Height = L.H;
        }

        void Build(double frac)
        {
            EndDragQuietly();
            stage.Children.Clear();
            double W = L.W;

            slatBrush = Art.SlatBrush(W, L.Pitch);
            slats = new Rectangle { Width = W - L.M * 0.8, Fill = slatBrush };
            Canvas.SetLeft(slats, L.M * 0.4);
            double tw = Math.Max(3, W * 0.004);
            tapeL = new Rectangle { Width = tw, Fill = Art.Tape, IsHitTestVisible = false };
            tapeR = new Rectangle { Width = tw, Fill = Art.Tape, IsHitTestVisible = false };
            Canvas.SetLeft(tapeL, W * 0.18); Canvas.SetLeft(tapeR, W * 0.82 - tw);

            bottomRail = new Rectangle
            {
                Width = W - L.M * 0.6, Height = L.BRailH, Fill = Art.BottomRail, Stroke = Art.RailEdge, StrokeThickness = 1,
                RadiusX = L.BRailH * 0.45, RadiusY = L.BRailH * 0.45,
                Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 3, Direction = 270, Opacity = 0.28, Color = Color.FromRgb(0x5A, 0x48, 0x38) }
            };
            Canvas.SetLeft(bottomRail, L.M * 0.3);

            headShade = new Rectangle
            {
                Width = W - L.M * 0.8, Height = L.RailH * 0.6, IsHitTestVisible = false,
                Fill = new LinearGradientBrush(Color.FromArgb(70, 0x6A, 0x50, 0x38), Color.FromArgb(0, 0x6A, 0x50, 0x38), 90)
            };
            Canvas.SetLeft(headShade, L.M * 0.4); Canvas.SetTop(headShade, L.RailH);
            headRail = new Rectangle
            {
                Width = W, Height = L.RailH, Fill = Art.HeadRail, Stroke = Art.RailEdge, StrokeThickness = 1,
                RadiusX = L.RailH * 0.3, RadiusY = L.RailH * 0.3
            };

            double cw = Math.Max(2.4, L.KeySize * 0.07);
            cordOuter = new Line { Stroke = Art.CordOuter, StrokeThickness = cw + 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false };
            cordInner = new Line { Stroke = Art.CordInner, StrokeThickness = cw * 0.55, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false };
            key = new Keyring(L.KeySize, ctl.Keyring, Keyring.FixedRing) { Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 2, Direction = 280, Opacity = 0.25 } };
            bubble = new HintBubble(Layout.Clamp(L.KeySize * 0.26, 12, 15), Dock.Right) { Text = "당기면 내려가요 ▼\n올리면 올라가요 ▲\n톡 누르면 끝까지 가려요" };
            bubbleShown = false;

            stage.Children.Add(slats);
            stage.Children.Add(tapeL);
            stage.Children.Add(tapeR);
            stage.Children.Add(headShade);
            stage.Children.Add(bottomRail);
            stage.Children.Add(headRail);
            stage.Children.Add(cordOuter);
            stage.Children.Add(cordInner);
            stage.Children.Add(key);
            stage.Children.Add(bubble);

            bottom = new Spring(L.MinB + Range * frac, 260, 0.5);
            dropping = false;
            kx = new Spring(L.AnchorX, 70, 0.12);   // 좌우로 살랑살랑 오래 흔들림
            ky = new Spring(L.AnchorY + L.Rest, 320, 0.3);
            Apply();
            StartRendering();
        }

        // ── 현재 스프링 값으로 화면 배치 ──
        void Apply()
        {
            double b = Math.Min(bottom.X, L.H - 1);
            double sh = Math.Max(0, b - L.BRailH * 0.5 - L.RailH);
            slats.Height = sh;
            Canvas.SetTop(slats, L.RailH);
            slats.Visibility = sh < 0.5 ? Visibility.Collapsed : Visibility.Visible;
            // 살은 하단 바에 붙어서 같이 내려옴
            slatBrush.Viewport = new Rect(0, sh % L.Pitch, L.W, L.Pitch);
            tapeL.Height = tapeR.Height = sh;
            Canvas.SetTop(tapeL, L.RailH); Canvas.SetTop(tapeR, L.RailH);
            headShade.Opacity = Layout.Clamp(sh / (L.RailH * 2), 0, 1);
            Canvas.SetTop(bottomRail, b - L.BRailH);

            double x = kx.X, y = ky.X;
            cordOuter.X1 = cordInner.X1 = L.AnchorX; cordOuter.Y1 = cordInner.Y1 = L.AnchorY;
            cordOuter.X2 = cordInner.X2 = x; cordOuter.Y2 = cordInner.Y2 = y + L.KeySize * 0.05;
            Canvas.SetLeft(key, x - L.KeySize / 2);
            Canvas.SetTop(key, y);
            key.Angle = -Math.Atan2(x - L.AnchorX, Math.Max(1, y - L.AnchorY)) * 180 / Math.PI;
            if (bubbleShown) PlaceBubble();
        }

        // 말풍선은 키링 왼쪽에 (키링을 가리키게)
        void PlaceBubble()
        {
            var sz = bubble.Measured;
            double bx = kx.X - L.KeySize * 0.62 - sz.Width;
            double by = ky.X + L.KeySize * 0.35 - L.KeySize * 0.26 * 0.6 - 6;
            Canvas.SetLeft(bubble, Math.Max(2, bx));
            Canvas.SetTop(bubble, Layout.Clamp(by, 2, L.H - sz.Height - 2));
        }

        void ShowBubble(bool show)
        {
            if (show && !ctl.HintsOn) return;   // 말풍선 안내를 끈 경우
            if (bubble == null || bubbleShown == show) return;
            bubbleShown = show;
            if (show) PlaceBubble();
            bubble.Show(show);
        }

        // ── 그리기 루프 ──
        void StartRendering()
        {
            if (rendering) return;
            rendering = true;
            lastTime = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRendering;
        }

        void StopRendering()
        {
            if (!rendering) return;
            rendering = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        void OnRendering(object sender, EventArgs e)
        {
            var now = ((RenderingEventArgs)e).RenderingTime;
            if (now == lastTime) return;
            double dt = lastTime == TimeSpan.Zero ? 1 / 60.0 : (now - lastTime).TotalSeconds;
            lastTime = now;
            if (dt > 0.05) dt = 0.05;

            bottom.Step(dt);
            if (!dragging) { kx.Step(dt); ky.Step(dt); LimitSwing(); }

            // 쭉 내려오다 끝에 닿으면 통 하고 살짝 튀어 오름
            if (dropping && bottom.X >= L.Full)
            {
                double v = bottom.V;
                bottom.X = L.Full;
                bottom.V = -Math.Abs(v) * 0.16;
                bottom.Target = L.Full;
                bottom.Set(300, 0.45);
                dropping = false;
                ky.V += Math.Min(900, Math.Abs(v) * 0.25);
                kx.V += (kx.X >= L.AnchorX ? 1 : -1) * L.KeySize * 1.5;
            }
            if (bottom.X < L.MinB) { bottom.X = L.MinB; if (bottom.V < 0) bottom.V = 0; }

            if (rolling && bottom.X <= L.MinB + 0.8)
            {
                FinishRoll();
                return;
            }

            Apply();
            bool resting = !dropping && bottom.Resting(0.3) && kx.Resting(0.3) && ky.Resting(0.3);
            if (resting && !dragging && !rolling) StopRendering();   // 멈춰 있으면 쉼 (가볍게)
        }

        // 키링을 톡: 끝까지 쭉 내려와 다 가림. 이미 다 내려와 있으면 키링만 살랑 (실수로 눌러도 안 올라감)
        public void DropAll()
        {
            if (L == null || rolling) return;
            kx.V += L.KeySize * 3;
            if (dropping || bottom.Target >= L.Full - 1) { StartRendering(); return; }
            LetDown();
        }

        void LetDown()
        {
            dropping = true;
            bottom.Set(55, 1.0);
            bottom.Target = L.Full + Range * 0.35;   // 끝보다 더 아래를 향해 가속 → 끝에서 멈춤
            StartRendering();
        }

        // ── 줄 당기기 (여기 키링이든 화면 위 줄이든 같은 방식) ──
        public void BeginPull()
        {
            if (L == null) return;
            if (rolling)
            {
                // 올라가던 중이면 그 자리에서 멈추고 이어서 조절
                rolling = false;
                rollDone = null;
                BeginAnimation(OpacityProperty, null);
                Opacity = 1;
                if (!IsVisible) Show();
            }
            dropping = false;
            pullStart = Layout.Clamp(bottom.X, L.MinB, L.Full);
            bottom.Set(420, 0.9);   // 살짝 늦게 따라와서 드르륵 끌려 내려오는 느낌
            StartRendering();
        }

        // frac: 이번에 당긴 양 (블라인드 전체 높이 대비, 아래 = +)
        public void Pull(double frac)
        {
            if (L == null || rolling) return;
            bottom.Target = Layout.Clamp(pullStart + frac * Range, L.MinB, L.Full);
            StartRendering();
        }

        public void EndPull()
        {
            if (L == null || rolling) return;
            if (bottom.Target < L.MinB + Range * 0.04) { ctl.Uncover(); return; }                       // 끝까지 올리면 걷힘
            if (bottom.Target > L.Full - Range * 0.02) { bottom.Target = L.Full; bottom.Set(300, 0.45); } // 끝까지 내리면 딱 맞게
            else bottom.Set(300, 0.8);                                                                    // 놓은 높이 그대로
            StartRendering();
        }

        // ── 올리기: 블라인드가 위 레일로 쏙 말려 올라가고 사라짐 ──
        public void RollUp(Action done)
        {
            if (rolling || !IsVisible || L == null) { if (done != null) done(); return; }
            rolling = true;
            rollDone = done;
            dropping = false;
            EndDragQuietly();
            ShowBubble(false);
            bottom.Set(120, 1.0);
            bottom.Target = L.MinB - 4;
            kx.V -= L.KeySize * 2;
            StartRendering();
        }

        void FinishRoll()
        {
            bottom.Snap(L.MinB);
            Apply();
            StopRendering();
            var fade = new DoubleAnimation(0, TimeSpan.FromSeconds(0.16));
            fade.Completed += delegate
            {
                if (!rolling) return;   // 그사이 다시 내렸으면 무시
                Hide();
                stage.Children.Clear();
                var d = rollDone;
                rollDone = null;
                rolling = false;
                if (d != null) d();
            };
            BeginAnimation(OpacityProperty, fade);
        }

        public void HideNow()
        {
            EndDragQuietly();
            StopRendering();
            BeginAnimation(OpacityProperty, null);
            Hide();
            rolling = false;
            rollDone = null;
        }

        // 세게 흔들어도 줄이 40도 넘게 기울거나 창 밖으로 나가지 않게
        void LimitSwing()
        {
            double m = Math.Tan(40 * Math.PI / 180) * Math.Max(0, ky.X - L.AnchorY);
            double lo = Math.Max(L.KeySize * 0.6, L.AnchorX - m), hi = Math.Min(L.W - L.KeySize * 0.6, L.AnchorX + m);
            if (kx.X > hi) { kx.X = hi; kx.V = -Math.Abs(kx.V) * 0.45; }
            else if (kx.X < lo) { kx.X = lo; kx.V = Math.Abs(kx.V) * 0.45; }
        }

        // 키링 한가운데의 화면 위치 (DIP). 이 점이 실제로 보이는지로 화면 위 키링을 숨길지 정함
        public bool KeyCenterScreen(out Point p)
        {
            p = new Point();
            if (L == null || kx == null || !IsVisible || rolling) return false;
            p = new Point(Left + kx.X, Top + ky.X + L.KeySize * 0.7);
            return true;
        }

        // 키링을 우클릭하면 화면 위 키링과 같은 메뉴
        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);
            if (L == null || rolling || !OnKey(e.GetPosition(this))) return;
            e.Handled = true;
            ShowBubble(false);
            ctl.ShowMenu(this);
        }

        public void HideHint() { ShowBubble(false); }

        // 줄 색을 바꾸면 바로 반영
        public void RefreshCord()
        {
            if (cordOuter == null) return;
            cordOuter.Stroke = Art.CordOuter;
            cordInner.Stroke = Art.CordInner;
        }

        bool OnKey(Point p)
        {
            double s = L.KeySize;
            return p.X > kx.X - s * 0.65 && p.X < kx.X + s * 0.65 && p.Y > ky.X - s * 0.15 && p.Y < ky.X + s * 1.25;
        }

        // ── 마우스 ──
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (L == null || rolling) return;
            Point p = e.GetPosition(this);
            if (!dragging)
            {
                bool over = OnKey(p);
                Cursor = over ? Cursors.Hand : null;
                ShowBubble(over);
                return;
            }
            double dx = p.X - downPt.X, dy = p.Y - downPt.Y;
            if (!moved && Math.Abs(dx) + Math.Abs(dy) < 4) return;
            if (!moved) { moved = true; ShowBubble(false); key.Pulling = true; BeginPull(); }

            double s = L.KeySize;
            double nx = Layout.Clamp(p.X - grab.X, s * 0.6, L.W - s * 0.6);
            double ny = Layout.Clamp(p.Y - grab.Y, L.AnchorY + 16, L.MaxKeyY);
            lastMoveX = nx - kx.X; lastMoveY = ny - ky.X;
            kx.Snap(nx); ky.Snap(ny);
            // 당긴 만큼만 움직임. 끝까지 한 번 당기면 전체 높이의 PullGain배
            double pull = ny - startKy;
            pull = Math.Abs(pull) < 5 ? 0 : pull - Math.Sign(pull) * 5;   // 옆으로 살랑 흔들 땐 그대로
            double room = pull > 0 ? L.MaxKeyY - startKy : startKy - (L.AnchorY + 16);
            Pull(pull / Math.Max(30, room) * PullGain);
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (!dragging) ShowBubble(false);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (rolling || L == null) return;
            Point p = e.GetPosition(this);
            if (!OnKey(p)) return;   // 살을 누르면 아무 일 없음 (가린 창은 그대로 가려짐)
            dragging = true;
            moved = false;
            downPt = p;
            lastMoveX = lastMoveY = 0;
            grab = new Point(p.X - kx.X, p.Y - ky.X);
            startKy = ky.X;
            CaptureMouse();
            StartRendering();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (dragging) EndDrag();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (dragging) EndDrag();
        }

        void EndDragQuietly()
        {
            dragging = false;
            if (key != null) key.Pulling = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
        }

        void EndDrag()
        {
            dragging = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            key.Pulling = false;
            // 놓으면 키링만 통통 튀며 제자리로 (블라인드는 그 자리)
            kx.V = lastMoveX * 40; ky.V = lastMoveY * 40;
            kx.Target = L.AnchorX; ky.Target = L.AnchorY + L.Rest;
            StartRendering();
            if (moved) EndPull();
            else { ShowBubble(false); DropAll(); }   // 톡 누르면 끝까지 쭉
        }
    }
}
