using DatabaseMigrationTool.Models;
using System.Windows;

namespace DatabaseMigrationTool
{
    public partial class SaveProfileWindow : Window
    {
        public ConnectionProfile? Profile { get; private set; }
        private readonly ConnectionProfile _sourceProfile;
        
        public SaveProfileWindow(ConnectionProfile sourceProfile)
        {
            InitializeComponent();
            _sourceProfile = sourceProfile;
            
            // Pre-populate fields
            ProfileNameTextBox.Text = GenerateDefaultName();
            UpdateConnectionPreview();
            
            // Focus on name field
            ProfileNameTextBox.Focus();
            ProfileNameTextBox.SelectAll();
        }
        
        private string GenerateDefaultName()
        {
            var provider = _sourceProfile.Provider;
            var server = !string.IsNullOrEmpty(_sourceProfile.Server) ? _sourceProfile.Server : "Local";
            var database = !string.IsNullOrEmpty(_sourceProfile.Database) ? _sourceProfile.Database : "";
            
            if (!string.IsNullOrEmpty(database))
            {
                return $"{provider} - {server} - {database}";
            }
            else
            {
                return $"{provider} - {server}";
            }
        }
        
        private void UpdateConnectionPreview()
        {
            try
            {
                var preview = $"Provider: {_sourceProfile.Provider}\n";
                preview += $"Server: {_sourceProfile.Server}\n";
                
                if (!string.IsNullOrEmpty(_sourceProfile.Database))
                    preview += $"Database: {_sourceProfile.Database}\n";
                    
                if (!string.IsNullOrEmpty(_sourceProfile.Username))
                    preview += $"Username: {_sourceProfile.Username}\n";
                    
                if (_sourceProfile.Port > 0)
                    preview += $"Port: {_sourceProfile.Port}\n";
                    
                if (_sourceProfile.UseWindowsAuth)
                    preview += "Authentication: Windows\n";
                    
                if (_sourceProfile.UseSsl)
                    preview += "SSL: Enabled\n";
                    
                if (_sourceProfile.ReadOnlyConnection)
                    preview += "Read-Only: Yes\n";
                    
                if (!string.IsNullOrEmpty(_sourceProfile.FirebirdVersion))
                    preview += $"Firebird Version: {_sourceProfile.FirebirdVersion}\n";
                    
                ConnectionPreviewTextBlock.Text = preview;
            }
            catch (Exception ex)
            {
                ConnectionPreviewTextBlock.Text = $"Preview error: {ex.Message}";
            }
        }
        
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var profileName = ProfileNameTextBox.Text?.Trim();
            
            if (string.IsNullOrEmpty(profileName))
            {
                MessageBox.Show("Please enter a profile name.", "Validation Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ProfileNameTextBox.Focus();
                return;
            }
            
            // Create the profile
            Profile = _sourceProfile.Clone();
            Profile.Name = profileName;
            Profile.Description = DescriptionTextBox.Text?.Trim() ?? "";
            
            // Clear password if not saving it
            if (!SavePasswordCheckBox.IsChecked == true)
            {
                Profile.Password = "";
            }
            
            DialogResult = true;
            Close();
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}