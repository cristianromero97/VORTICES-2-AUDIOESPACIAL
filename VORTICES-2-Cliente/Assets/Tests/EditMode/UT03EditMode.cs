using NUnit.Framework;
using System.Collections.Generic;
using System.IO;

public class UT03EditMode
{
    private List<string> supportedExtensions;

    [SetUp]
    public void Setup()
    {
        supportedExtensions = new List<string> { ".mp3", ".wav", ".ogg", ".aiff" };
    }

    [Test]
    public void FormatoCompatibleEsAceptado()
    {
        Assert.IsTrue(IsSupported("sonido.mp3"));
        Assert.IsTrue(IsSupported("audio.wav"));
        Assert.IsTrue(IsSupported("musica.ogg"));
    }

    [Test]
    public void FormatoNoCompatibleEsRechazado()
    {
        Assert.IsFalse(IsSupported("archivo.exe"));
        Assert.IsFalse(IsSupported("imagen.png"));
    }

    private bool IsSupported(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return supportedExtensions.Contains(ext);
    }
}
