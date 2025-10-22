namespace Circles.Profiles.Interfaces;

public interface INonceRegistry
{
    public bool SeenBefore(string nonce);
}