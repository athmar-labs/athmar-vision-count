using System;
using System.Collections.Generic;
using UnityEngine;

namespace AthmarLabs.VisionCount
{
    /// <summary>
    /// Converts generic product-localizer boxes into customer SKU detections. Unknown or ambiguous
    /// products are deliberately omitted from automatic counting and remain visible for human review.
    /// </summary>
    public sealed class BulkDetectionRecognizer : IDisposable
    {
        public const int DefaultMaximumProductsPerFrame = 12;

        private readonly ProductEvidenceResolver _resolver;
        private readonly IProductEmbeddingExtractor _embeddingExtractor;
        private readonly int _maximumProductsPerFrame;
        private ProductEnrollmentCatalogueData _manualHardCases;
        private bool _disposed;

        public BulkDetectionRecognizer(
            ProductEvidenceResolver resolver,
            ProductEnrollmentCatalogueData manualHardCases,
            IProductEmbeddingExtractor embeddingExtractor = null,
            int maximumProductsPerFrame = DefaultMaximumProductsPerFrame)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _manualHardCases = manualHardCases ?? new ProductEnrollmentCatalogueData
            {
                customerCode = string.Empty,
                products = new List<ProductEnrollmentRecord>()
            };
            _embeddingExtractor = embeddingExtractor ?? new SentisProductEmbeddingExtractor();
            if (maximumProductsPerFrame < 1 || maximumProductsPerFrame > 100)
                throw new ArgumentOutOfRangeException(nameof(maximumProductsPerFrame));
            _maximumProductsPerFrame = maximumProductsPerFrame;
        }

        public void SetManualHardCases(ProductEnrollmentCatalogueData manualHardCases)
        {
            ThrowIfDisposed();
            _manualHardCases = manualHardCases ?? new ProductEnrollmentCatalogueData
            {
                customerCode = string.Empty,
                products = new List<ProductEnrollmentRecord>()
            };
        }

        public IReadOnlyList<Detection> Recognize(
            Texture cameraTexture,
            IReadOnlyList<Detection> localizedProducts,
            int videoRotationAngle,
            bool verticallyMirrored)
        {
            ThrowIfDisposed();
            if (localizedProducts == null || localizedProducts.Count == 0)
                return Array.Empty<Detection>();
            if (!(cameraTexture is WebCamTexture camera) || !camera.isPlaying || camera.width <= 16 || camera.height <= 16)
                return Array.Empty<Detection>();

            if (!CameraProductCropper.TryReadCameraPixels(camera, out var pixels, out var error))
            {
                Debug.LogWarning("Bulk product recognition skipped camera frame: " + error);
                return Array.Empty<Detection>();
            }

            var ordered = new List<Detection>(localizedProducts.Count);
            for (var index = 0; index < localizedProducts.Count; index++)
            {
                if (localizedProducts[index] != null)
                    ordered.Add(localizedProducts[index]);
            }
            ordered.Sort((left, right) => right.Confidence.CompareTo(left.Confidence));

            var limit = Math.Min(_maximumProductsPerFrame, ordered.Count);
            var recognized = new List<Detection>(limit);
            for (var index = 0; index < limit; index++)
            {
                var localization = ordered[index];
                Texture2D crop = null;
                try
                {
                    if (!CameraProductCropper.TryCreateOrientedCrop(
                            pixels,
                            camera.width,
                            camera.height,
                            localization.Bounds,
                            videoRotationAngle,
                            verticallyMirrored,
                            out crop,
                            out error))
                    {
                        Debug.LogWarning("Bulk product crop skipped: " + error);
                        continue;
                    }

                    var embedding = _embeddingExtractor.Extract(crop);
                    var resolution = _resolver.Resolve(
                        new ProductRecognitionEvidence { VisualEmbedding = embedding },
                        _manualHardCases);
                    if (!resolution.IsMatch || string.IsNullOrWhiteSpace(resolution.Sku))
                        continue;

                    // Localization confidence proves that a physical product is present. Recognition
                    // score proves its identity. Multiplying them prevents a weak box from becoming a
                    // high-confidence SKU merely because its crop resembles a catalogue prototype.
                    var identityScore = Mathf.Clamp01(resolution.Score);
                    var combinedConfidence = Mathf.Clamp01(localization.Confidence) * identityScore;
                    recognized.Add(new Detection(
                        resolution.Sku,
                        combinedConfidence,
                        localization.Bounds));
                }
                catch (Exception exception)
                {
                    // One unreadable/unsupported product must not stop recognition for all other boxes.
                    Debug.LogWarning("Bulk product recognition failed for one localized product: " + exception.Message);
                }
                finally
                {
                    if (crop != null)
                        UnityEngine.Object.Destroy(crop);
                }
            }

            return recognized;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_embeddingExtractor is IDisposable disposable)
                disposable.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BulkDetectionRecognizer));
        }
    }

    public static class CameraProductCropper
    {
        private const int MinimumCropPixels = 16;

        public static bool TryReadCameraPixels(
            WebCamTexture camera,
            out Color32[] pixels,
            out string error)
        {
            pixels = null;
            error = string.Empty;
            if (camera == null || !camera.isPlaying || camera.width <= 16 || camera.height <= 16)
            {
                error = "Camera frame is unavailable.";
                return false;
            }

            try
            {
                pixels = camera.GetPixels32();
                if (pixels == null || pixels.Length != camera.width * camera.height)
                {
                    error = "Camera pixel buffer is incomplete.";
                    pixels = null;
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                pixels = null;
                return false;
            }
        }

        public static bool TryCreateOrientedCrop(
            Color32[] source,
            int sourceWidth,
            int sourceHeight,
            NormalizedRect bounds,
            int rotationAngle,
            bool verticallyMirrored,
            out Texture2D crop,
            out string error)
        {
            crop = null;
            error = string.Empty;
            if (source == null || sourceWidth <= 0 || sourceHeight <= 0 || source.Length != sourceWidth * sourceHeight)
            {
                error = "Source camera buffer is invalid.";
                return false;
            }

            var left = Mathf.Clamp(Mathf.FloorToInt(bounds.X * sourceWidth), 0, sourceWidth - 1);
            var right = Mathf.Clamp(Mathf.CeilToInt((bounds.X + bounds.Width) * sourceWidth), left + 1, sourceWidth);

            // YOLO coordinates are top-origin; Unity pixel arrays are bottom-origin.
            var bottomNormalized = 1f - (bounds.Y + bounds.Height);
            var bottom = Mathf.Clamp(Mathf.FloorToInt(bottomNormalized * sourceHeight), 0, sourceHeight - 1);
            var top = Mathf.Clamp(Mathf.CeilToInt((bottomNormalized + bounds.Height) * sourceHeight), bottom + 1, sourceHeight);

            var width = right - left;
            var height = top - bottom;
            if (width < MinimumCropPixels || height < MinimumCropPixels)
            {
                error = "Localized product crop is too small for recognition.";
                return false;
            }

            var raw = new Color32[checked(width * height)];
            for (var y = 0; y < height; y++)
            {
                var sourceY = bottom + y;
                var destinationY = verticallyMirrored ? height - 1 - y : y;
                Array.Copy(source, sourceY * sourceWidth + left, raw, destinationY * width, width);
            }

            NormalizeRotation(rotationAngle, out var quarterTurnsClockwise);
            var oriented = RotateClockwise(raw, width, height, quarterTurnsClockwise, out var outputWidth, out var outputHeight);
            crop = new Texture2D(outputWidth, outputHeight, TextureFormat.RGBA32, false);
            crop.SetPixels32(oriented);
            crop.Apply(false, false);
            return true;
        }

        private static void NormalizeRotation(int rotationAngle, out int quarterTurnsClockwise)
        {
            var normalized = rotationAngle % 360;
            if (normalized < 0)
                normalized += 360;
            // Android WebCamTexture reports the clockwise correction angle. The UI currently uses
            // a -angle transform; applying the same clockwise correction to crop pixels makes the
            // embedding domain match upright catalogue/packshot images.
            if (normalized < 45 || normalized >= 315)
                quarterTurnsClockwise = 0;
            else if (normalized < 135)
                quarterTurnsClockwise = 1;
            else if (normalized < 225)
                quarterTurnsClockwise = 2;
            else
                quarterTurnsClockwise = 3;
        }

        private static Color32[] RotateClockwise(
            Color32[] source,
            int width,
            int height,
            int quarterTurns,
            out int outputWidth,
            out int outputHeight)
        {
            quarterTurns &= 3;
            if (quarterTurns == 0)
            {
                outputWidth = width;
                outputHeight = height;
                return source;
            }

            outputWidth = quarterTurns == 2 ? width : height;
            outputHeight = quarterTurns == 2 ? height : width;
            var result = new Color32[source.Length];

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var color = source[y * width + x];
                    int destinationX;
                    int destinationY;
                    switch (quarterTurns)
                    {
                        case 1:
                            destinationX = height - 1 - y;
                            destinationY = x;
                            break;
                        case 2:
                            destinationX = width - 1 - x;
                            destinationY = height - 1 - y;
                            break;
                        default:
                            destinationX = y;
                            destinationY = width - 1 - x;
                            break;
                    }
                    result[destinationY * outputWidth + destinationX] = color;
                }
            }

            return result;
        }
    }
}
