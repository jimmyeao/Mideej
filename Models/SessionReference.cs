namespace Mideej.Models;

/// <summary>
/// Stable reference information for relinking audio sessions across restarts.
/// </summary>
public class SessionReference
{
    public string? SessionId { get; set; }
    public int? ProcessId { get; set; }
    public string? ProcessName { get; set; }
    public string? DisplayName { get; set; }
    public AudioSessionType? SessionType { get; set; }
    /// <summary>
    /// For device sessions, the underlying MMDevice endpoint ID (without input_/output_ prefix)
    /// </summary>
    public string? DeviceEndpointId { get; set; }
}
