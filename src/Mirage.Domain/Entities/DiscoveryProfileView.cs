using Mirage.Domain.Common;

namespace Mirage.Domain.Entities;

public sealed class DiscoveryProfileView : Entity
{
    private DiscoveryProfileView() { }

    public DiscoveryProfileView(Guid viewerUserId, Guid profileUserId)
    {
        ViewerUserId = viewerUserId;
        ProfileUserId = profileUserId;
    }

    public Guid ViewerUserId { get; private set; }
    public Guid ProfileUserId { get; private set; }
}
