using CakeCornerWeb.Models.Entities;

namespace CakeCornerWeb.Services;

/// <summary>Resolves database image URLs to files served locally from wwwroot.</summary>
public sealed class IngredientImageService(IWebHostEnvironment environment)
{
    public const string Placeholder = "/images/ingredient-placeholder.svg";
    private static readonly Dictionary<string, string> Folders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Biscuit"] = "biscuit",
        ["Cream"] = "cream",
        ["Filling"] = "fillings",
        ["Mousse"] = "mouses"
    };

    public string ForIngredient(Ingredient? ingredient, string type) => Resolve(ingredient?.IngredientImagePath, type);

    public string Resolve(string? databasePath, string? type = null)
    {
        var path = Normalize(databasePath);
        if (path == null) return Placeholder;
        if (Exists(path)) return path;
        // Older records contain /images/ingredients/name.jpeg. The actual files
        // in main are grouped by type; keep the database unchanged and locate them.
        if (path.StartsWith("/images/ingredients/", StringComparison.Ordinal) &&
            type != null && Folders.TryGetValue(type, out var folder))
        {
            var candidate = $"/images/ingredients/{folder}/{path[(path.LastIndexOf('/') + 1)..]}";
            if (Exists(candidate)) return candidate;
        }
        return Placeholder;
    }

    private bool Exists(string path) => environment.WebRootFileProvider.GetFileInfo(path.TrimStart('/')).Exists;

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = path.Trim().Replace('\\', '/');
        if (path.StartsWith("~/", StringComparison.Ordinal)) path = path[1..];
        if (path.StartsWith("images/", StringComparison.Ordinal) || path.StartsWith("media/", StringComparison.Ordinal)) path = "/" + path;
        try { path = Uri.UnescapeDataString(path); }
        catch (UriFormatException) { return null; }
        if (!(path.StartsWith("/images/", StringComparison.Ordinal) || path.StartsWith("/media/", StringComparison.Ordinal)) ||
            path.IndexOfAny(['?', '#', ':', '\0', '\\']) >= 0 || path.Split('/').Any(p => p is "." or "..")) return null;
        return path;
    }
}