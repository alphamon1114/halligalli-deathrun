using System.Diagnostics;
using BellRun.Launcher.Core;

namespace BellRun.Launcher;

internal sealed class LauncherForm : Form
{
    private readonly LauncherEngine engine;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Label latest = new() { Name = "LatestVersionLabel", AccessibleName = "최신 버전", AutoSize = true, Text = "최신 버전: 확인 전" };
    private readonly Label installed = new() { Name = "InstalledVersionLabel", AccessibleName = "설치 버전", AutoSize = true, Text = "설치 버전: 확인 전" };
    private readonly TextBox status = new()
    {
        Name = "StatusTextBox", AccessibleName = "런처 상태", ReadOnly = true, Multiline = true,
        ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, TabStop = true,
        Text = "최신 버전 확인 전에는 게임을 실행할 수 없습니다.", BackColor = SystemColors.Window
    };
    private readonly ProgressBar progressBar = new() { Name = "DownloadProgressBar", AccessibleName = "다운로드 및 검증 진행률", Dock = DockStyle.Fill, Maximum = 1000 };
    private readonly Label progressText = new() { Name = "ProgressLabel", AccessibleName = "진행 상태", AutoSize = true, Text = "대기 중" };
    private readonly Button check = new() { Name = "CheckUpdateButton", AccessibleName = "최신 확인 / 재시도", Text = "최신 확인 / 재시도", AutoSize = true, Padding = new Padding(10, 8, 10, 8) };
    private readonly Button install = new() { Name = "InstallButton", AccessibleName = "설치 / 업데이트", Text = "설치 / 업데이트", AutoSize = true, Padding = new Padding(10, 8, 10, 8), Enabled = false };
    private readonly Button play = new() { Name = "PlayButton", AccessibleName = "게임 실행", Text = "게임 실행", AutoSize = true, Padding = new Padding(18, 8, 18, 8), Enabled = false };
    private readonly IProgress<LauncherProgress> progress;
    private LauncherState? state;
    private bool busy;
    private bool closing;

    internal LauncherForm(LauncherEngine engine)
    {
        this.engine = engine;
        Name = "BellRunLauncherWindow";
        AccessibleName = "할리갈리런 런처";
        Text = "할리갈리런 런처";
        Font = new Font("맑은 고딕", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(720, 520);
        MinimumSize = new Size(650, 510);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 10 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 10; row++) layout.RowStyles.Add(new RowStyle(row == 4 ? SizeType.Percent : SizeType.AutoSize, row == 4 ? 100 : 0));
        layout.Controls.Add(new Label { Text = "할리갈리런", Font = new Font(Font.FontFamily, 21F, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 8) }, 0, 0);
        layout.Controls.Add(new Label { Text = "공식 GitHub 배포 · 매 실행 시 최신 버전 및 전체 파일 검증", AutoSize = true, Margin = new Padding(0, 0, 0, 16) }, 0, 1);
        layout.Controls.Add(latest, 0, 2);
        layout.Controls.Add(installed, 0, 3);
        status.Margin = new Padding(0, 16, 0, 12);
        layout.Controls.Add(status, 0, 4);
        progressBar.Height = 20;
        layout.Controls.Add(progressBar, 0, 5);
        progressText.Margin = new Padding(0, 4, 0, 12);
        layout.Controls.Add(progressText, 0, 6);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        buttons.Controls.AddRange([check, install, play]);
        layout.Controls.Add(buttons, 0, 7);
        layout.Controls.Add(new Label { Text = "인터넷 확인 실패 시 실행 불가 · 사용자 맵과 설정은 삭제하지 않습니다.", AutoSize = true, Margin = new Padding(0, 14, 0, 5) }, 0, 8);
        var releases = new LinkLabel { Text = "공식 Releases / 최신 런처 다운로드", AccessibleName = "공식 GitHub Releases 열기", AutoSize = true };
        releases.LinkClicked += (_, _) => OpenReleases();
        layout.Controls.Add(releases, 0, 9);
        Controls.Add(layout);
        progress = new Progress<LauncherProgress>(ReportProgress);
        check.Click += async (_, _) => await CheckAsync();
        install.Click += async (_, _) => await RunAsync(async token => SetState(await engine.InstallLatestAsync(progress, token)));
        play.Click += async (_, _) => await RunAsync(async token =>
        {
            await engine.PlayAsync(progress, token);
            state = null;
            status.Text = "게임을 시작했습니다. 게임 자체에서도 최신 배포를 다시 확인합니다.\r\n게임 종료 후 ‘최신 확인 / 재시도’를 눌러 주세요.";
        });
        Shown += async (_, _) => await CheckAsync();
        FormClosing += HandleClosing;
    }

    private Task CheckAsync() => RunAsync(async token => SetState(await engine.CheckAsync(progress, token)));

    private void SetState(LauncherState result)
    {
        state = result;
        latest.Text = $"최신 버전: {result.Latest.Version}  (프로토콜 {result.Latest.Protocol})";
        installed.Text = "설치 버전: " + (result.InstalledVersion ?? "설치 필요");
        status.Text = result.GameRunning
            ? "게임이 실행 중입니다. 업데이트 또는 재실행 전에 게임을 완전히 종료하세요. 종료 후 최신 확인을 눌러 주세요."
            : result.IsReady
                ? "최신 버전과 설치된 모든 파일을 확인했습니다.\r\n‘게임 실행’을 누르면 최신 배포를 새로 확인하고 실행합니다."
                : "최신 버전 설치 또는 손상된 파일 복구가 필요합니다.\r\n‘설치 / 업데이트’를 눌러 주세요. 기존 설치와 사용자 맵·설정은 유지됩니다.";
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (busy || closing) return;
        busy = true;
        state = null;
        check.Enabled = install.Enabled = play.Enabled = false;
        status.ForeColor = SystemColors.WindowText;
        progressBar.Style = ProgressBarStyle.Marquee;
        try
        {
            await action(lifetime.Token);
            progressText.Text = "완료";
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Value = 1000;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            status.Text = "작업을 취소했습니다. 기존 설치와 사용자 데이터는 유지됩니다.";
        }
        catch (Exception ex)
        {
            state = null;
            status.ForeColor = Color.DarkRed;
            status.Text = "실행 차단\r\n\r\n" + ex.Message + "\r\n\r\n연결 상태와 여유 공간을 확인한 뒤 ‘최신 확인 / 재시도’를 눌러 주세요.";
            progressText.Text = "실패 — 게임을 실행하지 않았습니다.";
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Value = 0;
        }
        finally
        {
            busy = false;
            check.Enabled = !closing;
            install.Enabled = !closing && state is { GameRunning: false, IsReady: false };
            play.Enabled = !closing && state is { GameRunning: false, IsReady: true };
            if (closing) BeginInvoke(new Action(Close));
        }
    }

    private void ReportProgress(LauncherProgress value)
    {
        if (!busy || closing) return;
        status.Text = value.Message;
        if (value.TotalBytes > 0)
        {
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Value = (int)Math.Clamp(value.CompletedBytes * 1000 / value.TotalBytes, 0, 1000);
            progressText.Text = $"{value.CompletedBytes / 1048576d:N1} / {value.TotalBytes / 1048576d:N1} MB  ({progressBar.Value / 10d:N1}%)";
        }
        else
        {
            progressBar.Style = ProgressBarStyle.Marquee;
            progressText.Text = value.Message;
        }
    }

    private void HandleClosing(object? sender, FormClosingEventArgs args)
    {
        if (!busy) return;
        args.Cancel = true;
        closing = true;
        status.Text = "안전하게 작업을 취소하는 중입니다…";
        lifetime.Cancel();
    }

    private static void OpenReleases()
    {
        try { using Process? process = Process.Start(new ProcessStartInfo(ReleaseManifest.Repository + "/releases") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show("브라우저를 열지 못했습니다.\r\n" + ex.Message, "공식 Releases", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) lifetime.Dispose();
        base.Dispose(disposing);
    }
}
