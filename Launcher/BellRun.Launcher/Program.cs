using BellRun.Launcher.Core;

namespace BellRun.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            using var engine = new LauncherEngine();
            using var form = new LauncherForm(engine);
            Application.Run(form);
        }
        catch (Exception ex)
        {
            MessageBox.Show("런처를 시작하지 못했습니다.\r\n\r\n" + ex.Message,
                "할리갈리런 런처", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
