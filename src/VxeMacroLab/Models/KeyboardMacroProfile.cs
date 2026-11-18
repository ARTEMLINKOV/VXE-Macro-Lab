namespace VxeMacroLab.Models;

public sealed class KeyboardMacroProfile
{
    public string Name { get; set; } = "Новый профиль клавиатуры";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<KeyboardEvent> Events { get; set; } = new();
}
