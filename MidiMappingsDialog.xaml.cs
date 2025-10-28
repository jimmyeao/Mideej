using Mideej.Models;
using System.Collections.ObjectModel;
using System.Windows;

namespace Mideej;

public partial class MidiMappingsDialog : Window
{
    public ObservableCollection<MidiMappingViewModel> Mappings { get; set; }

    public MidiMappingsDialog(List<MidiMapping> mappings, List<string> channelNames)
    {
        InitializeComponent();

        Mappings = new ObservableCollection<MidiMappingViewModel>(
            mappings.Select(m => new MidiMappingViewModel(m, channelNames))
        );

        MappingsDataGrid.ItemsSource = Mappings;
    }

    private void DeleteMapping_Click(object sender, RoutedEventArgs e)
    {
        if (MappingsDataGrid.SelectedItem is MidiMappingViewModel mapping)
        {
            var result = MessageBox.Show(
                $"Delete mapping: MIDI CH{mapping.Channel + 1} CC{mapping.ControlNumber} → {mapping.TargetDescription}?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Mappings.Remove(mapping);
            }
        }
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Delete ALL MIDI mappings?",
            "Confirm Clear All",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            Mappings.Clear();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}

public class MidiMappingViewModel
{
    public MidiMapping Original { get; }
    public int Channel => Original.Channel;
    public int ControlNumber => Original.ControlNumber;
    public MidiControlType ControlType => Original.ControlType;
    public string TargetDescription { get; }

    public MidiMappingViewModel(MidiMapping mapping, List<string> channelNames)
    {
        Original = mapping;

        if (mapping.TargetChannelIndex == -1)
        {
            TargetDescription = "Global Transport";
        }
        else if (mapping.TargetChannelIndex >= 0 && mapping.TargetChannelIndex < channelNames.Count)
        {
            TargetDescription = channelNames[mapping.TargetChannelIndex];
        }
        else
        {
            TargetDescription = $"Channel {mapping.TargetChannelIndex + 1}";
        }
    }
}
