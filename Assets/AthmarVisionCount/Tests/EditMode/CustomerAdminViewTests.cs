using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AthmarLabs.VisionCount.Tests
{
    public sealed class CustomerAdminViewTests
    {
        private GameObject _root;
        private EventSystem _createdEventSystem;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
                Object.DestroyImmediate(_root);
            if (_createdEventSystem != null)
                Object.DestroyImmediate(_createdEventSystem.gameObject);
        }

        [Test]
        public void LockedAndUnlockedViewsExposeOnlyRelevantControls()
        {
            var existingEventSystem = Object.FindFirstObjectByType<EventSystem>();
            _root = new GameObject("Test UI", typeof(RectTransform));
            _root.AddComponent<VisionCountView>();
            var view = _root.AddComponent<CustomerAdminView>();
            if (existingEventSystem == null)
                _createdEventSystem = Object.FindFirstObjectByType<EventSystem>();

            Assert.That(_root.GetComponentsInChildren<Canvas>(true), Has.Length.EqualTo(1));
            Assert.That(FindChildrenNamed(_root.transform, "Admin"), Is.EqualTo(1));
            var panel = _root.transform.Find("AdministrationPanel");
            Assert.That(panel, Is.Not.Null);

            view.ShowLocked(false, true);

            var authentication = panel.Find("AuthenticationSection");
            var package = panel.Find("PackageSection");
            Assert.That(panel.GetSiblingIndex(), Is.EqualTo(_root.transform.childCount - 1));
            Assert.That(panel.GetComponent<Image>().raycastTarget, Is.True);
            Assert.That(authentication.gameObject.activeSelf, Is.True);
            Assert.That(package.gameObject.activeSelf, Is.False);
            Assert.That(authentication.Find("AdminPinLabel").GetComponent<Text>().text, Is.Not.Empty);
            Assert.That(authentication.Find("AdminPin").GetComponent<InputField>().placeholder.GetComponent<Text>().text, Is.Not.Empty);

            view.ShowUnlocked("customer-a", false);

            Assert.That(authentication.gameObject.activeSelf, Is.False);
            Assert.That(package.gameObject.activeSelf, Is.True);
        }

        private static int FindChildrenNamed(Transform parent, string name)
        {
            var count = parent.name == name ? 1 : 0;
            for (var index = 0; index < parent.childCount; index++)
                count += FindChildrenNamed(parent.GetChild(index), name);
            return count;
        }

    }
}
