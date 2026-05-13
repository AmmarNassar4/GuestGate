using GuestGate.Api.Models;
using System.Globalization;
using System.Text;

namespace GuestGate.Api.Services
{
    public interface IConsentPdfWriter
    {
        Task<string> WriteAsync(ConsentRequest request, CancellationToken cancellationToken = default);
    }

    public class ConsentPdfWriter : IConsentPdfWriter
    {
        private readonly IWebHostEnvironment _environment;

        public ConsentPdfWriter(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        public async Task<string> WriteAsync(ConsentRequest request, CancellationToken cancellationToken = default)
        {
            var consentsDir = Path.Combine(_environment.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"), "consents");
            Directory.CreateDirectory(consentsDir);

            var fileName = $"consent-{request.Id}-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
            var physicalPath = Path.Combine(consentsDir, fileName);

            var pdf = BuildPdf(request);
            await File.WriteAllBytesAsync(physicalPath, pdf, cancellationToken);
            return $"/consents/{fileName}";
        }

        private static byte[] BuildPdf(ConsentRequest request)
        {
            var terms = string.Equals(request.Language, "ar", StringComparison.OrdinalIgnoreCase)
                ? request.TermsAr
                : request.TermsEn;
            if (string.IsNullOrWhiteSpace(terms)) terms = request.TermsEn;
            if (string.IsNullOrWhiteSpace(terms)) terms = request.TermsAr;

            var lines = new List<string>
            {
                "GuestGate Consent Agreement",
                $"Request ID: {request.Id}",
                $"Kiosk: {request.Kid}",
                $"Guest: {request.GuestName}",
                $"Language: {request.Language}",
                $"Accepted: {(request.Accepted ? "Yes" : "No")}",
                $"Signed at UTC: {(request.SignedAt ?? DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}",
                string.Empty,
                "Terms and conditions:"
            };
            lines.AddRange(WrapForPdf(terms, 92));

            var signatureBytes = TryReadDataUrlImage(request.SignatureImageDataUrl);
            var hasSignature = signatureBytes.Length > 0;

            var content = new StringBuilder();
            content.AppendLine("BT");
            content.AppendLine("/F1 18 Tf 50 792 Td");
            var first = true;
            foreach (var line in lines.Take(38))
            {
                if (!first) content.AppendLine("0 -18 Td");
                content.Append('<').Append(ToPdfUnicodeHex(line)).AppendLine("> Tj");
                first = false;
            }
            content.AppendLine("ET");
            content.AppendLine("0 0 0 RG 50 168 250 70 re S");
            content.AppendLine($"BT /F1 12 Tf 50 150 Td <{ToPdfUnicodeHex("Guest signature:")}> Tj ET");
            if (hasSignature)
            {
                content.AppendLine("q 240 0 0 68 55 172 cm /Sig Do Q");
            }

            var objects = new List<byte[]>();
            objects.Add(Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));
            objects.Add(Encoding.ASCII.GetBytes("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"));

            var resources = hasSignature
                ? "<< /Font << /F1 4 0 R >> /XObject << /Sig 6 0 R >> >>"
                : "<< /Font << /F1 4 0 R >> >>";
            objects.Add(Encoding.ASCII.GetBytes($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 842] /Resources {resources} /Contents 5 0 R >>"));
            objects.Add(Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));

            var contentBytes = Encoding.UTF8.GetBytes(content.ToString());
            objects.Add(StreamObject(contentBytes, ""));

            if (hasSignature)
            {
                objects.Add(StreamObject(signatureBytes, " /Type /XObject /Subtype /Image /Width 600 /Height 180 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode"));
            }

            return ComposePdf(objects);
        }

        private static IEnumerable<string> WrapForPdf(string? value, int max)
        {
            var normalized = (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var paragraph in normalized.Split('\n'))
            {
                var line = paragraph.Trim();
                while (line.Length > max)
                {
                    var breakAt = line.LastIndexOf(' ', Math.Min(max, line.Length - 1));
                    if (breakAt < max / 2) breakAt = max;
                    yield return line[..breakAt].Trim();
                    line = line[breakAt..].Trim();
                }
                yield return line;
            }
        }

        private static byte[] TryReadDataUrlImage(string? dataUrl)
        {
            if (string.IsNullOrWhiteSpace(dataUrl)) return Array.Empty<byte>();
            var comma = dataUrl.IndexOf(',');
            var raw = comma >= 0 ? dataUrl[(comma + 1)..] : dataUrl;
            try { return Convert.FromBase64String(raw); }
            catch { return Array.Empty<byte>(); }
        }

        private static string ToPdfUnicodeHex(string? text)
        {
            var bytes = Encoding.BigEndianUnicode.GetBytes("\uFEFF" + (text ?? string.Empty));
            return Convert.ToHexString(bytes);
        }

        private static byte[] StreamObject(byte[] content, string dictionary)
        {
            var prefix = Encoding.ASCII.GetBytes($"<< /Length {content.Length}{dictionary} >>\nstream\n");
            var suffix = Encoding.ASCII.GetBytes("\nendstream");
            return prefix.Concat(content).Concat(suffix).ToArray();
        }

        private static byte[] ComposePdf(IReadOnlyList<byte[]> objects)
        {
            using var ms = new MemoryStream();
            void WriteAscii(string value) => ms.Write(Encoding.ASCII.GetBytes(value));

            WriteAscii("%PDF-1.4\n");
            var offsets = new List<long> { 0 };
            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(ms.Position);
                WriteAscii($"{i + 1} 0 obj\n");
                ms.Write(objects[i]);
                WriteAscii("\nendobj\n");
            }

            var xref = ms.Position;
            WriteAscii($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
            foreach (var offset in offsets.Skip(1)) WriteAscii($"{offset:0000000000} 00000 n \n");
            WriteAscii($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
            return ms.ToArray();
        }
    }
}
