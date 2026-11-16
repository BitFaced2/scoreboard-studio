namespace ScoreboardStudio.Core;

public static class FrameProcessor
{
    public static VideoFrame Crop(VideoFrame input, Crop region)
    {
        region = region.Clamp();
        int x = Math.Min(input.Width - 1, (int)(region.X * input.Width));
        int y = Math.Min(input.Height - 1, (int)(region.Y * input.Height));
        int width = Math.Clamp((int)Math.Round(region.Width * input.Width), 1, input.Width - x);
        int height = Math.Clamp((int)Math.Round(region.Height * input.Height), 1, input.Height - y);
        var pixels = new byte[width * height * 4];
        for (int row = 0; row < height; row++)
            Buffer.BlockCopy(input.Pixels, ((y + row) * input.Width + x) * 4, pixels, row * width * 4, width * 4);
        return new(width, height, pixels);
    }

    public static VideoFrame Process(VideoFrame input, Crop region, Cleanup settings)
    {
        var crop = Crop(input, region);
        var s = settings.Validated();
        int w = crop.Width, h = crop.Height;
        var alpha = new byte[w * h];
        for (int i = 0; i < alpha.Length; i++)
        {
            int p = i * 4;
            int level = PixelIsolation.Level(crop.Pixels[p], crop.Pixels[p + 1], crop.Pixels[p + 2], s.Isolation);
            double mask = s.RemoveBackground ? Math.Clamp((level - s.Threshold) / (double)s.Softness, 0, 1) : 1;
            alpha[i] = (byte)Math.Round(mask * crop.Pixels[p + 3]);
        }
        var sourceAlpha = alpha;
        if (s.RemoveSpeckles && s.RemoveBackground)
        {
            var filtered = (byte[])alpha.Clone();
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int neighbors = 0;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    if ((dx != 0 || dy != 0) && x + dx >= 0 && x + dx < w && y + dy >= 0 && y + dy < h && alpha[(y + dy) * w + x + dx] > 32) neighbors++;
                if (neighbors < 2) filtered[y * w + x] = 0;
            }
            alpha = filtered;
        }
        if (s.Thickness != 0 && s.RemoveBackground)
        {
            var grown = new byte[alpha.Length];
            int radius = Math.Abs(s.Thickness);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                byte value = s.Thickness > 0 ? (byte)0 : (byte)255;
                for (int dy = -radius; dy <= radius; dy++) for (int dx = -radius; dx <= radius; dx++)
                {
                    byte a = x + dx >= 0 && x + dx < w && y + dy >= 0 && y + dy < h ? alpha[(y + dy) * w + x + dx] : (byte)0;
                    value = s.Thickness > 0 ? Math.Max(value, a) : Math.Min(value, a);
                }
                grown[y * w + x] = value;
            }
            alpha = grown;
        }
        if (s.Smooth && s.RemoveBackground)
        {
            var smooth = new byte[alpha.Length];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int sum = 0;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    sum += alpha[Math.Clamp(y + dy, 0, h - 1) * w + Math.Clamp(x + dx, 0, w - 1)] * (dx == 0 ? 2 : 1) * (dy == 0 ? 2 : 1);
                smooth[y * w + x] = (byte)(sum / 16);
            }
            alpha = smooth;
        }
        var color = Convert.ToInt32(s.CustomColor[1..], 16);
        // NDI requires even widths; pad with transparency without distorting the crop.
        int outputWidth = (w + 1) & ~1;
        var output = new byte[outputWidth * h * 4];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int source = (y * w + x) * 4, target = (y * outputWidth + x) * 4;
            // Expanded original-color edges borrow a foreground color instead of
            // making previously black background pixels opaque around the digit.
            if (s.Color == "Original" && alpha[y * w + x] > sourceAlpha[y * w + x])
            {
                int radius = Math.Max(0, s.Thickness) + (s.Smooth ? 1 : 0);
                byte strongest = sourceAlpha[y * w + x];
                for (int dy = -radius; dy <= radius; dy++) for (int dx = -radius; dx <= radius; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    if (sourceAlpha[ny * w + nx] > strongest)
                    { strongest = sourceAlpha[ny * w + nx]; source = (ny * w + nx) * 4; }
                }
            }
            for (int c = 0; c < 3; c++)
            {
                int value = s.Color == "Original" ? crop.Pixels[source + c] : s.Color == "Custom" ? (color >> (8 * c)) & 255 : 255;
                output[target + c] = (byte)Math.Clamp((int)(value * s.Brightness), 0, 255);
            }
            output[target + 3] = alpha[y * w + x];
        }
        return new(outputWidth, h, output);
    }
}
