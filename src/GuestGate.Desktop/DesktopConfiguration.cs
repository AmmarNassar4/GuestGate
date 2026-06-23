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
        public string PreferredTemplateName { get; private set; } = string.Empty;
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

                string preferredTemplateName = ReadSetting(appSettings, "PreferredTemplateName");
                if (string.IsNullOrWhiteSpace(preferredTemplateName))
                    preferredTemplateName = ReadSetting(appSettings, "PreferredTemplate");
                if (string.IsNullOrWhiteSpace(preferredTemplateName))
                    preferredTemplateName = ReadSetting(appSettings, "PreferredTemplateId");

                if (!string.IsNullOrWhiteSpace(preferredTemplateName))
                    settings.PreferredTemplateName = preferredTemplateName.Trim();
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
            SetupPreferredTemplateSelector(form, settings.PreferredTemplateName);
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

        private static void SetupPreferredTemplateSelector(ReceptionForm form, string preferredTemplateName)
        {
            preferredTemplateName = (preferredTemplateName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(preferredTemplateName))
                return;

            var templateBox = GetPrivateField<ComboBox>(form, "_templateBox");
            if (templateBox == null)
                return;

            if (TrySelectTemplate(templateBox, preferredTemplateName))
                return;

            var timer = new Timer { Interval = 250 };
            int remainingAttempts = 80;

            timer.Tick += delegate
            {
                remainingAttempts--;
                bool selected = TrySelectTemplate(templateBox, preferredTemplateName);
                if (selected || remainingAttempts <= 0)
                {
                    timer.Stop();
                    timer.Dispose();
                }
            };

            timer.Start();
        }

        private static bool TrySelectTemplate(ComboBox templateBox, string preferredTemplateName)
        {
            if (templateBox == null || string.IsNullOrWhiteSpace(preferredTemplateName))
                return false;

            for (int index = 0; index < templateBox.Items.Count; index++)
            {
                object item = templateBox.Items[index];
                if (TemplateMatches(item, preferredTemplateName))
                {
                    if (templateBox.SelectedIndex != index)
                        templateBox.SelectedIndex = index;
                    return true;
                }
            }

            return false;
        }

        private static bool TemplateMatches(object item, string preferredTemplateName)
        {
            if (item == null)
                return false;

            string preferred = NormalizeComparableText(preferredTemplateName);
            if (string.IsNullOrWhiteSpace(preferred))
                return false;

            foreach (string memberName in new[] { "Id", "Name", "Text" })
            {
                string value = GetPublicOrPrivateMemberValue(item, memberName);
                if (TextsMatch(value, preferred))
                    return true;
            }

            return TextsMatch(item.ToString(), preferred);
        }

        private static bool TextsMatch(string value, string preferred)
        {
            string normalizedValue = NormalizeComparableText(value);
            return string.Equals(normalizedValue, preferred, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeComparableText(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static string GetPublicOrPrivateMemberValue(object target, string memberName)
        {
            if (target == null)
                return string.Empty;

            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var field = type.GetField(memberName, flags);
            if (field != null)
                return field.GetValue(target)?.ToString() ?? string.Empty;

            var property = type.GetProperty(memberName, flags);
            if (property != null)
                return property.GetValue(target, null)?.ToString() ?? string.Empty;

            return string.Empty;
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
