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
* Copyright (C) 2025 Caleb, K4PHP
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
using Newtonsoft.Json;
using YamlDotNet.Core.Tokens;
using static System.Net.Mime.MediaTypeNames;
using System.Windows.Media;

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

        private int sampleSize = 1600;  // 100ms at 8000Hz (whackerlink)
        private int delay = 100;        // 100ms at 8000Hz (whackerlink)

        private const string IMPERIAL_URL = "https://imperialcad.app/api/1.1/wf/CallCreate";

        private bool pressed = false;

        /// <summary>
        /// Creates an instance of <see cref="MainWindow"/>
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

#if DEBUG
            ConsoleNative.ShowConsole();
#endif

            SendCadBtn.Visibility = Visibility.Collapsed;
            AddCadLbl.Visibility = Visibility.Collapsed;
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

            if (dispatchTemplate.Imperial != null)
            {
                if (dispatchTemplate.Imperial.Enabled)
                {
                    SendCadBtn.Visibility = Visibility.Visible;
                    AddCadLbl.Visibility = Visibility.Visible;
                }
            }

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
                if (peer.IsConnected)
                    peer.Disconnect();

                peer.Connect(dispatchTemplate.Network.Address, dispatchTemplate.Network.Port, dispatchTemplate.Network.AuthKey);

                GRP_AFF_REQ req = new GRP_AFF_REQ
                {
                    DstId = selectedChannel.DstId,
                    SrcId = dispatchTemplate.Network.SrcId,
                    Site = dispatchTemplate.Network.Site
                };

                peer.SendMessage(req.GetData());

                voiceChannel.SrcId = dispatchTemplate.Network.SrcId;
                voiceChannel.Site = dispatchTemplate.Network.Site;

                peer.OnOpen += () =>
                {
                    Dispatcher.Invoke(() => { Background = (Brush)new BrushConverter().ConvertFrom("#222222"); });
                };

                peer.OnClose += () =>
                {
                    Dispatcher.Invoke(() => { Background = Brushes.Red; });
                };

                peer.OnReconnecting += () =>
                {
                    Dispatcher.Invoke(() => { Background = Brushes.Orange; });
                };

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

                    Console.WriteLine("Voice channel assigned. Sending PCM data...");

                    Task.Factory.StartNew(() =>
                    {
                        Thread.CurrentThread.Priority = ThreadPriority.Highest;
                        SendPCMToPeer(peer);
                    }, TaskCreationOptions.LongRunning);
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

                bool sendCad = false;

                Dispatcher.Invoke(() => { sendCad = (bool)SendCadBtn.IsChecked; });

                Task.Factory.StartNew(() => {
                    if (dispatchTemplate.Imperial != null) {
                        if (dispatchTemplate.Imperial.Enabled && sendCad) {

                            ImperialCallRequest callReq = new ImperialCallRequest
                            {
                                CommId = dispatchTemplate.Imperial.CommId,
                                Street = "Test Street",             // need
                                CrossStreet = string.Empty,
                                Postal = "00000",                   // need
                                City = string.Empty,
                                County = string.Empty,
                                Info = string.Empty,
                                Nature = "Call Nature",             // need
                                Status = "PENDING",                 // static
                                Priority = 2                        // static
                            };

                            List<string> messageParts = new();

                            Dispatcher.Invoke(() =>
                            {
                                foreach (var (field, control) in dynamicControls)
                                {
                                    if (control is TextBox tb)
                                    {
                                        if (!string.IsNullOrWhiteSpace(tb.Text))
                                        {
                                            if (field.IsImperialStreet)
                                                callReq.Street = tb.Text;

                                            if (field.IsImperialPostal)
                                                callReq.Postal = tb.Text;

                                            if (field.IsImperialNature)
                                                callReq.Nature = tb.Text;

                                            if (field.IsImperialNote)
                                                messageParts.Add(tb.Text);
                                        }
                                    }
                                    else if (control is ComboBox cb)
                                    {
                                        var value = cb.SelectedItem?.ToString() ?? "";

                                        if (!string.IsNullOrWhiteSpace(value))
                                        {
                                            if (field.IsImperialStreet)
                                                callReq.Street = value;

                                            if (field.IsImperialPostal)
                                                callReq.Postal = value;

                                            if (field.IsImperialNature)
                                                callReq.Nature = value;

                                            if (field.IsImperialNote)
                                                messageParts.Add(value);
                                        }
                                    }
                                    else if (control is ListBox lb)
                                    {
                                        var selectedItems = lb.SelectedItems.Cast<string>();
                                        if (selectedItems.Any())
                                        {
                                            if (field.IsImperialNote)
                                                messageParts.AddRange(selectedItems);
                                        }
                                    }
                                }
                            });

                            callReq.Info = string.Join(Environment.NewLine, messageParts);

                            SendCallRequestAsync(callReq);
                        }
                    }
                });

                byte[] firstPcm = await GetPCMDataFromMurf();
                if (firstPcm == null)
                {
                    Console.WriteLine("Failed to retrieve PCM data.");
                    return;
                }

                byte[] secondPcm = await GetPCMDataFromMurf(true);
                if (secondPcm == null)
                {
                    Console.WriteLine("Failed to retrieve PCM second data.");
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

                    Console.WriteLine("Sent voice channel release");
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
            Console.WriteLine($"Total PCM data length: {pcmData.Length} bytes. Sending in {sampleSize}-byte chunks...");

            Stopwatch stopwatch = new Stopwatch();

            for (int i = 0; i < pcmData.Length; i += sampleSize)
            {
                stopwatch.Restart();

                int remaining = Math.Min(sampleSize, pcmData.Length - i);
                byte[] chunk = new byte[sampleSize];
                Array.Copy(pcmData, i, chunk, 0, remaining);

                Console.WriteLine($"Sending chunk: {i / sampleSize + 1} | Size: {chunk.Length} bytes");

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

                int sleepTime = delay - (int)stopwatch.ElapsedMilliseconds;
                while (stopwatch.ElapsedMilliseconds < delay) { /* stub */ }
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
                Console.WriteLine($"Error during PCM processing: {ex.Message}");
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
                Console.WriteLine($"File '{filePath}' not found. Skipping pre-tone playback.");
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

                        int sleepTime = delay - (int)stopwatch.ElapsedMilliseconds;
                        while (stopwatch.ElapsedMilliseconds < delay) { /* stub */ }
                    }
                }

                Console.WriteLine($"Finished sending '{filePath}'.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending WAV file '{filePath}': {ex.Message}");
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

                    // Console.WriteLine($"Sent {data.Length} bytes to {ipAddress}:{port}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending UDP data: {ex.Message}");
                }
            }
        }

        public async Task SendCallRequestAsync(ImperialCallRequest callReq)
        {
            HttpClient client = new HttpClient();

            var json = JsonConvert.SerializeObject(callReq);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            content.Headers.Add("APIKEY", dispatchTemplate.Imperial.ApiKey);

            try
            {
                HttpResponseMessage response = await client.PostAsync(IMPERIAL_URL, content);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine("Response: " + responseBody);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
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

        private void ChannelSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string selectedChannelName = ChannelSelector.SelectedItem.ToString();
            Channel selectedChannel = dispatchTemplate.Channels.FirstOrDefault(c => c.Name == selectedChannelName);

            GRP_AFF_REQ affReq = new GRP_AFF_REQ
            {
                SrcId = dispatchTemplate.Network.SrcId,
                DstId = selectedChannel.DstId,
                Site = dispatchTemplate.Network.Site
            };

            peer.SendMessage(affReq.GetData());

            Console.WriteLine("Sent WLINK master aff " + selectedChannel.DstId);
        }
    }
}
