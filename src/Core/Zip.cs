using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LiveDiag
{
    /// <summary>极简 ZIP 写入器（仅存储不压缩），避免引入任何第三方/额外程序集依赖。</summary>
    public class ZipWriter : IDisposable
    {
        private class Entry
        {
            public string Name;
            public uint Crc;
            public long Size;
            public long Offset;
            public ushort Time;
            public ushort Date;
        }

        private static readonly uint[] CrcTable = BuildTable();
        private readonly FileStream _fs;
        private readonly List<Entry> _entries = new List<Entry>();
        private bool _closed;

        public ZipWriter(string path)
        {
            _fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        }

        private static uint[] BuildTable()
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        public void AddText(string name, string text)
        {
            Add(name, new UTF8Encoding(true).GetBytes(text));
        }

        public void AddFile(string name, string path)
        {
            if (File.Exists(path)) Add(name, File.ReadAllBytes(path));
        }

        public void Add(string name, byte[] data)
        {
            if (_closed) return;
            DateTime now = DateTime.Now;
            Entry e = new Entry();
            e.Name = name.Replace('\\', '/');
            e.Crc = Crc32(data);
            e.Size = data.Length;
            e.Offset = _fs.Position;
            e.Time = (ushort)((now.Hour << 11) | (now.Minute << 5) | (now.Second / 2));
            e.Date = (ushort)(((now.Year - 1980) << 9) | (now.Month << 5) | now.Day);
            _entries.Add(e);

            byte[] nameBytes = Encoding.UTF8.GetBytes(e.Name);
            BinaryWriter w = new BinaryWriter(_fs);
            w.Write(0x04034b50);
            w.Write((ushort)20);
            w.Write((ushort)0x0800);
            w.Write((ushort)0);
            w.Write(e.Time);
            w.Write(e.Date);
            w.Write(e.Crc);
            w.Write((uint)data.Length);
            w.Write((uint)data.Length);
            w.Write((ushort)nameBytes.Length);
            w.Write((ushort)0);
            w.Write(nameBytes);
            w.Write(data);
            w.Flush();
        }

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            try
            {
                BinaryWriter w = new BinaryWriter(_fs);
                long cdStart = _fs.Position;
                foreach (Entry e in _entries)
                {
                    byte[] nameBytes = Encoding.UTF8.GetBytes(e.Name);
                    w.Write(0x02014b50);
                    w.Write((ushort)20);
                    w.Write((ushort)20);
                    w.Write((ushort)0x0800);
                    w.Write((ushort)0);
                    w.Write(e.Time);
                    w.Write(e.Date);
                    w.Write(e.Crc);
                    w.Write((uint)e.Size);
                    w.Write((uint)e.Size);
                    w.Write((ushort)nameBytes.Length);
                    w.Write((ushort)0);
                    w.Write((ushort)0);
                    w.Write((ushort)0);
                    w.Write((ushort)0);
                    w.Write((uint)0);
                    w.Write((uint)e.Offset);
                    w.Write(nameBytes);
                }
                long cdSize = _fs.Position - cdStart;
                w.Write(0x06054b50);
                w.Write((ushort)0);
                w.Write((ushort)0);
                w.Write((ushort)_entries.Count);
                w.Write((ushort)_entries.Count);
                w.Write((uint)cdSize);
                w.Write((uint)cdStart);
                w.Write((ushort)0);
                w.Flush();
            }
            finally
            {
                _fs.Dispose();
            }
        }
    }
}
