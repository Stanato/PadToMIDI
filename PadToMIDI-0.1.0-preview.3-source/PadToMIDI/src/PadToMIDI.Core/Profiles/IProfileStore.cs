namespace PadToMIDI.Core.Profiles;

public sealed record ProfileCatalog(IReadOnlyList<InstrumentProfile> Profiles, IReadOnlyList<string> Errors);

/// <summary>Persistence runs outside the live input/MIDI path.</summary>
public interface IProfileStore
{
    Task<ProfileCatalog> ListAsync();
    Task SaveAsync(InstrumentProfile profile);
    Task DeleteAsync(Guid id);
    Task<Guid?> ReadActiveIdAsync();
    Task WriteActiveIdAsync(Guid id);
}
