﻿using NAudio.Wave;
using System.Diagnostics;
using Whisper.net;
using Whisper.net.Ggml;

namespace AiTool3.Audio
{
    public class AudioRecorder : IDisposable
    {
        private MemoryStream? memoryStream;
        private WaveFileWriter? writer;

        public bool soundDetected = false;
        public DateTime lastDateTimeAboveThreshold { get; set; } = DateTime.MinValue;

        private WhisperFactory WhisperFactory;
        private WhisperProcessor WhisperProcessor;

        // Define the event
        public event EventHandler<string>? AudioProcessed;

        public async Task RecordAudioAsync(CancellationToken cancellationToken)

        {
            memoryStream = new MemoryStream();

            using (WaveInEvent waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 1)
            })
            using (writer = new WaveFileWriter(memoryStream, waveIn.WaveFormat))
            {
                var tcs = new TaskCompletionSource<bool>();

                waveIn.DataAvailable += (sender, e) =>
                {
                    writer.Write(e.Buffer, 0, e.BytesRecorded);
                    var levelCheck = 7000;

                    var max = 0;
                    for (int i = 0; i < e.BytesRecorded; i += 2)
                    {
                        var sample = BitConverter.ToInt16(e.Buffer, i);
                        if (sample > max)
                        {
                            max = sample;
                        }
                    }
                    if (max > levelCheck)
                    {
                        lastDateTimeAboveThreshold = DateTime.Now;
                        Debug.WriteLine($"Level is higher than {levelCheck}");
                        soundDetected = true;
                    }
                    else
                    {
                        if (!soundDetected)
                        {
                            GetNewMemoryWriter();
                        }
                    }

                    if (soundDetected && DateTime.Now - lastDateTimeAboveThreshold > TimeSpan.FromMilliseconds(1000))
                    {
                        writer.Flush();
                        memoryStream.Position = 0;
                        var buffer = new byte[memoryStream.Length];
                        memoryStream.Read(buffer, 0, buffer.Length);

                        Task.Run(async () =>
                        {
                            await ProcessAudio(buffer);
                        });

                        writer.Dispose();
                        GetNewMemoryWriter();
                        soundDetected = false;
                        lastDateTimeAboveThreshold = DateTime.Now;
                    }

                };

                waveIn.RecordingStopped += (sender, e) =>
                {
                    writer.Flush();
                    writer.Dispose();
                    tcs.TrySetResult(true);
                };

                cancellationToken.Register(() =>
                {
                    waveIn.StopRecording();
                });

                waveIn.StartRecording();

                await tcs.Task;
            }
        }

        public void Dispose()
        {
            writer?.Dispose();
            memoryStream?.Dispose();
            WhisperFactory?.Dispose();
            WhisperProcessor?.Dispose();

        }

        public static byte[] ApplyLowPassFilter(byte[] inputWav, int cutoffFrequency)
        {
            const int HEADER_SIZE = 44;
            const int SAMPLE_RATE = 16000;
            const int BYTES_PER_SAMPLE = 2;

            short[] audioData = new short[(inputWav.Length - HEADER_SIZE) / BYTES_PER_SAMPLE];
            Buffer.BlockCopy(inputWav, HEADER_SIZE, audioData, 0, inputWav.Length - HEADER_SIZE);

            double dt = 1.0 / SAMPLE_RATE;
            double rc = 1.0 / (2 * Math.PI * cutoffFrequency);
            double alpha = dt / (rc + dt);

            double[] filteredData = new double[audioData.Length];
            filteredData[0] = audioData[0];
            for (int i = 1; i < audioData.Length; i++)
            {
                filteredData[i] = filteredData[i - 1] + alpha * (audioData[i] - filteredData[i - 1]);
            }

            short[] filteredAudioData = filteredData.Select(x => (short)Math.Round(x)).ToArray();

            byte[] outputWav = new byte[inputWav.Length];
            Buffer.BlockCopy(inputWav, 0, outputWav, 0, HEADER_SIZE);
            Buffer.BlockCopy(filteredAudioData, 0, outputWav, HEADER_SIZE, filteredAudioData.Length * BYTES_PER_SAMPLE);

            return outputWav;
        }

        private void GetNewMemoryWriter()
        {
            if (writer != null)
                writer.Dispose();
            if (memoryStream != null)
                memoryStream.Dispose();

            memoryStream = new MemoryStream();
            writer = new WaveFileWriter(memoryStream, new WaveFormat(16000, 1));
        }

        internal int GetAudioLevel()
        {
            return (int)(DateTime.Now - lastDateTimeAboveThreshold).TotalMilliseconds;
        }
        private string modelName;
        public AudioRecorder(string modelNameIn)
        {
            modelName = modelNameIn;
            DownloadModel().Wait();

            WhisperFactory = WhisperFactory.FromPath(modelName);
            WhisperProcessor = WhisperFactory.CreateBuilder()
                    .WithLanguage("en")
                    .Build(); ;


        }



        private async Task ProcessAudio(byte[] audioData)
        {
            await DownloadModel();

            var filteredAudioData = ApplyLowPassFilter(audioData, 10000);

            var retVal = "";

            try
            {
                using var memoryStream = new MemoryStream(audioData);
                Debug.WriteLine(">>>");
                await foreach (var result in WhisperProcessor.ProcessAsync(memoryStream))
                {
                    retVal += $"{result.Text}";
                    Debug.WriteLine(result.Text);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

            if (!string.IsNullOrWhiteSpace(retVal))
            {
                // Fire the event
                OnAudioProcessed(retVal);
            }

            return;
        }

        private async Task DownloadModel()
        {
            if (!File.Exists(modelName))
            {
                using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(GgmlType.SmallEn);
                using var fileWriter = File.OpenWrite(modelName);
                await modelStream.CopyToAsync(fileWriter);
            }
        }

        // Method to invoke the event
        protected virtual void OnAudioProcessed(string result)
        {
            AudioProcessed?.Invoke(this, result);
        }
    }
}