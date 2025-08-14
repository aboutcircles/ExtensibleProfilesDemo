using System.Text;

namespace Circles.Profile.UI.Services;

/// <summary>
/// Proxy wrapper around <see cref="Circles.Profiles.Interfaces.IIpfsStore"/>
/// that makes it accessible to UI-layer code.
/// </summary>
public sealed class IpfsStore : Circles.Profiles.Interfaces.IIpfsStore
{
