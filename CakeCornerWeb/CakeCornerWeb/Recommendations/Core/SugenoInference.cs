namespace CakeCornerWeb.Recommendations.Core;

/// <summary>Two normalized fits, nine zero-order rules. No EF, I/O or budget input.</summary>
public sealed class SugenoInference
{
    public static readonly string[] Levels = ["Low", "Medium", "High"];
    public static readonly double[] DefaultConsequents = [0, .1, .25, .1, .45, .7, .25, .7, 1];
    private readonly double[] consequents;

    public SugenoInference(IEnumerable<double>? ruleConsequents = null)
    {
        consequents = (ruleConsequents ?? DefaultConsequents).ToArray();
        if (consequents.Length != 9 || consequents.Any(q => !double.IsFinite(q) || q is < 0 or > 1))
            throw new ArgumentException("Exactly nine finite rule consequents in [0,1] are required.", nameof(ruleConsequents));
        for (var t = 0; t < 3; t++)
        for (var s = 0; s < 3; s++)
        {
            if ((t < 2 && consequents[(t + 1) * 3 + s] < consequents[t * 3 + s]) ||
                (s < 2 && consequents[t * 3 + s + 1] < consequents[t * 3 + s]))
                throw new ArgumentException("Consequents must be monotone in both fits.", nameof(ruleConsequents));
        }
    }

    public static double[] Membership(double fit)
    {
        if (!double.IsFinite(fit) || fit is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(fit), "A fit must be finite and in [0,1].");
        return [Math.Max(0, 1 - 2 * fit), 1 - Math.Abs(2 * fit - 1), Math.Max(0, 2 * fit - 1)];
    }

    public SugenoEvaluation Evaluate(double tasteFit, double sweetnessFit)
    {
        var taste = Membership(tasteFit);
        var sweetness = Membership(sweetnessFit);
        double numerator = 0, denominator = 0;
        List<SugenoActivation> activated = [];
        for (var t = 0; t < 3; t++)
        for (var s = 0; s < 3; s++)
        {
            var i = t * 3 + s;
            var strength = taste[t] * sweetness[s];
            numerator += strength * consequents[i];
            denominator += strength;
            if (strength > 0)
                activated.Add(new($"R{i + 1:00}", Levels[t], Levels[s], strength, consequents[i]));
        }
        if (!double.IsFinite(denominator) || denominator <= 1e-12)
            throw new InvalidOperationException("The fuzzy rule base has no coverage.");
        return new(numerator / denominator, activated);
    }
}

public sealed record SugenoActivation(string RuleId, string TasteLevel, string SweetnessLevel, double Strength, double Consequent);
public sealed record SugenoEvaluation(double Score, IReadOnlyList<SugenoActivation> Rules);
