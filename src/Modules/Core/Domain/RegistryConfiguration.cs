namespace Vatowiec.Core.Domain;

public sealed class RegistryConfiguration
{
    private RegistryConfiguration() { }
    public RegistryConfiguration(string source, string testUrl, string productionUrl, string? testWl2Url = null, string? productionWl2Url = null)
    {
        Source = source; TestUrl = testUrl; ProductionUrl = productionUrl;
        TestWl2Url = testWl2Url; ProductionWl2Url = productionWl2Url;
    }
    public string Source { get; private set; } = "";
    public string Environment { get; private set; } = "Test";
    public string TestUrl { get; private set; } = "";
    public string ProductionUrl { get; private set; } = "";
    public string? TestWl2Url { get; private set; }
    public string? ProductionWl2Url { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public void Update(string environment, string testUrl, string productionUrl, string? testWl2Url, string? productionWl2Url)
    {
        Environment = environment; TestUrl = testUrl; ProductionUrl = productionUrl;
        TestWl2Url = testWl2Url; ProductionWl2Url = productionWl2Url;
    }
}

public sealed class RegistryConfigurationChange
{
    private RegistryConfigurationChange() { }
    public RegistryConfigurationChange(string source, Guid actorId, Guid actorIssuerId, string actorName,
        DateTimeOffset occurredAtUtc, string beforeJson, string afterJson)
    {
        Source = source; ActorId = actorId; ActorIssuerId = actorIssuerId; ActorName = actorName;
        OccurredAtUtc = occurredAtUtc; BeforeJson = beforeJson; AfterJson = afterJson;
    }
    public long Id { get; private set; }
    public string Source { get; private set; } = "";
    public Guid ActorId { get; private set; }
    public Guid ActorIssuerId { get; private set; }
    public string ActorName { get; private set; } = "";
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string BeforeJson { get; private set; } = "";
    public string AfterJson { get; private set; } = "";
}
