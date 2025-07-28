using Circles.Profiles.Models;

namespace Circles.Profiles.Interfaces;

/// <summary>Creates and verifies <see cref="CustomDataLink"/> signatures.</summary>
public interface ILinkSigner
{
    CustomDataLink Sign(CustomDataLink link, string privKeyHex);
}