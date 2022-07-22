using System;
using System.IO;
using System.Runtime;
using NVorbis;

namespace TestApp
{
    partial class Program
    {
        static void Main(string[] args)
        {
            string sourceDir = args.Length > 0 ? args[0] : "../../../../TestFiles";
            string targetDir = args.Length > 1 ? args[1] : "";

            string[] files = Directory.GetFiles(sourceDir);
            foreach (string file in files)
            {
                Console.WriteLine($"Decoding {file}...");

                string? dstFile = null;
                if (targetDir != "NULL")
                {
                    string dirName = Path.Join(targetDir, Path.GetFileNameWithoutExtension(file));
                    Directory.CreateDirectory(dirName);
                    Console.WriteLine($"Created directory {dirName}");

                    string fileName = Path.ChangeExtension(Path.GetFileName(file), "wav");
                    dstFile = Path.Join(dirName, fileName);
                }

                DecodeFile(file, dstFile);

                Console.WriteLine($"Finished decoding {file}\n");
            }
        }

        static void DecodeFile(string sourceFile, string? destinationFile)
        {
            float[] sampleBuf1 = new float[48000];
            float[] sampleBuf2 = new float[48000];

            byte[] bytes = File.ReadAllBytes(sourceFile);

            for (int j = 0; j < 2; j++)
            {
                Console.WriteLine($" Iteration {j + 1}");

                Decode(bytes, (vorbRead1, vorbRead2) =>
                {
                    if (j != 0)
                    {
                        vorbRead1.ClipSamples = false;
                        vorbRead2.ClipSamples = false;
                    }

                    Console.WriteLine("  Comparing seek vs forward decode");
                    do
                    {
                        int cnt1 = vorbRead1.ReadSamples(sampleBuf1);
                        int cnt2 = vorbRead2.ReadSamples(sampleBuf2);

                        Span<float> samples1 = sampleBuf1.AsSpan(0, cnt1 * vorbRead1.Channels);
                        Span<float> samples2 = sampleBuf2.AsSpan(0, cnt2 * vorbRead2.Channels);

                        if (cnt1 == 0 || cnt2 == 0)
                        {
                            if (cnt1 != cnt2)
                            {
                                throw new Exception("Decoded different sample counts!");
                            }
                            break;
                        }

                        if (!samples1.SequenceEqual(samples1))
                        {
                            throw new Exception("Decodes were not equal!");
                        }
                    }
                    while (true);
                });
                FullGC();

                Decode(bytes, (vorbRead1, vorbRead2) =>
                {
                    string? fileName = destinationFile;
                    if (j != 0)
                    {
                        vorbRead1.ClipSamples = false;
                        vorbRead2.ClipSamples = false;

                        if (fileName != null)
                            fileName = AppendToFileName(fileName, "-noclip");
                    }

                    Console.WriteLine($"  Writing interleaved {fileName}");
                    int channels = vorbRead1.Channels;
                    using (WaveWriter writer = new(CreateWriteStream(fileName), false, vorbRead1.SampleRate, channels))
                    {
                        int sampleCount;
                        while ((sampleCount = vorbRead1.ReadSamples(sampleBuf1)) > 0)
                        {
                            writer.WriteSamples(sampleBuf1.AsSpan(0, sampleCount * channels));
                        }
                    }
                });
                FullGC();

                Decode(bytes, (vorbRead1, vorbRead2) =>
                {
                    if (vorbRead1.Channels != 2)
                    {
                        Console.WriteLine($"  Skipped writing non-interleaved, source has {vorbRead1.Channels} channels");
                        return;
                    }

                    string? fileName = destinationFile;
                    if (j != 0)
                    {
                        vorbRead1.ClipSamples = false;
                        vorbRead2.ClipSamples = false;

                        if (fileName != null)
                            fileName = AppendToFileName(fileName, "-noclip");
                    }
                    Console.WriteLine($"  Writing non-interleaved {fileName}");

                    string? leftFile = fileName != null ? AppendToFileName(fileName, "-left") : null;
                    string? rightFile = fileName != null ? AppendToFileName(fileName, "-right") : null;

                    using (WaveWriter leftWriter = new(CreateWriteStream(leftFile), false, vorbRead1.SampleRate, 1))
                    using (WaveWriter rightWriter = new(CreateWriteStream(rightFile), false, vorbRead1.SampleRate, 1))
                    {
                        int stride = sampleBuf1.Length / 2;

                        int sampleCount;
                        while ((sampleCount = vorbRead1.ReadSamples(sampleBuf1, stride, stride)) > 0)
                        {
                            leftWriter.WriteSamples(sampleBuf1.AsSpan(0, sampleCount));
                            rightWriter.WriteSamples(sampleBuf1.AsSpan(stride, sampleCount));
                        }
                    }
                });
                FullGC();
            }
        }

        public static void Decode(byte[] bytes, Action<VorbisReader, VorbisReader> action)
        {
            using (MemoryStream fs = new(bytes, false))
            using (ForwardOnlyStream fwdStream = new(new MemoryStream(bytes, false), false))
            using (VorbisReader vorbRead1 = new(fs, false))
            using (VorbisReader vorbRead2 = new(fwdStream, false))
            {
                vorbRead1.Initialize();
                vorbRead2.Initialize();

                action.Invoke(vorbRead1, vorbRead2);
            }
        }

        public static void FullGC()
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        public static string AppendToFileName(ReadOnlySpan<char> source, ReadOnlySpan<char> appendValue)
        {
            ReadOnlySpan<char> dirName = Path.GetDirectoryName(source);
            ReadOnlySpan<char> fileName = Path.GetFileNameWithoutExtension(source);
            ReadOnlySpan<char> ext = Path.GetExtension(source);
            return $"{Path.Join(dirName, fileName)}{appendValue}{ext}";
        }

        public static Stream CreateWriteStream(string? fileName)
        {
            if (fileName == null)
            {
                return Stream.Null;
            }
            return new FileStream(fileName, FileMode.Create);
        }
    }
}
