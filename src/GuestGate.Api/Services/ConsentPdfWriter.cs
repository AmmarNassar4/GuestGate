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
            var signatureSize = TryGetJpegSize(signatureBytes);
            var hasSignature = signatureBytes.Length > 0 && signatureSize is not null;

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
            const double sigBoxX = 50;
            const double sigBoxY = 168;
            const double sigBoxW = 250;
            const double sigBoxH = 70;
            const double sigPad = 5;

            content.AppendLine(FormattableString.Invariant($"0 0 0 RG {sigBoxX} {sigBoxY} {sigBoxW} {sigBoxH} re S"));
            content.AppendLine($"BT /F1 12 Tf 50 150 Td <{ToPdfUnicodeHex("Guest signature:")}> Tj ET");
            if (hasSignature && signatureSize is { } sigSize)
            {
                var fitted = FitInside(sigSize.Width, sigSize.Height, sigBoxX + sigPad, sigBoxY + sigPad, sigBoxW - sigPad * 2, sigBoxH - sigPad * 2);
                content.AppendLine(FormattableString.Invariant($"q {fitted.W:0.###} 0 0 {fitted.H:0.###} {fitted.X:0.###} {fitted.Y:0.###} cm /Sig Do Q"));
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

            if (hasSignature && signatureSize is { } size)
            {
                objects.Add(StreamObject(signatureBytes, $" /Type /XObject /Subtype /Image /Width {size.Width} /Height {size.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode"));
            }

            return ComposePdf(objects);
        }


        private static (double X, double Y, double W, double H) FitInside(int imageWidth, int imageHeight, double boxX, double boxY, double boxW, double boxH)
        {
            var scale = Math.Min(boxW / imageWidth, boxH / imageHeight);
            var w = imageWidth * scale;
            var h = imageHeight * scale;
            return (boxX + (boxW - w) / 2, boxY + (boxH - h) / 2, w, h);
        }

        private static (int Width, int Height)? TryGetJpegSize(byte[] bytes)
        {
            if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8) return null;

            var i = 2;
            while (i + 9 < bytes.Length)
            {
                if (bytes[i] != 0xFF)
                {
                    i++;
                    continue;
                }

                while (i < bytes.Length && bytes[i] == 0xFF) i++;
                if (i >= bytes.Length) return null;

                var marker = bytes[i++];
                if (marker == 0xD9 || marker == 0xDA) return null;
                if (i + 1 >= bytes.Length) return null;

                var length = (bytes[i] << 8) + bytes[i + 1];
                if (length < 2 || i + length > bytes.Length) return null;

                var isSof =
                    (marker >= 0xC0 && marker <= 0xC3) ||
                    (marker >= 0xC5 && marker <= 0xC7) ||
                    (marker >= 0xC9 && marker <= 0xCB) ||
                    (marker >= 0xCD && marker <= 0xCF);

                if (isSof)
                {
                    var height = (bytes[i + 3] << 8) + bytes[i + 4];
                    var width = (bytes[i + 5] << 8) + bytes[i + 6];
                    return (width, height);
                }

                i += length;
            }

            return null;
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
