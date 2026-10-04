using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using RadioButton = System.Windows.Controls.RadioButton;
using PolarAd.Core;
using PolarAd.Core.Settings;
using PolarAd.Core.Startup;

namespace PolarAd.App;

public partial class MainWindow : Window
{
    private readonly ProtectionService _service;
    private bool _loadingSettings;

    public MainWindow(ProtectionService service)
    {
        InitializeComponent();
        _service = service;

        _service.QueryObserved += _ => Dispatcher.BeginInvoke(RefreshDashboard);
        _service.AutoUpdateCompleted += _ => Dispatcher.BeginInvoke(RefreshDashboard);

        RefreshAdminStatus();
        RefreshDashboard();
        RefreshRuleLists();
        LoadSettingsTab();
        ShowPage(HomePage, NavHome);
    }

    // ---------------------------------------------------------------- Navigation

    private void NavHome_Click(object sender, RoutedEventArgs e) => ShowPage(HomePage, NavHome);
    private void NavProtection_Click(object sender, RoutedEventArgs e) => ShowPage(ProtectionPage, NavProtection);
    private void NavBlockList_Click(object sender, RoutedEventArgs e) => ShowPage(BlockListPage, NavBlockList);
    private void NavSettings_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage, NavSettings);
    private void NavAbout_Click(object sender, RoutedEventArgs e) => ShowPage(AboutPage, NavAbout);

    private void ViewAllLog_Click(object sender, RoutedEventArgs e)
    {
        RefreshLog();
        ShowPage(LogPage, NavHome);
    }

    private void BackFromLog_Click(object sender, RoutedEventArgs e) => ShowPage(HomePage, NavHome);

    private void ShowPage(UIElement page, RadioButton? navButton)
    {
        foreach (var p in new UIElement[] { HomePage, LogPage, ProtectionPage, BlockListPage, SettingsPage, AboutPage })
        {
            p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
        }
        if (navButton != null) navButton.IsChecked = true;
    }

    // ---------------------------------------------------------------- Home

    private void RefreshAdminStatus()
    {
        bool isAdmin;
        using (var identity = WindowsIdentity.GetCurrent())
        {
            var principal = new WindowsPrincipal(identity);
            isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        AdminStatusText.Text = isAdmin
            ? ""
            : "Not running as administrator. Ad blocking cannot change this PC's DNS until you restart Polar Ad with administrator rights.";
    }

    private void RefreshDashboard()
    {
        bool on = _service.IsRunning;
        ProtectionTitle.Text = on ? "Protection active" : "Protection paused";
        ProtectionSubtitle.Text = on
            ? "Browsing is quieter and cleaner."
            : "Ad blocking is off. Turn it on to block ads and trackers.";
        ProtectionToggle.IsChecked = on;

        StatBlocked.Text = _service.LogStore.TotalBlockedCount.ToString("N0");
        StatBlocklist.Text = _service.RuleEngine.PublicBlocklist.Count.ToString("N0");
        StatAllowed.Text = _service.RuleEngine.Allowlist.Count.ToString("N0");

        RecentActivityList.ItemsSource = _service.LogStore.GetRecent(5)
            .Select(e => new LogRowViewModel(e)).ToList();

        var lastUpdate = _service.Settings.LastBlocklistUpdate;
        BlocklistInfoText.Text = lastUpdate.HasValue
            ? $"{_service.RuleEngine.PublicBlocklist.Count:N0} domains · last updated {lastUpdate.Value.LocalDateTime:yyyy-MM-dd HH:mm}"
            : $"{_service.RuleEngine.PublicBlocklist.Count:N0} domains · not updated yet";
    }

    private async void ProtectionToggle_Click(object sender, RoutedEventArgs e)
    {
        ProtectionToggle.IsEnabled = false;
        try
        {
            if (_service.IsRunning)
            {
                await _service.StopAsync();
            }
            else
            {
                await _service.StartAsync();
            }
        }
        finally
        {
            ProtectionToggle.IsEnabled = true;
            RefreshDashboard();
        }
    }

    // ---------------------------------------------------------------- Protection

    private void AutoStartCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;

        try
        {
            if (AutoStartCheckBox.IsChecked == true)
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName
                    ?? throw new InvalidOperationException("Could not determine the executable path.");
                AutoStartManager.Enable(exePath);
            }
            else
            {
                AutoStartManager.Disable();
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Could not update the startup task: {ex.Message}", "Polar Ad",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);

            _loadingSettings = true;
            AutoStartCheckBox.IsChecked = AutoStartManager.IsEnabled();
            _loadingSettings = false;
        }
    }

    private void SaveAutoUpdateIntervalButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(AutoUpdateIntervalTextBox.Text, out var hours) || hours < 1)
        {
            System.Windows.MessageBox.Show("Enter a whole number of hours, 1 or more.", "Polar Ad",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        _service.Settings.AutoUpdateIntervalHours = hours;
        SettingsStore.Save(AppPaths.SettingsFile, _service.Settings);
        System.Windows.MessageBox.Show("Saved.", "Polar Ad", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private async void EmergencyRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        await _service.EmergencyRestoreAsync();
        RefreshDashboard();
        System.Windows.MessageBox.Show("Network settings restored to their original DNS.", "Polar Ad",
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    // ---------------------------------------------------------------- Block List

    private async void UpdateBlocklistButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateBlocklistButton.IsEnabled = false;
        BlocklistUpdateStatusText.Text = "Updating…";
        try
        {
            var result = await _service.UpdateBlocklistAsync();
            BlocklistUpdateStatusText.Text = result.Success
                ? $"Done: {result.DomainCount:N0} domains loaded"
                : $"Failed: {string.Join("; ", result.Errors)}";
        }
        finally
        {
            UpdateBlocklistButton.IsEnabled = true;
            RefreshDashboard();
        }
    }

    private void RefreshRuleLists()
    {
        BlockRulesListBox.ItemsSource = null;
        BlockRulesListBox.ItemsSource = _service.RuleEngine.UserBlockRules.All().OrderBy(d => d).ToList();

        AllowlistListBox.ItemsSource = null;
        AllowlistListBox.ItemsSource = _service.RuleEngine.Allowlist.All().OrderBy(d => d).ToList();
    }

    private void AddBlockRuleButton_Click(object sender, RoutedEventArgs e)
    {
        var domain = NewBlockRuleTextBox.Text.Trim();
        if (domain.Length == 0) return;
        _service.AddUserBlockRule(domain);
        NewBlockRuleTextBox.Text = "";
        RefreshRuleLists();
    }

    private void RemoveBlockRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (BlockRulesListBox.SelectedItem is string domain)
        {
            _service.RemoveUserBlockRule(domain);
            RefreshRuleLists();
        }
    }

    private void AddAllowEntryButton_Click(object sender, RoutedEventArgs e)
    {
        var domain = NewAllowEntryTextBox.Text.Trim();
        if (domain.Length == 0) return;
        _service.AddAllowlistEntry(domain);
        NewAllowEntryTextBox.Text = "";
        RefreshRuleLists();
    }

    private void RemoveAllowEntryButton_Click(object sender, RoutedEventArgs e)
    {
        if (AllowlistListBox.SelectedItem is string domain)
        {
            _service.RemoveAllowlistEntry(domain);
            RefreshRuleLists();
        }
    }

    // ---------------------------------------------------------------- Activity log

    private void RefreshLog()
    {
        LogDataGrid.ItemsSource = _service.LogStore.GetRecent(500).Select(e => new LogRowViewModel(e)).ToList();
    }

    private void RefreshLogButton_Click(object sender, RoutedEventArgs e) => RefreshLog();

    private void AllowSelectedLogDomainButton_Click(object sender, RoutedEventArgs e)
    {
        if (LogDataGrid.SelectedItem is LogRowViewModel row)
        {
            _service.AddAllowlistEntry(row.Domain);
            RefreshRuleLists();
            System.Windows.MessageBox.Show($"'{row.Domain}' was added to your allowed sites. Reload the page in your browser.",
                "Polar Ad", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        else
        {
            System.Windows.MessageBox.Show("Select a domain in the log first.", "Polar Ad",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        _service.LogStore.Clear();
        RefreshLog();
        RefreshDashboard();
    }

    // ---------------------------------------------------------------- Settings

    private void LoadSettingsTab()
    {
        BlocklistSourcesTextBox.Text = string.Join(Environment.NewLine, _service.Settings.BlocklistSources);
        AutoUpdateIntervalTextBox.Text = _service.Settings.AutoUpdateIntervalHours.ToString();

        _loadingSettings = true;
        try
        {
            AutoStartCheckBox.IsChecked = AutoStartManager.IsEnabled();
        }
        catch
        {
            AutoStartCheckBox.IsEnabled = false;
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    private void SaveBlocklistSourcesButton_Click(object sender, RoutedEventArgs e)
    {
        var urls = BlocklistSourcesTextBox.Text
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(u => u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .ToList();

        _service.Settings.BlocklistSources = urls;
        SettingsStore.Save(AppPaths.SettingsFile, _service.Settings);
        System.Windows.MessageBox.Show("Saved. Run 'Update now' on the Block List page to apply.", "Polar Ad",
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    // ---------------------------------------------------------------- Window lifecycle

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Closing the window just hides it to the tray; protection keeps running.
        // Use the tray icon's Exit item to quit and restore DNS.
        e.Cancel = true;
        Hide();
    }
}
