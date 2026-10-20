using SkiaSharp;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;

namespace CakeCornerWeb.Services;

/// <summary>Stores re-encoded local photos. Client names and paths are never used.</summary>
public sealed class CatalogImageStore(IWebHostEnvironment environment, ILogger<CatalogImageStore> logger)
{
    public const long MaxBytes = 5 * 1024 * 1024;
    private static readonly Dictionary<string, string> Folders = new(StringComparer.Ordinal)
    {
        ["Biscuit"] = "biscuit",
        ["Cream"] = "cream",
        ["Filling"] = "fillings",
        ["Mousse"] = "mouses"
    };

    public async Task<string> SaveAsync(IFormFile file, string? ingredientType, CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > MaxBytes)
            throw new CatalogRuleException("Оберіть JPEG, PNG або WebP розміром до 5 МБ.");
        var folder = ingredientType == null ? "cakes/uploads" :
            Folders.TryGetValue(ingredientType, out var value) ? "ingredients/" + value :
            throw new CatalogRuleException("Непідтримуваний тип інгредієнта.");
        var relative = $"/images/{folder}/{Guid.NewGuid():N}.jpg";
        await using var input = file.OpenReadStream();
        using var data = SKData.Create(input);
        using var codec = SKCodec.Create(data);
        if (codec == null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
            throw new CatalogRuleException("Файл не є коректним JPEG, PNG або WebP.");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || info.Width > 8192 || info.Height > 8192 || (long)info.Width * info.Height > 16_000_000)
            throw new CatalogRuleException("Зображення завелике: до 8192 px на сторону і 16 мегапікселів.");
        // Decode only the first frame, then write new pixels without source metadata.
        using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new CatalogRuleException("Зображення пошкоджене. Оберіть інший файл.");
        var swapped = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = swapped ? info.Height : info.Width;
        var height = swapped ? info.Width : info.Height;
        var scale = Math.Min(1d, 1600d / Math.Max(width, height));
        using var surface = SKSurface.Create(new SKImageInfo(Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)), SKColorType.Bgra8888, SKAlphaType.Opaque));
        if (surface == null) throw new CatalogRuleException("Не вдалося обробити фотографію.");
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.Scale((float)scale);
        var matrix = Orientation(codec.EncodedOrigin, info.Width, info.Height);
        surface.Canvas.Concat(matrix);
        using var source = SKImage.FromBitmap(bitmap);
        surface.Canvas.DrawImage(source, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        if (encoded == null) throw new CatalogRuleException("Не вдалося зберегти фотографію.");
        var path = PhysicalPath(relative);
        var written = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous);
            written = true;
            await output.WriteAsync(encoded.ToArray(), cancellationToken);
            return relative;
        }
        catch
        {
            if (written) DeleteFailedUpload(relative);
            throw;
        }
    }

    // Only paths returned by SaveAsync; never posted paths or old/shared DB images.
    public void DeleteFailedUpload(string path)
    {
        try { File.Delete(PhysicalPath(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { logger.LogWarning(ex, "Could not remove an uncommitted catalog image."); }
    }

    private string PhysicalPath(string relative) => Path.Combine(environment.WebRootPath,
        relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    private static SKMatrix Orientation(SKEncodedOrigin origin, int width, int height) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, width, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, width, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, height, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, height, -1, 0, width, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, width, 0, 0, 1),
        _ => SKMatrix.Identity
    };
}
