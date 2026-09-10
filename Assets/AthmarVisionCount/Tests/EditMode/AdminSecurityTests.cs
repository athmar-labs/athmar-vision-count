using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AthmarLabs.VisionCount.Tests
{
    public sealed class AdminSecurityTests
    {
        [Test]
        public void SessionIsInactiveUntilAuthentication()
        {
            var session = new AdminSession();

            Assert.That(session.IsActive(0d), Is.False);
        }

        [Test]
        public void SessionExpiresAbsolutelyAfterFiveMinutes()
        {
            var session = new AdminSession();
            session.Start(100d);

            Assert.That(session.IsActive(399.999d), Is.True);
            Assert.That(session.IsActive(400d), Is.False);
        }

        [Test]
        public void SessionChecksDoNotExtendExpiry()
        {
            var session = new AdminSession();
            session.Start(0d);

            Assert.That(session.IsActive(299d), Is.True);
            Assert.That(session.IsActive(300d), Is.False);
        }

        [Test]
        public void SessionEndRelocksImmediately()
        {
            var session = new AdminSession();
            session.Start(0d);

            session.End();

            Assert.That(session.IsActive(1d), Is.False);
        }

        [Test]
        public void HiddenAdminViewIsNotReportedAsUnlocked()
        {
            var existingEventSystem = Object.FindFirstObjectByType<EventSystem>();
            var root = new GameObject("Admin security test");
            try
            {
                var view = root.AddComponent<CustomerAdminView>();
                view.ShowUnlocked("customer-a", false);
                Assert.That(view.IsUnlocked, Is.True);

                view.Hide();

                Assert.That(view.IsVisible, Is.False);
                Assert.That(view.IsUnlocked, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
                if (existingEventSystem == null)
                {
                    var createdEventSystem = Object.FindFirstObjectByType<EventSystem>();
                    if (createdEventSystem != null)
                        Object.DestroyImmediate(createdEventSystem.gameObject);
                }
            }
        }
    }
}
