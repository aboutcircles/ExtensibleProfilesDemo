namespace ExtensibleProfilesDemo;

internal static class Config
{
    public static readonly string RpcUrl =
        Environment.GetEnvironmentVariable("RPC_URL") ?? "https://rpc.aboutcircles.com";

    public static readonly string NameRegistryAddress = "0xA27566fD89162cC3D40Cb59c87AAaA49B85F3474";

    public static readonly string NameRegistryAbi = @"[
    {
      ""type"": ""function"",
      ""name"": ""updateMetadataDigest"",
      ""inputs"": [
        {
          ""name"": ""_metadataDigest"",
          ""type"": ""bytes32"",
          ""internalType"": ""bytes32""
        }
      ],
      ""outputs"": [],
      ""stateMutability"": ""nonpayable""
    }, {
      ""type"": ""function"",
      ""name"": ""getMetadataDigest"",
      ""inputs"": [
        {
          ""name"": ""_avatar"",
          ""type"": ""address"",
          ""internalType"": ""address""
        }
      ],
      ""outputs"": [
        {
          ""name"": """",
          ""type"": ""bytes32"",
          ""internalType"": ""bytes32""
        }
      ],
      ""stateMutability"": ""view""
    }
  ]";
}