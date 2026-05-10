using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AtlasAI.DJ
{
    public partial class LibraryModal : Window
    {
        private AudioEngine? _engine;
        private List<Track> _all = new();

        public LibraryModal()
        {
            InitializeComponent();
            SearchBox.TextChanged += (_, __) => Refresh();
            LoadA.Click += (_, __) => LoadTo(true);
            LoadB.Click += (_, __) => LoadTo(false);
            BuildInitialLibrary();
            Refresh();
        }

        public void SetEngine(AudioEngine engine) => _engine = engine;

        private void BuildInitialLibrary()
        {
            _all = new List<Track>
            {
                new Track { Name = "Neon Pulse", Artist = "Cyber Synthesis", Bpm = 128, Key = "Am", Duration = "4:32" },
                new Track { Name = "Digital Horizon", Artist = "Synthwave Collective", Bpm = 126, Key = "Dm", Duration = "5:18" },
                new Track { Name = "Quantum Dreams", Artist = "Future Bass", Bpm = 140, Key = "Gm", Duration = "3:45" },
                new Track { Name = "Cyber City Lights", Artist = "Neon Sounds", Bpm = 130, Key = "Cm", Duration = "4:12" },
                new Track { Name = "Electric Sunset", Artist = "Wave Rider", Bpm = 124, Key = "Em", Duration = "5:05" },
                new Track { Name = "Matrix Flow", Artist = "Digital Dreams", Bpm = 135, Key = "Fm", Duration = "3:58" },
                new Track { Name = "Neon Highway", Artist = "Retro Future", Bpm = 128, Key = "Bbm", Duration = "4:44" },
                new Track { Name = "Cosmic Waves", Artist = "Space Synth", Bpm = 132, Key = "Ebm", Duration = "5:20" },
            };

            foreach (var root in DjLibraryDiscovery.DiscoverRoots())
            {
                if (!Directory.Exists(root.Path))
                    continue;

                var files = Directory.EnumerateFiles(root.Path, "*.*", SearchOption.AllDirectories)
                    .Where(p => p.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                                p.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                                p.EndsWith(".wma", StringComparison.OrdinalIgnoreCase));
                foreach (var f in files)
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    foreach (var t in _all)
                    {
                        if (t.FilePath?.Length > 0) continue;
                        var n = (t.Name ?? "").ToLowerInvariant().Replace(" ", "");
                        var fn = name.ToLowerInvariant().Replace(" ", "");
                        if (fn.Contains(n))
                        {
                            t.FilePath = f;
                            break;
                        }
                    }
                }
            }
        }

        private void Refresh()
        {
            var q = (SearchBox.Text ?? "").Trim();
            IEnumerable<Track> src = _all;
            if (!string.IsNullOrWhiteSpace(q))
                src = src.Where(t =>
                    (t.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (t.Artist ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (t.Key ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
            List.ItemsSource = src.ToList();
        }

        private void LoadTo(bool left)
        {
            if (_engine == null) return;
            if (List.SelectedItem is not Track t) return;
            if (string.IsNullOrWhiteSpace(t.FilePath) || !File.Exists(t.FilePath))
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Audio|*.mp3;*.wav;*.wma",
                    Multiselect = false
                };
                if (dlg.ShowDialog() == true)
                    t.FilePath = dlg.FileName;
                else
                    return;
            }

            if (left) _engine.LoadA(t.FilePath, t.Bpm, t.Key, t.Name, t.Artist);
            else _engine.LoadB(t.FilePath, t.Bpm, t.Key, t.Name, t.Artist);
            Close();
        }
    }
}
