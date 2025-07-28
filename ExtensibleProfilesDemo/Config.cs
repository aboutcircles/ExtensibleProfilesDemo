namespace ExtensibleProfilesDemo;

internal static class Config
{
    public static readonly string RpcUrl =
        Environment.GetEnvironmentVariable("RPC_URL") ?? "https://rpc.aboutcircles.com";
}