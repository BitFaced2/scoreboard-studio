using ScoreboardStudio.Core;

internal static class SyntheticBoard
{
    public static List<ScoreField> Fields() => [
        new() { Name = "Game clock", Region = new(390.0/1280, 135.0/720, 485.0/1280, 140.0/720) },
        new() { Name = "Home score", Region = new(175.0/1280, 340.0/720, 220.0/1280, 155.0/720) },
        new() { Name = "Away score", Region = new(885.0/1280, 340.0/720, 220.0/1280, 155.0/720) },
        new() { Name = "Quarter", Region = new(600.0/1280, 370.0/720, 85.0/1280, 115.0/720) },
        new() { Name = "Home fouls", Region = new(230.0/1280, 565.0/720, 75.0/1280, 100.0/720) },
        new() { Name = "Away fouls", Region = new(940.0/1280, 565.0/720, 75.0/1280, 100.0/720) }
    ];

    public static VideoFrame Render(double seconds)
    {
        var f = new VideoFrame(1280, 720, new byte[1280 * 720 * 4]);
        for (int i = 0; i < f.Pixels.Length; i += 4) { f.Pixels[i] = 22; f.Pixels[i+1] = 18; f.Pixels[i+2] = 15; f.Pixels[i+3] = 255; }
        Rect(45, 40, 1190, 650, 35, 30, 27); Rect(53, 48, 1174, 634, 15, 13, 12);
        int remaining = 600 - ((int)seconds % 600);
        Text($"{remaining / 60:00}:{remaining % 60:00}", 405, 150, 80, 110);
        Text("48", 190, 355, 85, 125); Text("36", 900, 355, 85, 125);
        Text("3", 615, 385, 55, 85); Text("4", 245, 580, 45, 70); Text("2", 955, 580, 45, 70);
        return f;

        void Rect(int x, int y, int w, int h, byte r, byte g, byte b)
        {
            for (int yy = y; yy < y+h; yy++) for (int xx = x; xx < x+w; xx++)
            { int i = (yy * f.Width + xx) * 4; f.Pixels[i] = b; f.Pixels[i+1] = g; f.Pixels[i+2] = r; }
        }
        void Text(string value, int x, int y, int w, int h)
        {
            int[] segments = [0x3f, 0x06, 0x5b, 0x4f, 0x66, 0x6d, 0x7d, 0x07, 0x7f, 0x6f];
            int t = Math.Max(5, w / 7);
            foreach (char c in value)
            {
                if (c == ':') { Rect(x + 8, y + h/3, t, t, 255, 168, 35); Rect(x + 8, y + h*2/3, t, t, 255, 168, 35); x += 35; continue; }
                int bits = segments[c-'0'];
                (int X, int Y, int W, int H)[] bars = [(t,0,w-2*t,t),(w-t,t,t,h/2-t),(w-t,h/2,t,h/2-t),(t,h-t,w-2*t,t),(0,h/2,t,h/2-t),(0,t,t,h/2-t),(t,h/2-t/2,w-2*t,t)];
                for (int s = 0; s < 7; s++) if ((bits & (1 << s)) != 0) { var b = bars[s]; Rect(x+b.X,y+b.Y,b.W,b.H,255,168,35); }
                x += w + 25;
            }
        }
    }
}
