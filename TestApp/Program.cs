using System;
using System.IO;
using System.Runtime;
using NVorbis;

namespace TestApp
{
    static class Program
    {
        //const string OGG_FILE = @"..\..\..\..\TestFiles\4test.ogg";
        const string OGG_FILE = @"..\..\..\..\TestFiles\3test.ogg";
        //const string OGG_FILE = @"..\..\..\..\TestFiles\2test.ogg";

        static void Main()
        {
            string wavFileName = Path.ChangeExtension(Path.GetFileName(OGG_FILE), "wav");

            float[] sampleBuf = new float[48000 * 2];
            for (int i = 0; i < 1; i++)
            {
                using (FileStream fs = File.OpenRead(OGG_FILE))
                using (ForwardOnlyStream fwdStream = new(fs))
                using (VorbisReader vorbRead = new(fs, false))
                {
                    vorbRead.Initialize();

                    using (WaveWriter waveWriter = new(wavFileName, vorbRead.SampleRate, vorbRead.Channels))
                    {
                        int cnt;
                        while ((cnt = vorbRead.ReadSamples(sampleBuf, 0, sampleBuf.Length)) > 0)
                        {
                            waveWriter.WriteSamples(sampleBuf, 0, cnt);
                        }
                    }
                }
            }
            //Process.Start(wavFileName);

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
