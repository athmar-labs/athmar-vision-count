using NUnit.Framework;

namespace AthmarLabs.VisionCount.Tests
{
    public sealed class BarcodeEvidenceTests
    {
        [Test]
        public void CacheNormalizesBarcodeAndRequiresSpatialOverlap()
        {
            var cache = new BarcodeEvidenceCache();
            var original = new NormalizedRect(0.2f, 0.2f, 0.4f, 0.4f);
            cache.Record("6281-000 001", original, 10d);

            Assert.That(cache.TryGet(new NormalizedRect(0.22f, 0.21f, 0.4f, 0.4f), 10.5d, out var value), Is.True);
            Assert.That(value, Is.EqualTo("6281000001"));
            Assert.That(cache.TryGet(new NormalizedRect(0.75f, 0.75f, 0.2f, 0.2f), 10.5d, out _), Is.False);
        }

        [Test]
        public void CacheExpiresAndEmptyEvidenceFailsClosed()
        {
            var cache = new BarcodeEvidenceCache(ttlSeconds: 1d);
            var bounds = new NormalizedRect(0.1f, 0.1f, 0.5f, 0.5f);
            cache.Record("123456", bounds, 5d);
            Assert.That(cache.TryGet(bounds, 6.1d, out _), Is.False);

            cache.Record(" -- ", bounds, 7d);
            Assert.That(cache.TryGet(bounds, 7.1d, out _), Is.False);
        }
    }
}
