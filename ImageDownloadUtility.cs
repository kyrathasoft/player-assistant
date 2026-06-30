using System.Drawing;
using SkiaSharp;

namespace PlayerAssistant
{
    internal static class ImageDownloadUtility
    {
        private static readonly HttpClient HttpClient = CreateHttpClient();

        public static async Task<Image> DownloadImageAsync(string url, CancellationToken cancellationToken = default)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException("A valid absolute image URL is required.", nameof(url));
            }

            return await DownloadImageAsync(uri, cancellationToken);
        }

        public static async Task<Image> DownloadImageAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException("Only HTTP and HTTPS image URLs are supported.", nameof(uri));
            }

            if (RpolAuthUtility.IsRpolUri(uri))
            {
                var rpolResponse = await RpolAuthUtility.GetResponseAsync(uri, cancellationToken);
                EnsureImageContentType(rpolResponse.ContentType);

                await using var rpolImageStream = new MemoryStream(rpolResponse.Body, writable: false);
                using var rpolImage = Image.FromStream(rpolImageStream);
                return new Bitmap(rpolImage);
            }

            using var response = await HttpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"The URI did not return an image. Content type: {mediaType}");
            }

            await using var imageStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var image = Image.FromStream(imageStream);
            return new Bitmap(image);
        }

        public static async Task DownloadImageFileAsync(
            string url,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException("A valid absolute image URL is required.", nameof(url));
            }

            await DownloadImageFileAsync(uri, destinationPath, cancellationToken);
        }

        public static async Task DownloadImageFileAsync(
            Uri uri,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException("Only HTTP and HTTPS image URLs are supported.", nameof(uri));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

            var outputDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            if (RpolAuthUtility.IsRpolUri(uri))
            {
                var rpolResponse = await RpolAuthUtility.GetResponseAsync(uri, cancellationToken);
                EnsureImageContentType(rpolResponse.ContentType);
                await WriteBytesToFileAsync(destinationPath, rpolResponse.Body, cancellationToken);
                return;
            }

            using var response = await HttpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"The URI did not return an image. Content type: {mediaType}");
            }

            var tempPath = $"{destinationPath}.tmp";
            try
            {
                await using (var imageStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var outputStream = File.Create(tempPath))
                {
                    await imageStream.CopyToAsync(outputStream, cancellationToken);
                }

                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }

                File.Move(tempPath, destinationPath);
                FileDownloadCounters.AddCompletedDownload(destinationPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        public static async Task DownloadImageFileAsPngAsync(
            string url,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException("A valid absolute image URL is required.", nameof(url));
            }

            await DownloadImageFileAsPngAsync(uri, destinationPath, cancellationToken);
        }

        public static async Task DownloadImageFileAsPngAsync(
            Uri uri,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException("Only HTTP and HTTPS image URLs are supported.", nameof(uri));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

            var outputDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            if (RpolAuthUtility.IsRpolUri(uri))
            {
                var rpolResponse = await RpolAuthUtility.GetResponseAsync(uri, cancellationToken);
                EnsureImageContentType(rpolResponse.ContentType);
                await WritePngBytesToFileAsync(destinationPath, rpolResponse.Body, cancellationToken);
                return;
            }

            using var response = await HttpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"The URI did not return an image. Content type: {mediaType}");
            }

            var tempPath = $"{destinationPath}.tmp";
            var tempSourcePath = $"{destinationPath}.source";
            try
            {
                await using (var imageStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                {
                    await using (var sourceStream = File.Create(tempSourcePath))
                    {
                        await imageStream.CopyToAsync(sourceStream, cancellationToken);
                    }

                    using var bitmap = SKBitmap.Decode(tempSourcePath)
                        ?? throw new InvalidOperationException("The URI did not return a supported raster image.");
                    using var image = SKImage.FromBitmap(bitmap);
                    using var data = image.Encode(SKEncodedImageFormat.Png, 100)
                        ?? throw new InvalidOperationException("The image could not be encoded as PNG.");
                    await using var outputStream = File.Create(tempPath);
                    data.SaveTo(outputStream);
                }

                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }

                File.Move(tempPath, destinationPath);
                FileDownloadCounters.AddCompletedDownload(destinationPath);
            }
            finally
            {
                if (File.Exists(tempSourcePath))
                {
                    File.Delete(tempSourcePath);
                }

                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        internal static bool HasVisiblePixels(string imagePath)
        {
            try
            {
                using var bitmap = SKBitmap.Decode(imagePath);
                if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
                {
                    return false;
                }

                var step = Math.Max(1, Math.Min(bitmap.Width, bitmap.Height) / 250);
                for (var y = 0; y < bitmap.Height; y += step)
                {
                    for (var x = 0; x < bitmap.Width; x += step)
                    {
                        if (bitmap.GetPixel(x, y).Alpha > 0)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PlayerAssistant/1.0");
            return httpClient;
        }

        private static void EnsureImageContentType(string? mediaType)
        {
            if (mediaType is not null && !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"The URI did not return an image. Content type: {mediaType}");
            }
        }

        private static async Task WriteBytesToFileAsync(
            string destinationPath,
            byte[] bytes,
            CancellationToken cancellationToken)
        {
            var tempPath = $"{destinationPath}.tmp";
            try
            {
                await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);

                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }

                File.Move(tempPath, destinationPath);
                FileDownloadCounters.AddCompletedDownload(destinationPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static async Task WritePngBytesToFileAsync(
            string destinationPath,
            byte[] bytes,
            CancellationToken cancellationToken)
        {
            var tempSourcePath = $"{destinationPath}.source";
            try
            {
                await File.WriteAllBytesAsync(tempSourcePath, bytes, cancellationToken);
                await ConvertSourceImageToPngAsync(tempSourcePath, destinationPath);
            }
            finally
            {
                if (File.Exists(tempSourcePath))
                {
                    File.Delete(tempSourcePath);
                }
            }
        }

        private static async Task ConvertSourceImageToPngAsync(string sourcePath, string destinationPath)
        {
            var tempPath = $"{destinationPath}.tmp";
            try
            {
                using var bitmap = SKBitmap.Decode(sourcePath)
                    ?? throw new InvalidOperationException("The URI did not return a supported raster image.");
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100)
                    ?? throw new InvalidOperationException("The image could not be encoded as PNG.");
                await using (var outputStream = File.Create(tempPath))
                {
                    data.SaveTo(outputStream);
                }

                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }

                File.Move(tempPath, destinationPath);
                FileDownloadCounters.AddCompletedDownload(destinationPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
    }
}
