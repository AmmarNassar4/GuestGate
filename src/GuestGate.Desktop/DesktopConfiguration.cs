using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Xml.Linq;

namespace GuestGate.Desktop
{
    internal sealed class DesktopConfiguration
    {
        private const string ConfigFileName = "config.xml";
        private const string FallbackBaseUrl = "http://localhost:5264";

        public string DefaultBaseUrl { get; private set; } = FallbackBaseUrl;
        public string ApiBasePath { get; private set; } = "/api";
        public List<string> Kids { get; private set; } = new List<string> { "1", "2", "3" };
        public string PreferredKid { get; private set; } = "1";
        public string ConfigFilePath { get; private set; } = string.Empty;

        public static DesktopConfiguration Load()
        {
            var settings = new DesktopConfiguration();
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string path = Path.Combine(baseDirectory, ConfigFileName);
            settings.ConfigFilePath = path;

            if (!File.Exists(path))
                return settings;

            try
            {
                var document = XDocument.Load(path);
                var appSettings = document.Root?.Element("appSettings");
                if (appSettings == null)
                    return settings;

                string baseUrl = ReadSetting(appSettings, "DefaultBaseUrl");
                if (!string.IsNullOrWhiteSpace(baseUrl))
                    settings.DefaultBaseUrl = NormalizeBaseUrl(baseUrl);

                string apiBasePath = ReadSetting(appSettings, "ApiBasePath");
                if (!string.IsNullOrWhiteSpace(apiBasePath))
                    settings.ApiBasePath = NormalizeBasePath(apiBasePath);

                string kids = ReadSetting(appSettings, "Kids");
                if (!string.IsNullOrWhiteSpace(kids))
                {
                    var parsedKids = kids
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => NormalizeKid(x))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (parsedKids.Count > 0)
                        settings.Kids = parsedKids;
                }

                string preferredKid = ReadSetting(appSettings, "PreferredKid");
                if (!string.IsNullOrWhiteSpace(preferredKid))
                    settings.PreferredKid = NormalizeKid(preferredKid);
            }
            catch
            {
                // Keep safe defaults when the external config file is missing or invalid.
            }

            return settings;
        }

        public static void ApplyTo(ReceptionForm form, DesktopConfiguration settings)
        {
            if (form == null || settings == null)
                return;

            SetPrivateField(form, "_baseUrl", settings.DefaultBaseUrl);

            var kidBox = GetPrivateField<ComboBox>(form, "_kidBox");
            if (kidBox == null)
                return;

            var orderedKids = settings.Kids
                .Select(NormalizeKid)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (orderedKids.Count == 0)
                orderedKids.AddRange(new[] { "1", "2", "3" });

            string preferredKid = NormalizeKid(settings.PreferredKid);
            if (!string.IsNullOrWhiteSpace(preferredKid))
            {
                int preferredIndex = orderedKids.FindIndex(x => string.Equals(x, preferredKid, StringComparison.OrdinalIgnoreCase));
                if (preferredIndex > 0)
                {
                    orderedKids.RemoveAt(preferredIndex);
                    orderedKids.Insert(0, preferredKid);
                }
            }

            kidBox.Items.Clear();
            kidBox.Items.AddRange(orderedKids.Cast<object>().ToArray());
            if (kidBox.Items.Count > 0)
                kidBox.SelectedIndex = 0;

            SetPrivateField(form, "_currentKid", kidBox.SelectedItem?.ToString() ?? orderedKids[0]);
        }

        public static string NormalizeKid(string value)
        {
            string text = (value ?? string.Empty).Trim();
            if (text.StartsWith("K", StringComparison.OrdinalIgnoreCase) && text.Length > 1)
            {
                string numberPart = text.Substring(1).Trim();
                if (int.TryParse(numberPart, out int number) && number > 0)
                    return number.ToString();
            }

            return text;
        }

        private static string ReadSetting(XElement appSettings, string name)
        {
            var element = appSettings.Element(name);
            if (element != null)
                return element.Value?.Trim() ?? string.Empty;

            var addElement = appSettings
                .Elements("add")
                .FirstOrDefault(x => string.Equals((string)x.Attribute("key"), name, StringComparison.OrdinalIgnoreCase));

            return addElement?.Attribute("value")?.Value?.Trim() ?? string.Empty;
        }

        private static string NormalizeBaseUrl(string value)
        {
            return (value ?? string.Empty).Trim().TrimEnd('/');
        }

        private static string NormalizeBasePath(string value)
        {
            string text = (value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text) || text == "/")
                return string.Empty;

            if (!text.StartsWith("/"))
                text = "/" + text;

            return text.TrimEnd('/');
        }

        private static T GetPrivateField<T>(object target, string fieldName) where T : class
        {
            return target.GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(target) as T;
        }

        private static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field?.SetValue(target, value);
        }
    }
}
