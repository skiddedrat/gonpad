using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.Dsp;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;

namespace GoonPad.Soundboard
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<SoundPad> _soundPads = new();
        private WaveOutEvent? _outputDevice;
        private List<WaveOutEvent> _activePlayers = new();
        private int _maxConcurrent = 50;
        private bool _layeredMode = true;
        private float _bassGain = 0f;
        private float _trebleGain = 0f;

        public MainWindow()
        {
            InitializeComponent();
            InitializeDevices();
            PadsGrid.ItemsSource = _soundPads;
            UpdateActiveVoicesDisplay();
            
            // Keyboard shortcut handling
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
        }

        private void InitializeDevices()
        {
            // Populate output devices
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                var caps = WaveOut.GetCapabilities(i);
                OutputDeviceCombo.Items.Add(new ComboBoxItem { Content = caps.ProductName, Tag = i });
            }
            if (OutputDeviceCombo.Items.Count > 0)
                OutputDeviceCombo.SelectedIndex = 0;

            // Populate mic/input devices
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var caps = WaveIn.GetCapabilities(i);
                MicDeviceCombo.Items.Add(new ComboBoxItem { Content = caps.ProductName, Tag = i });
            }
            if (MicDeviceCombo.Items.Count > 0)
                MicDeviceCombo.SelectedIndex = 0;

            OutputDeviceCombo.SelectionChanged += (s, e) => ReinitializeOutput();
        }

        private void ReinitializeOutput()
        {
            StopAllPlayback();
            _outputDevice?.Dispose();
            _outputDevice = null;
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Audio Files|*.mp3;*.wav;*.ogg;*.flac;*.aac;*.m4a;*.wma|All Files|*.*",
                Multiselect = true,
                Title = "Import Sound Files"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (var file in openFileDialog.FileNames)
                {
                    try
                    {
                        var pad = new SoundPad
                        {
                            Name = Path.GetFileNameWithoutExtension(file),
                            FilePath = file,
                            BassGain = _bassGain,
                            TrebleGain = _trebleGain
                        };
                        
                        // Auto-assign keyboard shortcuts F1-F12, then 1-9
                        var existingShortcuts = _soundPads.Where(p => !string.IsNullOrEmpty(p.Shortcut)).Select(p => p.Shortcut).ToList();
                        foreach (var key in GetDefaultShortcuts().Except(existingShortcuts))
                        {
                            pad.Shortcut = key;
                            break;
                        }

                        _soundPads.Add(pad);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to load {file}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private List<string> GetDefaultShortcuts()
        {
            var shortcuts = new List<string>();
            for (int i = 1; i <= 12; i++) shortcuts.Add($"F{i}");
            for (int i = 1; i <= 9; i++) shortcuts.Add($"{i}");
            return shortcuts;
        }

        private void Pad_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is Button button && button.Tag is SoundPad pad)
            {
                var menu = new ContextMenu();
                
                var removeItem = new MenuItem { Header = "Remove Pad" };
                removeItem.Click += (s, args) => _soundPads.Remove(pad);
                menu.Items.Add(removeItem);

                var renameItem = new MenuItem { Header = "Rename" };
                renameItem.Click += (s, args) =>
                {
                    var dialog = new RenameDialog(pad.Name);
                    if (dialog.ShowDialog() == true)
                    {
                        pad.Name = dialog.NewName;
                    }
                };
                menu.Items.Add(renameItem);

                var bindKeyItem = new MenuItem { Header = "Bind Key (Press any key)" };
                bindKeyItem.Click += async (s, args) =>
                {
                    var keyDialog = new KeyBindDialog();
                    if (keyDialog.ShowDialog() == true)
                    {
                        pad.Shortcut = keyDialog.BoundKey;
                    }
                };
                menu.Items.Add(bindKeyItem);

                menu.IsOpen = true;
            }
        }

        private void PlaySound(SoundPad pad)
        {
            if (!File.Exists(pad.FilePath))
            {
                MessageBox.Show($"Sound file not found: {pad.FilePath}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Check concurrent limit unless layered mode allows stacking
            if (!_layeredMode && _activePlayers.Count >= _maxConcurrent)
                return;

            try
            {
                var audioFile = new AudioFileReader(pad.FilePath);
                
                // Apply EQ filters using ISampleProvider chain
                var eqProvider = audioFile.ToSampleProvider();
                if (_bassGain != 0f)
                    eqProvider = eqProvider.ApplyBassBoost(_bassGain, 200);
                if (_trebleGain != 0f)
                    eqProvider = eqProvider.ApplyTrebleBoost(_trebleGain, 3000);
                
                var player = new WaveOutEvent();
                
                if (OutputDeviceCombo.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is int deviceNum)
                {
                    player.DeviceNumber = deviceNum;
                }

                player.Init(eqProvider);
                player.PlaybackStopped += (s, e) =>
                {
                    audioFile.Dispose();
                    player.Dispose();
                    lock (_activePlayers)
                    {
                        _activePlayers.Remove(player);
                    }
                    Dispatcher.Invoke(() => UpdateActiveVoicesDisplay());
                };

                lock (_activePlayers)
                {
                    _activePlayers.Add(player);
                }

                player.Play();
                UpdateActiveVoicesDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Playback error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopAllButton_Click(object sender, RoutedEventArgs e)
        {
            StopAllPlayback();
        }

        private void StopAllPlayback()
        {
            lock (_activePlayers)
            {
                foreach (var player in _activePlayers)
                {
                    try
                    {
                        player.Stop();
                        player.Dispose();
                    }
                    catch { }
                }
                _activePlayers.Clear();
            }
            UpdateActiveVoicesDisplay();
        }

        private void UpdateActiveVoicesDisplay()
        {
            ActiveVoicesText.Text = $"{_activePlayers.Count} / {_maxConcurrent}";
        }

        private void BassSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (BassValueText != null)
            {
                _bassGain = (float)e.NewValue;
                BassValueText.Text = $"{_bassGain:F1} dB";
            }
        }

        private void TrebleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TrebleValueText != null)
            {
                _trebleGain = (float)e.NewValue;
                TrebleValueText.Text = $"{_trebleGain:F1} dB";
            }
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var keyStr = e.Key.ToString();
            var pad = _soundPads.FirstOrDefault(p => p.Shortcut == keyStr);
            if (pad != null)
            {
                PlaySound(pad);
                e.Handled = true;
            }
        }

        private void SaveProfileButton_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "JSON Profile|*.json",
                Title = "Save Soundboard Profile"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    var profile = new SoundboardProfile
                    {
                        SoundPads = _soundPads.Select(p => new SavedSoundPad
                        {
                            Name = p.Name,
                            FilePath = p.FilePath,
                            Shortcut = p.Shortcut,
                            BassGain = p.BassGain,
                            TrebleGain = p.TrebleGain
                        }).ToList(),
                        OutputDeviceIndex = OutputDeviceCombo.SelectedIndex,
                        MicDeviceIndex = MicDeviceCombo.SelectedIndex,
                        MaxConcurrent = _maxConcurrent,
                        LayeredMode = _layeredMode,
                        BassGain = _bassGain,
                        TrebleGain = _trebleGain
                    };

                    var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(saveDialog.FileName, json);
                    MessageBox.Show("Profile saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save profile: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LoadProfileButton_Click(object sender, RoutedEventArgs e)
        {
            var openDialog = new OpenFileDialog
            {
                Filter = "JSON Profile|*.json",
                Title = "Load Soundboard Profile"
            };

            if (openDialog.ShowDialog() == true)
            {
                try
                {
                    var json = File.ReadAllText(openDialog.FileName);
                    var profile = JsonSerializer.Deserialize<SoundboardProfile>(json);

                    if (profile != null)
                    {
                        StopAllPlayback();
                        _soundPads.Clear();

                        foreach (var savedPad in profile.SoundPads)
                        {
                            _soundPads.Add(new SoundPad
                            {
                                Name = savedPad.Name,
                                FilePath = savedPad.FilePath,
                                Shortcut = savedPad.Shortcut,
                                BassGain = savedPad.BassGain,
                                TrebleGain = savedPad.TrebleGain
                            });
                        }

                        if (profile.OutputDeviceIndex >= 0 && profile.OutputDeviceIndex < OutputDeviceCombo.Items.Count)
                            OutputDeviceCombo.SelectedIndex = profile.OutputDeviceIndex;

                        if (profile.MicDeviceIndex >= 0 && profile.MicDeviceIndex < MicDeviceCombo.Items.Count)
                            MicDeviceCombo.SelectedIndex = profile.MicDeviceIndex;

                        _maxConcurrent = profile.MaxConcurrent;
                        _layeredMode = profile.LayeredMode;
                        _bassGain = profile.BassGain;
                        _trebleGain = profile.TrebleGain;

                        BassSlider.Value = _bassGain;
                        TrebleSlider.Value = _trebleGain;

                        UpdateActiveVoicesDisplay();
                        MessageBox.Show("Profile loaded successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load profile: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    public class SoundPad : INotifyPropertyChanged
    {
        private string _name = "";
        private string? _shortcut;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        public string FilePath { get; set; } = "";
        
        public string? Shortcut
        {
            get => _shortcut;
            set { _shortcut = value; OnPropertyChanged(nameof(Shortcut)); }
        }

        public float BassGain { get; set; }
        public float TrebleGain { get; set; }

        public ICommand PlayCommand => new RelayCommand(_ => ((MainWindow)Application.Current.MainWindow)?.GetType().GetMethod("PlaySound", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(Application.Current.MainWindow, new object[] { this }));

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);
        public event EventHandler? CanExecuteChanged;
    }

    public class SoundboardProfile
    {
        public List<SavedSoundPad> SoundPads { get; set; } = new();
        public int OutputDeviceIndex { get; set; }
        public int MicDeviceIndex { get; set; }
        public int MaxConcurrent { get; set; } = 50;
        public bool LayeredMode { get; set; } = true;
        public float BassGain { get; set; }
        public float TrebleGain { get; set; }
    }

    public class SavedSoundPad
    {
        public string Name { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string? Shortcut { get; set; }
        public float BassGain { get; set; }
        public float TrebleGain { get; set; }
    }

    public class RenameDialog : Window
    {
        private TextBox _textBox;
        private Button _okButton;
        private Button _cancelButton;

        public string NewName { get; private set; } = "";

        public RenameDialog(string currentName)
        {
            Title = "Rename Pad";
            Width = 300;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = Application.Current.MainWindow;
            Background = Brushes.Transparent;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _textBox = new TextBox
            {
                Text = currentName,
                Margin = new Thickness(10),
                FontSize = 14,
                Background = Brushes.White,
                Foreground = Brushes.Black
            };
            Grid.SetRow(_textBox, 0);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10)
            };

            _okButton = new Button
            {
                Content = "OK",
                Width = 60,
                Margin = new Thickness(5)
            };
            _okButton.Click += (s, e) => { NewName = _textBox.Text; DialogResult = true; Close(); };

            _cancelButton = new Button
            {
                Content = "Cancel",
                Width = 60,
                Margin = new Thickness(5)
            };
            _cancelButton.Click += (s, e) => DialogResult = false;

            buttonPanel.Children.Add(_okButton);
            buttonPanel.Children.Add(_cancelButton);
            Grid.SetRow(buttonPanel, 1);

            grid.Children.Add(_textBox);
            grid.Children.Add(buttonPanel);

            Content = grid;
        }
    }

    public class KeyBindDialog : Window
    {
        private TextBlock _instruction;
        public string BoundKey { get; private set; } = "";

        public KeyBindDialog()
        {
            Title = "Bind Key";
            Width = 300;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = Application.Current.MainWindow;
            Background = Brushes.Transparent;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _instruction = new TextBlock
            {
                Text = "Press any key...",
                FontSize = 18,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(_instruction, 0);

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 80,
                Margin = new Thickness(10),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            cancelButton.Click += (s, e) => DialogResult = false;
            Grid.SetRow(cancelButton, 1);

            grid.Children.Add(_instruction);
            grid.Children.Add(cancelButton);

            Content = grid;
            PreviewKeyDown += KeyBindDialog_PreviewKeyDown;
        }

        private void KeyBindDialog_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            BoundKey = e.Key.ToString();
            _instruction.Text = $"Bound to: {BoundKey}";
            DialogResult = true;
            e.Handled = true;
        }
    }

    // Extension methods for EQ
    public static class AudioExtensions
    {
        public static ISampleProvider ApplyBassBoost(this ISampleProvider source, float gainDb, float frequency = 200f)
        {
            return new BiQuadFilterSource(source, CreateLowShelfFilter(source.WaveFormat, frequency, gainDb));
        }

        public static ISampleProvider ApplyTrebleBoost(this ISampleProvider source, float gainDb, float frequency = 3000f)
        {
            return new BiQuadFilterSource(source, CreateHighShelfFilter(source.WaveFormat, frequency, gainDb));
        }

        private static BiQuadFilter CreateLowShelfFilter(WaveFormat format, float frequency, float gainDb)
        {
            var sampleRate = format.SampleRate;
            var Q = 0.707f;
            var A = (float)Math.Pow(10, gainDb / 40);
            var w0 = 2 * Math.PI * frequency / sampleRate;
            var cosw0 = Math.Cos(w0);
            var sinw0 = Math.Sin(w0);
            var alpha = sinw0 / (2 * Q);

            var a0 = (A + 1) - (A - 1) * cosw0 + 2 * alpha * A;
            var a1 = 2 * ((A - 1) - (A + 1) * cosw0);
            var a2 = (A + 1) - (A - 1) * cosw0 - 2 * alpha * A;
            var b1 = -2 * ((A + 1) + (A - 1) * cosw0);
            var b2 = (A + 1) + (A - 1) * cosw0 - 2 * alpha * A;

            return new BiQuadFilter((float)(a1 / a0), (float)(a2 / a0), (float)(b1 / a0), (float)(b2 / a0), 1);
        }

        private static BiQuadFilter CreateHighShelfFilter(WaveFormat format, float frequency, float gainDb)
        {
            var sampleRate = format.SampleRate;
            var Q = 0.707f;
            var A = (float)Math.Pow(10, gainDb / 40);
            var w0 = 2 * Math.PI * frequency / sampleRate;
            var cosw0 = Math.Cos(w0);
            var sinw0 = Math.Sin(w0);
            var alpha = sinw0 / (2 * Q);

            var a0 = (A + 1) + (A - 1) * cosw0 + 2 * alpha * A;
            var a1 = -2 * ((A - 1) + (A + 1) * cosw0);
            var a2 = (A + 1) + (A - 1) * cosw0 - 2 * alpha * A;
            var b1 = 2 * ((A + 1) - (A - 1) * cosw0);
            var b2 = (A + 1) - (A - 1) * cosw0 - 2 * alpha * A;

            return new BiQuadFilter((float)(a1 / a0), (float)(a2 / a0), (float)(b1 / a0), (float)(b2 / a0), 1);
        }
    }

    public class BiQuadFilterSource : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly BiQuadFilter _filter;
        private readonly float[] _workBuffer;

        public BiQuadFilterSource(ISampleProvider source, BiQuadFilter filter)
        {
            _source = source;
            _filter = filter;
            _workBuffer = new float[source.WaveFormat.Channels];
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            for (int i = 0; i < samplesRead; i += WaveFormat.Channels)
            {
                for (int ch = 0; ch < WaveFormat.Channels; ch++)
                {
                    buffer[offset + i + ch] = _filter.Transform(buffer[offset + i + ch]);
                }
            }
            return samplesRead;
        }
    }
}
