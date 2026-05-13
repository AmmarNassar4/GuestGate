using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GuestGate.Desktop
{
    public class MainForm : Form
    {
        private TextBox _baseUrl;
        private TextBox _kid;
        private ComboBox _templateId;
        private Button _loadTplBtn;
        private Button _loadTplListBtn;
        private Button _connectBtn;
        private Button _disconnectBtn;
        private Button _startBtn;
        private Button _cancelBtn;
        private Button _openTabletBtn;
        private TableLayoutPanel _prefillTable;
        private TextBox _log;

        private readonly HttpClient _http = new HttpClient();
        private HubConnection _hub;
        private readonly Dictionary<string, Control> _fieldControls = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<int> _shownResultWindows = new HashSet<int>();

        public MainForm()
        {
            Text = "GuestGate Reception (Windows Forms)";
            Width = 980; Height = 720;
            StartPosition = FormStartPosition.CenterScreen;
            InitializeUi();
        }

        private void InitializeUi()
        {
            var pnlTop = new Panel { Left = 0, Top = 0, Width = ClientSize.Width, Height = 80, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right };
            Controls.Add(pnlTop);

            var y = 10;
            pnlTop.Controls.Add(new Label { Left = 10, Top = y + 4, Width = 90, Text = "Base URL:" });
            _baseUrl = new TextBox { Left = 100, Top = y, Width = 460, Text = "http://localhost:5264" };
            pnlTop.Controls.Add(_baseUrl);
            pnlTop.Controls.Add(new Label { Left = 570, Top = y + 4, Width = 90, Text = "Tablet (kid):" });
            _kid = new TextBox { Left = 660, Top = y, Width = 100, Text = "K1" };
            pnlTop.Controls.Add(_kid);
            _openTabletBtn = new Button { Left = 770, Top = y, Width = 180, Text = "Open Tablet in Browser" };
            _openTabletBtn.Click += (_, __) => System.Diagnostics.Process.Start(_baseUrl.Text.Trim().TrimEnd('/') + "/index.html?kid=" + Uri.EscapeDataString(_kid.Text.Trim()));
            pnlTop.Controls.Add(_openTabletBtn);

            y += 32;
            pnlTop.Controls.Add(new Label { Left = 10, Top = y + 4, Width = 90, Text = "Template:" });
            _templateId = new ComboBox { Left = 100, Top = y, Width = 220, DropDownStyle = ComboBoxStyle.DropDown };
            pnlTop.Controls.Add(_templateId);
            _loadTplBtn = new Button { Left = 330, Top = y, Width = 120, Text = "Load Template" };
            _loadTplListBtn = new Button { Left = 456, Top = y, Width = 120, Text = "Load IDs" };
            _loadTplBtn.Click += async (_, __) => await LoadTemplateAndRenderReceptionForm();
            _loadTplListBtn.Click += async (_, __) => await LoadTemplateIds();
            pnlTop.Controls.Add(_loadTplBtn);
            pnlTop.Controls.Add(_loadTplListBtn);

            _connectBtn = new Button { Left = 590, Top = y, Width = 100, Text = "Connect Hub" };
            _disconnectBtn = new Button { Left = 696, Top = y, Width = 100, Text = "Disconnect" };
            _startBtn = new Button { Left = 802, Top = y, Width = 70, Text = "Start" };
            _cancelBtn = new Button { Left = 878, Top = y, Width = 70, Text = "Cancel" };
            _connectBtn.Click += async (_, __) => await ConnectHub();
            _disconnectBtn.Click += async (_, __) => await DisconnectHub();
            _startBtn.Click += async (_, __) => await StartSession();
            _cancelBtn.Click += async (_, __) => await CancelSession();
            pnlTop.Controls.AddRange(new Control[]{ _connectBtn, _disconnectBtn, _startBtn, _cancelBtn });

            _prefillTable = new TableLayoutPanel { Left = 10, Top = 90, Width = ClientSize.Width - 20, Height = 420, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, ColumnCount = 2, AutoScroll = true };
            _prefillTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            _prefillTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            Controls.Add(_prefillTable);

            _log = new TextBox { Left = 10, Top = 520, Width = ClientSize.Width - 20, Height = 150, Multiline = true, ScrollBars = ScrollBars.Both, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom | AnchorStyles.Top };
            Controls.Add(_log);

            this.Resize += (_, __) =>
            {
                pnlTop.Width = ClientSize.Width;
                _prefillTable.Width = ClientSize.Width - 20;
                _log.Width = ClientSize.Width - 20;
                _log.Top = ClientSize.Height - 180;
                _prefillTable.Height = _log.Top - _prefillTable.Top - 10;
            };
        }

        private async Task LoadTemplateIds()
        {
            try
            {
                var baseUrl = _baseUrl.Text.Trim().TrimEnd('/');
                var json = await _http.GetStringAsync($"{baseUrl}/admin/templates");
                var arr = JArray.Parse(json);
                _templateId.Items.Clear();
                foreach (var id in arr.Select(a => a.ToString()))
                    _templateId.Items.Add(id);
                if (_templateId.Items.Count > 0 && _templateId.SelectedIndex < 0) _templateId.SelectedIndex = 0;
                Log($"Loaded {arr.Count} template id(s).");
            }
            catch (Exception ex) { Log("LoadTemplateIds error: " + ex.Message); }
        }

        private async Task LoadTemplateAndRenderReceptionForm()
        {
            var id = _templateId.Text.Trim();
            if (string.IsNullOrWhiteSpace(id)) { Log("TemplateId is required."); return; }
            try
            {
                var baseUrl = _baseUrl.Text.Trim().TrimEnd('/');
                var json = await _http.GetStringAsync($"{baseUrl}/admin/templates/{Uri.EscapeDataString(id)}");
                var template = JObject.Parse(json);
                RenderReceptionForm(template);
                Log($"Template '{id}' loaded and rendered.");
            }
            catch (Exception ex) { Log("LoadTemplate error: " + ex.Message); }
        }

        private void RenderReceptionForm(JObject template)
        {
            _prefillTable.SuspendLayout();
            _prefillTable.Controls.Clear();
            _prefillTable.RowStyles.Clear();
            _prefillTable.RowCount = 0;
            _fieldControls.Clear();

            var fields = template["fields"] as JArray ?? new JArray();
            var toRender = fields.Select(f => f as JObject).Where(f => f != null)
                .Where(f => {
                    var scope = f.Value<string>("scope");
                    if (!string.IsNullOrEmpty(scope) && !string.Equals(scope, "Reception", StringComparison.OrdinalIgnoreCase))
                    {
                        var rec = f["reception"] as JObject;
                        if (rec != null && rec.Value<bool?>("editable") == true) return true;
                        return false;
                    }
                    return true;
                })
                .OrderBy(f => f.Value<int?>("order") ?? 0).ToList();

            int r = 0;
            foreach (var f in toRender)
            {
                var key = f.Value<string>("key") ?? Guid.NewGuid().ToString("N");
                var label = f.Value<string>("label") ?? key;
                var dtype = (f.Value<string>("dataType") ?? "Text").ToLowerInvariant();
                var reception = f["reception"] as JObject;
                var editable = reception?.Value<bool?>("editable") != false;
                var required = reception?.Value<bool?>("required") == true;

                var lbl = new Label { Text = label + (required ? " *" : ""), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
                Control input;
                if (dtype == "enum")
                {
                    var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
                    var opts = f["options"] as JArray ?? new JArray();
                    foreach (var o in opts)
                    {
                        if (o.Type == JTokenType.String) cb.Items.Add(o.ToString());
                        else if (o is JObject oj)
                        {
                            var val = oj.Value<string>("value") ?? oj.Value<string>("key") ?? oj.Value<string>("id") ?? oj.Value<string>("text");
                            var text = oj.Value<string>("label") ?? oj.Value<string>("text") ?? val;
                            cb.Items.Add(text ?? val ?? "");
                        }
                    }
                    if (cb.Items.Count > 0) cb.SelectedIndex = 0;
                    input = cb;
                }
                else if (dtype == "date")
                {
                    var dt = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Width = 180 };
                    input = dt;
                }
                else
                {
                    var tb = new TextBox { Width = 320 };
                    input = tb;
                }

                input.Enabled = editable;
                input.Tag = new { key, dtype, required };

                _prefillTable.RowCount = r + 1;
                _prefillTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _prefillTable.Controls.Add(lbl, 0, r);
                _prefillTable.Controls.Add(input, 1, r);
                _fieldControls[key] = input;
                r++;
            }
            _prefillTable.ResumeLayout();
        }

        private JObject CollectPrefillJson()
        {
            var obj = new JObject();
            foreach (var kv in _fieldControls)
            {
                var key = kv.Key;
                var ctrl = kv.Value;
                object val = null;
                if (ctrl is DateTimePicker dt) val = dt.Value.ToString("yyyy-MM-dd");
                else if (ctrl is ComboBox cb) val = cb.SelectedItem?.ToString();
                else if (ctrl is TextBox tb) val = tb.Text?.Trim();
                obj[key] = val == null ? JValue.CreateNull() : JToken.FromObject(val);
            }
            return obj;
        }

        private async Task ConnectHub()
        {
            try
            {
                var kid = _kid.Text.Trim();
                if (string.IsNullOrWhiteSpace(kid)) { Log("kid is required"); return; }
                var baseUrl = _baseUrl.Text.Trim().TrimEnd('/');

                _hub = new HubConnectionBuilder()
                    .WithUrl($"{baseUrl}/hubs/guest?kid={Uri.EscapeDataString(kid)}")
                    .WithAutomaticReconnect()
                    .Build();

                _hub.On<object>("sessionStarted", payload => Log($"sessionStarted: {JsonConvert.SerializeObject(payload)}"));
                _hub.On<JsonElement>("sessionCompleted", je =>
                {
                    try
                    {
                        // Get the real JSON from the element
                        var token = JToken.Parse(je.GetRawText());
                        Log("sessionCompleted payload:\n" + token.ToString(Newtonsoft.Json.Formatting.Indented));

                        // Robust lookups (case/shape tolerant)
                        var guest = token.SelectToken("$..guest") ?? new JObject();
                        var kid = token.SelectToken("$..kid")?.ToString() ?? "";

                        int sessionId = 0;
                        var sid = token.SelectToken("$..sessionId");
                        if (sid != null) int.TryParse(sid.ToString(), out sessionId);

                        if (this.IsHandleCreated)
                        {
                            this.BeginInvoke(new Action(() =>
                            {
                                if (sessionId > 0 && _shownResultWindows.Contains(sessionId)) return;
                                if (sessionId > 0) _shownResultWindows.Add(sessionId);

                                var title = sessionId > 0
                                    ? $"Guest Result — Session {sessionId} / Kid {kid}"
                                    : $"Guest Result — Kid {kid}";

                                // OPTION A: show inline guest JSON (what you asked for)
                                var frm = new ResultsForm(guest, title) { TopMost = true };
                                frm.Show(this);

                                // OPTION B (if you prefer fetching from API instead):
                                // var baseUrl = _baseUrl.Text.Trim().TrimEnd('/');
                                // var frm = new ResultsForm(baseUrl, sessionId, title) { TopMost = true };
                                // frm.Show(this);
                            }));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("sessionCompleted parse error: " + ex.Message);
                    }
                });





                await _hub.StartAsync();
                Log("Hub connected.");
            }
            catch (Exception ex) { Log("ConnectHub error: " + ex.Message); }
        }

        private async Task DisconnectHub()
        {
            if (_hub != null)
            {
                try { await _hub.StopAsync(); await _hub.DisposeAsync(); Log("Hub disconnected."); }
                catch (Exception ex) { Log("DisconnectHub error: " + ex.Message); }
                finally { _hub = null; }
            }
        }

        private async Task StartSession()
        {
            try
            {
                var baseUrl = _baseUrl.Text.Trim().TrimEnd('/');
                var kid = _kid.Text.Trim();
                var templateId = _templateId.Text.Trim();
                if (string.IsNullOrWhiteSpace(kid)) { Log("kid is required."); return; }
                if (string.IsNullOrWhiteSpace(templateId)) { Log("templateId is required."); return; }

                var prefillObj = CollectPrefillJson();
                var bodyObj = new JObject { ["kid"] = kid, ["templateId"] = templateId, ["prefill"] = prefillObj };

                var req = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"{baseUrl}/api/sessions/start");
                req.Content = new StringContent(bodyObj.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                var res = await _http.SendAsync(req);
                var payload = await res.Content.ReadAsStringAsync();
                Log($"Start -> {res.StatusCode}: {payload}");
            }
            catch (Exception ex) { Log("StartSession error: " + ex.Message); }
        }

        private async Task CancelSession()
        {
            try
            {
                var baseUrl = _baseUrl.Text.Trim().TrimEnd('/');
                var kid = _kid.Text.Trim();
                var url = $"{baseUrl}/api/sessions/active?kid={Uri.EscapeDataString(kid)}";
                var res = await _http.DeleteAsync(url);
                Log($"Cancel -> {res.StatusCode}");
            }
            catch (Exception ex) { Log("CancelSession error: " + ex.Message); }
        }
        private static JToken? FindPropCI(JToken token, string name)
        {
            if (token is JObject obj)
            {
                var hit = obj.Properties()
                             .FirstOrDefault(p => string.Equals(p.Name, name, System.StringComparison.OrdinalIgnoreCase));
                if (hit != null) return hit.Value;

                foreach (var child in obj.Properties().Select(p => p.Value))
                {
                    var found = FindPropCI(child, name);
                    if (found != null) return found;
                }
                return null;
            }
            if (token is JArray arr)
            {
                foreach (var child in arr)
                {
                    var found = FindPropCI(child, name);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static int GetIntCI(JToken token, string name)
        {
            var t = FindPropCI(token, name);
            if (t == null) return 0;
            if (t.Type == JTokenType.Integer) return (int)t;
            if (int.TryParse(t.ToString(), out var n)) return n;
            return 0;
        }

        private void Log(string text) => _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
    }
}
