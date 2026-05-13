using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GuestGate.Desktop
{
    public partial class ReceptionForm : Form
    {
        // ========= Settings =========
        private const string DefaultBaseUrl = "http://localhost:5264";

        // ========= State =========
        private readonly HttpClient _http = new HttpClient();
        private HubConnection _hub;

        private string _baseUrl = DefaultBaseUrl;
        private string _currentKid = "K1";
        private string _selectedTemplateId = "";
        private JObject _selectedTemplateDef = new JObject();
        private int _activeSessionId = 0;
        private Button _consentBtn;

        // keeps references to the dynamic inputs by field key
        private readonly Dictionary<string, Control> _fieldControls =
            new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);

        // Avoid opening multiple results windows for the same session
        private readonly HashSet<int> _shownResultWindows = new HashSet<int>();

        public ReceptionForm()
        {
            InitializeComponent(); // ← عناصر الواجهة من الـ Designer
            BuildConsentLauncherButton();

            // قيم افتراضية
            if (_kidBox.Items.Count == 0)
                _kidBox.Items.AddRange(new object[] { "K1", "K2", "K3" });
            _kidBox.SelectedIndex = 0;

            // ربط الأحداث (لا تضعها في Designer لتبقى القراءة واضحة)
            _kidBox.SelectedIndexChanged += async (_, __) => await OnKidChangedAsync();
            _templateBox.SelectedIndexChanged += async (_, __) => await LoadTemplateAndRenderReceptionForm();
            _startBtn.Click += async (_, __) => await StartSessionAsync();
            _endBtn.Click += async (_, __) => await EndSessionAsync();
            _consentBtn.Click += (_, __) => OpenConsentRequestForm();
            _retryTimer.Tick += async (_, __) =>
            {
                if (_hub == null || _hub.State == HubConnectionState.Disconnected)
                    await ConnectHubAsync();
            };

            // تهيئة الحالة
            UpdateUiEnabled(false);
            UpdateStartEnabled();

            // Bootstrap عند إظهار النافذة
            this.Shown += async (_, __) =>
            {
                if (_kidBox.Items.Count > 0) _kidBox.SelectedIndex = 0;
                _currentKid = _kidBox.SelectedItem != null ? _kidBox.SelectedItem.ToString() : "K1";

                await ConnectHubAsync();
                await LoadTemplatesListAsync();
                await RefreshActiveSessionAsync();
                UpdateStartEnabled();
            };
        }

        // =========================================================
        // Consent approval launcher
        // =========================================================
        private void BuildConsentLauncherButton()
        {
            _consentBtn = new Button();
            _consentBtn.Location = new Point(347, 10);
            _consentBtn.Name = "_consentBtn";
            _consentBtn.Size = new Size(120, 55);
            _consentBtn.Text = "Consent form";
            _consentBtn.UseVisualStyleBackColor = true;
            _top.Controls.Add(_consentBtn);

            if (_top.Width < 490)
            {
                _top.Width = 490;
                this.Width = Math.Max(this.Width, 510);
            }
        }

        private void OpenConsentRequestForm()
        {
            var form = new ConsentRequestForm(_baseUrl, () => _currentKid);
            form.RequestSent += delegate (object sender, ConsentRequestSentEventArgs e)
            {
                SetMsg("Approval request sent: #" + e.RequestId);
            };
            form.Show(this);
        }

        // =========================================================
        // UI helpers
        // =========================================================
        private void UpdateUiEnabled(bool online)
        {
            // Keep the top panel available so the standalone consent request form can be opened
            // even when the reception hub connection is retrying.
            _top.Enabled = true;
            _endBtn.Enabled = online;
            if (_consentBtn != null) _consentBtn.Enabled = true;
            _hubLbl.Text = online ? "Hub: online" : "Hub: offline";
            _hubLbl.ForeColor = online ? Color.ForestGreen : Color.Firebrick;
        }
        private void UpdateStartEnabled()
        {
            bool online = _hub != null && _hub.State == HubConnectionState.Connected;
            bool hasTemplate = !string.IsNullOrWhiteSpace(_selectedTemplateId);
            _startBtn.Enabled = online && hasTemplate;
        }
        private void SetSessionLabel()
        {
            _sessionLbl.Text = _activeSessionId > 0 ? string.Format("Session: {0}", _activeSessionId) : "Session: —";
        }
        private void SetTemplateLabel(string id, string version, string name)
        {
            string idv = id ?? _selectedTemplateId;
            string ver = version ?? (_selectedTemplateDef != null ? (_selectedTemplateDef["version"] != null ? _selectedTemplateDef["version"].ToString() : "") : "");
            string nm = name ?? (_selectedTemplateDef != null ? (_selectedTemplateDef["name"] != null ? _selectedTemplateDef["name"].ToString() : "") : "");
            if (!string.IsNullOrEmpty(idv))
            {
                string display = !string.IsNullOrEmpty(nm) ? string.Format("{0} [{1}]", nm, idv) : idv;
                _templateLbl.Text = string.IsNullOrEmpty(ver) ? string.Format("Template: {0}", display) : string.Format("Template: {0} (v{1})", display, ver);
            }
            else _templateLbl.Text = "Template: —";
        }
        private void SetMsg(string text) { _msgLbl.Text = text ?? ""; }

        // =========================================================
        // Kid change
        // =========================================================
        private async Task OnKidChangedAsync()
        {
            _currentKid = _kidBox.SelectedItem != null ? _kidBox.SelectedItem.ToString() : "K1";
            await DisconnectHubAsync();
            await ConnectHubAsync();
            await LoadTemplatesListAsync();
            await RefreshActiveSessionAsync();
            UpdateStartEnabled();
        }

        // =========================================================
        // Templates: list + selection → render dynamic UI
        // =========================================================
        private async Task<(string Name, string Version)> FetchTemplateMetaAsync(string id)
        {
            try
            {
                string url = $"{_baseUrl.TrimEnd('/')}/admin/templates/{Uri.EscapeDataString(id)}";
                string json = await _http.GetStringAsync(url);
                var o = JObject.Parse(json);
                var name = o["name"]?.ToString();
                var version = o["version"]?.ToString();
                if (string.IsNullOrWhiteSpace(name)) name = id; // fallback to id if name missing
                return (name!, version ?? "");
            }
            catch
            {
                // If anything fails, fall back to id as the display name
                return (id, "");
            }
        }

        // Load and bind templates so that ComboBox displays Name and stores Id (templateId)
        private async Task LoadTemplatesListAsync()
        {
            _templateBox.BeginUpdate(); // avoid flicker while binding
            try
            {
                _templateBox.Items.Clear();

                // Display = Name, Value = Id
                _templateBox.DisplayMember = "Name";
                _templateBox.ValueMember = "Id";

                // 1) Get the list
                string url = $"{_baseUrl.TrimEnd('/')}/admin/templates";
                string listJson = await _http.GetStringAsync(url);
                var arr = JArray.Parse(listJson);

                var items = new List<TemplateItem>();

                // 2) Normalize items (support both: array of strings or array of objects)
                foreach (var t in arr)
                {
                    string id = "";
                    string? name = null;
                    string version = "";

                    if (t.Type == JTokenType.String)
                    {
                        // List returned IDs only
                        id = t.ToString();
                    }
                    else if (t is JObject o)
                    {
                        // List returned rich objects
                        id = o["id"]?.ToString() ?? o["templateId"]?.ToString() ?? "";
                        name = o["name"]?.ToString();
                        version = o["version"]?.ToString() ?? "";
                    }

                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    // 3) If name is missing, fetch template meta to get name/version
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        var meta = await FetchTemplateMetaAsync(id);
                        name = meta.Name;
                        version = meta.Version;
                    }

                    items.Add(new TemplateItem { Id = id, Name = name ?? id});
                }

                // Optional: sort by Name for nicer UX
                items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

                // 4) Bind to ComboBox
                foreach (var it in items) _templateBox.Items.Add(it);

                // 5) Try to pre-select the applied template from tablet form-config
                string appliedId = "";
                try
                {
                    // Expected shape: { "templateId": "...", "template": {...}, "prefill": {...} }
                    var cfg = await GetTabletFormConfigAsync();
                    appliedId = cfg["templateId"]?.ToString() ?? "";
                }
                catch { /* ignore */ }

                if (!string.IsNullOrWhiteSpace(appliedId))
                {
                    for (int i = 0; i < _templateBox.Items.Count; i++)
                    {
                        var ti = _templateBox.Items[i] as TemplateItem;
                        if (ti != null && ti.Id.Equals(appliedId, StringComparison.OrdinalIgnoreCase))
                        {
                            _templateBox.SelectedIndex = i;
                            break;
                        }
                    }
                }

                if (_templateBox.SelectedIndex < 0 && _templateBox.Items.Count > 0)
                    _templateBox.SelectedIndex = 0;

                // 6) Render the selected template
                await LoadTemplateAndRenderReceptionForm();
                SetMsg("Templates loaded.");
            }
            catch (Exception ex)
            {
                // Fallback: clear state and render empty form
                _templateBox.Items.Clear();
                _selectedTemplateId = "";
                _selectedTemplateDef = new JObject();
                RenderReceptionForm(_selectedTemplateDef);
                SetTemplateLabel(null, null, null);
                SetMsg("Templates load failed: " + ex.Message);
            }
            finally
            {
                _templateBox.EndUpdate();
                UpdateStartEnabled();
            }
        }


        private async Task LoadTemplateAndRenderReceptionForm()
        {
            // Resolve selected template id:
            // 1) Preferred: SelectedValue (because ComboBox is data-bound: ValueMember = "Id")
            // 2) Fallbacks: SelectedItem as TemplateItem, then plain Text
            _selectedTemplateId =
                (_templateBox.SelectedValue as string)
                ?? (_templateBox.SelectedItem as TemplateItem)?.Id
                ?? (_templateBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(_selectedTemplateId))
            {
                // Nothing selected → render empty form and bail
                RenderReceptionForm(new JObject());
                UpdateStartEnabled();
                return;
            }

            // Helper local function to extract safe string from JObject
            static string? JStr(JObject o, string prop) => o?[prop]?.ToString();

            // -------- Primary path: /admin/templates/{id} --------
            try
            {
                var url = _baseUrl.TrimEnd('/') + "/admin/templates/" + Uri.EscapeDataString(_selectedTemplateId);
                var json = await _http.GetStringAsync(url);
                var template = JObject.Parse(json); // Expected to be the template JSON (fields, name, version, ...)

                _selectedTemplateDef = template;
                RenderReceptionForm(template);

                SetTemplateLabel(
                    _selectedTemplateId,
                    JStr(template, "version"),
                    JStr(template, "name")
                );

                SetMsg("Template loaded.");
            }
            catch
            {
                // -------- Fallback #1: /admin/policies?templateId=...&tabletId=... --------
                try
                {
                    var purl = _baseUrl.TrimEnd('/') + "/admin/policies?templateId="
                               + Uri.EscapeDataString(_selectedTemplateId)
                               + "&tabletId=" + Uri.EscapeDataString(_currentKid);

                    var json = await _http.GetStringAsync(purl);
                    var policies = JObject.Parse(json);

                    var tpl = ConvertPoliciesToTemplate(_selectedTemplateId, policies);

                    _selectedTemplateDef = tpl;
                    RenderReceptionForm(tpl);

                    SetTemplateLabel(
                        _selectedTemplateId,
                        JStr(tpl, "version"),
                        JStr(tpl, "name")
                    );

                    SetMsg("Template loaded from policies.");
                }
                catch
                {
                    // -------- Fallback #2: tablet form-config --------
                    try
                    {
                        var cfg = await GetTabletFormConfigAsync();    // { templateId, template, prefill }
                        var tpl = cfg["template"] as JObject ?? new JObject();

                        _selectedTemplateDef = tpl;
                        RenderReceptionForm(tpl);

                        SetTemplateLabel(
                            _selectedTemplateId,
                            JStr(tpl, "version"),
                            JStr(tpl, "name")
                        );

                        SetMsg("Template loaded from form-config.");
                    }
                    catch (Exception ex3)
                    {
                        // Final fallback: clear UI
                        _selectedTemplateDef = new JObject();
                        RenderReceptionForm(_selectedTemplateDef);
                        SetTemplateLabel(null, null, null);
                        SetMsg("LoadTemplate error: " + ex3.Message);
                    }
                }
            }

            UpdateStartEnabled();
        }


        // =========================================================
        // Dynamic UI
        // =========================================================
        private void RenderReceptionForm(JObject template)
        {
            _prefillTable.SuspendLayout();
            _prefillTable.Controls.Clear();
            _prefillTable.RowStyles.Clear();
            _prefillTable.RowCount = 0;
            _fieldControls.Clear();

            JArray fields = template["fields"] as JArray ?? new JArray();

            List<JObject> toRender = new List<JObject>();
            foreach (JToken tok in fields)
            {
                JObject f = tok as JObject;
                if (f == null) continue;

                string scope = (f["scope"] != null ? f["scope"].ToString() : "StartForm").Trim();
                if (string.Equals(scope, "StartForm", StringComparison.OrdinalIgnoreCase))
                    toRender.Add(f);
            }

            toRender.Sort(delegate (JObject a, JObject b)
            {
                int ao = a["order"] != null ? (a["order"].ToObject<int?>() ?? 0) : 0;
                int bo = b["order"] != null ? (b["order"].ToObject<int?>() ?? 0) : 0;
                return ao.CompareTo(bo);
            });

            if (toRender.Count == 0)
            {
                Label lbl = new Label();
                lbl.Text = "No fields for this template.";
                lbl.AutoSize = true;
                lbl.ForeColor = SystemColors.GrayText;
                lbl.Margin = new Padding(0, 8, 0, 8);

                _prefillTable.RowCount = 1;
                _prefillTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _prefillTable.Controls.Add(lbl, 0, 0);
                _prefillTable.SetColumnSpan(lbl, 2);

                _prefillTable.ResumeLayout(true);
                _prefillTable.PerformLayout();
                _prefillHost.PerformLayout();
                _prefillHost.BringToFront();
                return;
            }

            int r = 0;
            foreach (JObject f in toRender)
            {
                string key = f["key"] != null ? f["key"].ToString() : "";
                string label = f["label"] != null ? f["label"].ToString() : key;
                string dt = (f["dataType"] != null ? f["dataType"].ToString() : "Text").Trim().ToLowerInvariant();

                bool editable = (f["reception"] != null && f["reception"]["editable"] != null) ? (f["reception"]["editable"].ToObject<bool?>() ?? true) : true;
                bool required = (f["reception"] != null && f["reception"]["required"] != null) ? (f["reception"]["required"].ToObject<bool?>() ?? false) : false;

                Label lbl = new Label();
                lbl.Text = label + (required ? " *" : "");
                lbl.AutoSize = true;
                lbl.Margin = new Padding(0, 6, 8, 6);
                lbl.Anchor = AnchorStyles.Left;

                Control input;
                if (dt == "enum")
                {
                    ComboBox cmb = new ComboBox();
                    cmb.DropDownStyle = ComboBoxStyle.DropDownList;
                    cmb.Width = 520;
                    cmb.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                    cmb.Margin = new Padding(0, 3, 0, 3);

                    JArray opts = f["options"] as JArray;
                    if (opts != null)
                    {
                        foreach (JToken o in opts)
                        {
                            if (o.Type == JTokenType.String) cmb.Items.Add(o.ToString());
                            else
                            {
                                JObject oo = o as JObject;
                                if (oo != null)
                                {
                                    string val = oo["value"] != null ? oo["value"].ToString()
                                        : (oo["key"] != null ? oo["key"].ToString()
                                        : (oo["id"] != null ? oo["id"].ToString() : ""));
                                    string txt = oo["label"] != null ? oo["label"].ToString()
                                        : (oo["text"] != null ? oo["text"].ToString() : val);
                                    cmb.Items.Add(string.IsNullOrEmpty(txt) ? val : txt);
                                }
                            }
                        }
                    }
                    input = cmb;
                }
                else
                {
                    TextBox tb = new TextBox();
                    tb.Width = 520;
                    tb.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                    tb.Margin = new Padding(0, 3, 0, 3);
                    input = tb;
                }

                input.Enabled = editable;
                input.Name = "pf_" + key;
                input.Tag = new { key = key, dt = dt, required = required };

                string defVal = f["defaultValue"] != null ? f["defaultValue"].ToString() : null;
                if (!string.IsNullOrEmpty(defVal))
                {
                    ComboBox cb = input as ComboBox;
                    if (cb != null)
                    {
                        int ix = cb.FindStringExact(defVal);
                        cb.SelectedIndex = ix >= 0 ? ix : -1;
                    }
                    else
                    {
                        TextBox tbx = input as TextBox;
                        if (tbx != null) tbx.Text = defVal;
                    }
                }

                _prefillTable.RowCount = r + 1;
                _prefillTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _prefillTable.Controls.Add(lbl, 0, r);
                _prefillTable.Controls.Add(input, 1, r);

                _fieldControls[key] = input;
                r++;
            }

            _prefillTable.ResumeLayout(true);
            _prefillTable.PerformLayout();
            _prefillHost.PerformLayout();
            _prefillHost.BringToFront();
            this.PerformLayout();
            this.Refresh();
        }

        private JObject CollectPrefillFromPreview()
        {
            JObject obj = new JObject();
            foreach (KeyValuePair<string, Control> kv in _fieldControls)
            {
                string key = kv.Key;
                Control c = kv.Value;

                TextBox tb = c as TextBox;
                if (tb != null)
                {
                    string val = (tb.Text ?? string.Empty).Trim();
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(val))
                        obj[key] = val;
                    continue;
                }
                ComboBox cb = c as ComboBox;
                if (cb != null)
                {
                    string val = cb.SelectedItem != null ? cb.SelectedItem.ToString() : null;
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(val))
                        obj[key] = val;
                }
            }
            return obj;
        }

        // =========================================================
        // Hub
        // =========================================================
        private async Task ConnectHubAsync()
        {
            try
            {
                await DisconnectHubAsync();
                string url = _baseUrl.TrimEnd('/') + "/hubs/guest?kid=" + Uri.EscapeDataString(_currentKid);

                _hub = new HubConnectionBuilder()
                    .WithUrl(url)
                    .WithAutomaticReconnect()
                    .Build();

                _hub.Reconnecting += delegate (Exception error)
                {
                    BeginInvoke(new Action(delegate ()
                    {
                        UpdateUiEnabled(false);
                        SetMsg("Reconnecting… " + (error != null ? error.Message : ""));
                        UpdateStartEnabled();
                    }));
                    return Task.CompletedTask;
                };
                _hub.Reconnected += delegate (string connectionId)
                {
                    BeginInvoke(new Action(delegate ()
                    {
                        UpdateUiEnabled(true);
                        SetMsg("Reconnected.");
                        UpdateStartEnabled();
                    }));
                    return Task.CompletedTask;
                };
                _hub.Closed += delegate (Exception error)
                {
                    BeginInvoke(new Action(delegate ()
                    {
                        UpdateUiEnabled(false);
                        SetMsg("Hub closed. " + (error != null ? error.Message : ""));
                        UpdateStartEnabled();
                    }));
                    _retryTimer.Enabled = true;
                    return Task.CompletedTask;
                };

                _hub.On<object>("sessionStarted", delegate (object p)
                {
                    try
                    {
                        JToken t = p as JToken ?? JToken.FromObject(p);
                        JToken sidTok = t.SelectToken("$..sessionId");
                        if (sidTok != null)
                        {
                            int sid;
                            if (int.TryParse(sidTok.ToString(), out sid))
                            {
                                _activeSessionId = sid;
                                BeginInvoke(new Action(SetSessionLabel));
                            }
                        }
                        BeginInvoke(new Action(delegate { SetMsg("sessionStarted."); }));
                    }
                    catch { }
                });

                _hub.On<object>("consentChanged", delegate (object p)
                {
                    try
                    {
                        var token = p as JToken ?? JToken.FromObject(p);
                        BeginInvoke(new Action(delegate
                        {
                            SetMsg("consentChanged: #" + (token["consentId"]?.ToString() ?? "") + " " + (token["status"]?.ToString() ?? ""));
                        }));
                    }
                    catch { }
                });

                _hub.On<JsonElement>("sessionCompleted", je =>
                {
                    try
                    {
                        var token = Newtonsoft.Json.Linq.JToken.Parse(je.GetRawText());

                        int sid = 0;
                        var sidTok = token.SelectToken("$..sessionId");
                        if (sidTok != null) int.TryParse(sidTok.ToString(), out sid);

                        var kid = token.SelectToken("$..kid")?.ToString() ?? _currentKid;
                        var guestInline = token.SelectToken("$..guest") ?? new Newtonsoft.Json.Linq.JObject();

                        BeginInvoke(new Action(async () =>
                        {
                            SetMsg("sessionCompleted.");

                            var title = (sid > 0)
                                ? $"Guest Result — Session {sid} / Kid {kid}"
                                : $"Guest Result — Kid {kid}";

                            if (sid > 0)
                            {
                                // Preferred: source of truth
                                var frm = new ResultsForm(_baseUrl, sid, title) { TopMost = true };
                                frm.Show(this);
                            }
                            else
                            {
                                // Fallback: inline
                                var frm = new ResultsForm(guestInline, title) { TopMost = true };
                                frm.Show(this);

                            }
                            // Reset prefill inputs for next session
                            ResetPrefillInputs();
                            _endBtn.PerformClick();

                        }));
                    }
                    catch (Exception ex)
                    {
                        BeginInvoke(new Action(() => SetMsg("sessionCompleted parse error: " + ex.Message)));
                    }
                });

                await _hub.StartAsync();
                _retryTimer.Enabled = false;
                UpdateUiEnabled(true);
                SetMsg("Hub connected.");
                UpdateStartEnabled();
            }
            catch (Exception ex)
            {
                UpdateUiEnabled(false);
                _retryTimer.Enabled = true;
                SetMsg("Connect failed: " + ex.Message);
                UpdateStartEnabled();
            }
        }

        private async Task DisconnectHubAsync()
        {
            if (_hub == null) return;
            try { await _hub.StopAsync(); await _hub.DisposeAsync(); }
            catch { }
            finally { _hub = null; UpdateUiEnabled(false); UpdateStartEnabled(); }
        }

        // =========================================================
        // REST (sessions + helpers)
        // =========================================================
        private async Task RefreshActiveSessionAsync()
        {
            try
            {
                string url = _baseUrl.TrimEnd('/') + "/api/sessions/active?kid=" + Uri.EscapeDataString(_currentKid);
                using (HttpResponseMessage r = await _http.GetAsync(url))
                {
                    if (r.IsSuccessStatusCode)
                    {
                        string json = await r.Content.ReadAsStringAsync();
                        JObject o = JObject.Parse(json);
                        _activeSessionId = o["sessionId"] != null ? (o["sessionId"].ToObject<int?>() ?? 0) : 0;
                    }
                    else _activeSessionId = 0;
                }
                SetSessionLabel();
            }
            catch { _activeSessionId = 0; SetSessionLabel(); }
        }

        private async Task StartSessionAsync()
        {
            if (_hub == null || _hub.State != HubConnectionState.Connected)
            { SetMsg("Hub is offline."); return; }
            if (string.IsNullOrWhiteSpace(_selectedTemplateId))
            { SetMsg("Select a template first."); return; }

            try
            {
                JObject prefill = CollectPrefillFromPreview();
                string url = _baseUrl.TrimEnd('/') + "/api/sessions/start?kid="
                              + Uri.EscapeDataString(_currentKid)
                              + "&templateId=" + Uri.EscapeDataString(_selectedTemplateId);

                JObject body = new JObject();
                body["prefill"] = prefill;

                using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    req.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                    using (HttpResponseMessage res = await _http.SendAsync(req))
                    {
                        string payload = await res.Content.ReadAsStringAsync();
                        if (res.IsSuccessStatusCode)
                        {
                            JObject o = string.IsNullOrWhiteSpace(payload) ? null : JObject.Parse(payload);
                            _activeSessionId = (o != null && o["sessionId"] != null) ? (o["sessionId"].ToObject<int?>() ?? 0) : 0;
                            SetSessionLabel();
                            SetMsg("Session started.");
                        }
                        else
                        {
                            SetMsg(string.Format("Start failed: {0} {1}", (int)res.StatusCode, res.ReasonPhrase));
                        }
                    }
                }
            }
            catch (Exception ex) { SetMsg("Start failed: " + ex.Message); }
        }

        private async Task EndSessionAsync()
        {
            try
            {
                string baseUrl = _baseUrl.TrimEnd('/');
                string kidEsc = Uri.EscapeDataString(_currentKid);

                // Preferred API path used by the tablet page.
                using (var res = await _http.DeleteAsync($"{baseUrl}/api/sessions/active?kid={kidEsc}"))
                {
                    if (res.IsSuccessStatusCode)
                    {
                        SetMsg("Session cancelled.");
                    }
                    else
                    {
                        // Backward-compatible fallback for deployments that still use the older POST route.
                        using (var fallback = await _http.PostAsync($"{baseUrl}/api/sessions/cancel?kid={kidEsc}", null))
                        {
                            if (fallback.IsSuccessStatusCode) SetMsg("Session cancelled.");
                            else SetMsg($"Cancel failed: {(int)fallback.StatusCode} {fallback.ReasonPhrase}");
                        }
                    }
                }

                _activeSessionId = 0;
                SetSessionLabel();
                ResetPrefillInputs();
                UpdateStartEnabled();
            }
            catch (Exception ex)
            {
                SetMsg("End failed: " + ex.Message);
            }
        }
        private void ResetPrefillInputs()
        {
            foreach (var kv in _fieldControls)
            {
                var c = kv.Value;

                var tb = c as TextBox;
                if (tb != null)
                {
                    tb.Clear();
                    continue;
                }

                var cb = c as ComboBox;
                if (cb != null)
                {
                    cb.SelectedIndex = -1; // no selection
                    continue;
                }
            }

            // Optional: clear status line to “Ready”
            SetMsg("Ready for a new session.");
        }
        private async Task<JObject> GetTabletFormConfigAsync()
        {
            string url = _baseUrl.TrimEnd('/') + "/tablet/" + Uri.EscapeDataString(_currentKid) + "/form-config";
            try
            {
                using (HttpResponseMessage resp = await _http.GetAsync(url))
                {
                    if (!resp.IsSuccessStatusCode) return new JObject();
                    string json = await resp.Content.ReadAsStringAsync();
                    return JObject.Parse(json);
                }
            }
            catch { return new JObject(); }
        }

        private static JObject ConvertPoliciesToTemplate(string templateId, JObject policies)
        {
            JArray fields = policies["fields"] as JArray
                         ?? policies["items"] as JArray
                         ?? policies.SelectToken("$..fields") as JArray
                         ?? new JArray();

            JArray norm = new JArray();
            foreach (JToken f in fields)
            {
                JObject o = f as JObject;
                if (o == null) continue;
                JObject nf = new JObject();
                nf["key"] = o["key"] ?? o["fieldKey"] ?? o["name"] ?? "";
                nf["label"] = o["label"] ?? o["display"] ?? o["name"] ?? o["key"] ?? "";
                nf["dataType"] = (o["dataType"] ?? "Text").ToString();
                nf["scope"] = o["scope"] ?? "StartForm";
                nf["order"] = o["order"] ?? 0;
                nf["guest"] = o["guest"] ?? new JObject();
                nf["reception"] = o["reception"] ?? new JObject();
                nf["validation"] = o["validation"] ?? new JObject();
                nf["options"] = o["options"] ?? new JArray();
                nf["defaultValue"] = o["defaultValue"] ?? JValue.CreateNull();
                norm.Add(nf);
            }

            JObject t = new JObject();
            t["templateId"] = templateId;
            t["name"] = policies["name"] ?? templateId;
            t["version"] = policies["version"] ?? policies.SelectToken("$..version") ?? "";
            t["fields"] = norm;
            return t;
        }

        protected override async void OnFormClosed(FormClosedEventArgs e)
        {
            await DisconnectHubAsync();
            base.OnFormClosed(e);
        }

        private sealed class TemplateItem
        {
            public string Id;
            public string Name;
            public string Version;

            public string Text
            {
                get
                {
                    if (string.IsNullOrEmpty(Version))
                        return string.IsNullOrEmpty(Name) ? Id : Name;
                    return string.Format("{0} (v{1})", string.IsNullOrEmpty(Name) ? Id : Name, Version);
                }
            }
            public override string ToString() { return Text; }
        }

        private void _retryTimer_Tick(object sender, EventArgs e)
        {

        }

        private void _endBtn_Click(object sender, EventArgs e)
        {

        }
    }
}
