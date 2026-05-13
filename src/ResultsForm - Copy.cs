using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Windows.Forms;

namespace GuestGate.Desktop
{
    public partial class ResultsForm : Form
    {
        private enum SourceMode { Inline, ApiBySessionId, ApiByToken }

        // --- State ---
        private readonly Dictionary<string, string> _pairs =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private SourceMode _mode = SourceMode.Inline;
        private string _baseUrl = "";
        private int _sessionId = 0;
        private Guid _editToken = Guid.Empty;
        private JToken _inlineGuest = new JObject();

        private static readonly HttpClient _http = new HttpClient();

        // ---------- Constructors ----------

        // 1) INLINE guest JSON (already available)
        public ResultsForm(JToken guestJson, string title = "Guest Result")
        {
            InitializeComponent();
            _mode = SourceMode.Inline;
            _inlineGuest = guestJson ?? new JObject();

            if (!string.IsNullOrWhiteSpace(title)) this.Text = title;
            BindPairsFromToken(_inlineGuest);
        }

        // 2) API: by SessionId
        public ResultsForm(string baseUrl, int sessionId, string title = "Guest Result")
        {
            InitializeComponent();
            _mode = SourceMode.ApiBySessionId;
            _baseUrl = (baseUrl ?? "").Trim().TrimEnd('/');
            _sessionId = sessionId;

            if (!string.IsNullOrWhiteSpace(title)) this.Text = title;
            this.Shown += async (_, __) => await LoadFromApiAsync();
        }

        // 3) API: by EditToken (et)
        public ResultsForm(string baseUrl, Guid editToken, string title = "Guest Result")
        {
            InitializeComponent();
            _mode = SourceMode.ApiByToken;
            _baseUrl = (baseUrl ?? "").Trim().TrimEnd('/');
            _editToken = editToken;

            if (!string.IsNullOrWhiteSpace(title)) this.Text = title;
            this.Shown += async (_, __) => await LoadFromApiAsync();
        }

        // ---------- Data binding / rendering ----------

        private void BindPairsFromToken(JToken token)
        {
            _pairs.Clear();
            foreach (KeyValuePair<string, string> kv in Flatten(token, ""))
            {
                string val = kv.Value == null ? "" : kv.Value.Trim();
                if (!string.IsNullOrEmpty(val))
                    _pairs[kv.Key] = val;
            }

            // preferred ordering
            List<string> preferred = new List<string> { "NationalId", "FullName", "Mobile", "Email", "ArrivalDate" };
            List<string> orderedKeys = new List<string>();
            foreach (string k in preferred)
                if (_pairs.ContainsKey(k)) orderedKeys.Add(k);

            List<string> rest = new List<string>();
            foreach (string k in _pairs.Keys)
                if (!orderedKeys.Contains(k)) rest.Add(k);
            rest.Sort(StringComparer.OrdinalIgnoreCase);
            orderedKeys.AddRange(rest);

            // rebuild table
            _table.SuspendLayout();
            _table.Controls.Clear();
            _table.RowStyles.Clear();
            _table.RowCount = 0;

            if (orderedKeys.Count == 0)
            {
                _table.RowCount = 1;
                _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                Label empty = new Label();
                empty.Text = "No guest fields to display.";
                empty.AutoSize = true;
                empty.ForeColor = System.Drawing.SystemColors.GrayText;
                _table.Controls.Add(empty, 0, 0);
                _table.SetColumnSpan(empty, 3);

                _table.ResumeLayout(true);
                _table.PerformLayout();
                return;
            }

            int row = 0;
            foreach (string key in orderedKeys)
            {
                string value = _pairs[key] ?? "";

                _table.RowCount = row + 1;
                _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                Label lbl = new Label();
                lbl.Text = key;
                lbl.AutoSize = true;
                lbl.Margin = new Padding(0, 6, 8, 6);
                lbl.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
                lbl.Anchor = AnchorStyles.Left;

                TextBox txt = new TextBox();
                txt.Text = value;
                txt.ReadOnly = true;
                txt.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                txt.Margin = new Padding(0, 3, 6, 3);
                txt.Width = 420;

                Button btn = new Button();
                btn.Text = "Copy";
                btn.AutoSize = true;
                btn.Margin = new Padding(0, 2, 0, 2);
                btn.Click += delegate
                {
                    try
                    {
                        Clipboard.SetText(txt.Text ?? "");
                        MessageBox.Show("Copied to clipboard.", "Copy", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Copy failed: " + ex.Message, "Copy", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };

                _table.Controls.Add(lbl, 0, row);
                _table.Controls.Add(txt, 1, row);
                _table.Controls.Add(btn, 2, row);

                row++;
            }

            _table.ResumeLayout(true);
            _table.PerformLayout();

            _titleInfo.Text = string.Format("Fields: {0}", orderedKeys.Count);
        }

        private async System.Threading.Tasks.Task LoadFromApiAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_baseUrl))
                    throw new InvalidOperationException("Base URL is empty.");

                string url;
                if (_mode == SourceMode.ApiBySessionId)
                {
                    if (_sessionId <= 0) throw new InvalidOperationException("Invalid sessionId.");
                    url = _baseUrl + "/api/sessions/" + _sessionId + "/result";
                }
                else if (_mode == SourceMode.ApiByToken)
                {
                    if (_editToken == Guid.Empty) throw new InvalidOperationException("Invalid EditToken.");
                    url = _baseUrl + "/api/sessions/by-token?et=" + _editToken.ToString();
                }
                else
                {
                    return; // Inline mode
                }

                string json = await _http.GetStringAsync(url);
                JObject obj = JObject.Parse(json);
                JToken guest = obj["guest"] ?? new JObject();
                BindPairsFromToken(guest);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fetch failed: " + ex.Message, "Fetch", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---------- Helpers ----------
        private static IEnumerable<KeyValuePair<string, string>> Flatten(JToken token, string prefix)
        {
            if (token == null || token.Type == JTokenType.Null)
                yield break;

            JObject o = token as JObject;
            if (o != null)
            {
                foreach (JProperty p in o.Properties())
                {
                    string key = string.IsNullOrEmpty(prefix) ? p.Name : (prefix + "." + p.Name);
                    foreach (KeyValuePair<string, string> kv in Flatten(p.Value, key))
                        yield return kv;
                }
                yield break;
            }

            JArray a = token as JArray;
            if (a != null)
            {
                for (int i = 0; i < a.Count; i++)
                {
                    string key = string.IsNullOrEmpty(prefix) ? "[" + i + "]" : (prefix + "[" + i + "]");
                    foreach (KeyValuePair<string, string> kv in Flatten(a[i], key))
                        yield return kv;
                }
                yield break;
            }

            // primitive
            string value = (token.Type == JTokenType.String)
                ? token.ToString()
                : token.ToString(Newtonsoft.Json.Formatting.None);
            yield return new KeyValuePair<string, string>(prefix, value);
        }
    }
}
