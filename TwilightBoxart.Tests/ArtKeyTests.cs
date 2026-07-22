using TwilightBoxart.Pipeline;

namespace TwilightBoxart.Tests;

[TestClass]
public class ArtKeyTests
{
    [TestMethod]
    public void Normalize_FoldsCaseOntoTheCanonicalForms()
    {
        // Title ids uppercase, digests lowercase: the exact casing DeriveKey emits. The record table
        // collates NOCASE but the render cache path does not, so a case-variant URL must never
        // address a second file for the same row.
        Assert.AreEqual("ASME", ArtKey.Normalize("asme"));
        Assert.AreEqual("ASME", ArtKey.Normalize("ASME"));
        Assert.AreEqual("0123456789abcdef", ArtKey.Normalize("0123456789ABCDEF"));
        Assert.AreEqual("0123456789abcdef", ArtKey.Normalize("0123456789abcdef"));

        // 16 characters but not hex: not a digest, so it folds up like any other key.
        Assert.AreEqual("GAME12345678WXYZ", ArtKey.Normalize("game12345678wxyz"));
    }
}
