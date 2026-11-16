using System.Text.Json;

namespace ScoreboardStudio.Core;

// Pixels are packed BGRA with straight (not premultiplied) alpha.
public sealed record VideoFrame(int Width, int Height, byte[] Pixels)
{
    public int Stride => Width * 4;
}

public sealed record Crop(double X, double Y, double Width, double Height)
{
    public Crop Clamp()
    {
        var x = Math.Clamp(double.IsFinite(X) ? X : 0, 0, .999);
        var y = Math.Clamp(double.IsFinite(Y) ? Y : 0, 0, .999);
        return new(x, y, Math.Clamp(double.IsFinite(Width) ? Width : .1, .001, 1 - x),
            Math.Clamp(double.IsFinite(Height) ? Height : .1, .001, 1 - y));
    }
}

public sealed record Cleanup
{
    public string Isolation { get; set; } = "Bright colors";
    public int Threshold { get; set; } = 90;
    public int Softness { get; set; } = 32;
    public double Brightness { get; set; } = 1;
    public int Thickness { get; set; }
    public bool Smooth { get; set; }
    public bool RemoveSpeckles { get; set; } = true;
    public bool RemoveBackground { get; set; } = true;
    public string Color { get; set; } = "White";
    public string CustomColor { get; set; } = "#FFFFFF";

    public Cleanup Validated() => this with {
        Isolation = Isolation == "White highlights" ? Isolation : "Bright colors",
        Threshold = Math.Clamp(Threshold, 0, 254), Softness = Math.Clamp(Softness, 1, 128),
        Brightness = Math.Clamp(double.IsFinite(Brightness) ? Brightness : 1, .25, 3),
        Thickness = Math.Clamp(Thickness, -2, 3),
        Color = Color is "Original" or "Custom" ? Color : "White",
        CustomColor = System.Text.RegularExpressions.Regex.IsMatch(CustomColor ?? "", "^#[0-9a-fA-F]{6}$") ? CustomColor! : "#FFFFFF"
    };
}

public sealed class ScoreField
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New field";
    public Crop Region { get; set; } = new(.1, .1, .2, .2);
    public Cleanup Cleanup { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public override string ToString() => Name;
    // Identity must stay stable while the editor changes the name and crop.
    // Mutable record value equality breaks WPF's selected-item tracking.
    public ScoreField Snapshot() => new() { Id = Id, Name = Name, Region = Region, Cleanup = Cleanup with { }, Enabled = Enabled };
}

public sealed class Layout
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Untitled setup";
    public string? SourceName { get; set; }
    public string? ImagePath { get; set; }
    public PerspectiveSettings Perspective { get; set; } = new();
    public List<ScoreField> Fields { get; set; } = [];
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public void Save(string path)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, Options));
        File.Move(temporary, path, true);
    }

    public static Layout Load(string path)
    {
        if (new FileInfo(path).Length > 2_000_000) throw new InvalidDataException("This setup file is too large.");
        var layout = JsonSerializer.Deserialize<Layout>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("The setup file is empty.");
        if (layout.Version != 1) throw new InvalidDataException("This setup version is not supported.");
        if (layout.Perspective is null || !layout.Perspective.IsValid(out var reason))
            throw new InvalidDataException("The saved perspective correction is invalid.");
        if (layout.Fields is null || layout.Fields.Count > 24) throw new InvalidDataException("A setup can contain up to 24 fields.");
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in layout.Fields)
        {
            if (field is null || field.Region is null || field.Cleanup is null) throw new InvalidDataException("A field is incomplete.");
            if (field.Id == Guid.Empty || !ids.Add(field.Id)) throw new InvalidDataException("Field identifiers must be unique.");
            field.Name = CleanName(field.Name);
            if (!names.Add(field.Name)) throw new InvalidDataException("Field names must be unique.");
            field.Region = field.Region.Clamp();
            field.Cleanup = field.Cleanup.Validated();
        }
        return layout;
    }

    public static string CleanName(string? name)
    {
        var result = new string((name ?? "").Where(c => !char.IsControl(c) && !"\\/:*?\"<>|".Contains(c)).ToArray()).Trim();
        return result.Length == 0 ? "Field" : result[..Math.Min(60, result.Length)];
    }
}
