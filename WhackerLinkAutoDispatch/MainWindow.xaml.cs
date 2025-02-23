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

namespace WhackerLinkAutoDispatch
{
    public partial class MainWindow : Window
    {
        private List<(Field FieldInfo, Control InputControl)> dynamicControls = new();
        private DispatchTemplate dispatchTemplate = null;

        private VoiceChannel voiceChannel = new VoiceChannel();
        private readonly HttpClient httpClient = new HttpClient();
        private IPeer peer = new Peer();

        private readonly SemaphoreSlim playbackLock = new SemaphoreSlim(1, 1);

        private bool pressed = false;

        public MainWindow()
        {
            InitializeComponent();
        }

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

            var selectedChannelName = ChannelSelector.SelectedItem.ToString();
            var selectedChannel = dispatchTemplate.Channels.FirstOrDefault(c => c.Name == selectedChannelName);

            peer.Connect(dispatchTemplate.Network.Address, dispatchTemplate.Network.Port);

            voiceChannel.SrcId = dispatchTemplate.Network.SrcId;
            voiceChannel.DstId = selectedChannel.DstId;
            voiceChannel.Site = dispatchTemplate.Network.Site;

            peer.OnVoiceChannelResponse += async (GRP_VCH_RSP response) =>
            {
                if (!pressed)
                    return;

                if ((response.DstId != selectedChannel.DstId || response.SrcId != dispatchTemplate.Network.SrcId) || (ResponseType)response.Status != ResponseType.GRANT)
                {
                    Debug.WriteLine("Failed");
                    return;
                }

                voiceChannel.Frequency = response.Channel;

                Debug.WriteLine("Voice channel assigned. Sending PCM data...");
                await SendPCMToPeer(peer);
            };
        }

        private async void DispatchNow_Click(object sender, RoutedEventArgs e)
        {
            pressed = true;

            GRP_VCH_REQ vchReq = new GRP_VCH_REQ
            {
                SrcId = dispatchTemplate.Network.SrcId,
                DstId = voiceChannel.DstId,
                Site = dispatchTemplate.Network.Site
            };

            peer.SendMessage(vchReq.GetData());
        }

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
                });

                byte[] pcmData = await GetPCMDataFromMurf();
                if (pcmData == null)
                {
                    Debug.WriteLine("Failed to retrieve PCM data.");
                    return;
                }

                const int sampleSize = 1600; // 100ms at 8000Hz
                //Debug.WriteLine($"Total PCM data length: {pcmData.Length} bytes. Sending in {sampleSize}-byte chunks...");

                Stopwatch stopwatch = new Stopwatch();

                for (int i = 0; i < pcmData.Length; i += sampleSize)
                {
                    stopwatch.Restart();

                    int remaining = Math.Min(sampleSize, pcmData.Length - i);
                    byte[] chunk = new byte[sampleSize];
                    Array.Copy(pcmData, i, chunk, 0, remaining);

                    // Debug.WriteLine($"Sending chunk: {i / sampleSize + 1} | Size: {chunk.Length} bytes");

                    AudioPacket packet = new AudioPacket
                    {
                        Data = chunk,
                        VoiceChannel = voiceChannel
                    };

                    peer.SendMessage(packet.GetData());

                    stopwatch.Stop();
                    int sleepTime = 100 - (int)stopwatch.ElapsedMilliseconds;
                    if (sleepTime > 0)
                        await Task.Delay(sleepTime);
                }


                GRP_VCH_RLS vchRelease = new GRP_VCH_RLS
                {
                    Channel = voiceChannel.Frequency,
                    SrcId = dispatchTemplate.Network.SrcId,
                    DstId = selectedChannel.DstId,
                    Site = dispatchTemplate.Network.Site
                };

                peer.SendMessage(vchRelease.GetData());

                voiceChannel.Frequency = null;

                pressed = false;
            }
            finally
            {
                playbackLock.Release();
            }
        }

        private async Task<byte[]> GetPCMDataFromMurf()
        {
            try
            {
                string text = "";

                Dispatcher.Invoke(() =>
                {
                    List<string> messageParts = new();
                    foreach (var (field, control) in dynamicControls)
                    {
                        if (control is TextBox tb)
                        {
                            if (!string.IsNullOrWhiteSpace(tb.Text))
                                messageParts.Add((field.IncludeFieldName ? $"{field.Name} {tb.Text}" : tb.Text) + field.Ender);
                        }
                        else if (control is ComboBox cb)
                        {
                            var value = cb.SelectedItem?.ToString() ?? "";
                            if (!string.IsNullOrWhiteSpace(value))
                                messageParts.Add((field.IncludeFieldName ? $"{field.Name} {value}" : value) + field.Ender);
                        }
                        else if (control is ListBox lb)
                        {
                            var selectedItems = lb.SelectedItems.Cast<string>();
                            if (selectedItems.Any())
                            {
                                var joined = string.Join(field.Separator ?? ", ", selectedItems);
                                messageParts.Add((field.IncludeFieldName ? $"{field.Name} {joined}" : joined) + field.Ender);
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
                    rate = -8,
                    pitch = -8,
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

    public class DispatchTemplate
    {
        public string TemplateName { get; set; }
        public string MurfApiKey { get; set; }
        public NetworkConfig Network { get; set; }
        public List<Channel> Channels { get; set; }
        public List<Field> Fields { get; set; }
    }

    public class Channel
    {
        public string Name { get; set; }
        public string DstId { get; set; }
    }

    public class NetworkConfig
    {
        public string Address { get; set; }
        public int Port { get; set; }
        public Site Site { get; set; }
        public string SrcId { get; set; } = "1";
    }

    public class Field
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public bool IncludeFieldName { get; set; } = false;
        public bool Multiple { get; set; } = false;
        public string Separator { get; set; } = ", ";
        public string Ender { get; set; } = "";
        public List<string> Options { get; set; }
    }
}
