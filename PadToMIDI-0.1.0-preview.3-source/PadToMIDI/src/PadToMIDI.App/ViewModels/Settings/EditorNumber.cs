namespace PadToMIDI.App.ViewModels.Settings;

internal static class EditorNumber
{
    public static decimal Required(decimal? value, string name) => value ?? throw new InvalidOperationException($"Enter {name}.");

    public static int Integer(decimal? value, int minimum, int maximum, string name)
    {
        decimal number = Required(value, name);
        if (number != decimal.Truncate(number) || number < minimum || number > maximum)
            throw new InvalidOperationException($"{name} must be a whole number from {minimum} to {maximum}.");
        return (int)number;
    }
}
