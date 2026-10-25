namespace CakeCornerWeb.Recommendations.Core;

public static class RecommendationDefaults
{
    public static RecommendationMethod Create()
    {
        var method = new RecommendationMethod();
        for (var t = 0; t < 3; t++)
        for (var s = 0; s < 3; s++)
        {
            var index = t * 3 + s;
            method.Rules.Add(new($"R{index + 1:00}", SugenoInference.Levels[t], SugenoInference.Levels[s],
                SugenoInference.DefaultConsequents[index]));
        }
        return method;
    }
}
