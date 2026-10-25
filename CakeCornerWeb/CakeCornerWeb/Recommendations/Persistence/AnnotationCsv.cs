using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace CakeCornerWeb.Recommendations.Persistence;

public sealed record CsvProfile(int Id, string? Type, double? Sweetness, Dictionary<string, double> Flavors,
    string? Availability, string Reviewer, DateTime? ReviewedAt, string Source, string[] Contains, string[] MayContain);
public sealed record CsvCompatibility(int A, int B, string Decision, string Reason, string Reviewer, DateTime? ReviewedAt, string Source);
public sealed record CsvAvailability(int Id, string Status, string Reviewer, DateTime? ReviewedAt, string Source);
public sealed record AnnotationCsvPackage(List<CsvProfile> Ingredients, List<CsvProfile> Cakes,
    List<CsvCompatibility> Compatibility, List<CsvAvailability> Availability);

/// <summary>Strict RFC-style CSV parsing. Import is always unverified Demo/Draft, never expert publication.</summary>
public static class AnnotationCsv
{
    public const int MaxRows = 100_000;
    private static readonly Dictionary<string, string[]> Headers = new(StringComparer.Ordinal)
    {
        ["ingredient-features.csv"] = ["ingredient_id", "ingredient_type", "sweetness", "flavor_vector_json", "availability", "allergen_status", "contains", "may_contain", "annotation_version", "reviewer_ref", "verified_at_utc", "source_ref"],
        ["ready-cake-features.csv"] = ["cake_id", "sweetness", "flavor_vector_json", "allergen_status", "contains", "may_contain", "annotation_version", "reviewer_ref", "verified_at_utc", "source_ref"],
        ["compatibility.csv"] = ["ingredient_a_id", "ingredient_b_id", "decision", "reason", "annotation_version", "reviewer_ref", "verified_at_utc", "source_ref"],
        ["ready-variant-availability.csv"] = ["cake_weight_id", "availability", "annotation_version", "reviewer_ref", "verified_at_utc", "source_ref"]
    };

    public static AnnotationCsvPackage Parse(IReadOnlyDictionary<string, Stream> streams, string version, RecommendationMethod method)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > 80 || !System.Text.RegularExpressions.Regex.IsMatch(version, "^[A-Za-z0-9][A-Za-z0-9._-]*$"))
            throw new RecommendationValidationException("Версія має містити 1–80 латинських літер, цифр, крапок, дефісів або підкреслень.");
        if (streams.Count != Headers.Count || Headers.Keys.Any(k => !streams.ContainsKey(k)))
            throw new RecommendationValidationException("Потрібні всі чотири CSV-файли з початковими назвами.");
        var tables = Headers.ToDictionary(h => h.Key, h => Read(streams[h.Key], h.Value, version, h.Key));
        CsvProfile Profile(Dictionary<string, string> row, string idKey, bool ingredient)
        {
            var type = ingredient ? row["ingredient_type"] : null;
            if (ingredient && !method.Slots.Contains(type, StringComparer.Ordinal)) Fail("Непідтримуваний тип інгредієнта.");
            if (row["allergen_status"] != "unknown")
                Fail("Демонстраційний імпорт приймає лише allergen_status=unknown. Експертне підтвердження виконується окремо.");
            double? sweetness = string.IsNullOrWhiteSpace(row["sweetness"]) ? null : Number(row["sweetness"], 0, 5);
            Dictionary<string, double> vector;
            try
            {
                using var json = JsonDocument.Parse(row["flavor_vector_json"]);
                if (json.RootElement.ValueKind != JsonValueKind.Object) Fail("flavor_vector_json має бути JSON-об'єктом.");
                vector = new(StringComparer.Ordinal);
                foreach (var p in json.RootElement.EnumerateObject())
                {
                    if (!p.Value.TryGetDouble(out var value) || !double.IsFinite(value) || value is < 0 or > 1 || !vector.TryAdd(p.Name, value))
                        Fail("Некоректна або повторна інтенсивність смаку.");
                }
                if (!vector.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(method.FlavorTags)) Fail("Вектор має містити всі й лише відомі смакові теги.");
            }
            catch (JsonException) { throw new RecommendationValidationException("Некоректний JSON смакового вектора."); }
            return new(Id(row[idKey]), type, sweetness, vector, ingredient ? State(row["availability"]) : null,
                Text(row["reviewer_ref"], 100), Date(row["verified_at_utc"]), Text(row["source_ref"], 500),
                AllergenList(row["contains"], method), AllergenList(row["may_contain"], method));
        }
        var ingredients = tables["ingredient-features.csv"].Select(r => Profile(r, "ingredient_id", true)).ToList();
        var cakes = tables["ready-cake-features.csv"].Select(r => Profile(r, "cake_id", false)).ToList();
        var compatibility = tables["compatibility.csv"].Select(r =>
        {
            var a = Id(r["ingredient_a_id"]); var b = Id(r["ingredient_b_id"]);
            if (a >= b) Fail("Пара має бути канонічною: ingredient_a_id < ingredient_b_id.");
            if (r["decision"] is not ("allowed" or "forbidden" or "unknown")) Fail("Невідомий статус сумісності.");
            return new CsvCompatibility(a, b, r["decision"], Text(r["reason"], 1000), Text(r["reviewer_ref"], 100), Date(r["verified_at_utc"]), Text(r["source_ref"], 500));
        }).ToList();
        var availability = tables["ready-variant-availability.csv"].Select(r => new CsvAvailability(Id(r["cake_weight_id"]), State(r["availability"]), Text(r["reviewer_ref"], 100), Date(r["verified_at_utc"]), Text(r["source_ref"], 500))).ToList();
        Unique(ingredients.Select(x => x.Id)); Unique(cakes.Select(x => x.Id)); Unique(availability.Select(x => x.Id));
        if (compatibility.Select(p => (p.A, p.B)).Distinct().Count() != compatibility.Count) Fail("Повторні пари сумісності.");
        var ids = ingredients.ToDictionary(x => x.Id);
        foreach (var p in compatibility)
            if (!ids.TryGetValue(p.A, out var a) || !ids.TryGetValue(p.B, out var b) || a.Type == b.Type)
                Fail("Пара має посилатися на два імпортовані інгредієнти різних типів.");
        if (ingredients.Count == 0 || cakes.Count == 0 || availability.Count == 0) Fail("Таблиці профілів і доступності не можуть бути порожніми.");
        return new(ingredients, cakes, compatibility, availability);
    }

    private static List<Dictionary<string, string>> Read(Stream stream, string[] headers, string version, string name)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 4096, leaveOpen: true);
        using var parser = new TextFieldParser(reader) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        try
        {
            var actual = parser.ReadFields();
            if (actual == null || !actual.SequenceEqual(headers, StringComparer.Ordinal)) Fail(name + ": неправильні заголовки або порядок стовпців.");
            var result = new List<Dictionary<string, string>>();
            while (!parser.EndOfData)
            {
                if (result.Count >= MaxRows) Fail(name + ": забагато рядків.");
                var fields = parser.ReadFields();
                if (fields == null || fields.Length != headers.Length) Fail(name + ": неправильна кількість клітинок.");
                if (fields.Any(f => f.Length > 20_000)) Fail(name + ": надто велика клітинка.");
                var row = headers.Zip(fields).ToDictionary(p => p.First, p => p.Second, StringComparer.Ordinal);
                if (row["annotation_version"] != version) Fail(name + ": annotation_version має збігатися з версією імпорту.");
                result.Add(row);
            }
            return result;
        }
        catch (MalformedLineException) { throw new RecommendationValidationException(name + ": пошкоджена структура CSV."); }
        catch (DecoderFallbackException) { throw new RecommendationValidationException(name + ": очікується UTF-8."); }
    }
    private static string[] AllergenList(string value, RecommendationMethod method)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var values = value.Split([';', '|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToArray();
        if (values.Any(x => !method.AllergenCodes.Contains(x, StringComparer.Ordinal))) Fail("Невідомий код алергену.");
        return values;
    }
    private static int Id(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : throw new RecommendationValidationException("ID має бути додатним цілим числом.");
    private static double Number(string text, double min, double max) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) && n >= min && n <= max ? n : throw new RecommendationValidationException("Оцінка поза допустимим діапазоном.");
    private static string State(string value) => value is "available" or "unavailable" or "unknown" ? value : throw new RecommendationValidationException("Невідомий статус доступності.");
    private static string Text(string value, int max) => value.Length <= max ? value : throw new RecommendationValidationException("Значення CSV перевищує довжину поля БД.");
    private static DateTime? Date(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) || parsed > DateTimeOffset.UtcNow.AddMinutes(5))
            Fail("Некоректна дата перевірки CSV.");
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).UtcDateTime;
    }
    private static void Unique(IEnumerable<int> ids) { var a = ids.ToArray(); if (a.Length != a.Distinct().Count()) Fail("Повторний ID у CSV."); }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string message) => throw new RecommendationValidationException(message);
}
