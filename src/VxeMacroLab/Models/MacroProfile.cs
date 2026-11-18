namespace VxeMacroLab.Models;

public sealed class MacroProfile
{
    public string Name { get; set; } = "Новый профиль";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<MouseEvent> Events { get; set; } = new();
}
