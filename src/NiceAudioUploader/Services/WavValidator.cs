using System.Text;

namespace NiceAudioUploader.Services;

public static class WavValidator
{
    public static void Validate(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII);
        bool formatValid = false, hasData = false;
        if (stream.Length < 12 || new string(reader.ReadChars(4)) != "RIFF") Fail();
        var declaredSize = reader.ReadUInt32();
        if (declaredSize + 8L != stream.Length || new string(reader.ReadChars(4)) != "WAVE") Fail();
        while (stream.Position + 8 <= stream.Length)
        {
            var id = new string(reader.ReadChars(4));
            var length = reader.ReadUInt32();
            var next = stream.Position + length + (length % 2);
            if (next > stream.Length) Fail();
            if (id == "fmt ")
            {
                if (length < 16) Fail();
                var codec = reader.ReadUInt16();
                var channels = reader.ReadUInt16();
                var sampleRate = reader.ReadUInt32();
                var byteRate = reader.ReadUInt32();
                var blockAlign = reader.ReadUInt16();
                var bits = reader.ReadUInt16();
                formatValid = codec == 7 && channels == 1 && sampleRate == 8000 &&
                    byteRate == 8000 && blockAlign == 1 && bits == 8;
            }
            else if (id == "data") hasData = length > 0;
            stream.Position = next;
        }
        if (!formatValid || !hasData) Fail();
    }

    private static void Fail() => throw new UserVisibleException("A saída não é um WAV válido de 8000 Hz / mono / PCM μ-law com áudio.");
}
