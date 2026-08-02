namespace GameSubTranslate.Profiles;

public sealed class CaptureRegion
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public string RegionName { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int MonitorIndex { get; set; }
    public bool IsActiveDefault { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Human-readable label for list bindings.</summary>
    public string Display => string.IsNullOrWhiteSpace(RegionName)
        ? $"({X},{Y}) {Width}x{Height} [m{MonitorIndex}]"
        : $"{RegionName} - ({X},{Y}) {Width}x{Height} [m{MonitorIndex}]";
}
