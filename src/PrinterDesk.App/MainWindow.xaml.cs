using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PrinterDesk.App.Services;
using PrinterDesk.App.ViewModels;
using PrinterDesk.Core.Documents;
using PrinterDesk.Core.Settings;

namespace PrinterDesk.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private Point _dragStart;
    private PageViewModel? _dragPage;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(new DialogService(this), new SettingsStore(), new WorkingSession(WorkingSession.DefaultFolder));
        DataContext = _vm;
        RestorePlacement();
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += (_, _) => _vm.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _vm.InitializeAsync();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = !_vm.ConfirmClose();
        if (!e.Cancel)
            SavePlacement();
    }

    // ---------------------------------------------------------------- window placement

    /// <summary>Restores the saved size and position, clamped to the work area (e.g. above the taskbar at 125 %).</summary>
    private void RestorePlacement()
    {
        var s = _vm.Settings;
        var work = SystemParameters.WorkArea;
        Width = Math.Min(s.WindowWidth, work.Width);
        Height = Math.Min(s.WindowHeight, work.Height);
        if (s.WindowLeft is { } left && s.WindowTop is { } top
            && left >= SystemParameters.VirtualScreenLeft - 50 && top >= SystemParameters.VirtualScreenTop - 50
            && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100
            && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = Math.Min(left, Math.Max(work.Left, work.Right - Width));
            Top = Math.Min(top, Math.Max(work.Top, work.Bottom - Height));
        }

        if (s.WindowMaximized)
            WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        var s = _vm.Settings;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        (s.WindowLeft, s.WindowTop, s.WindowWidth, s.WindowHeight) = (bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        s.WindowMaximized = WindowState == WindowState.Maximized;
        _vm.SaveSettings();
    }

    // ---------------------------------------------------------------- files dropped on the window

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;
        e.Effects = _vm.IsScanning ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            return;
        e.Handled = true;
        await _vm.ImportFilesAsync(files);
    }

    // ---------------------------------------------------------------- drag & drop reordering

    private void PageList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragPage = ItemUnder(e.OriginalSource)?.DataContext as PageViewModel;
    }

    private void PageList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragPage is null || _vm.IsScanning)
            return;

        var delta = _dragStart - e.GetPosition(null);
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var page = _dragPage;
        _dragPage = null;
        DragDrop.DoDragDrop(PageList, page, DragDropEffects.Move);
    }

    private void PageList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PageViewModel)) is not PageViewModel page)
            return;

        var target = ItemUnder(e.OriginalSource)?.DataContext as PageViewModel;
        var index = target is null ? _vm.Pages.Count - 1 : _vm.Pages.IndexOf(target);
        _vm.MovePage(page, index);
    }

    private static ListBoxItem? ItemUnder(object source)
    {
        for (var node = source as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ListBoxItem item)
                return item;
            if (node is not Visual)
                return null;
        }
        return null;
    }
}
