namespace VxeMacroLab.Models;

public sealed class KeyboardEvent
{
    public string KeyName { get; set; } = string.Empty;
    public bool IsKeyDown { get; set; }
    public long TimeMs { get; set; }
}
