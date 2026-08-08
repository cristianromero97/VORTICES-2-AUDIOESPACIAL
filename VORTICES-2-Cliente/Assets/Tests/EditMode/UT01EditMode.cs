using NUnit.Framework;

public class UT01EditMode
{
    [Test]
    public void VolumenFinalEsProductoDeBaseYGlobal()
    {
        float baseVolume   = 0.8f;
        float globalVolume = 0.5f;

        float resultado = baseVolume * globalVolume;

        Assert.AreEqual(0.4f, resultado, 0.001f);
    }
}
