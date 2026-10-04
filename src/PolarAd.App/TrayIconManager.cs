using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using PolarAd.Core;

namespace PolarAd.App;

/// <summary>
/// System tray icon with a quick on/off toggle and status, so protection state is
/// visible even when the main window is hidden.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ProtectionService _service;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly Window _mainWindow;

    public TrayIconManager(ProtectionService service, Window mainWindow)
    {
        _service = service;
        _mainWindow = mainWindow;

        var menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("PolarAd 열기");
        openItem.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(openItem);

        _toggleItem = new ToolStripMenuItem("보호 기능 켜기/끄기");
        _toggleItem.Click += async (_, _) => await ToggleProtectionAsync();
        menu.Items.Add(_toggleItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("종료 (네트워크 설정 복구 후)");
        exitItem.Click += async (_, _) => await ExitAppAsync();
        menu.Items.Add(exitItem);

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Visible = true,
            ContextMenuStrip = menu,
            Text = "PolarAd",
        };
        _icon.DoubleClick += (_, _) => ShowMainWindow();

        UpdateStatusText();
    }

    public void UpdateStatusText()
    {
        _icon.Text = _service.IsRunning ? "PolarAd - 보호 중" : "PolarAd - 꺼짐";
        _toggleItem.Text = _service.IsRunning ? "보호 기능 끄기" : "보호 기능 켜기";
    }

    private void ShowMainWindow()
    {
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private async Task ToggleProtectionAsync()
    {
        if (_service.IsRunning)
        {
            await _service.StopAsync();
        }
        else
        {
            await _service.StartAsync();
        }
        UpdateStatusText();
    }

    private async Task ExitAppAsync()
    {
        try
        {
            await _service.ShutdownAsync();
        }
        catch { }
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
