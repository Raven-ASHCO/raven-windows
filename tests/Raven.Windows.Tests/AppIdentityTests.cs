using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Raven.Windows.Core;

namespace Raven.Windows.Tests;

/// <summary>One app id for Raven (desktop/README.md, "App identity"), used everywhere on Windows.</summary>
[TestClass]
public sealed class AppIdentityTests
{
    [TestMethod]
    public void AppId_IsTheOneInTheDesktopReadme()
    {
        string readme = File.ReadAllText(Path.Combine(RepoPaths.RequireWindowsRoot(), "core", "desktop", "README.md"));
        string? row = readme.Split('\n').FirstOrDefault(line => line.StartsWith("| Windows", System.StringComparison.Ordinal));
        Assert.IsNotNull(row, "core/desktop/README.md has a Windows row in \"App identity\"");
        StringAssert.Contains(row, "`" + AppIdentity.Id + "`");
    }

    [TestMethod]
    public void PackageManifest_UsesTheAppId()
    {
        string path = Path.Combine(RepoPaths.RequireWindowsRoot(), "src", "Raven.Windows", "Package.appxmanifest");
        XDocument manifest = XDocument.Load(path);
        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        XElement? identity = manifest.Root?.Element(ns + "Identity");
        Assert.IsNotNull(identity);
        Assert.AreEqual(AppIdentity.Id, (string?)identity.Attribute("Name"));
    }

    [TestMethod]
    public void AppManifest_UsesTheAppId()
    {
        string path = Path.Combine(RepoPaths.RequireWindowsRoot(), "src", "Raven.Windows", "app.manifest");
        XDocument manifest = XDocument.Load(path);
        XNamespace ns = "urn:schemas-microsoft-com:asm.v1";
        Assert.AreEqual(AppIdentity.Id, (string?)manifest.Root?.Element(ns + "assemblyIdentity")?.Attribute("name"));
    }
}
