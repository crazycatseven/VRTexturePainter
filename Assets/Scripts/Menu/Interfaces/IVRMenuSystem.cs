public interface IVRMenuSystem
{
    void ShowMenu();
    void HideMenu();
    bool IsVisible { get; }
    void RegisterMenuItem(IVRMenuItem item);
    void UnregisterMenuItem(IVRMenuItem item);
}

public interface IVRMenuItem
{
    string Name { get; }
    void OnValueChanged(object value);
    void Initialize();
} 