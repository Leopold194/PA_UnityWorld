public static class GameChoice
{
    public enum Mode { None, Jouer, Visualisation }
    public static Mode Selected { get; set; } = Mode.None;
}