using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DatabaseMigrationTool.Views
{
    public partial class ManageProfilesWindow : Window
    {
        private readonly ConnectionProfileManager _profileManager;
        private readonly ObservableCollection<ConnectionProfile> _filteredProfiles;
        private ConnectionProfile? _selectedProfile;
        
        public ConnectionProfile? SelectedProfile { get; private set; }
        
        public ManageProfilesWindow(ConnectionProfileManager profileManager)
        {
            InitializeComponent();
            _profileManager = profileManager;
            _filteredProfiles = new ObservableCollection<ConnectionProfile>();
            
            ProfilesListView.ItemsSource = _filteredProfiles;
            LoadProfiles();
        }
        
        private void LoadProfiles()
        {
            var profiles = _profileManager.GetProfiles().OrderBy(p => p.Name).ToList();
            
            _filteredProfiles.Clear();
            foreach (var profile in profiles)
            {
                _filteredProfiles.Add(profile);
            }
            
            ApplyFilter();
        }
        
        private void ApplyFilter()
        {
            var filter = FilterTextBox.Text?.Trim().ToLower() ?? "";
            
            if (string.IsNullOrEmpty(filter))
            {
                // Show all profiles
                var allProfiles = _profileManager.GetProfiles().OrderBy(p => p.Name).ToList();
                _filteredProfiles.Clear();
                foreach (var profile in allProfiles)
                {
                    _filteredProfiles.Add(profile);
                }
            }
            else
            {
                // Filter profiles
                var filtered = _profileManager.GetProfiles()
                    .Where(p => p.Name.ToLower().Contains(filter) ||
                               p.Provider.ToLower().Contains(filter) ||
                               p.Server?.ToLower().Contains(filter) == true ||
                               p.Database?.ToLower().Contains(filter) == true)
                    .OrderBy(p => p.Name)
                    .ToList();
                
                _filteredProfiles.Clear();
                foreach (var profile in filtered)
                {
                    _filteredProfiles.Add(profile);
                }
            }
        }
        
        private void UpdateProfileDetails(ConnectionProfile? profile)
        {
            _selectedProfile = profile;
            
            if (profile == null)
            {
                NoSelectionText.Visibility = Visibility.Visible;
                DetailsGrid.Visibility = Visibility.Collapsed;
                
                SelectButton.IsEnabled = false;
                EditButton.IsEnabled = false;
                DuplicateButton.IsEnabled = false;
                DeleteButton.IsEnabled = false;
            }
            else
            {
                NoSelectionText.Visibility = Visibility.Collapsed;
                DetailsGrid.Visibility = Visibility.Visible;
                
                DetailNameText.Text = profile.Name;
                DetailProviderText.Text = profile.Provider;
                DetailServerText.Text = profile.Server;
                DetailDatabaseText.Text = string.IsNullOrEmpty(profile.Database) ? "(not specified)" : profile.Database;
                DetailUsernameText.Text = string.IsNullOrEmpty(profile.Username) ? "(not specified)" : profile.Username;
                DetailCreatedText.Text = profile.CreatedDate.ToString("MM/dd/yyyy HH:mm");
                DetailLastUsedText.Text = profile.LastUsed.ToString("MM/dd/yyyy HH:mm");
                DetailDescriptionText.Text = string.IsNullOrEmpty(profile.Description) ? "(no description)" : profile.Description;
                
                SelectButton.IsEnabled = true;
                EditButton.IsEnabled = true;
                DuplicateButton.IsEnabled = true;
                DeleteButton.IsEnabled = true;
            }
        }
        
        private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }
        
        private void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            FilterTextBox.Text = "";
        }
        
        private void ProfilesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedProfile = ProfilesListView.SelectedItem as ConnectionProfile;
            UpdateProfileDetails(selectedProfile);
        }
        
        private void ProfilesListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_selectedProfile != null)
            {
                SelectProfile_Click(sender, e);
            }
        }
        
        private void SelectProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile != null)
            {
                SelectedProfile = _selectedProfile;
                DialogResult = true;
                Close();
            }
        }
        
        private void EditProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile != null)
            {
                var editWindow = new SaveProfileWindow(_selectedProfile)
                {
                    Owner = this,
                    Title = "Edit Connection Profile"
                };
                
                if (editWindow.ShowDialog() == true && editWindow.Profile != null)
                {
                    _profileManager.SaveProfile(editWindow.Profile);
                    LoadProfiles();
                    
                    // Reselect the edited profile
                    var updatedProfile = _filteredProfiles.FirstOrDefault(p => p.Name == editWindow.Profile.Name);
                    if (updatedProfile != null)
                    {
                        ProfilesListView.SelectedItem = updatedProfile;
                    }
                }
            }
        }
        
        private void DuplicateProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile != null)
            {
                var duplicateProfile = _selectedProfile.Clone();
                duplicateProfile.Name = $"{duplicateProfile.Name} - Copy";
                
                var saveWindow = new SaveProfileWindow(duplicateProfile)
                {
                    Owner = this,
                    Title = "Duplicate Connection Profile"
                };
                
                if (saveWindow.ShowDialog() == true && saveWindow.Profile != null)
                {
                    _profileManager.SaveProfile(saveWindow.Profile);
                    LoadProfiles();
                    
                    // Select the new profile
                    var newProfile = _filteredProfiles.FirstOrDefault(p => p.Name == saveWindow.Profile.Name);
                    if (newProfile != null)
                    {
                        ProfilesListView.SelectedItem = newProfile;
                    }
                }
            }
        }
        
        private void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile != null)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete the profile '{_selectedProfile.Name}'?\n\nThis action cannot be undone.",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    _profileManager.DeleteProfile(_selectedProfile.Name);
                    LoadProfiles();
                    UpdateProfileDetails(null);
                }
            }
        }
        
        private void ImportProfiles_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Import Connection Profiles"
            };
            
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var json = File.ReadAllText(dialog.FileName);
                    var profiles = JsonSerializer.Deserialize<List<ConnectionProfile>>(json);
                    
                    if (profiles != null)
                    {
                        int importedCount = 0;
                        foreach (var profile in profiles)
                        {
                            try
                            {
                                _profileManager.SaveProfile(profile);
                                importedCount++;
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"Failed to import profile '{profile.Name}': {ex.Message}",
                                    "Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                        
                        LoadProfiles();
                        MessageBox.Show($"Successfully imported {importedCount} profiles.",
                            "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to import profiles: {ex.Message}",
                        "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        private void ExportProfiles_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = "json",
                FileName = "connection_profiles.json",
                Title = "Export Connection Profiles"
            };
            
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var profiles = _profileManager.GetProfiles().ToList();
                    
                    // Clear passwords for export (security)
                    var exportProfiles = profiles.Select(p =>
                    {
                        var exportProfile = p.Clone();
                        exportProfile.Password = ""; // Don't export passwords
                        return exportProfile;
                    }).ToList();
                    
                    var json = JsonSerializer.Serialize(exportProfiles, new JsonSerializerOptions 
                    { 
                        WriteIndented = true 
                    });
                    
                    File.WriteAllText(dialog.FileName, json);
                    
                    MessageBox.Show($"Successfully exported {profiles.Count} profiles.\n\nNote: Passwords are not included in exports for security.",
                        "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export profiles: {ex.Message}",
                        "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}