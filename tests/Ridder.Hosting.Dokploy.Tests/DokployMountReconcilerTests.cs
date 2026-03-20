using Ridder.Hosting.Dokploy;

namespace Ridder.Hosting.Dokploy.Tests;

public class DokployMountReconcilerTests
{
    [Fact]
    public void MountLocationMatches_IgnoresTrailingSlashAndTypeShape()
    {
        var existingMount = new DokployApi.Mount
        {
            Type = "volume-mount",
            MountPath = "/data/"
        };

        var matches = DokployMountReconciler.MountLocationMatches(existingMount, "/data");

        Assert.True(matches);
    }

    [Fact]
    public void MountIdentityMatches_NormalizesVolumeTypeAliases()
    {
        var existingMount = new DokployApi.Mount
        {
            Type = "volume-mount",
            MountPath = "/data/",
            VolumeName = "cache-data"
        };

        var matches = DokployMountReconciler.MountIdentityMatches(existingMount, "volume", "/data", hostPath: null, volumeName: "cache-data");

        Assert.True(matches);
    }

    [Fact]
    public void GetMountIdentity_NormalizesTrailingSlash()
    {
        var left = DokployMountReconciler.GetMountIdentity("volume", "/data/", hostPath: null, volumeName: "cache-data");
        var right = DokployMountReconciler.GetMountIdentity("volume", "/data", hostPath: null, volumeName: "cache-data");

        Assert.Equal(left, right);
    }
}