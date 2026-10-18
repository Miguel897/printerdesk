using System.Windows;
using Microsoft.Win32;

namespace PrinterDesk.App.Services;

public sealed class DialogService(Window owner) : IDialogService
{
    private const string AppName = "PrinterDesk";

    public void ShowError(string message) =>
        MessageBox.Show(owner, message, AppName, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowInfo(string message) =>
        MessageBox.Show(owner, message, AppName, MessageBoxButton.OK, MessageBoxImage.Information);

    public bool Confirm(string message, string title) =>
        MessageBox.Show(owner, message, $"{AppName} – {title}", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    public string? AskSavePath(string folder, string fileName, string filter)
    {
        var dialog = new SaveFileDialog
        {
            InitialDirectory = folder,
            FileName = fileName,
            Filter = filter,
            AddExtension = true,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public void ShowAbout() => new AboutWindow { Owner = owner }.ShowDialog();
}
