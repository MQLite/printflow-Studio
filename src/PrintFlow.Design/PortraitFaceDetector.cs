using System.Numerics;
using ImageMagick;
using Microsoft.ML.OnnxRuntime;

namespace PrintFlow.Design;

// YuNet predicts one box and five landmarks per grid cell at strides 8, 16 and 32.
internal static class PortraitFaceDetector
{
    private const int Edge = 640;
    private static readonly int[] Strides = [8, 16, 32];
    private static readonly string[] OutputNames = Strides.SelectMany(s =>
        new[] { $"cls_{s}", $"obj_{s}", $"bbox_{s}", $"kps_{s}" }).ToArray();
    private static readonly Lazy<InferenceSession> Model = new(Load);

    private static InferenceSession Load()
    {
        using var resource = typeof(PortraitFaceDetector).Assembly.GetManifestResourceStream("YuNet.onnx")!;
        var model = new byte[resource.Length];
        resource.ReadExactly(model);
        using var options = new SessionOptions
        {
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount, 1, 4),
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
        };
        return new InferenceSession(model, options);
    }

    public static FaceLandmarks? Locate(MagickImage image, CancellationToken ct)
    {
        using var thumbnail = image.Clone();
        thumbnail.Resize(Edge, Edge);
        int width = (int)thumbnail.Width, height = (int)thumbnail.Height;
        using var pixels = thumbnail.GetPixels();
        var rgb = pixels.ToByteArray(PixelMapping.RGBA)!;
        var input = new float[3 * Edge * Edge];
        for (int y = 0; y < height; y++)
        {
            ct.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                int cell = y * Edge + x;
                float alpha = rgb[offset + 3] / 255f;
                // The model takes unnormalised, planar BGR. Pad the unused rows/columns with black.
                input[cell] = rgb[offset + 2] * alpha;
                input[Edge * Edge + cell] = rgb[offset + 1] * alpha;
                input[2 * Edge * Edge + cell] = rgb[offset] * alpha;
            }
        }

        using var tensor = OrtValue.CreateTensorValueFromMemory(input, [1, 3, Edge, Edge]);
        using var run = new RunOptions();
        using var cancellation = ct.Register(() => run.Terminate = true);
        using var outputs = Model.Value.Run(run, ["input"], [tensor], OutputNames);
        ct.ThrowIfCancellationRequested();
        var layers = outputs.ToArray();
        var faces = new List<Detection>();
        var scale = new Vector2((float)image.Width / width, (float)image.Height / height);
        for (int level = 0; level < Strides.Length; level++)
        {
            Decode(Strides[level], scale, layers[level * 4].GetTensorDataAsSpan<float>(),
                layers[level * 4 + 1].GetTensorDataAsSpan<float>(), layers[level * 4 + 2].GetTensorDataAsSpan<float>(),
                layers[level * 4 + 3].GetTensorDataAsSpan<float>(), faces);
        }

        if (faces.Count == 0) return null;
        var best = faces.MaxBy(face => face.Score)!;
        // Duplicates of the strongest prediction overlap it. A separate plausible face makes selection ambiguous.
        if (best.Score < .85f || faces.Any(face => face.Bounds.Overlap(best.Bounds) < .3f)) return null;
        return best.Landmarks;
    }

    private static void Decode(int stride, Vector2 scale, ReadOnlySpan<float> classes, ReadOnlySpan<float> objects,
        ReadOnlySpan<float> boxes, ReadOnlySpan<float> points, List<Detection> detections)
    {
        int columns = Edge / stride;
        for (int i = 0; i < classes.Length; i++)
        {
            float score = MathF.Sqrt(Math.Clamp(classes[i], 0, 1) * Math.Clamp(objects[i], 0, 1));
            if (score < .65f) continue;
            var cell = new Vector2(i % columns, i / columns);
            var center = (cell + new Vector2(boxes[i * 4], boxes[i * 4 + 1])) * stride * scale;
            var size = new Vector2(MathF.Exp(boxes[i * 4 + 2]), MathF.Exp(boxes[i * 4 + 3])) * stride * scale;
            var bounds = new Bounds(center - size / 2, center + size / 2);
            var landmarks = points.Slice(i * 10, 10);
            Vector2 Point(float x, float y) => (cell + new Vector2(x, y)) * stride * scale;
            detections.Add(new Detection(score, bounds, new FaceLandmarks(
                Point(landmarks[0], landmarks[1]), Point(landmarks[2], landmarks[3]),
                Point(landmarks[4], landmarks[5]), Point(landmarks[6], landmarks[7]),
                Point(landmarks[8], landmarks[9]))));
        }
    }

    private sealed record Detection(float Score, Bounds Bounds, FaceLandmarks Landmarks);

    private readonly record struct Bounds(Vector2 Min, Vector2 Max)
    {
        private static float Area(Vector2 size) => size.X * size.Y;

        public float Overlap(Bounds other)
        {
            var intersection = Vector2.Max(Vector2.Zero, Vector2.Min(Max, other.Max) - Vector2.Max(Min, other.Min));
            float shared = Area(intersection);
            return shared / (Area(Max - Min) + Area(other.Max - other.Min) - shared);
        }
    }
}

internal sealed record FaceLandmarks(Vector2 FirstEye, Vector2 SecondEye, Vector2 Nose,
    Vector2 FirstMouthCorner, Vector2 SecondMouthCorner)
{
    // Fit a similarity transform from canonical eye/nose coordinates to these three landmarks.
    // A similarity preserves facial proportions and permits translation, in-plane rotation and uniform scale.
    public FaceFrame Frame()
    {
        const float noseY = .5f;
        var center = (FirstEye + SecondEye + Nose) / 3;
        var noseOffset = Nose - center;
        var axis = ((SecondEye - FirstEye) / 2 + new Vector2(noseOffset.Y, -noseOffset.X) * noseY)
            / (.5f + 2 * noseY * noseY / 3);
        return new FaceFrame(center - new Vector2(-axis.Y, axis.X) * (noseY / 3), axis);
    }

    public bool HasUsablePose()
    {
        var eyeLine = SecondEye - FirstEye;
        if (eyeLine.Length() < 16 || eyeLine.X <= 0 || MathF.Abs(eyeLine.Y) > eyeLine.X * .6f) return false;
        var eyes = new FaceFrame((FirstEye + SecondEye) / 2, eyeLine);
        var nose = eyes.ToLocal(Nose);
        var left = eyes.ToLocal(FirstMouthCorner);
        var right = eyes.ToLocal(SecondMouthCorner);
        return MathF.Abs(nose.X) < .34f && nose.Y is > .15f and < .95f &&
            MathF.Abs((left.X + right.X) / 2) < .34f && right.X - left.X is > .28f and < 1.35f &&
            Math.Min(left.Y, right.Y) > nose.Y + .08f && Math.Max(left.Y, right.Y) < 1.55f;
    }
}

internal readonly record struct FaceFrame(Vector2 Origin, Vector2 Axis)
{
    private Vector2 Down => new(-Axis.Y, Axis.X);
    public Vector2 ToImage(Vector2 point) => Origin + point.X * Axis + point.Y * Down;
    public Vector2 ToLocal(Vector2 point)
    {
        var delta = point - Origin;
        return new Vector2(Vector2.Dot(delta, Axis), Vector2.Dot(delta, Down)) / Axis.LengthSquared();
    }
}
