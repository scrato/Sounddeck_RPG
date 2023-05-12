using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Newtonsoft.Json;

namespace RPG_Deck
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private WaveOutEvent _outputDevice;
        private AudioFileReader _audioFile;
        private AudioFileReader _nextAudioFile;
        private VolumeSampleProvider _volumeProvider;
        private bool _isMuted;

        private SongList _songList = new SongList();
        private const string c_ConfigPath = "config.json";

        private Brush _standardButtonBGColor;


        public MainWindow()
        {
            InitializeComponent();

            _standardButtonBGColor = new Button().Background;


            if (File.Exists(c_ConfigPath))
            {
                try
                {
                    _songList = JsonConvert.DeserializeObject<SongList>(File.ReadAllText(c_ConfigPath));
                } catch (Exception) { }
            }

            foreach (var song in _songList.Songs)
            {
                Button audioButton = InitButton(song.Name, song.Path);
                ButtonsPanel.Children.Add(audioButton);
            }

        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var song = AddOrEditSongInfo();
            if (song == null)
                return;
            var audioButton = InitButton(song.Name, song.Path);
            ButtonsPanel.Children.Add(audioButton);
        }

        private Button InitButton(string name, string path)
        {
            string displayName = ShrinkTextToFit(System.IO.Path.GetFileNameWithoutExtension(name));
            Style customButtonStyle = (Style)Resources["CustomButtonStyle"];

            Button audioButton = new Button
            {
                Content = displayName,
                Tag = path,
                ContextMenu = (ContextMenu)Resources["ButtonContextMenu"],
                Style = customButtonStyle,
                ToolTip = System.IO.Path.GetFileNameWithoutExtension(path)
            };

            // Anpassen der Button-Größe basierend auf der Schriftgröße
            if (displayName.Length > 10)
            {
                audioButton.Height = 50;
                audioButton.Width = 50;
            }

            audioButton.Click += AudioButton_Click;
            return audioButton;
        }

        private string ShrinkTextToFit(string text)
        {
            int maxLength = 10; // Maximal erlaubte Zeichen
            int minFontSize = 8; // Mindestschriftgröße
            int fontSize = 14;

            if (text.Length > maxLength)
            {
                fontSize = minFontSize;
                text = text.Substring(0, maxLength) + "..."; // Schneide den Text ab und füge "..." hinzu
            }

            return text;
        }

        // 5. Aktualisiere den Slider, wenn der Wert geändert wird
        private void ProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_audioFile != null)
            {
                _audioFile.Position = (long)(_audioFile.Length * (e.NewValue / 100));
            }
        }

        private void AudioButton_Click(object sender, RoutedEventArgs e)
        {
            Button clickedButton = sender as Button;
            if (clickedButton != null)
            {
                PlayAudio(clickedButton.Tag.ToString());
                UpdateButtonColors(clickedButton);
            }
        }

        private void UpdateButtonColors(Button activeButton)
        {
            foreach (Button button in ButtonsPanel.Children)
            {
                button.Background = button == activeButton ? new SolidColorBrush(Color.FromRgb(243, 106, 1)) : new SolidColorBrush(Color.FromRgb(63, 63, 63));
            }
        }

        private async void PlayAudio(string fileName)
        {
            if (_outputDevice != null)
            {
                // Fade out the current audio file
                await FadeVolume(0, TimeSpan.FromSeconds(2));

                // Reset the output device
                _outputDevice.Stop();
                _outputDevice.Dispose();
                _outputDevice = null;
            }

            // Update the audio file
            _audioFile?.Dispose();
            _audioFile = new AudioFileReader(fileName);
            _volumeProvider = new VolumeSampleProvider(_audioFile);

            // Initialize and play the new output device
            _outputDevice = new WaveOutEvent();
            _outputDevice.Init(_volumeProvider);
            _volumeProvider.Volume = 0;
            _outputDevice.Play();


            // 6.1. Aktualisiere den Slider
            _outputDevice.PlaybackStopped += (s, e) => { ProgressSlider.Value = 0; };
            var task = Task.Run(async () =>
            {
                while (_outputDevice.PlaybackState == PlaybackState.Playing)
                {
                    await Task.Delay(1000);
                    Dispatcher.Invoke(() => { ProgressSlider.Value = (double)_audioFile.Position / _audioFile.Length * 100; });
                }
            });

            // Fade in the new audio file
            await FadeVolume(1, TimeSpan.FromSeconds(2));
        }

        private async void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_outputDevice != null)
            {
                _isMuted = !_isMuted;
                // Aktualisiere das Lautsprecher-Symbol, wenn der Button geklickt wird
                if ((string)MuteIcon.Tag == "unmuted")
                {
                    MuteIcon.Tag = "muted";
                    MuteIcon.Data = Geometry.Parse("M0,6 L0,18 L4,18 L11,24 L11,0 L4,6 L0,6 z M14,12 L22,19 L20,21 L12,13 L4,21 L6,19 L14,12 z");
                }
                else
                {
                    MuteIcon.Tag = "unmuted";
                    MuteIcon.Data = Geometry.Parse("M3,9 L3,15 L9,15 L15,21 L15,3 L9,9 L3,9 z M19,15 L19,9 L17,9 L17,15 z M23,5 L23,19 L21,19 L21,5 z");
                }
                await FadeVolume(_isMuted ? 0 : 1, TimeSpan.FromSeconds(2));
            }
        }
        private async Task FadeVolume(float targetVolume, TimeSpan duration)
        {
            if (_volumeProvider == null) return;

            float startVolume = _volumeProvider.Volume;
            float volumeChange = targetVolume - startVolume;

            int steps = 20;
            for (int i = 1; i <= steps; i++)
            {
                _volumeProvider.Volume = startVolume + (volumeChange * i / steps);
                await Task.Delay((int) duration.TotalMilliseconds / steps);
            }
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            MenuItem menuItem = sender as MenuItem;
            if (menuItem != null)
            {
                Button audioButton = ((ContextMenu)menuItem.Parent).PlacementTarget as Button;
                if (audioButton != null)
                {
                    var newInfo = AddOrEditSongInfo(new Song(audioButton.Content.ToString(), audioButton.Tag.ToString()));
                    if (newInfo == null)
                        return;
                    // Change the button content
                    audioButton.Content = newInfo.Name;

                    // Change the audio file path
                    audioButton.Tag = newInfo.Path;
                }
            }
        }

        private Song AddOrEditSongInfo(Song original = null)
        {
            var result = original ?? new Song();


            // Change the audio file path
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Audio files (*.mp3;*.wav)|*.mp3;*.wav"
            };
            if (!openFileDialog.ShowDialog() == true)
                return null;
                
            result.Path = openFileDialog.FileName;

            // Change the button content
            string newName = Microsoft.VisualBasic.Interaction.InputBox("Enter the new button name:", "Edit Button Name", result.Name);
            if (string.IsNullOrEmpty(newName))
                return null;
            
            result.Name = newName;
            
            return result;
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            MenuItem menuItem = sender as MenuItem;
            if (menuItem != null)
            {
                Button audioButton = ((ContextMenu)menuItem.Parent).PlacementTarget as Button;
                if (audioButton != null)
                {
                    ButtonsPanel.Children.Remove(audioButton);
                }
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _songList.Songs.Clear();
            foreach (var button in ButtonsPanel.Children.OfType<Button>())
            {
                var song = new Song((string) button.Content, (string) button.Tag);
                _songList.Songs.Add(song);
            }

            File.WriteAllText(c_ConfigPath ,JsonConvert.SerializeObject(_songList));
        }

    }
}