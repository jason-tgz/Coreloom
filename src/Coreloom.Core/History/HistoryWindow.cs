namespace Coreloom.Core.History;

/// <summary>Selectable chart time ranges.</summary>
public enum HistoryWindow
{
    Seconds60,
    Minutes5,
    Minutes15,
    Hour1,
}

public static class HistoryWindowExtensions
{
    public static TimeSpan ToTimeSpan(this HistoryWindow window) => window switch
    {
        HistoryWindow.Seconds60 => TimeSpan.FromSeconds(60),
        HistoryWindow.Minutes5 => TimeSpan.FromMinutes(5),
        HistoryWindow.Minutes15 => TimeSpan.FromMinutes(15),
        HistoryWindow.Hour1 => TimeSpan.FromHours(1),
        _ => TimeSpan.FromSeconds(60),
    };

    public static string ToLabel(this HistoryWindow window) => window switch
    {
        HistoryWindow.Seconds60 => "60s",
        HistoryWindow.Minutes5 => "5m",
        HistoryWindow.Minutes15 => "15m",
        HistoryWindow.Hour1 => "1h",
        _ => "60s",
    };

    public static string ToLongLabel(this HistoryWindow window) => window switch
    {
        HistoryWindow.Seconds60 => "60 seconds",
        HistoryWindow.Minutes5 => "5 minutes",
        HistoryWindow.Minutes15 => "15 minutes",
        HistoryWindow.Hour1 => "1 hour",
        _ => "60 seconds",
    };
}
