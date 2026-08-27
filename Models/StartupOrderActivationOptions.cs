namespace KaApi.Models;

public class StartupOrderActivationOptions
{
    public const string SectionName = "StartupOrderActivation";

    public bool Enabled { get; set; } = true;

    public string Url { get; set; } = "https://b2b.profiplus.bg/cronporachki.aspx";

    public int TimeoutSeconds { get; set; } = 30;

    public bool FailStartupOnError { get; set; } = false;
}
