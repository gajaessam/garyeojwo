using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BlindCover
{
    // 사용자가 넣은 캐릭터 그림 처리: 배경 지우기 + 여백 잘라내기
    static class CharacterImage
    {
        public static BitmapSource LoadSaved()
        {
            try
            {
                if (!File.Exists(Settings.CharacterPath)) return null;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bi.UriSource = new Uri(Settings.CharacterPath);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        public static void Save(BitmapSource bmp)
        {
            Directory.CreateDirectory(Settings.Dir);
            using (var fs = new FileStream(Settings.CharacterPath, FileMode.Create))
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                enc.Save(fs);
            }
        }

        // 이불을 덮을 높이 (그림 위에서부터의 비율).
        // 머리와 몸 사이 가장 잘록한 곳(목)을 찾아 그보다 조금 아래, 어깨쯤까지 덮음
        public static double TuckRatio(BitmapSource bmp)
        {
            try
            {
                var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight;
                if (w < 4 || h < 8) return 0.62;
                var px = new byte[w * h * 4];
                conv.CopyPixels(px, w * 4, 0);
                var width = new double[h];
                for (int y = 0; y < h; y++)
                {
                    int first = -1, last = -1;
                    for (int x = 0; x < w; x++)
                        if (px[(y * w + x) * 4 + 3] > 40) { if (first < 0) first = x; last = x; }
                    width[y] = first < 0 ? 0 : last - first + 1;
                }
                // 머리 폭 (위쪽 15~45% 중 가장 넓은 곳)
                double head = 0;
                for (int y = (int)(h * 0.15); y < (int)(h * 0.45); y++) head = Math.Max(head, width[y]);
                // 목 찾기 (40~85% 구간에서 가장 좁은 곳, 위아래 몇 줄 평균으로 매끄럽게)
                int k = Math.Max(1, h / 60), best = -1;
                double bestW = double.MaxValue;
                for (int y = (int)(h * 0.40); y < (int)(h * 0.85); y++)
                {
                    double s = 0; int c = 0;
                    for (int d = -k; d <= k; d++) { int yy = y + d; if (yy >= 0 && yy < h) { s += width[yy]; c++; } }
                    s /= c;
                    if (s < bestW) { bestW = s; best = y; }
                }
                double r = (best > 0 && bestW < head * 0.85) ? best / (double)h + 0.07 : 0.62;
                return Math.Max(0.45, Math.Min(0.85, r));
            }
            catch { return 0.62; }
        }

        // 머리 꼭대기 위치(위에서부터 비율), 머리 가운데(왼쪽부터 비율), 머리 폭(비율)
        public static double[] HeadInfo(BitmapSource bmp)
        {
            var res = new[] { 0.0, 0.5, 0.6 };
            try
            {
                var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight;
                var px = new byte[w * h * 4];
                conv.CopyPixels(px, w * 4, 0);
                int top = -1;
                for (int y = 0; y < h && top < 0; y++)
                    for (int x = 0; x < w; x++)
                        if (px[(y * w + x) * 4 + 3] > 40) { top = y; break; }
                if (top < 0) return res;
                // 귀처럼 뾰족한 부분은 건너뛰고, 머리 위쪽 8~25% 구간의 좌우 끝으로 머리 폭과 가운데를 잡음
                int y0 = Math.Min(h - 1, top + (int)(h * 0.08)), y1 = Math.Min(h - 1, top + (int)(h * 0.25));
                int minX = w, maxX = -1;
                for (int y = y0; y <= y1; y++)
                    for (int x = 0; x < w; x++)
                        if (px[(y * w + x) * 4 + 3] > 40) { if (x < minX) minX = x; if (x > maxX) maxX = x; }
                res[0] = top / (double)h;
                if (maxX > minX)
                {
                    res[1] = (minX + maxX) / 2.0 / w;
                    res[2] = (maxX - minX) / (double)w;
                }
            }
            catch { }
            return res;
        }

        public static BitmapSource Process(string path)
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(path);
            bi.EndInit();
            bi.Freeze();

            BitmapSource src = bi;
            double maxSide = Math.Max(src.PixelWidth, src.PixelHeight);
            if (maxSide > 1024)
            {
                double f = 1024 / maxSide;
                src = new TransformedBitmap(src, new ScaleTransform(f, f));
            }
            var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            conv.CopyPixels(px, stride, 0);

            // 이미 투명 배경이면 그대로, 아니면 단색 배경 지우기
            int transparent = 0;
            for (int i = 3; i < px.Length; i += 4) if (px[i] < 200) transparent++;
            if (transparent < w * h / 100) RemoveBackground(px, w, h);

            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (px[(y * w + x) * 4 + 3] > 24)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
            if (maxX < 0) throw new InvalidOperationException("그림에서 캐릭터를 찾지 못했어요.");

            int cw = maxX - minX + 1, ch = maxY - minY + 1;
            var outPx = new byte[cw * ch * 4];
            for (int y = 0; y < ch; y++)
                Buffer.BlockCopy(px, ((minY + y) * w + minX) * 4, outPx, y * cw * 4, cw * 4);
            var bmp = BitmapSource.Create(cw, ch, 96, 96, PixelFormats.Bgra32, null, outPx, cw * 4);
            bmp.Freeze();
            return bmp;
        }

        static int Diff(byte[] px, int i, byte b, byte g, byte r)
        {
            return Math.Max(Math.Abs(px[i] - b), Math.Max(Math.Abs(px[i + 1] - g), Math.Abs(px[i + 2] - r)));
        }

        static void RemoveBackground(byte[] px, int w, int h)
        {
            int[] corners = { 0, (w - 1) * 4, ((h - 1) * w) * 4, ((h - 1) * w + w - 1) * 4 };
            byte b = px[0], g = px[1], r = px[2];
            foreach (int c in corners)
                if (Diff(px, c, b, g, r) > 40) return; // 배경이 단색이 아니면 건드리지 않음

            const int tol = 42;
            var removed = new bool[w * h];
            var stack = new Stack<int>();
            for (int x = 0; x < w; x++) { stack.Push(x); stack.Push((h - 1) * w + x); }
            for (int y = 0; y < h; y++) { stack.Push(y * w); stack.Push(y * w + w - 1); }
            while (stack.Count > 0)
            {
                int p = stack.Pop();
                if (removed[p] || Diff(px, p * 4, b, g, r) > tol) continue;
                removed[p] = true;
                px[p * 4 + 3] = 0;
                int x = p % w, y = p / w;
                if (x > 0) stack.Push(p - 1);
                if (x < w - 1) stack.Push(p + 1);
                if (y > 0) stack.Push(p - w);
                if (y < h - 1) stack.Push(p + w);
            }
            // 가장자리 부드럽게
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int p = y * w + x;
                    if (removed[p]) continue;
                    if (removed[p - 1] || removed[p + 1] || removed[p - w] || removed[p + w])
                    {
                        int d = Diff(px, p * 4, b, g, r);
                        if (d < tol * 2) px[p * 4 + 3] = (byte)Math.Min(255, 255 * (d - tol) / tol + 60);
                    }
                }
        }
    }
}
