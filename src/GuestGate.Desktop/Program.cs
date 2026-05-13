using System;
using System.Windows.Forms;
namespace GuestGate.Desktop { internal static class Program { [STAThread] static void Main() { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new ReceptionForm()); } } }