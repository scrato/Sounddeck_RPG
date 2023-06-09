﻿using System;
using System.Drawing;
using System.IO;
using Color = System.Windows.Media.Color;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.WaveFormRenderer;
using Newtonsoft.Json;
using System.Threading;
using System.Windows.Input;
using MaterialDesignColors.Recommended;
using RPGDeck.WPF;
using Point = System.Windows.Point;

namespace RPG_Deck
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private WaveOutEvent _outputDevice;
        private AudioFileReader _audioFile;
        private VolumeSampleProvider _volumeProvider;
        private readonly TimeSpan _progressUpdateInterval = TimeSpan.FromMilliseconds(100);
        private CancellationTokenSource _progressCancellationTokenSource;
        private bool _isMuted;
        private bool _isRepeating;
        private string _currentPlaying;

        private SongList _songList = new SongList();
        private const string c_ConfigPath = "config.json";

        private System.Windows.Media.Brush _standardButtonBGColor;
        double fadeInterval = 2;

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
            UpdateButtonColors(null);
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            AddNewSong();
        }

        private void AddNewSong()
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
            Style customButtonStyle = (Style)FindResource("MaterialDesignFloatingActionButton");
            Button audioButton = new Button
            {
                Content = displayName,
                Tag = path,
                ContextMenu = (ContextMenu)Resources["ButtonContextMenu"],
                Style = customButtonStyle,
                ToolTip = System.IO.Path.GetFileNameWithoutExtension(path),
                Height = 60,
                Width = 60,
                Margin = new Thickness(5)
            };

            audioButton.AllowDrop = true;
            audioButton.MouseMove += AudioButton_MouseMove;
            audioButton.MouseLeave += AudioButton_MouseLeave;
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

        private void AudioButton_Click(object sender, RoutedEventArgs e)
        {
            Button clickedButton = sender as Button;
            if (clickedButton != null)
            {
                SetRepeat(false);
                PlayAudio(clickedButton.Tag.ToString());
                UpdateButtonColors(clickedButton);
                RenderWaveform(clickedButton.Tag.ToString());
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
                await FadeVolume(0, TimeSpan.FromSeconds(fadeInterval));

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
            _outputDevice.PlaybackStopped -= OnPlaybackStopped;
            _outputDevice.PlaybackStopped += OnPlaybackStopped;
            _outputDevice.Init(_volumeProvider);
            _volumeProvider.Volume = 0;

            _outputDevice.Play();
            

            // Fade in the new audio file
            await FadeVolume(1, TimeSpan.FromSeconds(fadeInterval));

            // Start updating the progress bar
            await UpdateProgress();
            _currentPlaying = fileName;
        }

        private void FadeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            fadeInterval = e.NewValue;
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
                    MuteIcon.Kind = PackIconKind.VolumeOff;
                }
                else
                {
                    MuteIcon.Tag = "unmuted";
                    MuteIcon.Kind = PackIconKind.VolumeHigh;
                }
                await FadeVolume(_isMuted ? 0 : 1, TimeSpan.FromSeconds(fadeInterval));
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
                FileName = result.Path,
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

        private void RenderWaveform(string audioFilePath)
        {
            if (File.Exists(audioFilePath))
            {
                using (var reader = new AudioFileReader(audioFilePath))
                {
                    var settings = new StandardWaveFormRendererSettings
                    {
                        Width = (int)Math.Max(WaveFormImage.ActualWidth, 780),
                        TopHeight = (int)Math.Max(WaveFormImage.ActualHeight, 100) / 2,
                        BottomHeight = (int)Math.Max(WaveFormImage.ActualHeight, 100) / 2,
                        TopPeakPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 243, 106, 1)),
                        BottomPeakPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(255,166,72,0)),
                        BackgroundColor = System.Drawing.Color.FromArgb(0, 0, 0, 0)
                    };

                    WaveFormRenderer renderer = new WaveFormRenderer();
                    var waveformImage = renderer.Render(reader, settings);
                    WaveFormImage.Source = ConvertBitmapToImageSource(waveformImage as System.Drawing.Bitmap);
                }
            }
        }

        private ImageSource ConvertBitmapToImageSource(Bitmap bitmap)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                memory.Position = 0;
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                return bitmapImage;
            }
        }

        private async Task UpdateProgress()
        {
            _progressCancellationTokenSource?.Cancel();
            _progressCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _progressCancellationTokenSource.Token;

            while (_outputDevice != null && _outputDevice.PlaybackState.HasFlag(PlaybackState.Playing) && !cancellationToken.IsCancellationRequested)
            {
                // Calculate progress
                double progress = _audioFile.Position / (double)_audioFile.Length;

                // Update progress bar on UI thread
                await Dispatcher.InvokeAsync(() => ProgressRectangle.SetValue(Canvas.LeftProperty, progress * WaveFormImage.ActualWidth));

                // Wait for the interval
                await Task.Delay(_progressUpdateInterval);
            }
        }



        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            ProgressRectangle.SetValue(Canvas.LeftProperty, 0.0);
            if(_isRepeating && _currentPlaying != null) { PlayAudio(_currentPlaying); }
        }

        private async void WaveFormImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_audioFile != null && _outputDevice != null)
            {
                double mouseX = e.GetPosition(WaveFormImage).X;
                double clickedRatio = mouseX / WaveFormImage.ActualWidth;
                long newPosition = (long)(_audioFile.Length * clickedRatio);


                SetRepeat(false);

                // Fade out the current audio file
                await FadeVolume(0, TimeSpan.FromSeconds(Math.Max(fadeInterval, 1) / 2));

                // Change the position of the audio file
                _audioFile.Position = newPosition;

                // Fade in the audio file
                await FadeVolume(1, TimeSpan.FromSeconds(Math.Max(fadeInterval, 1) / 2));

                // Restart progress update
                await UpdateProgress();
            }
        }



        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_outputDevice != null)
            {
                SetRepeat(false);
                _isMuted = !_isMuted;
                // Aktualisiere das Lautsprecher-Symbol, wenn der Button geklickt wird
                if ((string)MuteIcon.Tag == "unmuted")
                {
                    MuteIcon.Tag = "muted";
                    MuteIcon.Kind = PackIconKind.VolumeOff;
                }
                else
                {
                    MuteIcon.Tag = "unmuted";
                    MuteIcon.Kind = PackIconKind.VolumeHigh;
                }
                await FadeVolume(0 , TimeSpan.FromSeconds(2));
                _outputDevice.Stop();
                UpdateButtonColors(null);
            }
        }

        private void RepeatButton_Click(object sender, RoutedEventArgs e)
        {
            SetRepeat(!_isRepeating);
        }


        private void SetRepeat(bool value)
        {
            if (_outputDevice != null)
                {
                _isRepeating = value;
                // Aktualisiere das Lautsprecher-Symbol, wenn der Button geklickt wird
                if (!value)
                {
                    RepeatIcon.Tag = "repeatOff";
                    RepeatIcon.Kind = PackIconKind.RepeatOff;
                }
                else
                {
                    RepeatIcon.Tag = "repeating";
                    RepeatIcon.Kind = PackIconKind.Repeat;
                }
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_songList.Songs.Count == 0)
                AddNewSong();
        }

        // Handle the MouseMove event for each Button.
        private void AudioButton_MouseMove(object sender, MouseEventArgs e)
        {
            Cursor = Cursors.Hand;
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Button button = (Button)sender;
                DragDrop.DoDragDrop(button, button, DragDropEffects.Move);
            }
        }

        private void AudioButton_MouseLeave(object sender, MouseEventArgs e)
        {
            Cursor = Cursors.Arrow;
        }


        // Handle the DragEnter event for the ButtonsPanel.
        private void ButtonsPanel_DragEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(Button)) || sender == e.Source)
            {
                e.Effects = DragDropEffects.None;
            }
        }

        // Handle the Drop event for the ButtonsPanel.
        private void ButtonsPanel_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(Button)))
            {
                Button button = e.Data.GetData(typeof(Button)) as Button;
                ButtonsPanel.Children.Remove(button);
                Point dropPosition = e.GetPosition(ButtonsPanel);
                int index = CalculateDropIndex(dropPosition);
                ButtonsPanel.Children.Insert(index, button);
            }
        }

        // This method calculates the index where the button should be dropped.
        private int CalculateDropIndex(Point dropPosition)
        {
            for (int i = 0; i < ButtonsPanel.Children.Count; i++)
            {
                Rect childBounds = VisualTreeHelper.GetDescendantBounds((Visual)ButtonsPanel.Children[i]);
                if (dropPosition.X < childBounds.Width)
                {
                    return i;
                }
                dropPosition.X -= childBounds.Width;
            }
            return ButtonsPanel.Children.Count;
        }

   
    }
}