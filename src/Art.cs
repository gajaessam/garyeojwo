using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace BlindCover
{
    // 블라인드 그림 재료 (살, 레일, 줄)
    static class Art
    {
        public static Color C(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        static LinearGradientBrush V(params object[] stops)
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            for (int i = 0; i < stops.Length; i += 2)
                g.GradientStops.Add(new GradientStop(C((string)stops[i]), (double)stops[i + 1]));
            g.Freeze();
            return g;
        }

        // 살 한 장(위아래로 반복). 완전히 불투명해서 뒤가 절대 비치지 않음
        public static DrawingBrush SlatBrush(double w, double p)
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(V("#FFFFFDF8", 0.0, "#FFF9F0E2", 0.45, "#FFEEDFC8", 0.82, "#FFDCC8A8", 1.0), null,
                new RectangleGeometry(new Rect(0, 0, w, p))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(C("#FFFFFFFF")), null, new RectangleGeometry(new Rect(0, 0, w, Math.Max(1, p * 0.07)))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(C("#FFC2AA88")), null, new RectangleGeometry(new Rect(0, p - Math.Max(1, p * 0.07), w, Math.Max(1, p * 0.07)))));
            g.Freeze();
            return new DrawingBrush(g)
            {
                TileMode = TileMode.Tile,
                Stretch = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, w, p),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, w, p)
            };
        }

        public static Brush HeadRail { get { return V("#FFFFFFFF", 0.0, "#FFF6EEE1", 0.45, "#FFE3D3BA", 0.9, "#FFCDB898", 1.0); } }
        public static Brush BottomRail { get { return V("#FFFFFBF3", 0.0, "#FFF1E4CF", 0.5, "#FFD9C3A0", 1.0); } }
        public static Brush RailEdge { get { var b = new SolidColorBrush(C("#FFC9B190")); b.Freeze(); return b; } }
        public static Brush Tape { get { var b = new SolidColorBrush(C("#CCE6D6BC")); b.Freeze(); return b; } }
        // 줄 색 (꾸미기 ▸ 줄 색에서 고름). 바깥 테두리색 / 가운데 밝은 줄
        public static readonly string[] CordColorNames = { "분홍", "하늘", "민트", "노랑", "보라", "베이지", "회색" };
        static readonly string[,] CordColors =
        {
            { "#FFE08FA3", "#FFFFC7D3" },   // 분홍
            { "#FF7FB3E0", "#FFC9E3FA" },   // 하늘
            { "#FF6FC2A6", "#FFC2EEDD" },   // 민트
            { "#FFE3B44A", "#FFFFE9A8" },   // 노랑
            { "#FFA891DD", "#FFDCD0F7" },   // 보라
            { "#FFC2A57E", "#FFEFE2CC" },   // 베이지 (실제 블라인드 줄 느낌)
            { "#FF9A9AA3", "#FFDCDCE2" },   // 회색
        };
        public static int CordColor;   // 지금 고른 줄 색 번호
        static int CordIndex { get { return Math.Max(0, Math.Min(CordColorNames.Length - 1, CordColor)); } }
        public static Brush CordOuterOf(int i) { var b = new SolidColorBrush(C(CordColors[Math.Max(0, Math.Min(CordColorNames.Length - 1, i)), 0])); b.Freeze(); return b; }
        public static Brush CordOuter { get { return CordOuterOf(CordIndex); } }
        public static Brush CordInner { get { var b = new SolidColorBrush(C(CordColors[CordIndex, 1])); b.Freeze(); return b; } }
    }

    // 블라인드 줄 끝에 달린 키링. 맨 위(가운데)가 줄에 매달린 점
    // 나중에 캐릭터 그림을 넣으면 그 그림이 키링이 됨
    class Keyring : Canvas
    {
        public readonly double S;
        readonly RotateTransform rot;
        readonly ScaleTransform stretch;
        readonly System.Collections.Generic.List<UIElement> openEyes = new System.Collections.Generic.List<UIElement>();
        readonly System.Collections.Generic.List<UIElement> squintEyes = new System.Collections.Generic.List<UIElement>();

        // 금속 고리 기본 크기 (키링을 키워도 고리는 이 크기 그대로)
        public const double FixedRing = 8.6;
        readonly Canvas body = new Canvas();   // 캐릭터 그림 (고리 아래에 매달림)

        // ringPx: 금속 고리 지름. 0이면 캐릭터 크기에 맞춰 같이 커짐(아이콘 만들 때), 아니면 그 크기로 고정
        public Keyring(double s, BitmapSource custom, double ringPx = 0)
        {
            S = s;
            double rr = ringPx > 0 ? ringPx : s * 0.24;
            double th = ringPx > 0 ? 2.0 : Math.Max(1.6, s * 0.055);
            // 캐릭터를 고리 바로 아래에 붙임 (원래 그림은 고리가 s*0.24 크기라고 보고 귀 끝이 s*0.18에 있음)
            double dy = rr * 0.72 - s * 0.18;
            Width = s; Height = dy + s * 1.1;
            SetTop(body, dy);
            Children.Add(body);
            rot = new RotateTransform(0, s / 2, 0);
            stretch = new ScaleTransform(1, 1, s / 2, 0);
            var tg = new TransformGroup();
            tg.Children.Add(stretch);
            tg.Children.Add(rot);
            RenderTransform = tg;
            Background = null;

            if (custom != null)
            {
                var img = new Image { Source = custom, Width = s * 1.05, Height = s * 1.0, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                SetLeft(img, -s * 0.025); SetTop(img, s * 0.2);
                body.Children.Add(img);
            }
            else DrawDefault(s);

            // 금속 고리
            var ring = new Ellipse
            {
                Width = rr, Height = rr, StrokeThickness = th,
                Stroke = new LinearGradientBrush(Art.C("#FFFFFFFF"), Art.C("#FFB9B4C4"), 45)
            };
            SetLeft(ring, s / 2 - rr / 2); SetTop(ring, 0);
            Children.Add(ring);
        }

        public double Angle { set { rot.Angle = value; } }

        // 당기는 동안: 쭉 늘어나고 눈을 질끈 (영차!)
        bool pulling;
        public bool Pulling
        {
            get { return pulling; }
            set
            {
                if (pulling == value) return;
                pulling = value;
                stretch.ScaleX = value ? 0.94 : 1;
                stretch.ScaleY = value ? 1.08 : 1;
                foreach (var e in openEyes) e.Visibility = value ? Visibility.Hidden : Visibility.Visible;
                foreach (var e in squintEyes) e.Visibility = value ? Visibility.Visible : Visibility.Hidden;
            }
        }

        // 기본 키링: 동글동글 곰돌이 모찌
        void DrawDefault(double s)
        {
            var fill = new SolidColorBrush(Art.C("#FFFFE1CF"));
            var line = new SolidColorBrush(Art.C("#FFE3A58C"));
            double lw = Math.Max(1.2, s * 0.04);
            Action<double, double, double, double, Brush, Brush> E = (x, y, w, h, f, st) =>
            {
                var e = new Ellipse { Width = w, Height = h, Fill = f, Stroke = st, StrokeThickness = st == null ? 0 : lw };
                SetLeft(e, x); SetTop(e, y);
                body.Children.Add(e);
            };
            var pink = new SolidColorBrush(Art.C("#FFFFB8C4"));
            // 귀
            E(s * 0.10, s * 0.18, s * 0.27, s * 0.27, fill, line);
            E(s * 0.63, s * 0.18, s * 0.27, s * 0.27, fill, line);
            E(s * 0.16, s * 0.24, s * 0.14, s * 0.14, pink, null);
            E(s * 0.70, s * 0.24, s * 0.14, s * 0.14, pink, null);
            // 얼굴
            E(s * 0.06, s * 0.26, s * 0.88, s * 0.80, fill, line);
            // 볼
            var blush = new SolidColorBrush(Art.C("#88FF9AAE"));
            E(s * 0.15, s * 0.70, s * 0.17, s * 0.10, blush, null);
            E(s * 0.68, s * 0.70, s * 0.17, s * 0.10, blush, null);
            // 눈
            var dark = new SolidColorBrush(Art.C("#FF4B3A3E"));
            int before = body.Children.Count;
            E(s * 0.29, s * 0.55, s * 0.09, s * 0.11, dark, null);
            E(s * 0.62, s * 0.55, s * 0.09, s * 0.11, dark, null);
            var white = Brushes.White;
            E(s * 0.31, s * 0.565, s * 0.035, s * 0.035, white, null);
            E(s * 0.64, s * 0.565, s * 0.035, s * 0.035, white, null);
            for (int i = before; i < body.Children.Count; i++) openEyes.Add(body.Children[i]);
            // 질끈 감은 눈 ( > < )
            foreach (var d in new[] { "M {0},{1} L {2},{3} L {0},{4}", "M {5},{1} L {6},{3} L {5},{4}" })
            {
                var sq = new Path
                {
                    Stroke = dark, StrokeThickness = Math.Max(1.1, s * 0.035), Visibility = Visibility.Hidden,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                    Data = Geometry.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture, d,
                        s * 0.29, s * 0.55, s * 0.38, s * 0.605, s * 0.66, s * 0.71, s * 0.62))
                };
                body.Children.Add(sq);
                squintEyes.Add(sq);
            }
            // 입 (ω)
            var mouth = new Path
            {
                Stroke = dark, StrokeThickness = Math.Max(1, s * 0.03),
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Data = Geometry.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "M {0},{1} Q {2},{3} {4},{1} Q {5},{3} {6},{1}",
                    s * 0.43, s * 0.71, s * 0.465, s * 0.77, s * 0.5, s * 0.535, s * 0.57))
            };
            body.Children.Add(mouth);
        }
    }

    // 키링 옆에 뜨는 사용법 말풍선 (꼬리 방향: Top = 위쪽 키링을 가리킴, Right = 오른쪽 키링을 가리킴)
    class HintBubble : Grid
    {
        readonly TextBlock text;

        public HintBubble(double fs, Dock tail)
        {
            IsHitTestVisible = false;
            Opacity = 0;
            var bg = new SolidColorBrush(Art.C("#FFFFFCF7"));
            var edge = new SolidColorBrush(Art.C("#FFF0A3B5"));
            text = new TextBlock
            {
                FontSize = fs, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Art.C("#FF6A4752")),
                FontFamily = new FontFamily("Malgun Gothic"), TextAlignment = TextAlignment.Center,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight, LineHeight = fs * 1.45
            };
            var box = new Border
            {
                Background = bg, BorderBrush = edge, BorderThickness = new Thickness(1.6), CornerRadius = new CornerRadius(fs * 0.9),
                Padding = new Thickness(fs * 0.85, fs * 0.45, fs * 0.85, fs * 0.5), Child = text,
                Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Direction = 280, Opacity = 0.18 }
            };
            double t = fs * 0.75;
            var tailFill = new Polygon { Fill = bg };
            var tailLine = new Polyline { Stroke = edge, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
            var tailBox = new Canvas();
            tailBox.Children.Add(tailFill);
            tailBox.Children.Add(tailLine);
            if (tail == Dock.Top)
            {
                RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                tailBox.Width = t * 2; tailBox.Height = t;
                tailFill.Points = new PointCollection { new Point(0, t + 2), new Point(t, 0), new Point(t * 2, t + 2) };
                tailLine.Points = new PointCollection { new Point(0, t + 0.5), new Point(t, 0), new Point(t * 2, t + 0.5) };
                tailBox.HorizontalAlignment = HorizontalAlignment.Center;
                tailBox.Margin = new Thickness(0, 0, 0, -1.6);
                SetRow(box, 1);
                SetRow(tailBox, 0);
            }
            else
            {
                ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                tailBox.Width = t; tailBox.Height = t * 2;
                tailFill.Points = new PointCollection { new Point(-2, 0), new Point(t, t), new Point(-2, t * 2) };
                tailLine.Points = new PointCollection { new Point(-0.5, 0), new Point(t, t), new Point(-0.5, t * 2) };
                tailBox.VerticalAlignment = VerticalAlignment.Top;
                tailBox.Margin = new Thickness(-1.6, fs * 0.6, 0, 0);
                SetColumn(box, 0);
                SetColumn(tailBox, 1);
            }
            Children.Add(box);
            Children.Add(tailBox);
            SetZIndex(tailBox, 1);
        }

        public string Text { set { text.Text = value; } }

        public Size Measured
        {
            get { Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); return DesiredSize; }
        }

        public void Show(bool show) { Show(show, show ? 0.5 : 0); }   // 0.5초 머물러야 뜸 (그냥 지나가면 안 뜸)

        public void Show(bool show, double delay)
        {
            BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(show ? 1 : 0,
                TimeSpan.FromSeconds(show ? 0.18 : 0.12)) { BeginTime = TimeSpan.FromSeconds(delay) });
        }
    }
}