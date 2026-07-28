namespace TabletopCore.Engine;

public readonly record struct PlayerId(int Value)
{
    public override string ToString() => $"P{Value}";
}

public readonly record struct ZoneId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct CardInstanceId(int Value)
{
    public override string ToString() => $"C{Value}";
}

public readonly record struct CardDefinitionId(string Value)
{
    public override string ToString() => Value;
}
