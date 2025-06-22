using System.Text.Json.Serialization;

namespace ExtensibleProfilesDemo.Model;

/// <summary>Metadata that lets readers verify link signatures.</summary>
public sealed record SigningKey
{
    [JsonPropertyName("publicKey")]  public string PublicKey  { get; init; } = string.Empty;
    [JsonPropertyName("validFrom")]  public long   ValidFrom  { get; init; }
    [JsonPropertyName("validTo")]    public long?  ValidTo    { get; init; }
    [JsonPropertyName("revokedAt")]  public long?  RevokedAt  { get; init; }
}