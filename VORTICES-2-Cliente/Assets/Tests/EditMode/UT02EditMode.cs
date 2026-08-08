using NUnit.Framework;

public class UT02EditMode
{
    private float[] expectedGlobalVolumes = { 0.6f, 0.7f, 0.8f, 0.9f, 1.0f, 1.0f };

    [Test]
    public void NivelInmersionTieneVolumenGlobalCorrecto()
    {
        for (int i = 0; i < expectedGlobalVolumes.Length; i++)
        {
            Assert.AreEqual(expectedGlobalVolumes[i],
                            GetGlobalVolumeForLevel(i), 0.001f,
                            $"Nivel {i} tiene globalVolume incorrecto");
        }
    }

    private float GetGlobalVolumeForLevel(int level)
    {
        float[] volumes = { 0.6f, 0.7f, 0.8f, 0.9f, 1.0f, 1.0f };
        return volumes[level];
    }
}
