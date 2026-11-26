using System;
using System.Windows;

namespace BlindCover
{
    // 가릴 창 크기에 맞춘 블라인드 창 배치 (모두 DIP, 블라인드 창 기준 좌표)
    class Layout
    {
        public readonly Rect WindowRect;   // 화면에서 블라인드 창의 위치
        public readonly double W, H;
        public readonly Rect Target;       // 가릴 창 (블라인드 창 기준)
        public readonly double M;          // 위 레일이 창보다 양옆으로 삐져나오는 폭
        public readonly double RailH;      // 위 레일(헤드레일) 높이
        public readonly double BRailH;     // 아래 레일(하단 바) 높이
        public readonly double Pitch;      // 살 한 장의 높이
        public readonly double MinB, Full; // 하단 바 아래끝: 다 올렸을 때 / 다 내렸을 때
        public readonly double AnchorX, AnchorY, Rest; // 줄이 매달린 곳, 평소 줄 길이
        public readonly double KeySize, MaxKeyY;

        public Layout(Rect target, Rect work)
        {
            M = 6;
            RailH = Clamp(target.Height * 0.035, 14, 24);
            double top = Math.Max(work.Top, target.Top - RailH * 0.45);
            if (top > target.Top) top = target.Top;
            double extra = 28;   // 하단 바가 툭 떨어질 때 살짝 지나칠 여유
            WindowRect = new Rect(target.Left - M, top, target.Width + M * 2, target.Bottom - top + extra);
            W = WindowRect.Width; H = WindowRect.Height;
            Target = new Rect(M, target.Top - top, target.Width, target.Height);

            BRailH = Clamp(RailH * 0.72, 9, 17);
            Pitch = Clamp(target.Height / 24, 12, 24);
            MinB = RailH + BRailH;
            Full = Target.Bottom + 2;

            KeySize = Clamp(Math.Min(target.Width, target.Height) * 0.11, 36, 72);
            AnchorX = W - M - Math.Max(KeySize * 0.8, target.Width * 0.05);
            AnchorY = RailH * 0.55;
            MaxKeyY = H - KeySize * 1.3;
            Rest = Clamp(target.Height * 0.28, 60, 220);
            Rest = Math.Max(20, Math.Min(Rest, MaxKeyY - AnchorY - 30));
        }

        public bool SameSize(Layout o)
        {
            return o != null && Math.Abs(o.W - W) < 1 && Math.Abs(o.H - H) < 1 && Math.Abs(o.Target.Y - Target.Y) < 1;
        }

        public static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }
    }
}
