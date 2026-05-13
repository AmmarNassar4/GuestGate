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
            Application.Run(new ConsentRequestForm("http://localhost:5264", () => "K1"));
        }
    }
}
