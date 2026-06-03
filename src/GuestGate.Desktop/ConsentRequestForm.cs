using Newtonsoft.Json.Linq;
using System;
using System.Drawing;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GuestGate.Desktop
{
    public sealed class ConsentRequestSentEventArgs : EventArgs
    {
        public ConsentRequestSentEventArgs(int requestId)
        {
            RequestId = requestId;
        }

        public int RequestId { get; }
    }

    public class ConsentRequestForm : Form
    {
        private const string DefaultTermsEn = "I confirm that I have read, understood, and agree to the hotel terms and conditions.";
        private const string DefaultTermsAr = "أؤكد أنني قرأت وفهمت وأوافق على شروط وأحكام الفندق.";

        private readonly HttpClient _http = new HttpClient();
        private readonly Func<string> _getKid;
        private readonly TextBox _baseUrlBox;
        private readonly TextBox _kidBox;
        private readonly TextBox _guestNameBox;
        private readonly TextBox _identityNumberBox;
        private readonly TextBox _checkInTimeBox;
        private readonly ComboBox _languageBox;
        private readonly TextBox _termsEnBox;
        private readonly TextBox _termsArBox;
        private readonly Button _sendBtn;
        private readonly Button _openKioskBtn;
        private readonly Label _statusLabel;

        public event EventHandler<ConsentRequestSentEventArgs> RequestSent;

        public ConsentRequestForm(string baseUrl, Func<string> getKid)
        {
            _getKid = getKid ?? (() => "1");

            Text = "GuestGate — Consent approval request";
            StartPosition = FormStartPosition.CenterParent;
            Width = 760;
            Height = 620;
            MinimumSize = new Size(680, 540);

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(12);
            root.ColumnCount = 2;
            root.RowCount = 9;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            Controls.Add(root);

            _baseUrlBox = new TextBox { Dock = DockStyle.Fill, Text = string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:5264" : baseUrl.TrimEnd('/') };
            _kidBox = new TextBox { Dock = DockStyle.Left, Width = 140, Text = NormalizeKid(_getKid()) };
            _guestNameBox = new TextBox { Dock = DockStyle.Fill };
            _identityNumberBox = new TextBox { Dock = DockStyle.Fill };
            _checkInTimeBox = new TextBox { Dock = DockStyle.Left, Width = 180, Text = DateTime.Now.ToString("HH:mm") };
            _languageBox = new ComboBox { Dock = DockStyle.Left, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            _languageBox.Items.Add(new LanguageItem("en", "English"));
            _languageBox.Items.Add(new LanguageItem("ar", "Arabic / العربية"));
            _languageBox.SelectedIndex = 0;
            _termsEnBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = DefaultTermsEn };
            _termsArBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, RightToLeft = RightToLeft.Yes, Text = DefaultTermsAr };
            _sendBtn = new Button { Width = 170, Height = 30, Text = "Send approval request" };
            _openKioskBtn = new Button { Width = 150, Height = 30, Text = "Open consent kiosk" };
            _statusLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.DimGray };

            AddRow(root, 0, "Base URL:", _baseUrlBox);
            AddRow(root, 1, "Tablet (kid):", _kidBox);
            AddRow(root, 2, "Guest name:", _guestNameBox);
            AddRow(root, 3, "Identity no:", _identityNumberBox);
            AddRow(root, 4, "Check-in time:", _checkInTimeBox);
            AddRow(root, 5, "Language:", _languageBox);
            AddRow(root, 6, "Terms EN:", _termsEnBox);
            AddRow(root, 7, "Terms AR:", _termsArBox);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            actions.Controls.Add(_sendBtn);
            actions.Controls.Add(_openKioskBtn);
            actions.Controls.Add(_statusLabel);
            root.Controls.Add(actions, 1, 8);

            _sendBtn.Click += async delegate { await SendAsync(); };
            _openKioskBtn.Click += delegate { OpenConsentKiosk(); };
            Shown += delegate
            {
                _kidBox.Text = NormalizeKid(_getKid());
                if (string.IsNullOrWhiteSpace(_checkInTimeBox.Text)) _checkInTimeBox.Text = DateTime.Now.ToString("HH:mm");
            };
        }

        private static void AddRow(TableLayoutPanel table, int row, string label, Control input)
        {
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            table.Controls.Add(input, 1, row);
        }

        private async Task SendAsync()
        {
            var baseUrl = (_baseUrlBox.Text ?? string.Empty).Trim().TrimEnd('/');
            var kidText = NormalizeKid(_kidBox.Text);
            var checkInTime = (_checkInTimeBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(baseUrl)) { SetStatus("Base URL is required.", true); return; }
            if (!int.TryParse(kidText, out var kid) || kid <= 0) { SetStatus("Tablet kid must be a positive number.", true); return; }
            if (string.IsNullOrWhiteSpace(checkInTime)) { SetStatus("Check-in time is required.", true); return; }

            var language = (_languageBox.SelectedItem as LanguageItem)?.Code ?? "en";
            var body = new JObject
            {
                ["kid"] = kid,
                ["guestName"] = (_guestNameBox.Text ?? string.Empty).Trim(),
                ["identityNumber"] = (_identityNumberBox.Text ?? string.Empty).Trim(),
                ["language"] = language,
                ["checkInTime"] = checkInTime,
                ["termsEn"] = (_termsEnBox.Text ?? string.Empty).Trim(),
                ["termsAr"] = (_termsArBox.Text ?? string.Empty).Trim()
            };

            _sendBtn.Enabled = false;
            SetStatus("Sending...", false);
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/consents"))
                {
                    req.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                    using (var res = await _http.SendAsync(req))
                    {
                        var payload = await res.Content.ReadAsStringAsync();
                        if (!res.IsSuccessStatusCode)
                        {
                            SetStatus(string.Format("Failed: {0} {1}", (int)res.StatusCode, res.ReasonPhrase), true);
                            return;
                        }

                        var created = JObject.Parse(payload);
                        var id = created["id"] != null ? created["id"].ToObject<int>() : 0;
                        SetStatus("Approval request sent: #" + id, false);
                        RequestSent?.Invoke(this, new ConsentRequestSentEventArgs(id));
                    }
                }
            }
            catch (Exception ex)
            {
                SetStatus("Failed: " + ex.Message, true);
            }
            finally
            {
                _sendBtn.Enabled = true;
            }
        }

        private void OpenConsentKiosk()
        {
            var baseUrl = (_baseUrlBox.Text ?? string.Empty).Trim().TrimEnd('/');
            var kid = NormalizeKid(_kidBox.Text);
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(kid)) return;
            var url = baseUrl + "/consent?kid=" + Uri.EscapeDataString(kid);
            System.Diagnostics.Process.Start(url);
        }

        private void SetStatus(string text, bool error)
        {
            _statusLabel.Text = text ?? string.Empty;
            _statusLabel.ForeColor = error ? Color.Firebrick : Color.ForestGreen;
        }

        private static string NormalizeKid(string kid)
        {
            var value = (kid ?? string.Empty).Trim();
            return int.TryParse(value, out var parsed) && parsed > 0 ? parsed.ToString() : string.Empty;
        }

        private sealed class LanguageItem
        {
            public string Code { get; }
            private readonly string _text;

            public LanguageItem(string code, string text)
            {
                Code = code;
                _text = text;
            }

            public override string ToString()
            {
                return _text;
            }
        }
    }
}
