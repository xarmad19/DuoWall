using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DuoWall.App.Helpers;
using Microsoft.Win32;

namespace DuoWall.App.Windows;

public partial class InstalledAppPickerWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private List<InstalledAppEntry> _allApps = new();

    public InstalledAppEntry? SelectedApp { get; private set; }

    public InstalledAppPickerWindow()
    {
        InitializeComponent();
        Loaded += InstalledAppPickerWindow_Loaded;
        Closed += (_, _) => _cts.Cancel();
    }

    private async void InstalledAppPickerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        try
        {
            _allApps = await InstalledAppsHelper.DiscoverInstalledAppsAsync(_cts.Token);
            LoadingText.Visibility = Visibility.Collapsed;
            ApplyFilter();

            // Extract crisp Windows icons asynchronously in small batches so UI stays 60fps
            _ = LoadIconsAsync(_allApps, _cts.Token);
        }
        catch (Exception ex)
        {
            LoadingText.Text = $"Could not enumerate installed apps: {ex.Message}";
        }
    }

    private async Task LoadIconsAsync(IReadOnlyList<InstalledAppEntry> entries, CancellationToken ct)
    {
        foreach (var app in entries)
        {
            if (ct.IsCancellationRequested) break;
            if (app.Icon != null) continue;

            var icon = await Task.Run(() => IconExtractorHelper.TryExtractAppIcon(app.LaunchTarget), ct);
            if (icon != null && !ct.IsCancellationRequested)
            {
                await Dispatcher.InvokeAsync(() => app.Icon = icon);
            }
        }
    }

    private void ApplyFilter()
    {
        string q = SearchBox.Text.Trim();
        IEnumerable<InstalledAppEntry> filtered = _allApps;
        if (!string.IsNullOrWhiteSpace(q))
        {
            filtered = filtered.Where(a =>
                a.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.LaunchTarget.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var list = filtered.ToList();
        AppsListBox.ItemsSource = list;
        if (list.Count > 0 && AppsListBox.SelectedItem == null)
            AppsListBox.SelectedIndex = 0;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void AppsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AddAppBtn.IsEnabled = AppsListBox.SelectedItem is InstalledAppEntry;
    }

    private void AppsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ConfirmSelection();
    }

    private void AddAppBtn_Click(object sender, RoutedEventArgs e)
    {
        ConfirmSelection();
    }

    private void BrowseManualBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose Application or Executable",
            Filter = "Applications & Shortcuts (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;

        SelectedApp = new InstalledAppEntry
        {
            DisplayName = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName),
            LaunchTarget = dlg.FileName,
            SourceKind = "Custom Path"
        };
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ConfirmSelection()
    {
        if (AppsListBox.SelectedItem is InstalledAppEntry app)
        {
            SelectedApp = app;
            DialogResult = true;
            Close();
        }
    }
}
