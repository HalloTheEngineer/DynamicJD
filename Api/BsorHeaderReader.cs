using System;
using System.IO;
using System.Text;

namespace DynamicJD.Api
{
    /// <summary>
    /// Reads the leading metadata out of a BeatLeader BSOR replay without decoding the movement and
    /// note streams. The CDN supports range requests, so the clone service only downloads a few KB.
    /// </summary>
    public static class BsorHeaderReader
    {
        private const int Magic = 0x442d3d69;

        public static BsorHeader Read(byte[] data)
        {
            if (data == null || data.Length < 3) return null;
            try
            {
                using (var stream = new MemoryStream(data, false))
                using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                {
                    if (reader.ReadInt32() != Magic) return null;
                    byte version = reader.ReadByte();
                    if (version != 1) return null;
                    if (reader.ReadByte() != 0) return null;

                    // Info block: 13 strings, score, mode, environment, modifiers, jumpDistance.
                    ReadString(reader); // mod version
                    ReadString(reader); // game version
                    ReadString(reader); // timestamp
                    string playerId = ReadString(reader);
                    string playerName = ReadString(reader);
                    ReadString(reader); // platform
                    ReadString(reader); // tracking system
                    ReadString(reader); // hmd
                    ReadString(reader); // controller
                    string hash = ReadString(reader);
                    ReadString(reader); // song name
                    ReadString(reader); // mapper
                    string difficulty = ReadString(reader);

                    reader.ReadInt32(); // score
                    string mode = ReadString(reader);
                    ReadString(reader); // environment
                    string modifiers = ReadString(reader);
                    float jumpDistance = reader.ReadSingle();

                    return new BsorHeader
                    {
                        JumpDistance = jumpDistance,
                        Hash = hash,
                        Difficulty = difficulty,
                        Mode = mode,
                        Modifiers = modifiers,
                        PlayerId = playerId,
                        PlayerName = playerName,
                    };
                }
            }
            catch
            {
                return null;
            }
        }

        private static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > 1024 * 1024)
            {
                throw new InvalidDataException("Invalid BSOR string length");
            }

            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
            {
                throw new EndOfStreamException();
            }

            return Encoding.UTF8.GetString(bytes);
        }
    }
}
