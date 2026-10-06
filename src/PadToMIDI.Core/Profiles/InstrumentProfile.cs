using PadToMIDI.Core.Configuration;

namespace PadToMIDI.Core.Profiles;

/// <summary>Portable configuration only. No connection IDs, held notes, filters, or runtime latches.</summary>
public sealed record InstrumentProfile
{
    public const int CurrentVersion = 1;
    public static Guid DefaultId { get; } = Guid.Parse("ad29b01f-9707-4abf-a0d6-c7aaf141c001");
    public required int Version { get; init; }
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required InstrumentConfiguration Configuration { get; init; }
    // SDL currently exposes a name, not a durable hardware identity. Ambiguous names require selection.
    public string? ControllerName { get; init; }
    public string? MidiEndpointId { get; init; }
    public string? MidiEndpointName { get; init; }

    public static InstrumentProfile Default => new()
    {
        Version = CurrentVersion, Id = DefaultId, Name = "Default C Major", Configuration = new()
    };

    public void Validate()
    {
        if (Version != CurrentVersion) throw new ArgumentException($"Unsupported profile version {Version}; expected {CurrentVersion}.");
        if (Id == Guid.Empty) throw new ArgumentException("A profile needs a nonempty ID.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || Name != Name.Trim() || Name.Any(char.IsControl))
            throw new ArgumentException("Profile names must contain 1–80 characters without surrounding whitespace or control characters.");
        ArgumentNullException.ThrowIfNull(Configuration);
        Configuration.Validate();
        foreach (var preference in new[] { ControllerName, MidiEndpointId, MidiEndpointName })
            if (preference is not null && (string.IsNullOrWhiteSpace(preference) || preference.Length > 2048 || preference.Any(char.IsControl)))
                throw new ArgumentException("Device preferences must be nonempty text of at most 2048 characters.");
    }
}
