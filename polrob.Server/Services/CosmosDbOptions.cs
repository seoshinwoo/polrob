public sealed class CosmosDbOptions
{
    public const string SectionName = "CosmosDb";

    public string DatabaseId { get; set; } = "PolRobDB";
    public string UsersContainerId { get; set; } = "Users";
    public string GameRecordsContainerId { get; set; } = "GameRecords";
    public string PlayerGameRecordsContainerId { get; set; } = "PlayerGameRecords";
}
