using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
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

        public ResultsForm(JToken guestJson, string title = "Guest Result")
        {
            InitializeComponent();
            _mode = SourceMode.Inline;
            _inlineGuest = guestJson ?? new JObject();
            if (!string.IsNullOrWhiteSpace(title)) this.Text = title;

            // If the passed token is actually the whole response, try to extract guest first
            var guest = TryExtractGuestFromAny(_inlineGuest as JObject) ?? _inlineGuest;
            BindPairsFromToken(guest);
        }

        public ResultsForm(string baseUrl, int sessionId, string title = "Guest Result")
        {
            InitializeComponent();
            _mode = SourceMode.ApiBySessionId;
            _baseUrl = (baseUrl ?? "").Trim().TrimEnd('/');
            _sessionId = sessionId;
            if (!string.IsNullOrWhiteSpace(title)) this.Text = title;

            this.Shown += async (_, __) => await LoadFromApiAsync();
        }

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

            // Flatten the token
            foreach (var kv in Flatten(token, ""))
            {
                var val = kv.Value == null ? "" : kv.Value.Trim();
                if (!string.IsNullOrEmpty(val))
                    _pairs[kv.Key] = val;
            }

            // If still empty and token is an object with a single property that contains JSON string → parse and retry
            if (_pairs.Count == 0)
            {
                var obj = token as JObject;
                if (obj != null && obj.Count == 1)
                {
                    foreach (var p in obj.Properties())
                    {
                        if (p.Value != null && p.Value.Type == JTokenType.String)
                        {
                            var s = p.Value.ToString().Trim();
                            if (LooksLikeJson(s))
                            {
                                try
                                {
                                    var parsed = JToken.Parse(s);
                                    foreach (var kv in Flatten(parsed, ""))
                                    {
                                        var val = kv.Value == null ? "" : kv.Value.Trim();
                                        if (!string.IsNullOrEmpty(val))
                                            _pairs[kv.Key] = val;
                                    }
                                }
                                catch { /* ignore */ }
                            }
                        }
                    }
                }
            }

            // Preferred order then rest
            var preferred = new List<string> { "NationalId", "FullName", "Mobile", "Email", "ArrivalDate" };
            var ordered = new List<string>();
            foreach (var k in preferred) if (_pairs.ContainsKey(k)) ordered.Add(k);
            var rest = new List<string>();
            foreach (var k in _pairs.Keys) if (!ordered.Contains(k)) rest.Add(k);
            rest.Sort(StringComparer.OrdinalIgnoreCase);
            ordered.AddRange(rest);

            // Rebuild the table
            _table.SuspendLayout();
            _table.Controls.Clear();
            _table.RowStyles.Clear();
            _table.RowCount = 0;

            if (ordered.Count == 0)
            {
                // FINAL SAFETY: render the whole response (so the user never sees empty)
                var whole = token as JObject;
                if (whole != null && whole.Count > 0)
                {
                    ordered = new List<string>();
                    foreach (var prop in whole.Properties())
                        ordered.Add(prop.Name);
                }
            }

            if (ordered.Count == 0)
            {
                _table.RowCount = 1;
                _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var empty = new Label
                {
                    Text = "No guest fields to display.",
                    AutoSize = true,
                    ForeColor = SystemColors.GrayText
                };
                _table.Controls.Add(empty, 0, 0);
                _table.SetColumnSpan(empty, 3);
                _table.ResumeLayout(true);
                _table.PerformLayout();
                _scrollHost.PerformLayout();
                _titleInfo.Text = "Fields: 0";
                return;
            }

            int row = 0;
            foreach (var key in ordered)
            {
                // prefer flattened value if we have it, else try direct property read
                string value;
                if (!_pairs.TryGetValue(key, out value))
                {
                    var v = (token as JObject)?[key];
                    value = v == null || v.Type == JTokenType.Null
                        ? ""
                        : (v.Type == JTokenType.String ? v.ToString() : v.ToString(Newtonsoft.Json.Formatting.None));
                }

                _table.RowCount = row + 1;
                _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                var lbl = new Label
                {
                    Text = key,
                    AutoSize = true,
                    Margin = new Padding(0, 6, 8, 6),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Anchor = AnchorStyles.Left
                };

                var txt = new TextBox
                {
                    Text = value ?? "",
                    ReadOnly = true,
                    Anchor = AnchorStyles.Left | AnchorStyles.Right,
                    Margin = new Padding(0, 3, 6, 3),
                    Width = 420
                };

                var btn = new Button
                {
                    Text = "Copy",
                    AutoSize = true,
                    Margin = new Padding(0, 2, 0, 2)
                };
                btn.Click += delegate
                {
                    try { Clipboard.SetText(txt.Text ?? "");  }
                    catch (Exception ex) { MessageBox.Show("Copy failed: " + ex.Message, "Copy", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                };

                _table.Controls.Add(lbl, 0, row);
                _table.Controls.Add(txt, 1, row);
                _table.Controls.Add(btn, 2, row);

                row++;
            }

            _table.ResumeLayout(true);
            _table.PerformLayout();
            _scrollHost.PerformLayout();
            _titleInfo.Text = "Fields: " + ordered.Count;
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

                var json = await _http.GetStringAsync(url);
                var root = JObject.Parse(json);

                // Try to find guest; if not found, fall back to whole response.
                var guest = TryExtractGuestFromAny(root) ?? (JToken)root;

                BindPairsFromToken(guest);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fetch failed: " + ex.Message, "Fetch", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Robust guest extraction from many shapes
        private static JToken TryExtractGuestFromAny(JObject root)
        {
            if (root == null) return null;

            // direct object
            var g = root["guest"];
            if (g != null && g.Type != JTokenType.Null)
            {
                // guest may contain dataJson string
                var dj = g["dataJson"] ?? g["DataJson"];
                if (dj != null && dj.Type == JTokenType.String && LooksLikeJson(dj.ToString()))
                {
                    try { return JToken.Parse(dj.ToString()); } catch { }
                }
                return g;
            }

            // other common places
            string[] paths = new[]
            {
                "result.guest",
                "payload.guest",
                "data.guest",
                "data",
                "guest.data",
                "dataJson",
                "DataJson"
            };

            for (int i = 0; i < paths.Length; i++)
            {
                var t = root.SelectToken(paths[i]);
                if (t == null) continue;

                if (t.Type == JTokenType.String && LooksLikeJson(t.ToString()))
                {
                    try { return JToken.Parse(t.ToString()); } catch { }
                }

                if (t.Type != JTokenType.Null)
                    return t;
            }

            return null;
        }

        private static bool LooksLikeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            return (s.Length > 1 && ((s[0] == '{' && s[s.Length - 1] == '}') || (s[0] == '[' && s[s.Length - 1] == ']')));
        }

        // ---------- Flatten helper ----------
        private static IEnumerable<KeyValuePair<string, string>> Flatten(JToken token, string prefix)
        {
            if (token == null || token.Type == JTokenType.Null)
                yield break;

            var obj = token as JObject;
            if (obj != null)
            {
                foreach (var prop in obj.Properties())
                {
                    var key = string.IsNullOrEmpty(prefix) ? prop.Name : (prefix + "." + prop.Name);
                    foreach (var kv in Flatten(prop.Value, key))
                        yield return kv;
                }
                yield break;
            }

            var arr = token as JArray;
            if (arr != null)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    var key = string.IsNullOrEmpty(prefix) ? "[" + i + "]" : (prefix + "[" + i + "]");
                    foreach (var kv in Flatten(arr[i], key))
                        yield return kv;
                }
                yield break;
            }

            // primitive
            var value = (token.Type == JTokenType.String)
                ? token.ToString()
                : token.ToString(Newtonsoft.Json.Formatting.None);
            yield return new KeyValuePair<string, string>(prefix, value);
        }
    }
}
