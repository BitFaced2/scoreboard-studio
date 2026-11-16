namespace ScoreboardStudio.Core;

public static class PixelIsolation
{
    // White LED cores have all three channels bright. A saturated red glow or blue
    // board face has only one or two bright channels and is rejected in white mode.
    public static int Level(byte blue, byte green, byte red, string mode) => mode == "White highlights"
        ? Math.Min(blue, Math.Min(green, red))
        : Math.Max(blue, Math.Max(green, red));
}
