namespace TabletopCore.Games.Agony;

public enum AgonyColor
{
    Red = 0,
    Yellow = 1,
    Green = 2,
    Blue = 3,
}

public static class AgonyColors
{
    public static string Name(AgonyColor color) => color switch
    {
        AgonyColor.Red => "red",
        AgonyColor.Yellow => "yellow",
        AgonyColor.Green => "green",
        AgonyColor.Blue => "blue",
        _ => throw new ArgumentOutOfRangeException(nameof(color)),
    };

    public static AgonyColor Parse(string name) => name switch
    {
        "red" => AgonyColor.Red,
        "yellow" => AgonyColor.Yellow,
        "green" => AgonyColor.Green,
        "blue" => AgonyColor.Blue,
        _ => throw new ArgumentException($"'{name}' is not an Agony color.", nameof(name)),
    };
}
