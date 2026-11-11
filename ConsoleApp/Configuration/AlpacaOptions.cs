namespace ConsoleApp.Configuration;

public sealed class AlpacaOptions
{
    public const string SectionName = "Alpaca";

    public string ApiKeyId { get; set; } = string.Empty;
    public string ApiSecretKey { get; set; } = string.Empty;
    public string Feed { get; set; } = "iex";
}
