using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Kinema.MotionMatching.Editor
{
    /// <summary>
    /// Step 1 of Learned Motion Matching (Holden et al., SIGGRAPH 2020): turn a baked database into a
    /// training dataset a network pipeline can consume, without shipping the database itself at
    /// runtime.
    ///
    /// The bake already produced everything the training needs - a normalized feature matrix, the
    /// per-dimension mean/std, per-frame clip/time metadata and the gait phase. This writes them as
    /// flat little-endian binaries plus a self-describing JSON manifest, so a numpy/PyTorch loader is
    /// a few `np.fromfile` calls with no parsing. Binary, not CSV: a real mocap set is millions of
    /// floats, and text would be an order of magnitude larger and slower.
    ///
    /// The clip boundaries matter: the stepper network predicts the next frame from the current one,
    /// and must never be trained to step across a clip cut. They are exported explicitly so the
    /// training script can build sequences that respect them.
    /// </summary>
    public static class TrainingDataExporter
    {
        #region Main API

        private const int FormatVersion = 1;

        [MenuItem("Tools/Kinema/Learned MM/Export Training Dataset", priority = 80)]
        public static void ExportMenu()
        {
            MotionMatchingDatabase db = FindRichestDatabase();
            if (db == null)
            {
                EditorUtility.DisplayDialog("Kinema", "No baked database found. Bake one first (Tools > Kinema > Demo Scene, or the Bake tab).", "OK");
                return;
            }

            string folder = EditorUtility.SaveFolderPanel("Export Learned MM Dataset", Application.dataPath, db.name + "_dataset");
            if (string.IsNullOrEmpty(folder)) return;

            Export(db, folder);
            EditorUtility.RevealInFinder(folder);
        }

        /// <summary>Writes the dataset for <paramref name="db"/> into <paramref name="folder"/>. Returns the manifest path.</summary>
        public static string Export(MotionMatchingDatabase db, string folder)
        {
            Directory.CreateDirectory(folder);

            int frames = db.FrameCount;
            int dim = db.Dimension;
            FeatureSchema schema = db.Schema;

            // X: the normalized feature matrix, row-major [frames x dim]. This is what the projector
            // searches and the decompressor reconstructs a pose from.
            WriteFloats(Path.Combine(folder, "features.f32"), db.Features, frames * dim);

            // Per-dimension normalization, so a network can move between normalized and metric space.
            WriteFloats(Path.Combine(folder, "mean.f32"), db.FeatureMean, dim);
            WriteFloats(Path.Combine(folder, "std.f32"), db.FeatureStd, dim);

            // Gait phase per frame (-1 where no cycle) - an extra conditioning signal for the networks.
            var phases = db.HasFootPhases ? db.FootPhases : null;
            if (phases != null) WriteFloats(Path.Combine(folder, "phase.f32"), phases, frames);

            // Per-frame clip index and clip-local time: the sequence structure the stepper trains on.
            var clipIndex = new int[frames];
            var time = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                MotionFrameInfo info = db.GetFrame(f);
                clipIndex[f] = info.ClipIndex;
                time[f] = info.Time;
            }
            WriteInts(Path.Combine(folder, "clip_index.i32"), clipIndex);
            WriteFloats(Path.Combine(folder, "time.f32"), time, frames);

            string manifest = WriteManifest(folder, db, schema, phases != null);
            WritePythonLoader(folder);

            Debug.Log($"[Kinema] Learned MM dataset exported: {frames:N0} frames x {dim} dims → {folder}\n" +
                      $"[Kinema]   features.f32 ({(long)frames * dim * 4 / 1024:N0} KB), mean/std, " +
                      $"clip_index.i32, time.f32{(phases != null ? ", phase.f32" : "")}, manifest.json, load_dataset.py");
            return manifest;
        }

        #endregion

        #region Tools and Utilities — Writers

        private static void WriteFloats(string path, float[] data, int count)
        {
            using var stream = new FileStream(path, FileMode.Create);
            using var writer = new BinaryWriter(stream);
            for (int i = 0; i < count; i++) writer.Write(data[i]); // BinaryWriter is little-endian.
        }

        private static void WriteInts(string path, int[] data)
        {
            using var stream = new FileStream(path, FileMode.Create);
            using var writer = new BinaryWriter(stream);
            foreach (int v in data) writer.Write(v);
        }

        /// <summary>
        /// A self-describing manifest: shapes, dtypes, the feature layout the networks need to know
        /// (which columns are trajectory vs pose), and the clip ranges the stepper must respect.
        /// Hand-rolled JSON - it is small and fixed-shape, not worth a serializer dependency.
        /// </summary>
        private static string WriteManifest(string folder, MotionMatchingDatabase db, FeatureSchema schema, bool hasPhase)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"format_version\": {FormatVersion},");
            sb.AppendLine($"  \"source\": \"{db.name}\",");
            sb.AppendLine($"  \"frame_count\": {db.FrameCount},");
            sb.AppendLine($"  \"dimension\": {db.Dimension},");
            sb.AppendLine($"  \"clip_count\": {db.ClipCount},");
            sb.AppendLine($"  \"bake_frame_rate\": {db.BakeFrameRate},");
            sb.AppendLine("  \"dtype\": \"float32\", \"int_dtype\": \"int32\", \"byte_order\": \"little\",");

            sb.AppendLine("  \"files\": {");
            sb.AppendLine($"    \"features\": {{ \"file\": \"features.f32\", \"shape\": [{db.FrameCount}, {db.Dimension}] }},");
            sb.AppendLine($"    \"mean\": {{ \"file\": \"mean.f32\", \"shape\": [{db.Dimension}] }},");
            sb.AppendLine($"    \"std\": {{ \"file\": \"std.f32\", \"shape\": [{db.Dimension}] }},");
            sb.AppendLine($"    \"clip_index\": {{ \"file\": \"clip_index.i32\", \"shape\": [{db.FrameCount}] }},");
            sb.Append($"    \"time\": {{ \"file\": \"time.f32\", \"shape\": [{db.FrameCount}] }}");
            sb.AppendLine(hasPhase ? "," : "");
            if (hasPhase)
                sb.AppendLine($"    \"phase\": {{ \"file\": \"phase.f32\", \"shape\": [{db.FrameCount}] }}");
            sb.AppendLine("  },");

            // Feature layout: the column ranges each network conditions on / reconstructs.
            sb.AppendLine("  \"layout\": {");
            sb.AppendLine($"    \"trajectory_point_count\": {schema.TrajectoryPointCount},");
            sb.AppendLine($"    \"bone_count\": {schema.BoneCount},");
            sb.AppendLine($"    \"trajectory_position\": [{schema.TrajectoryPositionOffset}, {schema.TrajectoryPointCount * 2}],");
            sb.AppendLine($"    \"trajectory_direction\": [{schema.TrajectoryDirectionOffset}, {schema.TrajectoryPointCount * 2}],");
            // Named for what the columns hold, so a training script never has to guess: under
            // InertializationCost there is no velocity block, and bone_position is the composite.
            sb.AppendLine($"    \"pose_mode\": \"{schema.PoseMode}\",");
            if (schema.PoseMode != PoseCostMode.Naive)
                sb.AppendLine($"    \"inertialization_halflife\": {schema.InertializationHalflife.ToString(CultureInfo.InvariantCulture)},");
            sb.AppendLine($"    \"bone_position\": [{schema.BonePositionOffset}, {schema.BoneCount * 3}],");
            sb.AppendLine($"    \"bone_velocity\": [{schema.BoneVelocityOffset}, {schema.GetGroupLength(FeatureGroup.BoneVelocity)}],");
            sb.AppendLine($"    \"root_velocity\": [{schema.RootVelocityOffset}, 2]");
            sb.AppendLine("  },");

            // Clip ranges [start, count] - the sequences the stepper trains within, never across.
            sb.Append("  \"clips\": [");
            for (int c = 0; c < db.ClipCount; c++)
            {
                MotionClipEntry clip = db.GetClip(c);
                sb.Append(c > 0 ? ", " : "");
                sb.Append($"[{clip.StartFrame}, {clip.FrameCount}]");
            }
            sb.AppendLine("]");
            sb.AppendLine("}");

            string path = Path.Combine(folder, "manifest.json");
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        /// <summary>A ready-to-run numpy loader, so the dataset is usable without reverse-engineering the layout.</summary>
        private static void WritePythonLoader(string folder)
        {
            const string py =
@"""""""Load a Kinema Learned MM dataset. Requires numpy.""""""
import json, os, numpy as np

def load(folder):
    m = json.load(open(os.path.join(folder, 'manifest.json')))
    def arr(key, dtype):
        f = m['files'][key]
        data = np.fromfile(os.path.join(folder, f['file']), dtype=dtype)
        return data.reshape(f['shape'])
    d = {
        'features':   arr('features', np.float32),   # [frames, dim] normalized
        'mean':       arr('mean', np.float32),        # [dim]
        'std':        arr('std', np.float32),         # [dim]
        'clip_index': arr('clip_index', np.int32),    # [frames]
        'time':       arr('time', np.float32),        # [frames]
        'clips':      m['clips'],                      # [[start, count], ...]
        'layout':     m['layout'],
    }
    if 'phase' in m['files']:
        d['phase'] = arr('phase', np.float32)
    return d

if __name__ == '__main__':
    import sys
    d = load(sys.argv[1] if len(sys.argv) > 1 else '.')
    print('features', d['features'].shape, '| clips', len(d['clips']))
    # Stepper pairs (frame t -> t+1) that never cross a clip boundary:
    pairs = [(s + i, s + i + 1) for s, n in d['clips'] for i in range(n - 1)]
    print('valid step pairs:', len(pairs))
";
            File.WriteAllText(Path.Combine(folder, "load_dataset.py"), py);
        }

        private static MotionMatchingDatabase FindRichestDatabase()
        {
            MotionMatchingDatabase best = null;
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(MotionMatchingDatabase)))
            {
                var candidate = AssetDatabase.LoadAssetAtPath<MotionMatchingDatabase>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate == null || !candidate.IsValid) continue;
                if (best == null || candidate.FrameCount > best.FrameCount) best = candidate;
            }
            return best;
        }

        #endregion
    }
}
