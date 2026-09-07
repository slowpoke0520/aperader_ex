using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

internal sealed class UpdateProgressForm : Form
{
    private readonly UpdateRequest request;
    private readonly UpdateStrings text;
    private readonly Label statusLabel;
    private readonly ProgressBar progressBar;
    private readonly TextBox detailBox;
    private readonly Button closeButton;
    private readonly CancellationTokenSource cancellation = new();
    private bool running = true;

    public UpdateProgressForm(UpdateRequest request)
    {
        this.request = request;
        text = new UpdateStrings(request.Language);
        Text = text.Title;
        Width = 620;
        Height = 270;
        MinimumSize = new Size(520, 250);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        Font = new Font("Segoe UI", 9F);

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 18, 20, 16),
            ColumnCount = 1,
            RowCount = 5
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Label titleLabel = new()
        {
            Text = text.Heading,
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 12)
        };
        statusLabel = new Label { Text = text.Preparing, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        progressBar = new ProgressBar { Dock = DockStyle.Top, Height = 22, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 24, Margin = new Padding(0, 0, 0, 10) };
        detailBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.Window,
            ScrollBars = ScrollBars.Vertical,
            TabStop = false
        };
        closeButton = new Button { Text = text.Cancel, Width = 96, Height = 30, Enabled = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 12, 0, 0) };
        closeButton.Click += (_, _) =>
        {
            if (running)
            {
                closeButton.Enabled = false;
                statusLabel.Text = text.Cancelling;
                cancellation.Cancel();
            }
            else Close();
        };

        layout.Controls.Add(titleLabel, 0, 0);
        layout.Controls.Add(statusLabel, 0, 1);
        layout.Controls.Add(progressBar, 0, 2);
        layout.Controls.Add(detailBox, 0, 3);
        layout.Controls.Add(closeButton, 0, 4);
        Controls.Add(layout);
    }

    public int ExitCode { get; private set; } = 1;

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Progress<UpdateProgressInfo> progress = new(ShowProgress);
        try
        {
            await Task.Run(() => UpdateEngine.Apply(request, text, progress, cancellation.Token));
            ExitCode = 0;
            running = false;
            closeButton.Text = text.Close;
            closeButton.Enabled = true;
            await Task.Delay(1400);
            Close();
        }
        catch (OperationCanceledException)
        {
            bool restarted = UpdateEngine.TryRestartExisting(request);
            ShowProgress(new UpdateProgressInfo(0, text.Cancelled, text.CancelledDetail(restarted)));
            progressBar.Style = ProgressBarStyle.Continuous;
            running = false;
            closeButton.Text = text.Close;
            closeButton.Enabled = true;
        }
        catch (Exception ex)
        {
            string logPath = UpdateEngine.WriteErrorLog(ex);
            bool restarted = UpdateEngine.TryRestartExisting(request);
            ShowProgress(new UpdateProgressInfo(100, text.Failed, text.FailureDetail(ex.Message, logPath, restarted)));
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.ForeColor = Color.Firebrick;
            running = false;
            closeButton.Text = text.Close;
            closeButton.Enabled = true;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (running)
        {
            cancellation.Cancel();
            closeButton.Enabled = false;
            statusLabel.Text = text.Cancelling;
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        cancellation.Dispose();
        UpdateEngine.ScheduleSelfDelete(request.UpdaterPath);
        base.OnFormClosed(e);
    }

    private void ShowProgress(UpdateProgressInfo value)
    {
        statusLabel.Text = value.Status;
        if (!string.IsNullOrWhiteSpace(value.Detail))
        {
            detailBox.Text = value.Detail;
            detailBox.SelectionStart = detailBox.TextLength;
            detailBox.ScrollToCaret();
        }
        progressBar.Style = value.Indeterminate ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        if (!value.Indeterminate) progressBar.Value = Math.Clamp(value.Percent, progressBar.Minimum, progressBar.Maximum);
    }
}

internal sealed class UpdateStrings
{
    private readonly bool chinese;

    public UpdateStrings(string language)
    {
        chinese = language.Equals("AUTO", StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
            : language.StartsWith("ZH", StringComparison.OrdinalIgnoreCase);
    }

    public string Title => chinese ? "ApeRadar EX 更新" : "ApeRadar EX Update";
    public string Heading => chinese ? "正在安装软件更新" : "Installing software update";
    public string Preparing => chinese ? "正在准备更新…" : "Preparing the update...";
    public string WaitingForApp => chinese ? "正在等待 ApeRadar 安全退出…" : "Waiting for ApeRadar to close safely...";
    public string ForcingAppClose => chinese ? "主程序关闭超时，正在结束本次更新对应的进程…" : "ApeRadar did not close in time; stopping the process for this update...";
    public string Downloading => chinese ? "正在下载软件更新…" : "Downloading the software update...";
    public string CheckingDownload => chinese ? "正在校验下载文件…" : "Verifying the downloaded package...";
    public string Extracting => chinese ? "正在解压并检查更新包…" : "Extracting and checking the update package...";
    public string BackingUp => chinese ? "正在备份当前版本…" : "Backing up the current version...";
    public string Installing => chinese ? "正在安装新版本…" : "Installing the new version...";
    public string Verifying => chinese ? "正在验证安装结果…" : "Verifying the installation...";
    public string Starting => chinese ? "正在启动新版本…" : "Starting the new version...";
    public string RollingBack => chinese ? "安装失败，正在恢复原版本…" : "Installation failed; restoring the previous version...";
    public string Completed => chinese ? "更新完成，新版本已经启动。" : "Update complete. The new version has started.";
    public string Failed => chinese ? "更新失败，已尝试恢复并重新启动原版本。" : "Update failed. The previous version was restored and restarted when possible.";
    public string Cancelling => chinese ? "正在取消并恢复原版本…" : "Cancelling and restoring the previous version...";
    public string Cancelled => chinese ? "更新已取消。" : "The update was cancelled.";
    public string Cancel => chinese ? "取消" : "Cancel";
    public string Close => chinese ? "关闭" : "Close";

    public string WaitingDetail(int seconds) => chinese ? $"已经等待 {seconds} 秒。更新器会在必要时安全结束本次 ApeRadar 进程。" : $"Waiting for {seconds} seconds. The updater will stop this ApeRadar process if necessary.";
    public string DownloadDetail(long downloaded, long? total)
    {
        string downloadedText = FormatBytes(downloaded);
        string totalText = total.HasValue ? FormatBytes(total.Value) : (chinese ? "未知大小" : "unknown size");
        return chinese ? $"已下载 {downloadedText} / {totalText}" : $"Downloaded {downloadedText} / {totalText}";
    }
    public string FileDetail(int completed, int total, string file) => chinese ? $"{completed}/{total}\r\n{file}" : $"{completed}/{total}\r\n{file}";
    public string CancelledDetail(bool restarted) => chinese
        ? $"原版本重启：{(restarted ? "成功或仍在运行" : "失败，请手动启动 ApeRadar.exe")}"
        : $"Previous version restart: {(restarted ? "successful or still running" : "failed; start ApeRadar.exe manually")}";
    public string FailureDetail(string message, string logPath, bool restarted) => chinese
        ? $"{message}\r\n\r\n错误日志：{logPath}\r\n原版本重启：{(restarted ? "成功或仍在运行" : "失败，请手动启动 ApeRadar.exe")}" 
        : $"{message}\r\n\r\nError log: {logPath}\r\nPrevious version restart: {(restarted ? "successful or still running" : "failed; start ApeRadar.exe manually")}";

    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024L
        ? $"{bytes / 1024d / 1024d:0.0} MB"
        : $"{bytes / 1024d:0.0} KB";
}
