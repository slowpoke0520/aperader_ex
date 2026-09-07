using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!UpdateRequest.TryParse(args, out UpdateRequest? request) || request == null)
        {
            MessageBox.Show("更新参数无效。\nInvalid update arguments.", "ApeRadar EX Updater", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using UpdateProgressForm form = new(request);
        Application.Run(form);
        return form.ExitCode;
    }
}
