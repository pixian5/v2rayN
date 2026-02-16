namespace ServiceLib.Models;

public class CheckUpdateModel : ReactiveObject
{
    public bool? IsSelected { get; set; }
    public string? CoreType { get; set; }
    public bool IsGeoCore => string.Equals(CoreType, "GeoFiles", StringComparison.Ordinal);
    public bool IsNotGeoCore => !IsGeoCore;
    [Reactive] public string? Version { get; set; }
    [Reactive] public string? Remarks { get; set; }
    [Reactive] public bool ShowCheckUpdateButton { get; set; } = true;
    public string? FileName { get; set; }
    public bool? IsFinished { get; set; }
}
