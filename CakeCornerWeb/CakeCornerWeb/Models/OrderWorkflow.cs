namespace CakeCornerWeb.Models;

public static class OrderWorkflow
{
    public const string Pending = "PendingReview",
                        Confirmed = "Confirmed",
                        Preparing = "InPreparation",
                        Ready = "ReadyToPickup",
                        Completed = "Completed",
                        Cancelled = "Cancelled",
                        Rejected = "Rejected";

    public static readonly string[] Terminal = [Completed, Cancelled, Rejected];
    public static readonly Dictionary<string, string> Labels = new()
    {
        [Pending] = "Очікує розгляду",
        ["WaitingPrepayment"] = "Очікує передоплати",
        [Confirmed] = "Підтверджено",
        [Preparing] = "Виготовлюється",
        [Ready] = "Готовий до вичачі",
        [Completed] = "Виконано",
        [Cancelled] = "Скасовано",
        [Rejected] = "Відхилено"
    };

    public static string Label(string? code) => code != null && Labels.TryGetValue(code, out var label) ? label : "Невідомий статус";
    public static string[] Next(string? code) => code switch
    {
        Pending => [Confirmed, "WaitingPrepayment"],
        "WaitingPrepayment" => [Confirmed, Cancelled],
        Confirmed => [Preparing, Cancelled],
        Preparing => [Ready, Cancelled],
        Ready => [Completed, Cancelled],
        _ => []
    };

    public static bool CanCancel(string? code) => code is Pending or Confirmed or "WaitingPrepayment";
}

public class OrderRuleException(string message) : Exception(message);