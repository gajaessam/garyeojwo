using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BlindCover
{
    // 기본 곰돌이 키링 그림으로 프로그램 아이콘(app.ico)을 만드는 도구. 배포판에는 들어가지 않음
    // 사용: tools\make-icon.bat
    static class MakeIcon
    {
        [STAThread]
        static void Main(string[] args)
        {
            string outPath = args.Length > 0 ? args[0] : "app.ico";
            var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
            var pngs = new List<byte[]>();
            foreach (int n in sizes) pngs.Add(Render(n));
            if (args.Length > 1) File.WriteAllBytes(args[1], pngs[pngs.Count - 1]);   // 확인용 큰 그림

            using (var fs = File.Create(outPath))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int n = sizes[i];
                    w.Write((byte)(n >= 256 ? 0 : n)); w.Write((byte)(n >= 256 ? 0 : n));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var p in pngs) w.Write(p);
            }
        }

        // 고리는 빼고 곰돌이 얼굴만 정사각형에 꽉 차게
        static byte[] Render(int n)
        {
            double s = n * 1.04;
            var key = new Keyring(s, null);
            key.Children[key.Children.Count - 1].Visibility = Visibility.Collapsed;   // 금속 고리
            var box = new Canvas { Width = n, Height = n, ClipToBounds = true };
            Canvas.SetLeft(key, (n - s) / 2);
            Canvas.SetTop(key, -s * 0.17);
            box.Children.Add(key);
            box.Measure(new Size(n, n));
            box.Arrange(new Rect(0, 0, n, n));
            box.UpdateLayout();
            var rtb = new RenderTargetBitmap(n, n, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(box);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var ms = new MemoryStream()) { enc.Save(ms); return ms.ToArray(); }
        }
    }
}
