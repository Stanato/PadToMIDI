using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PadToMIDI.Core.Musical;

namespace PadToMIDI.Core.Profiles;

public static class ProfileJson
{
    public const int MaximumBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        AllowOutOfOrderMetadataProperties = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static string Serialize(InstrumentProfile profile)
    {
        profile.Validate();
        string json = JsonSerializer.Serialize(profile, Options) + Environment.NewLine;
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new ArgumentException("Profile exceeds the 1 MiB limit.");
        return json;
    }

    public static InstrumentProfile Deserialize(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new JsonException("Profile exceeds the 1 MiB limit.");
        try
        {
            using var document = JsonDocument.Parse(json, new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 32 });
            RejectDuplicates(document.RootElement);
            var profile = JsonSerializer.Deserialize<InstrumentProfile>(json, Options) ?? throw new JsonException("A profile cannot be null.");
            // Preserve legacy Start assignments: only new/default profiles reserve Start for chords.
            if (profile.Configuration is { } legacy &&
                document.RootElement.TryGetProperty("configuration", out var configurationJson) &&
                !configurationJson.TryGetProperty("chords", out _) &&
                (legacy.Mappings?.ContainsKey(Input.PhysicalControl.Start) == true ||
                 legacy.TemporaryOctaveModifiers?.ContainsKey(Input.PhysicalControl.Start) == true ||
                 legacy.TemporarySemitoneModifiers?.ContainsKey(Input.PhysicalControl.Start) == true))
                profile = profile with { Configuration = legacy with { Chords = new() { ToggleButton = null } } };
            profile.Validate();
            var preset = ScalePresets.All.FirstOrDefault(scale => scale.Name == profile.Configuration.Scale.Name && scale.Intervals.SequenceEqual(profile.Configuration.Scale.Intervals));
            if (preset is not null) profile = profile with { Configuration = profile.Configuration with { Scale = preset } };
            if (profile.Configuration.Chords.HarmonyScale is { } harmony)
            {
                var harmonyPreset = ScalePresets.All.FirstOrDefault(scale => scale.Name == harmony.Name && scale.Intervals.SequenceEqual(harmony.Intervals));
                if (harmonyPreset is not null) profile = profile with { Configuration = profile.Configuration with
                    { Chords = profile.Configuration.Chords with { HarmonyScale = harmonyPreset } } };
            }
            return profile;
        }
        catch (ArgumentException error) { throw new JsonException($"Invalid profile: {error.Message}", error); }
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException($"Duplicate JSON property: {property.Name}.");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var value in element.EnumerateArray()) RejectDuplicates(value);
    }
}
