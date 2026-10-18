namespace PrinterDesk.App.Services;

public interface IDialogService
{
    void ShowError(string message);

    void ShowInfo(string message);

    bool Confirm(string message, string title);

    /// <summary>Returns the chosen path, or null if the user cancelled.</summary>
    string? AskSavePath(string folder, string fileName, string filter);

    void ShowAbout();
}
