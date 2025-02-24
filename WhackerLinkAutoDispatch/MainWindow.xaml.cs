/*
* WhackerLink - WhackerLink Auto Dispatch
*
* This program is free software: you can redistribute it and/or modify
* it under the terms of the GNU General Public License as published by
* the Free Software Foundation, either version 3 of the License, or
* (at your option) any later version.
*
* This program is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
* GNU General Public License for more details.
*
* You should have received a copy of the GNU General Public License
* along with this program.  If not, see <http://www.gnu.org/licenses/>.
* 
* Copyright (C) 2024 Caleb, K4PHP
* 
*/

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NAudio.SoundFont;
using NAudio.Wave;
using WhackerLinkLib.Interfaces;
using WhackerLinkLib.Models;
using WhackerLinkLib.Models.IOSP;
using WhackerLinkLib.Network;
using Microsoft.Win32;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using System.Net.Sockets;
using System.Net;
using WhackerLinkLib.Utils;

namespace WhackerLinkAutoDispatch
{
    /// <summary>
    /// MainWindow
    /// </summary>
    public partial class MainWindow : Window
    {
        private List<(Field FieldInfo, Control InputControl)> dynamicControls = new();
        private DispatchTemplate dispatchTemplate = null;

        private VoiceChannel voiceChannel = new VoiceChannel();
        private readonly HttpClient httpClient = new HttpClient();
        private IPeer peer = new Peer();

        private readonly SemaphoreSlim playbackLock = new SemaphoreSlim(1, 1);

        private int sampleSize = 1600; // 100ms at 8000Hz (whackerlink)
        private int delay = 100; // 100ms at 8000Hz (whackerlink)

        private bool pressed = false;

        /// <summary>
        /// Creates an instance of <see cref="MainWindow"/>
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Clears all field selected values
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ClearFields_Click(object sender, RoutedEventArgs e)
        {
            foreach (var (_, control) in dynamicControls)
            {
                switch (control)
                {
                    case TextBox tb:
                        tb.Clear();
                        break;
                    case ComboBox cb:
                        if (cb.Items.Count > 0)
                            cb.SelectedIndex = 0;
                        break;
                    case ListBox lb:
                        lb.UnselectAll();
                        break;
                }
            }
        }

        /// <summary>
        /// Load template file (config file)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LoadTemplate_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "YAML files (*.yml)|*.yml|All files (*.*)|*.*"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                string yamlContent = File.ReadAllText(openFileDialog.FileName);
                LoadTemplate(yamlContent);
            }
        }

        /// <summary>
        /// Load template file (config file)
        /// </summary>
        /// <param name="yamlContent"></param>
        private void LoadTemplate(string yamlContent)
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            dispatchTemplate = deserializer.Deserialize<DispatchTemplate>(yamlContent);

            ChannelSelector.Items.Clear();

            foreach (var channel in dispatchTemplate.Channels)
            {
                ChannelSelector.Items.Add(channel.Name);
            }

            if (ChannelSelector.Items.Count > 0)
                ChannelSelector.SelectedIndex = 0;

            DynamicFieldsPanel.Children.Clear();
            dynamicControls.Clear();

            foreach (var field in dispatchTemplate.Fields)
            {
                TextBlock label = new() { Text = field.Name, Foreground = System.Windows.Media.Brushes.White, FontSize = 16 };
                DynamicFieldsPanel.Children.Add(label);

                if (field.Type == "TextBox")
                {
                    TextBox textBox = new() { Width = 400, Height = 30, Margin = new Thickness(0, 5, 0, 15) };
                    DynamicFieldsPanel.Children.Add(textBox);
                    dynamicControls.Add((field, textBox));
                }
                else if (field.Type == "Dropdown")
                {
                    if (field.Type == "Dropdown")
                    {
                        if (field.Multiple)
                        {
                            ListBox listBox = new()
                            {
                                Width = 400,
                                Height = 100,
                                Margin = new Thickness(0, 5, 0, 15),
                                SelectionMode = SelectionMode.Multiple
                            };
                            listBox.ItemsSource = field.Options;
                            DynamicFieldsPanel.Children.Add(listBox);
                            dynamicControls.Add((field, listBox));
                        }
                        else
                        {
                            ComboBox comboBox = new()
                            {
                                Width = 400,
                                Height = 30,
                                Margin = new Thickness(0, 5, 0, 15)
                            };
                            comboBox.ItemsSource = field.Options;
                            comboBox.SelectedIndex = 0;
                            DynamicFieldsPanel.Children.Add(comboBox);
                            dynamicControls.Add((field, comboBox));
                        }
                    }

                }
            }

            if (dispatchTemplate.Dvm != null && dispatchTemplate.Dvm.Enabled)
            {
                // DVM bridge
                sampleSize = 320;
                delay = 20;
            }

            var selectedChannelName = ChannelSelector.SelectedItem.ToString();
            var selectedChannel = dispatchTemplate.Channels.FirstOrDefault(c => c.Name == selectedChannelName);

            if (dispatchTemplate == null || !dispatchTemplate.Dvm.Enabled)
            {
                peer.Connect(dispatchTemplate.Network.Address, dispatchTemplate.Network.Port);

                voiceChannel.SrcId = dispatchTemplate.Network.SrcId;
                voiceChannel.Site = dispatchTemplate.Network.Site;

                peer.OnVoiceChannelResponse += (GRP_VCH_RSP response) =>
                {
                    if (!pressed)
                        return;

                    if ((response.DstId != voiceChannel.DstId || response.SrcId != dispatchTemplate.Network.SrcId) || (ResponseType)response.Status != ResponseType.GRANT)
                    {
                        //Debug.WriteLine("Failed");
                        return;
                    }

                    voiceChannel.Frequency = response.Channel;

                    Debug.WriteLine("Voice channel assigned. Sending PCM data...");

                    Task.Factory.StartNew(() =>
                    {
                        SendPCMToPeer(peer);
                    });
                };
            }
        }

        /// <summary>
        /// Start the dispatch
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void DispatchNow_Click(object sender, RoutedEventArgs e)
        {
            pressed = true;

            if (dispatchTemplate.Dvm == null || !dispatchTemplate.Dvm.Enabled)
            {
                Dispatcher.Invoke(() =>
                {
                    var selectedChannelName = ChannelSelector.SelectedItem.ToString();
                    var selectedChannel = dispatchTemplate.Channels.FirstOrDefault(c => c.Name == selectedChannelName);

                    voiceChannel.DstId = selectedChannel.DstId;
                });

                GRP_VCH_REQ vchReq = new GRP_VCH_REQ
                {
                    SrcId = dispatchTemplate.Network.SrcId,
                    DstId = voiceChannel.DstId,
                    Site = dispatchTemplate.Network.Site
                };

                peer.SendMessage(vchReq.GetData());
            } else
            {
                await SendPCMToPeer(peer);
            }
        }

        /// <summary>
        /// Send the dispatch to the master
        /// </summary>
        /// <param name="peer"></param>
        /// <returns></returns>
        private async Task SendPCMToPeer(IPeer peer)
        {
            await playbackLock.WaitAsync();
            try
            {
                Channel selectedChannel = null;

                Dispatcher.Invoke(() =>
                {
                    var selectedChannelName = ChannelSelector.SelectedItem.ToString();
                    selectedChannel = dispatchTemplate.Channels.FirstOrDefault(c => c.Name == selectedChannelName);

                    voiceChannel.DstId = selectedChannel.DstId;
                });

                byte[] firstPcm = await GetPCMDataFromMurf();
                if (firstPcm == null)
                {
                    Debug.WriteLine("Failed to retrieve PCM data.");
                    return;
                }

                byte[] secondPcm = await GetPCMDataFromMurf(true);
                if (secondPcm == null)
                {
                    Debug.WriteLine("Failed to retrieve PCM second data.");
                    return;
                }

                await SendWavFileToPeer(peer, Path.Combine(Directory.GetCurrentDirectory(), "avdtones.wav"));

                await SendNetworkPCM(firstPcm);

                if (dispatchTemplate.Repeat)
                    await SendNetworkPCM(secondPcm, true);


                if (dispatchTemplate == null || !dispatchTemplate.Dvm.Enabled)
                {
                    GRP_VCH_RLS vchRelease = new GRP_VCH_RLS
                    {
                        Channel = voiceChannel.Frequency,
                        SrcId = dispatchTemplate.Network.SrcId,
                        DstId = selectedChannel.DstId,
                        Site = dispatchTemplate.Network.Site
                    };

                    peer.SendMessage(vchRelease.GetData());

                    voiceChannel.Frequency = null;
                }

                pressed = false;
            }
            finally
            {
                playbackLock.Release();
            }
        }

        /// <summary>
        /// Send PCM to the network
        /// </summary>
        /// <param name="second"></param>
        /// <returns></returns>
        private async Task<bool> SendNetworkPCM(byte[] pcmData, bool second = false)
        {
            //Debug.WriteLine($"Total PCM data length: {pcmData.Length} bytes. Sending in {sampleSize}-byte chunks...");

            Stopwatch stopwatch = new Stopwatch();

            for (int i = 0; i < pcmData.Length; i += sampleSize)
            {
                stopwatch.Restart();

                int remaining = Math.Min(sampleSize, pcmData.Length - i);
                byte[] chunk = new byte[sampleSize];
                Array.Copy(pcmData, i, chunk, 0, remaining);

                // Debug.WriteLine($"Sending chunk: {i / sampleSize + 1} | Size: {chunk.Length} bytes");

                if (dispatchTemplate == null || !dispatchTemplate.Dvm.Enabled)
                {
                    AudioPacket packet = new AudioPacket
                    {
                        Data = chunk,
                        VoiceChannel = voiceChannel
                    };

                    peer.SendMessage(packet.GetData());
                }
                else
                {
                    byte[] udpPayload = new byte[324]; // length + PCM


                    byte[] lengthBytes = BitConverter.GetBytes(sampleSize);

                    Array.Reverse(lengthBytes);

                    Array.Copy(lengthBytes, udpPayload, 4);
                    Array.Copy(chunk, 0, udpPayload, 4, sampleSize);

                    //Debug.WriteLine(BitConverter.ToString(udpPayload));

                    SendUDP(dispatchTemplate.Dvm.Address, dispatchTemplate.Dvm.Port, udpPayload);
                }

                stopwatch.Stop();
                int sleepTime = delay - (int)stopwatch.ElapsedMilliseconds;
                if (sleepTime > 0)
                    await Task.Delay(sleepTime);
            }

            return true;
        }

        /// <summary>
        /// Get PCM from the Murf API
        /// </summary>
        /// <param name="second"></param>
        /// <returns></returns>
        private async Task<byte[]> GetPCMDataFromMurf(bool second = false)
        {
            try
            {
                string text = "";

                Dispatcher.Invoke(() =>
                {
                    List<string> messageParts = new();
                    foreach (var (field, control) in dynamicControls)
                    {
                        if (second && field.NoRepeat)
                            continue;

                        if (!second && field.EndOnly)
                            continue;

                        if (control is TextBox tb)
                        {
                            if (!string.IsNullOrWhiteSpace(tb.Text))
                                messageParts.Add((field.IncludeFieldName && field.SaidName != string.Empty ? $"{field.SaidName} {tb.Text}" : tb.Text) + field.Ender);
                        }
                        else if (control is ComboBox cb)
                        {
                            var value = cb.SelectedItem?.ToString() ?? "";

                            if (!string.IsNullOrWhiteSpace(value))
                                messageParts.Add((field.IncludeFieldName && field.SaidName != string.Empty ? $"{field.SaidName} {value}" : value) + field.Ender);
                        }
                        else if (control is ListBox lb)
                        {
                            var selectedItems = lb.SelectedItems.Cast<string>();
                            if (selectedItems.Any())
                            {
                                var joined = string.Join(field.Separator ?? ", ", selectedItems);
                                messageParts.Add((field.IncludeFieldName && field.SaidName != string.Empty ? $"{field.SaidName} {joined}" : joined) + field.Ender);
                            }
                        }
                    }
                    text = string.Join(Environment.NewLine, messageParts);
                });

                var requestBody = new
                {
                    voiceId = "en-US-charlotte",
                    style = "Promo",
                    text = text,
                    rate = dispatchTemplate.TtsConfig.Rate,
                    pitch = dispatchTemplate.TtsConfig.Pitch,
                    sampleRate = 8000,
                    format = "WAV",
                    channelType = "MONO",
                    encodeAsBase64 = false
                };

                var requestContent = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json"
                );

                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("api-key", dispatchTemplate.MurfApiKey);
                httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                var response = await httpClient.PostAsync("https://api.murf.ai/v1/speech/generate", requestContent);
                response.EnsureSuccessStatusCode();

                var jsonResponse = await response.Content.ReadAsStringAsync();
                string audioUrl = System.Text.Json.JsonDocument
                    .Parse(jsonResponse)
                    .RootElement
                    .GetProperty("audioFile")
                    .GetString();

                var wavData = await httpClient.GetByteArrayAsync(audioUrl);

                return ConvertWavToPcm(wavData);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during PCM processing: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Send wav file to the master
        /// </summary>
        /// <param name="peer"></param>
        /// <param name="filePath"></param>
        /// <returns></returns>
        private async Task SendWavFileToPeer(IPeer peer, string filePath)
        {
            if (!File.Exists(filePath))
            {
                Debug.WriteLine($"File '{filePath}' not found. Skipping pre-tone playback.");
                return;
            }

            try
            {
                using (var wavStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (var reader = new WaveFileReader(wavStream))
                using (var pcmStream = new MemoryStream())
                using (var pcmProvider = new WaveFormatConversionStream(new WaveFormat(8000, 16, 1), reader))
                {
                    pcmProvider.CopyTo(pcmStream);
                    byte[] pcmData = pcmStream.ToArray();

                    Stopwatch stopwatch = new Stopwatch();

                    //Debug.WriteLine($"Sending '{filePath}' ({pcmData.Length} bytes) before TTS audio.");

                    for (int i = 0; i < pcmData.Length; i += sampleSize)
                    {
                        stopwatch.Restart();

                        int remaining = Math.Min(sampleSize, pcmData.Length - i);
                        byte[] chunk = new byte[sampleSize];
                        Array.Copy(pcmData, i, chunk, 0, remaining);

                        if (dispatchTemplate == null || !dispatchTemplate.Dvm.Enabled)
                        {
                            AudioPacket packet = new AudioPacket
                            {
                                Data = chunk,
                                VoiceChannel = voiceChannel,
                                LopServerVocode = true
                            };

                            peer.SendMessage(packet.GetData());
                        }
                        else
                        {
                            byte[] udpPayload = new byte[324]; // length + PCM


                            byte[] lengthBytes = BitConverter.GetBytes(sampleSize);

                            Array.Reverse(lengthBytes);

                            Array.Copy(lengthBytes, udpPayload, 4);
                            Array.Copy(chunk, 0, udpPayload, 4, sampleSize);

                            // Debug.WriteLine(BitConverter.ToString(udpPayload));

                            SendUDP(dispatchTemplate.Dvm.Address, dispatchTemplate.Dvm.Port, udpPayload);
                        }

                        stopwatch.Stop();
                        int sleepTime = delay - (int)stopwatch.ElapsedMilliseconds;
                        if (sleepTime > 0)
                            await Task.Delay(sleepTime);
                    }
                }

                Debug.WriteLine($"Finished sending '{filePath}'.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sending WAV file '{filePath}': {ex.Message}");
            }
        }

        /// <summary>
        /// Helper to send byte[] to UDP endpoint
        /// </summary>
        /// <param name="ipAddress"></param>
        /// <param name="port"></param>
        /// <param name="data"></param>
        public static void SendUDP(string ipAddress, int port, byte[] data)
        {
            using (UdpClient udpClient = new UdpClient())
            {
                try
                {
                    IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);

                    udpClient.Send(data, data.Length, endPoint);

                    Console.WriteLine($"Sent {data.Length} bytes to {ipAddress}:{port}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending UDP data: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Helper to convert wav data to raw PCM
        /// </summary>
        /// <param name="wavData"></param>
        /// <returns></returns>
        private byte[] ConvertWavToPcm(byte[] wavData)
        {
            using (var wavStream = new MemoryStream(wavData))
            using (var reader = new WaveFileReader(wavStream))
            using (var pcmStream = new MemoryStream())
            using (var pcmProvider = new WaveFormatConversionStream(new WaveFormat(8000, 16, 1), reader))
            {
                pcmProvider.CopyTo(pcmStream);
                return pcmStream.ToArray();
            }
        }
    }

    /// <summary>
    /// Config template object
    /// </summary>
    public class DispatchTemplate
    {
        public string TemplateName { get; set; }
        public string MurfApiKey { get; set; }
        public bool Repeat { get; set; } = false;
        public NetworkConfig Network { get; set; }
        public List<Channel> Channels { get; set; }
        public TtsConfig TtsConfig { get; set; }
        public DvmConfig Dvm { get; set; } = null;
        public List<Field> Fields { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class DvmConfig
    {
        public bool Enabled { get; set; } = false;
        public int Port { get; set; } = 34001;
        public string Address { get; set; } = "127.0.0.1";
    }

    /// <summary>
    /// 
    /// </summary>
    public class Channel
    {
        public string Name { get; set; }
        public string DstId { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    public class NetworkConfig
    {
        public string Address { get; set; }
        public int Port { get; set; }
        public Site Site { get; set; }
        public string SrcId { get; set; } = "1";
    }

    /// <summary>
    /// 
    /// </summary>
    public class TtsConfig
    {
        public int Rate { get; set; } = -8;
        public int Pitch { get; set; } = -8;
    }
    
    /// <summary>
    /// 
    /// </summary>
    public class Field
    {
        public string Name { get; set; }
        public string SaidName { get; set; }
        public string Type { get; set; }
        public bool IncludeFieldName { get; set; } = false;
        public bool Multiple { get; set; } = false;
        public bool NoRepeat { get; set; } = false;
        public bool EndOnly { get; set; } = false;
        public string Separator { get; set; } = ", ";
        public string Ender { get; set; } = "";
        public List<string> Options { get; set; }
    }
}
