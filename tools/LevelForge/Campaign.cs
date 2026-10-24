using Portal2D.Game;

static class Campaign
{
    public sealed record Blueprint(string Name, string[] Rooms, bool Horizontal = false);
    public static readonly Blueprint[] Blueprints = [
        new("Archipelago", ["Moat", "Double Decker"]),
        new("Line of Sight", ["Peephole", "Spotlight"], true),
        new("Lost Property", ["Light Well", "Hold the Door"]),
        new("Loading Bay", ["Catapult", "Moat"], true),
        new("Observation Deck", ["Double Decker", "Peephole"]),
        new("Spiral Stair", ["Ascent", "Hold the Door"]),
        new("Flood Control", ["Moat", "Fizzler Row"]),
        new("Air Mail", ["Catapult", "Fizzler Row"]),
        new("Optical Bench", ["Bank Shot", "Spotlight"], true),
        new("Underpass", ["Hold the Door", "Double Decker"]),
        new("Quick Change", ["Flyover", "Safe Cracker"]),
        new("Elastic Limit", ["Rebound", "Catapult"], true),
        new("Lake Effect", ["Moat", "Dry Dock"]),
        new("Blind Corner", ["Peephole", "Freight Lift"]),
        new("Return to Sender", ["Dry Dock", "Light Well"]),
        new("Counterweight", ["Light Well", "Catapult"], true),
        new("Cloud Chamber", ["Ascent", "Double Decker"]),
        new("Zigzag", ["Peephole", "Moat", "Spotlight"]),
        new("Causeway", ["Hold the Door", "Fizzler Row"], true),
        new("Upper Management", ["Freight Lift", "Catapult"], true),
        new("Signal Box", ["Bank Shot", "Flyover"], true),
        new("Sky Bridge", ["Rooftops", "Double Decker"]),
        new("Against the Clock", ["Safe Cracker", "Moat", "Closing Time"], true),
        new("Blue Shift", ["Rebound", "Fizzler Row", "Spotlight"]),
        new("Rock Pool", ["Moat", "Catapult", "Double Decker"], true),
        new("Keyhole", ["Peephole", "Flyover", "Hold the Door"]),
        new("Cold Storage", ["Dry Dock", "Double Decker", "Flyover"]),
        new("Cargo Net", ["Catapult", "Light Well", "Rebound"], true),
        new("Roof Access", ["Ascent", "Freight Lift", "Spotlight"]),
        new("The Long Way Up", ["Ascent", "Bank Shot", "Double Decker"]),
        new("Deep Water", ["Moat", "Fizzler Row", "Dry Dock"]),
        new("Heavy Lifting", ["Light Well", "Double Decker", "Catapult"], true),
        new("Light Industry", ["Freight Lift", "Flyover", "Bank Shot"]),
        new("Crossing Paths", ["Hold the Door", "Moat", "Freight Lift"]),
        new("Last Orders", ["Dry Dock", "Safe Cracker", "Closing Time"]),
        new("Escape Velocity", ["Ascent", "Terminal Velocity", "Bank Shot"]),
        new("Island Hopping", ["Moat", "Fizzler Row", "Double Decker", "Spotlight"]),
        new("Through the Looking Glass", ["Light Well", "Bank Shot", "Flyover", "Spotlight"], true),
        new("Round Trip", ["Dry Dock", "Double Decker", "Moat", "Safe Cracker"])
    ];

    public static Dictionary<string, LevelDef> Sources()
    {
        var sources = Levels.All.Take(41).Where(d => d.Solution != null).ToDictionary(d => d.Name);
        for (int i = 0; i < 8; i++) { var d = Circuits.Build(i); sources.Add(d.Name, d); }
        return sources;
    }
}
