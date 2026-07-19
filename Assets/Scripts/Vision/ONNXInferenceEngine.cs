using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.InferenceEngine;

namespace NomadGo.Vision
{
    /// <summary>
    /// Wraps Unity Inference Engine (Sentis 2.2+) inference for YOLOv8n ONNX model.
    /// </summary>
    public class ONNXInferenceEngine : MonoBehaviour
    {
        private string modelPath;
        private string labelsPath;
        private int   inputWidth           = 640;
        private int   inputHeight          = 640;
        private float confidenceThreshold  = 0.45f;
        private float nmsThreshold         = 0.5f;
        private int   maxDetections        = 100;

        private string[] labels;
        private bool     isLoaded            = false;
        private float    lastInferenceTimeMs = 0f;

        // Unity Inference Engine: Worker executes the imported model
        private Worker worker;
        private Model  runtimeModel;

        public bool  IsLoaded            => isLoaded;
        public float LastInferenceTimeMs => lastInferenceTimeMs;

        public void Initialize(AppShell.ModelConfig config)
        {
            modelPath           = config.path;
            labelsPath          = config.labels_path;
            inputWidth          = config.input_width;
            inputHeight         = config.input_height;
            confidenceThreshold = config.confidence_threshold;
            nmsThreshold        = config.nms_threshold;
            maxDetections       = config.max_detections;
            LoadLabels();
            LoadModel();
        }

        private void LoadLabels()
        {
            string resourcePath = labelsPath.Replace(".txt", "");
            TextAsset labelsAsset = Resources.Load<TextAsset>(resourcePath)
                                 ?? Resources.Load<TextAsset>("labels");
            if (labelsAsset != null)
            {
                labels = labelsAsset.text.Split(
                    new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                Debug.Log($"[ONNXEngine] Loaded {labels.Length} labels.");
            }
            else
            {
                labels = new[] { "bottle","can","box","carton","bag","jar","container","package","pouch","tube" };
                Debug.LogWarning("[ONNXEngine] Labels file not found. Using default labels.");
            }
        }

        private void LoadModel()
        {
            try
            {
                string assetName = System.IO.Path.GetFileNameWithoutExtension(modelPath);
                ModelAsset modelAsset = Resources.Load<ModelAsset>(assetName)
                    ?? Resources.Load<ModelAsset>(modelPath.Replace(".onnx","").Replace("Models/",""));

                if (modelAsset == null)
                {
                    Debug.LogError($"[ONNXEngine] Model asset not found: \"{modelPath}\". Inference disabled.");
                    isLoaded = false;
                    return;
                }

                // Unity Inference Engine API
                runtimeModel = ModelLoader.Load(modelAsset);
                worker       = new Worker(runtimeModel, BackendType.CPU);
                isLoaded     = true;
                Debug.Log($"[ONNXEngine] Model loaded: {modelAsset.name}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ONNXEngine] Failed to load model: {ex.Message}");
                isLoaded = false;
            }
        }

        public List<DetectionResult> RunInference(Texture2D frame)
        {
            var detections = new List<DetectionResult>();
            if (!isLoaded || worker == null || frame == null) return detections;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                float scaleX = (float)frame.width  / inputWidth;
                float scaleY = (float)frame.height / inputHeight;

                // Pre-process
                Texture2D resized = ResizeTexture(frame, inputWidth, inputHeight);
                Color[]   pixels  = resized.GetPixels();
                Destroy(resized);

                float[] data = new float[3 * inputHeight * inputWidth];
                int stride = inputHeight * inputWidth;
                for (int y = 0; y < inputHeight; y++)
                    for (int x = 0; x < inputWidth; x++)
                    {
                        int idx = y * inputWidth + x;
                        data[0 * stride + idx] = pixels[idx].r;
                        data[1 * stride + idx] = pixels[idx].g;
                        data[2 * stride + idx] = pixels[idx].b;
                    }

                using var input = new TensorFloat(new TensorShape(1, 3, inputHeight, inputWidth), data);

                // Schedule inference and read the output back on CPU
                worker.Schedule(input);
                using TensorFloat output = worker.PeekOutput() as TensorFloat;
                if (output == null) { Debug.LogError("[ONNXEngine] Null output."); return detections; }

                // Make readable on CPU
                var cpuOutput = output.ReadbackAndClone();

                var shape = cpuOutput.shape;
                if (shape.rank < 3) { Debug.LogError($"[ONNXEngine] Bad rank: {shape.rank}"); return detections; }

                int numAnchors = shape[2];
                int rowSize    = shape[1];
                int numClasses = labels.Length;

                for (int i = 0; i < numAnchors; i++)
                {
                    float cx = cpuOutput[0, 0, i];
                    float cy = cpuOutput[0, 1, i];
                    float w  = cpuOutput[0, 2, i];
                    float h  = cpuOutput[0, 3, i];

                    float bestConf = 0f; int bestClass = -1;
                    for (int c = 0; c < numClasses && (c + 4) < rowSize; c++)
                    {
                        float conf = cpuOutput[0, 4 + c, i];
                        if (conf > bestConf) { bestConf = conf; bestClass = c; }
                    }

                    if (bestConf >= confidenceThreshold && bestClass >= 0)
                        detections.Add(new DetectionResult(
                            bestClass, GetLabel(bestClass), bestConf,
                            new Rect((cx - w/2f)*scaleX, (cy - h/2f)*scaleY, w*scaleX, h*scaleY)));
                }

                cpuOutput.Dispose();
                detections = ApplyNMS(detections);
                if (detections.Count > maxDetections) detections = detections.GetRange(0, maxDetections);
                Debug.Log($"[ONNXEngine] Final detections: {detections.Count}");
            }
            catch (Exception ex) { Debug.LogError($"[ONNXEngine] Inference error: {ex.Message}"); }
            finally { sw.Stop(); lastInferenceTimeMs = (float)sw.Elapsed.TotalMilliseconds; }
            return detections;
        }

        private Texture2D ResizeTexture(Texture2D src, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var result = new Texture2D(w, h, TextureFormat.RGB24, false);
            result.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            result.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return result;
        }

        private List<DetectionResult> ApplyNMS(List<DetectionResult> dets)
        {
            if (dets.Count == 0) return dets;
            dets.Sort((a, b) => b.confidence.CompareTo(a.confidence));
            var kept = new List<DetectionResult>();
            var sup  = new bool[dets.Count];
            for (int i = 0; i < dets.Count; i++)
            {
                if (sup[i]) continue;
                kept.Add(dets[i]);
                for (int j = i+1; j < dets.Count; j++)
                {
                    if (sup[j] || dets[i].classId != dets[j].classId) continue;
                    if (ComputeIOU(dets[i].boundingBox, dets[j].boundingBox) > nmsThreshold) sup[j] = true;
                }
            }
            return kept;
        }

        public static float ComputeIOU(Rect a, Rect b)
        {
            float x1 = Mathf.Max(a.xMin, b.xMin), y1 = Mathf.Max(a.yMin, b.yMin);
            float x2 = Mathf.Min(a.xMax, b.xMax), y2 = Mathf.Min(a.yMax, b.yMax);
            float inter = Mathf.Max(0, x2-x1) * Mathf.Max(0, y2-y1);
            float union = a.width*a.height + b.width*b.height - inter;
            return union <= 0 ? 0f : inter / union;
        }

        public string GetLabel(int id) =>
            (labels != null && id >= 0 && id < labels.Length) ? labels[id] : $"class_{id}";

        private void OnDestroy()
        {
            worker?.Dispose();
            worker = null;
            Debug.Log("[ONNXEngine] Worker disposed.");
        }
    }
}
