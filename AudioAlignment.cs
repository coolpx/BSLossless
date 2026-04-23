using System;
using NAudio.Wave;
using NVorbis;

namespace BSSidecarAudio
{
    internal static class AudioAlignment
    {
        private const int AnalysisSampleRate = 11025;
        private const int EnvelopeFramesPerSecond = 100;
        private const int MaxLagSeconds = 8;
        private const int AnalysisSeconds = 24;

        public static float EstimateLeadInCompensation(
            string referenceAudioPath, string flacPath)
        {
            if (string.IsNullOrEmpty(referenceAudioPath)
                || string.IsNullOrEmpty(flacPath))
                return 0f;

            try
            {
                float[] referenceEnvelope = ReadEnvelopeFromVorbis(referenceAudioPath);
                float[] flacEnvelope = ReadEnvelopeFromFlac(flacPath);

                if (referenceEnvelope.Length == 0 || flacEnvelope.Length == 0)
                    return 0f;

                int maxLagFrames = Math.Min(
                    MaxLagSeconds * EnvelopeFramesPerSecond,
                    Math.Max(0, referenceEnvelope.Length - 1));
                int windowLength = Math.Min(
                    flacEnvelope.Length,
                    Math.Max(0, referenceEnvelope.Length - maxLagFrames));

                if (windowLength <= EnvelopeFramesPerSecond)
                    return 0f;

                int bestLag = FindBestLag(referenceEnvelope, flacEnvelope,
                    maxLagFrames, windowLength);
                float compensation = (float)bestLag / EnvelopeFramesPerSecond;

                Plugin.Log.Info(
                    $"Envelope alignment: lagFrames={bestLag} compensation={compensation:F6}s");
                return compensation;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"Audio alignment failed, using zero compensation: {ex.Message}");
                return 0f;
            }
        }

        private static int FindBestLag(float[] referenceEnvelope,
            float[] flacEnvelope, int maxLagFrames, int windowLength)
        {
            int bestLag = 0;
            double bestScore = double.NegativeInfinity;

            for (int lag = 0; lag <= maxLagFrames; lag++)
            {
                int comparableLength = Math.Min(windowLength,
                    referenceEnvelope.Length - lag);
                if (comparableLength <= EnvelopeFramesPerSecond)
                    break;

                double score = ScoreLag(referenceEnvelope, flacEnvelope,
                    lag, comparableLength);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestLag = lag;
                }
            }

            return bestLag;
        }

        private static double ScoreLag(float[] referenceEnvelope,
            float[] flacEnvelope, int lag, int length)
        {
            double dot = 0d;
            double referencePower = 0d;
            double flacPower = 0d;

            for (int i = 0; i < length; i++)
            {
                double a = referenceEnvelope[lag + i];
                double b = flacEnvelope[i];
                dot += a * b;
                referencePower += a * a;
                flacPower += b * b;
            }

            if (referencePower <= double.Epsilon || flacPower <= double.Epsilon)
                return double.NegativeInfinity;

            return dot / Math.Sqrt(referencePower * flacPower);
        }

        private static float[] ReadEnvelopeFromFlac(string path)
        {
            using (var reader = new AudioFileReader(path))
            {
                return ReadEnvelope(reader, reader.WaveFormat.SampleRate,
                    reader.WaveFormat.Channels);
            }
        }

        private static float[] ReadEnvelopeFromVorbis(string path)
        {
            using (var reader = new VorbisReader(path))
            {
                return ReadEnvelope(reader, reader.SampleRate, reader.Channels);
            }
        }

        private static float[] ReadEnvelope(ISampleProvider sampleProvider,
            int sourceSampleRate, int channels)
        {
            int targetFrames = AnalysisSeconds * AnalysisSampleRate;
            int targetSamples = targetFrames * channels;
            int blockSize = 4096 * channels;
            float[] sourceBuffer = new float[blockSize];
            float[] monoSamples = new float[targetFrames];
            int monoCount = 0;
            double frameAccumulator = 0d;
            double sampleRateRatio = (double)sourceSampleRate / AnalysisSampleRate;

            while (monoCount < targetFrames)
            {
                int read = sampleProvider.Read(sourceBuffer, 0, sourceBuffer.Length);
                if (read <= 0)
                    break;

                int framesRead = read / channels;
                for (int frame = 0; frame < framesRead && monoCount < targetFrames; frame++)
                {
                    float mono = 0f;
                    int baseIndex = frame * channels;
                    for (int channel = 0; channel < channels; channel++)
                        mono += sourceBuffer[baseIndex + channel];

                    mono /= channels;
                    frameAccumulator += 1d;

                    if (frameAccumulator >= sampleRateRatio)
                    {
                        monoSamples[monoCount++] = Math.Abs(mono);
                        frameAccumulator -= sampleRateRatio;
                    }
                }
            }

            if (monoCount == 0)
                return Array.Empty<float>();

            int samplesPerEnvelopeFrame = AnalysisSampleRate / EnvelopeFramesPerSecond;
            int envelopeLength = monoCount / samplesPerEnvelopeFrame;
            float[] envelope = new float[envelopeLength];

            for (int i = 0; i < envelopeLength; i++)
            {
                float sum = 0f;
                int start = i * samplesPerEnvelopeFrame;
                for (int j = 0; j < samplesPerEnvelopeFrame; j++)
                    sum += monoSamples[start + j];

                envelope[i] = sum / samplesPerEnvelopeFrame;
            }

            return envelope;
        }

        private static float[] ReadEnvelope(VorbisReader reader,
            int sourceSampleRate, int channels)
        {
            int targetFrames = AnalysisSeconds * AnalysisSampleRate;
            int blockSize = 4096 * channels;
            float[] sourceBuffer = new float[blockSize];
            float[] monoSamples = new float[targetFrames];
            int monoCount = 0;
            double frameAccumulator = 0d;
            double sampleRateRatio = (double)sourceSampleRate / AnalysisSampleRate;

            while (monoCount < targetFrames)
            {
                int read = reader.ReadSamples(sourceBuffer, 0, sourceBuffer.Length);
                if (read <= 0)
                    break;

                int framesRead = read / channels;
                for (int frame = 0; frame < framesRead && monoCount < targetFrames; frame++)
                {
                    float mono = 0f;
                    int baseIndex = frame * channels;
                    for (int channel = 0; channel < channels; channel++)
                        mono += sourceBuffer[baseIndex + channel];

                    mono /= channels;
                    frameAccumulator += 1d;

                    if (frameAccumulator >= sampleRateRatio)
                    {
                        monoSamples[monoCount++] = Math.Abs(mono);
                        frameAccumulator -= sampleRateRatio;
                    }
                }
            }

            if (monoCount == 0)
                return Array.Empty<float>();

            int samplesPerEnvelopeFrame = AnalysisSampleRate / EnvelopeFramesPerSecond;
            int envelopeLength = monoCount / samplesPerEnvelopeFrame;
            float[] envelope = new float[envelopeLength];

            for (int i = 0; i < envelopeLength; i++)
            {
                float sum = 0f;
                int start = i * samplesPerEnvelopeFrame;
                for (int j = 0; j < samplesPerEnvelopeFrame; j++)
                    sum += monoSamples[start + j];

                envelope[i] = sum / samplesPerEnvelopeFrame;
            }

            return envelope;
        }
    }
}
