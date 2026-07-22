using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;

namespace NomadGo.Spatial
{
    public class PlaneDetector : MonoBehaviour
    {
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private Material planeMaterial;
        [SerializeField] private bool showPlaneVisuals = true;
        [SerializeField] private int maxPlaneCount = 10;

        private Dictionary<TrackableId, GameObject> planeVisuals = new Dictionary<TrackableId, GameObject>();
        public int DetectedPlaneCount => planeVisuals.Count;

        private void OnEnable()
        {
            if (planeManager != null)
            {
                // AR Foundation 6.0: use trackablesChanged instead of planesChanged
                planeManager.trackablesChanged.AddListener(HandlePlanesChanged);
            }
        }

        private void OnDisable()
        {
            if (planeManager != null)
            {
                planeManager.trackablesChanged.RemoveListener(HandlePlanesChanged);
            }
        }

        public void Configure(int maxPlanes, string detectionMode)
        {
            maxPlaneCount = maxPlanes;
            if (planeManager != null)
            {
                switch (detectionMode)
                {
                    case "Horizontal":
                        planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
                        break;
                    case "Vertical":
                        planeManager.requestedDetectionMode = PlaneDetectionMode.Vertical;
                        break;
                    case "Everything":
                        planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
                        break;
                    default:
                        planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
                        break;
                }
            }
            Debug.Log($"[PlaneDetector] Configured: maxPlanes={maxPlanes}, mode={detectionMode}");
        }

        // AR Foundation 6.0: ARTrackablesChangedEventArgs<ARPlane>
        private void HandlePlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
        {
            foreach (var plane in args.added)
            {
                if (planeVisuals.Count >= maxPlaneCount)
                {
                    Debug.Log($"[PlaneDetector] Max plane count ({maxPlaneCount}) reached. Ignoring new plane.");
                    continue;
                }
                if (showPlaneVisuals && !planeVisuals.ContainsKey(plane.trackableId))
                {
                    planeVisuals[plane.trackableId] = plane.gameObject;
                    Debug.Log($"[PlaneDetector] Plane added: {plane.trackableId}");
                }
            }
            foreach (var kvp in args.removed)
            {
                if (planeVisuals.ContainsKey(kvp.Key))
                {
                    planeVisuals.Remove(kvp.Key);
                    Debug.Log($"[PlaneDetector] Plane removed: {kvp.Key}");
                }
            }
        }

        public void SetPlaneVisualsEnabled(bool enabled)
        {
            showPlaneVisuals = enabled;
            foreach (var kvp in planeVisuals)
            {
                if (kvp.Value != null)
                    kvp.Value.SetActive(enabled);
            }
        }
    }
}
