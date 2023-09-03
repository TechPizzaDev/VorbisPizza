using System;
using System.Buffers;
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
            bool writeToFile = args.Length > 2 ? bool.Parse(args[2]) : true;

            FileMode? writeMode = writeToFile ? FileMode.Create : null;

            string[] files = Directory.GetFiles(sourceDir);
            foreach (string file in files)
            {
                Console.WriteLine($"Decoding {file}...");

                string dirName = Path.Join(targetDir, Path.GetFileNameWithoutExtension(file));
                if (writeToFile)
                {
                    Directory.CreateDirectory(dirName);
                    Console.WriteLine($"Created directory {dirName}");
                }

                string fileName = Path.ChangeExtension(Path.GetFileName(file), "wav");
                string dstFile = Path.Join(dirName, fileName);
                
                DecodeFile(file, dstFile, writeMode);

                Console.WriteLine($"Finished decoding {file}\n");
            }
        }

        static void DecodeFile(string sourceFile, string destinationFile, FileMode? writeMode)
        {
            float[] sampleBuf1 = new float[48000];
            float[] sampleBuf2 = new float[48000];

            byte[] bytes = File.ReadAllBytes(sourceFile);

            int iterationCount = 2;

            Console.WriteLine("  Comparing seek vs forward decode");
            for (int j = 0; j < iterationCount; j++)
            {
                Console.WriteLine($" Iteration {j + 1}");

                Decode(bytes, (vorbRead1, vorbRead2) =>
                {
                    if (j % 2 != 0)
                    {
                        vorbRead1.ClipSamples = false;
                        vorbRead2.ClipSamples = false;
                    }

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
            }
            FullGC();

            Console.WriteLine($"  Writing interleaved {destinationFile}");
            for (int j = 0; j < iterationCount; j++)
            {
                Console.WriteLine($" Iteration {j + 1}");

                Decode(bytes, (vorbRead1, vorbRead2) =>
                {
                    string fileName = destinationFile;
                    if (j % 2 != 0)
                    {
                        vorbRead1.ClipSamples = false;
                        vorbRead2.ClipSamples = false;

                            fileName = AppendToFileName(fileName, "-noclip");
                    }

                    int channels = vorbRead1.Channels;
                    using (WaveWriter writer = new(CreateStream(fileName, writeMode), false, vorbRead1.SampleRate, channels))
                    {
                        int sampleCount;
                        while ((sampleCount = vorbRead1.ReadSamples(sampleBuf1)) > 0)
                        {
                            writer.WriteSamples(sampleBuf1.AsSpan(0, sampleCount * channels));
                        }
                    }
                });
            }
            FullGC();

            Console.WriteLine($"  Writing non-interleaved {destinationFile}");
            for (int j = 0; j < iterationCount; j++)
            {
                Console.WriteLine($" Iteration {j + 1}");

                Decode(bytes, (vorbRead1, vorbRead2) =>
                {
                    if (vorbRead1.Channels != 2)
                    {
                        Console.WriteLine($"  Skipped writing non-interleaved, source has {vorbRead1.Channels} channels");
                        return;
                    }

                    string fileName = destinationFile;
                    if (j % 2 != 0)
                    {
                        vorbRead1.ClipSamples = false;
                        vorbRead2.ClipSamples = false;

                            fileName = AppendToFileName(fileName, "-noclip");
                    }

                    string leftFile = AppendToFileName(fileName, "-left");
                    string rightFile = AppendToFileName(fileName, "-right");

                    using (WaveWriter leftWriter = new(CreateStream(leftFile, writeMode), false, vorbRead1.SampleRate, 1))
                    using (WaveWriter rightWriter = new(CreateStream(rightFile, writeMode), false, vorbRead1.SampleRate, 1))
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
            }
            FullGC();
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

        public static Stream CreateStream(string fileName, FileMode? mode)
        {
            if (mode == null)
            {
                return Stream.Null;
            }
            return new FileStream(fileName, mode.Value);
        }
    }
}
