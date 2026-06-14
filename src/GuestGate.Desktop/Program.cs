using System;
using System.Windows.Forms;

namespace GuestGate.Desktop
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            DesktopConfiguration settings = DesktopConfiguration.Load();
            ReceptionForm mainForm = new ReceptionForm();
            mainForm.Load += (_, __) => DesktopConfiguration.ApplyTo(mainForm, settings);

            Application.Run(mainForm);
        }
    }
}
