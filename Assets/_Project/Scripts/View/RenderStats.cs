using Unity.Profiling;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// The render counters PLAN §2's budgets are written in: draw calls,
    /// set-pass calls, triangles, vertices, and GPU memory.
    ///
    /// Draw calls are the sum of every path Unity counts separately — SRP
    /// Batcher, standard, instanced, BatchRendererGroup, procedural. The
    /// obvious counter, "Draw Calls Count", is registered under UI Toolkit in
    /// this Unity version: read under Render it returned 0 for a frame with
    /// a hundred thousand triangles in it, which is a budget that can never be
    /// missed.
    ///
    /// Read through <see cref="ProfilerRecorder"/>, which works in the Editor
    /// and in development builds. In a release build the counters are compiled
    /// out; <see cref="Available"/> says so rather than passing zeros off as
    /// "within budget".
    /// </summary>
    public sealed class RenderStats : System.IDisposable
    {
        private static readonly string[] DrawCounters =
        {
            "SRP Batcher Draw Calls Count",
            "Standard Draw Calls Count",
            "Standard Instanced Draw Calls Count",
            "Standard Indirect Draw Calls Count",
            "BRG Draw Calls Count",
            "BRG Indirect Draw Calls Count",
            "Null Geometry Draw Calls Count",
            "Null Geometry Indirect Draw Calls Count",
        };

        private readonly ProfilerRecorder[] _draws;
        private ProfilerRecorder _setPass, _tris, _verts, _casters, _texBytes, _vram;

        public RenderStats()
        {
            _draws = new ProfilerRecorder[DrawCounters.Length];
            for (int i = 0; i < DrawCounters.Length; i++)
            {
                _draws[i] = ProfilerRecorder.StartNew(ProfilerCategory.Render, DrawCounters[i]);
            }
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            _verts = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
            _casters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
            _texBytes = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Used Textures Bytes");
            _vram = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Video Memory Bytes");
        }

        public bool Available => _tris.Valid && _draws[0].Valid;

        public long DrawCalls
        {
            get
            {
                long n = 0;
                foreach (var r in _draws) if (r.Valid) n += r.LastValue;
                return n;
            }
        }

        public long SrpBatcherDraws => _draws[0].Valid ? _draws[0].LastValue : 0;
        public long SetPass => _setPass.LastValue;
        public long Triangles => _tris.LastValue;
        public long Vertices => _verts.LastValue;
        public long ShadowCasters => _casters.LastValue;
        public long TextureBytes => _texBytes.LastValue;
        public long VideoMemoryBytes => _vram.LastValue;

        public override string ToString()
            => Available
                ? $"draws {DrawCalls} ({SrpBatcherDraws} SRP-batched), set-pass {SetPass}, tris {Triangles:N0}, "
                  + $"verts {Vertices:N0}, shadow casters {ShadowCasters}, textures {TextureBytes / 1048576.0:F1} MB, "
                  + $"video memory {VideoMemoryBytes / 1048576.0:F1} MB"
                : "render counters unavailable in this build";

        public void Dispose()
        {
            foreach (var r in _draws) r.Dispose();
            _setPass.Dispose(); _tris.Dispose(); _verts.Dispose(); _casters.Dispose();
            _texBytes.Dispose(); _vram.Dispose();
        }
    }
}
