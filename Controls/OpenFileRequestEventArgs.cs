using System;

namespace AtlasAI.Controls
{
    public sealed class OpenFileRequestEventArgs : EventArgs
    {
        public string Path { get; }
        public int Line { get; }
        public int Column { get; }

        public OpenFileRequestEventArgs(string path, int line = 0, int column = 0)
        {
            Path = path ?? "";
            Line = line;
            Column = column;
        }
    }
}

