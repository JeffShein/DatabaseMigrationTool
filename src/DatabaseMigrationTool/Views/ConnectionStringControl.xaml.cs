using DatabaseMigrationTool.Helpers;
using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Services;
using Microsoft.Win32;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace DatabaseMigrationTool.Views
{
    public partial class ConnectionStringControl : UserControl
    {
        #region Fields

        private ConnectionProfileManager _profileManager;
        private bool _isLoadingProfile = false;
        private Action<ConnectionProfile?, string>? _sharedProfileChangedCallback;
        private string _tabIdentifier = "";

        #endregion

        #region Dependency Properties

        public static readonly DependencyProperty ConnectionStringProperty =
            DependencyProperty.Register("ConnectionString", typeof(string), typeof(ConnectionStringControl),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnConnectionStringChanged));

        public static readonly DependencyProperty ProviderIndexProperty =
            DependencyProperty.Register("ProviderIndex", typeof(int), typeof(ConnectionStringControl),
                new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnProviderIndexChanged));

        // SQL Server properties
        public static readonly DependencyProperty SqlServerServerProperty =
            DependencyProperty.Register("SqlServerServer", typeof(string), typeof(ConnectionStringControl), 
                new FrameworkPropertyMetadata("LocalHost", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty SqlServerDatabaseProperty =
            DependencyProperty.Register("SqlServerDatabase", typeof(string), typeof(ConnectionStringControl),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty SqlServerUsernameProperty =
            DependencyProperty.Register("SqlServerUsername", typeof(string), typeof(ConnectionStringControl),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty SqlServerAdditionalParamsProperty =
            DependencyProperty.Register("SqlServerAdditionalParams", typeof(string), typeof(ConnectionStringControl),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        // MySQL properties
        public static readonly DependencyProperty MySqlServerProperty =
            DependencyProperty.Register("MySqlServer", typeof(string), typeof(ConnectionStringControl),
                new FrameworkPropertyMetadata("LocalHost", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty MySqlPortProperty =
            DependencyProperty.Register("MySqlPort", typeof(string), typeof(ConnectionStringControl),
                new FrameworkPropertyMetadata("3306", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty MySqlDatabaseProperty =
            DependencyProperty.Register("MySqlDatabase", typeof(string), typeof(ConnectionStringControl),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty MySqlUsernameProperty =
            DependencyProperty.Register("MySqlUsername", typeof(string), typeof(ConnectionStringControl),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty MySqlAdditionalParamsProperty =
            DependencyProperty.Register("MySqlAdditionalParams", typeof(string), typeof(ConnectionStringControl),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        // PostgreSQL properties
        public static readonly DependencyProperty PostgreSqlHostProperty =
            DependencyProperty.Register("PostgreSqlHost", typeof(string), typeof(ConnectionStringControl),
                new FrameworkPropertyMetadata("LocalHost", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty PostgreSqlPortProperty =
            DependencyProperty.Register("PostgreSqlPort", typeof(string), typeof(ConnectionStringControl),
                new FrameworkPropertyMetadata("5432", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty PostgreSqlDatabaseProperty =
            DependencyProperty.Register("PostgreSqlDatabase", typeof(string), typeof(ConnectionStringControl),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty PostgreSqlUsernameProperty =
            DependencyProperty.Register("PostgreSqlUsername", typeof(string), typeof(ConnectionStringControl),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty PostgreSqlAdditionalParamsProperty =
            DependencyProperty.Register("PostgreSqlAdditionalParams", typeof(string), typeof(ConnectionStringControl),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        // Firebird properties
        public static readonly DependencyProperty FirebirdDatabaseFileProperty =
            DependencyProperty.Register("FirebirdDatabaseFile", typeof(string), typeof(ConnectionStringControl),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty FirebirdUsernameProperty =
            DependencyProperty.Register("FirebirdUsername", typeof(string), typeof(ConnectionStringControl),
                new FrameworkPropertyMetadata("SYSDB", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty FirebirdAdditionalOptionsProperty =
            DependencyProperty.Register("FirebirdAdditionalOptions", typeof(string), typeof(ConnectionStringControl),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        #endregion

        #region Properties

        public string ConnectionString
        {
            get { return (string)GetValue(ConnectionStringProperty); }
            set { SetValue(ConnectionStringProperty, value); }
        }

        public int ProviderIndex
        {
            get { return (int)GetValue(ProviderIndexProperty); }
            set { SetValue(ProviderIndexProperty, value); }
        }

        // SQL Server properties
        public string SqlServerServer
        {
            get { return (string)GetValue(SqlServerServerProperty); }
            set { SetValue(SqlServerServerProperty, value); }
        }

        public string SqlServerDatabase
        {
            get { return (string)GetValue(SqlServerDatabaseProperty); }
            set { SetValue(SqlServerDatabaseProperty, value); }
        }

        public string SqlServerUsername
        {
            get { return (string)GetValue(SqlServerUsernameProperty); }
            set { SetValue(SqlServerUsernameProperty, value); }
        }

        public string SqlServerAdditionalParams
        {
            get { return (string)GetValue(SqlServerAdditionalParamsProperty); }
            set { SetValue(SqlServerAdditionalParamsProperty, value); }
        }

        // MySQL properties
        public string MySqlServer
        {
            get { return (string)GetValue(MySqlServerProperty); }
            set { SetValue(MySqlServerProperty, value); }
        }

        public string MySqlPort
        {
            get { return (string)GetValue(MySqlPortProperty); }
            set { SetValue(MySqlPortProperty, value); }
        }

        public string MySqlDatabase
        {
            get { return (string)GetValue(MySqlDatabaseProperty); }
            set { SetValue(MySqlDatabaseProperty, value); }
        }

        public string MySqlUsername
        {
            get { return (string)GetValue(MySqlUsernameProperty); }
            set { SetValue(MySqlUsernameProperty, value); }
        }

        public string MySqlAdditionalParams
        {
            get { return (string)GetValue(MySqlAdditionalParamsProperty); }
            set { SetValue(MySqlAdditionalParamsProperty, value); }
        }

        // PostgreSQL properties
        public string PostgreSqlHost
        {
            get { return (string)GetValue(PostgreSqlHostProperty); }
            set { SetValue(PostgreSqlHostProperty, value); }
        }

        public string PostgreSqlPort
        {
            get { return (string)GetValue(PostgreSqlPortProperty); }
            set { SetValue(PostgreSqlPortProperty, value); }
        }

        public string PostgreSqlDatabase
        {
            get { return (string)GetValue(PostgreSqlDatabaseProperty); }
            set { SetValue(PostgreSqlDatabaseProperty, value); }
        }

        public string PostgreSqlUsername
        {
            get { return (string)GetValue(PostgreSqlUsernameProperty); }
            set { SetValue(PostgreSqlUsernameProperty, value); }
        }

        public string PostgreSqlAdditionalParams
        {
            get { return (string)GetValue(PostgreSqlAdditionalParamsProperty); }
            set { SetValue(PostgreSqlAdditionalParamsProperty, value); }
        }

        // Firebird properties
        public string FirebirdDatabaseFile
        {
            get { return (string)GetValue(FirebirdDatabaseFileProperty); }
            set { SetValue(FirebirdDatabaseFileProperty, value); }
        }

        public string FirebirdUsername
        {
            get { return (string)GetValue(FirebirdUsernameProperty); }
            set { SetValue(FirebirdUsernameProperty, value); }
        }

        public string FirebirdAdditionalOptions
        {
            get { return (string)GetValue(FirebirdAdditionalOptionsProperty); }
            set { SetValue(FirebirdAdditionalOptionsProperty, value); }
        }

        #endregion

        // Store passwords separately since they can't be bound directly
        private string _sqlServerPassword = string.Empty;
        private string _mySqlPassword = string.Empty;
        private string _postgreSqlPassword = string.Empty;
        private string _firebirdPassword = "Hosis11223344"; // Default password

        // Flag to prevent recursion when updating
        private bool _isUpdatingConnectionString = false;

        public ConnectionStringControl()
        {
            InitializeComponent();
            _profileManager = new ConnectionProfileManager();
            
            // Need to use the Loaded event to ensure UI elements are initialized
            Loaded += (s, e) => 
            {
                LoadProfiles();
                UpdatePanelVisibility();
                UpdateOptionAvailability();
                
                // Set default values for all providers - use Dispatcher to ensure UI is ready
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    InitializeDefaultValues();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
                
                // Set default Firebird credentials and override states
                if (FirebirdUsernameTextBox != null && FirebirdPasswordBox != null)
                {
                    // Set defaults if not already set
                    if (string.IsNullOrEmpty(FirebirdUsername))
                        FirebirdUsername = "SYSDB";
                    if (string.IsNullOrEmpty(_firebirdPassword))
                        _firebirdPassword = "Hosis11223344";
                    
                    FirebirdPasswordBox.Password = _firebirdPassword;
                    
                    // Set override checkboxes to unchecked by default (fields disabled)
                    if (FirebirdUsernameOverrideCheckBox != null)
                        FirebirdUsernameOverrideCheckBox.IsChecked = false;
                    if (FirebirdPasswordOverrideCheckBox != null)
                        FirebirdPasswordOverrideCheckBox.IsChecked = false;
                }
            };
        }

        private void InitializeDefaultValues()
        {
            // Set SQL Server defaults - both property and direct TextBox access
            SqlServerServer = "LocalHost";
            if (SqlServerServerTextBox != null)
                SqlServerServerTextBox.Text = "LocalHost";
            
            // Ensure Windows Authentication is selected by default and username/password are disabled
            if (SqlServerWindowsAuthRadioButton != null)
                SqlServerWindowsAuthRadioButton.IsChecked = true;
            if (SqlServerSqlAuthRadioButton != null)
                SqlServerSqlAuthRadioButton.IsChecked = false;
            
            // Explicitly disable username/password fields for Windows Auth
            UpdateSqlServerAuthFields();
            
            // Set MySQL defaults  
            MySqlServer = "LocalHost";
            MySqlPort = "3306";
            if (MySqlServerTextBox != null)
                MySqlServerTextBox.Text = "LocalHost";
            if (MySqlPortTextBox != null)
                MySqlPortTextBox.Text = "3306";
            
            // Set PostgreSQL defaults
            PostgreSqlHost = "LocalHost"; 
            PostgreSqlPort = "5432";
            if (PostgreSqlHostTextBox != null)
                PostgreSqlHostTextBox.Text = "LocalHost";
            if (PostgreSqlPortTextBox != null)
                PostgreSqlPortTextBox.Text = "5432";
            
            // Firebird defaults are handled separately in the existing logic below
        }

        #region Event Handlers

        private void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePanelVisibility();
            UpdateConnectionString();
            UpdateOptionAvailability();
        }

        private void SqlServerAuthType_Changed(object sender, RoutedEventArgs e)
        {
            UpdateSqlServerAuthFields();
            UpdateConnectionString();
        }

        private void UpdateSqlServerAuthFields()
        {
            // Update username/password field states based on authentication type
            if (SqlServerUsernameTextBox != null && SqlServerPasswordBox != null)
            {
                bool useSqlAuth = SqlServerSqlAuthRadioButton?.IsChecked == true;
                SqlServerUsernameTextBox.IsEnabled = useSqlAuth;
                SqlServerPasswordBox.IsEnabled = useSqlAuth;
            }
        }

        private void SqlServerPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _sqlServerPassword = SqlServerPasswordBox.Password;
            UpdateConnectionString();
        }

        private void MySqlPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _mySqlPassword = MySqlPasswordBox.Password;
            UpdateConnectionString();
        }

        private void PostgreSqlPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _postgreSqlPassword = PostgreSqlPasswordBox.Password;
            UpdateConnectionString();
        }

        private void FirebirdPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _firebirdPassword = FirebirdPasswordBox.Password;
            UpdateConnectionString();
        }

        private void ConnectionField_TextChanged(object sender, RoutedEventArgs e)
        {
            UpdateConnectionString();
        }

        private void BrowseFirebirdFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select Firebird Database File",
                Filter = "Firebird Database Files (*.fdb;*.gdb)|*.fdb;*.gdb|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                FirebirdDatabaseFile = openFileDialog.FileName;
                UpdateConnectionString();
            }
        }

        private void FirebirdUsernameOverride_Changed(object sender, RoutedEventArgs e)
        {
            if (FirebirdUsernameOverrideCheckBox?.IsChecked == false)
            {
                // Reset to default when override is unchecked
                FirebirdUsername = "SYSDB";
            }
            UpdateConnectionString();
        }

        private void FirebirdPasswordOverride_Changed(object sender, RoutedEventArgs e)
        {
            if (FirebirdPasswordOverrideCheckBox?.IsChecked == false)
            {
                // Reset to default when override is unchecked
                _firebirdPassword = "Hosis11223344";
                if (FirebirdPasswordBox != null)
                    FirebirdPasswordBox.Password = _firebirdPassword;
            }
            UpdateConnectionString();
        }

        #endregion

        #region Update Methods

        private void UpdatePanelVisibility()
        {
            // Check if UI is ready
            if (SqlServerPanel == null || MySqlPanel == null || PostgreSqlPanel == null || FirebirdPanel == null)
            {
                // UI elements not ready yet
                return;
            }
            
            // Hide all panels
            SqlServerPanel.Visibility = Visibility.Collapsed;
            MySqlPanel.Visibility = Visibility.Collapsed;
            PostgreSqlPanel.Visibility = Visibility.Collapsed;
            FirebirdPanel.Visibility = Visibility.Collapsed;

            // Show the selected panel and set default values if empty
            switch (ProviderIndex)
            {
                case 0: // SQL Server
                    SqlServerPanel.Visibility = Visibility.Visible;
                    // Set default server value if empty
                    if (string.IsNullOrEmpty(SqlServerServer))
                    {
                        SqlServerServer = "LocalHost";
                    }
                    // Ensure Windows Authentication is default and username/password are disabled
                    if (SqlServerWindowsAuthRadioButton != null && SqlServerSqlAuthRadioButton != null)
                    {
                        SqlServerWindowsAuthRadioButton.IsChecked = true;
                        SqlServerSqlAuthRadioButton.IsChecked = false;
                        // Update username/password field states
                        UpdateSqlServerAuthFields();
                    }
                    break;
                case 1: // MySQL
                    MySqlPanel.Visibility = Visibility.Visible;
                    // Set default values if empty
                    if (string.IsNullOrEmpty(MySqlServer))
                    {
                        MySqlServer = "LocalHost";
                    }
                    if (string.IsNullOrEmpty(MySqlPort))
                    {
                        MySqlPort = "3306";
                    }
                    break;
                case 2: // PostgreSQL
                    PostgreSqlPanel.Visibility = Visibility.Visible;
                    // Set default values if empty
                    if (string.IsNullOrEmpty(PostgreSqlHost))
                    {
                        PostgreSqlHost = "LocalHost";
                    }
                    if (string.IsNullOrEmpty(PostgreSqlPort))
                    {
                        PostgreSqlPort = "5432";
                    }
                    break;
                case 3: // Firebird
                    FirebirdPanel.Visibility = Visibility.Visible;
                    // Firebird defaults are handled separately in the Loaded event
                    break;
            }
        }

        private void UpdateConnectionString()
        {
            if (_isUpdatingConnectionString)
                return;

            // Check if UI elements are initialized
            if (!IsLoaded || SqlServerWindowsAuthRadioButton == null || 
                SqlServerTrustCertCheckBox == null || SqlServerAdditionalParamsTextBox == null ||
                MySqlSslCheckBox == null || MySqlAdditionalParamsTextBox == null ||
                PostgreSqlSslCheckBox == null || PostgreSqlAdditionalParamsTextBox == null ||
                FirebirdDatabaseFileTextBox == null || FirebirdUsernameTextBox == null || FirebirdPasswordBox == null)
            {
                return;
            }

            _isUpdatingConnectionString = true;

            try
            {
                switch (ProviderIndex)
                {
                    case 0: // SQL Server
                        ConnectionString = ConnectionStringBuilder.BuildSqlServerConnectionString(
                            SqlServerServer ?? string.Empty,
                            SqlServerDatabase ?? string.Empty,
                            SqlServerWindowsAuthRadioButton.IsChecked ?? false,
                            SqlServerUsername ?? string.Empty,
                            _sqlServerPassword ?? string.Empty,
                            SqlServerTrustCertCheckBox.IsChecked ?? true,
                            SqlServerAdditionalParamsTextBox?.Text ?? string.Empty);
                        break;
                    case 1: // MySQL
                        int mySqlPort = 3306;
                        int.TryParse(MySqlPort, out mySqlPort);

                        ConnectionString = ConnectionStringBuilder.BuildMySqlConnectionString(
                            MySqlServer ?? string.Empty,
                            MySqlDatabase ?? string.Empty,
                            mySqlPort,
                            MySqlUsername ?? string.Empty,
                            _mySqlPassword ?? string.Empty,
                            MySqlSslCheckBox.IsChecked ?? false,
                            MySqlAdditionalParamsTextBox?.Text ?? string.Empty);
                        break;
                    case 2: // PostgreSQL
                        int postgreSqlPort = 5432;
                        int.TryParse(PostgreSqlPort, out postgreSqlPort);

                        ConnectionString = ConnectionStringBuilder.BuildPostgreSqlConnectionString(
                            PostgreSqlHost ?? string.Empty,
                            PostgreSqlDatabase ?? string.Empty,
                            postgreSqlPort,
                            PostgreSqlUsername ?? string.Empty,
                            _postgreSqlPassword ?? string.Empty,
                            PostgreSqlSslCheckBox.IsChecked ?? true,
                            PostgreSqlAdditionalParamsTextBox?.Text ?? string.Empty);
                        break;
                    case 3: // Firebird
                        string version = "";
                        if (FirebirdFormatComboBox != null)
                        {
                            // Get version based on selection
                            switch(FirebirdFormatComboBox.SelectedIndex)
                            {
                                case 1:
                                    version = "2.5";
                                    break;
                                default:
                                    version = "";
                                    break;
                            };
                        }
                        bool readOnly = FirebirdReadOnlyCheckBox != null ? FirebirdReadOnlyCheckBox.IsChecked ?? true : true;

                        // Use default values when override is not checked
                        string username = FirebirdUsernameOverrideCheckBox?.IsChecked == true ? 
                            FirebirdUsername ?? "SYSDB" : "SYSDB";
                        string password = FirebirdPasswordOverrideCheckBox?.IsChecked == true ? 
                            _firebirdPassword ?? "Hosis11223344" : "Hosis11223344";

                        ConnectionString = ConnectionStringBuilder.BuildFirebirdConnectionString(
                            FirebirdDatabaseFile ?? string.Empty,
                            username,
                            password,
                            version,
                            readOnly,
                            FirebirdAdditionalOptionsTextBox?.Text ?? string.Empty);
                        break;
                }
            }
            finally
            {
                _isUpdatingConnectionString = false;
            }
        }

        private void ParseExistingConnectionString()
        {
            if (_isUpdatingConnectionString)
                return;

            _isUpdatingConnectionString = true;

            try
            {
                switch (ProviderIndex)
                {
                    case 0: // SQL Server
                        if (ConnectionStringBuilder.TryParseSqlServerConnectionString(
                            ConnectionString,
                            out string sqlServer,
                            out string sqlDatabase,
                            out bool integratedSecurity,
                            out string sqlUsername,
                            out string sqlPassword))
                        {
                            SqlServerServer = sqlServer;
                            SqlServerDatabase = sqlDatabase;
                            SqlServerWindowsAuthRadioButton.IsChecked = integratedSecurity;
                            SqlServerSqlAuthRadioButton.IsChecked = !integratedSecurity;
                            SqlServerUsername = sqlUsername;
                            SqlServerPasswordBox.Password = sqlPassword;
                            _sqlServerPassword = sqlPassword;
                        }
                        break;
                    case 1: // MySQL
                        if (ConnectionStringBuilder.TryParseMySqlConnectionString(
                            ConnectionString,
                            out string mysqlServer,
                            out string mysqlDatabase,
                            out int mysqlPort,
                            out string mysqlUsername,
                            out string mysqlPassword,
                            out bool mysqlUseSsl))
                        {
                            MySqlServer = mysqlServer;
                            MySqlDatabase = mysqlDatabase;
                            MySqlPort = mysqlPort.ToString();
                            MySqlUsername = mysqlUsername;
                            MySqlPasswordBox.Password = mysqlPassword;
                            _mySqlPassword = mysqlPassword;
                            MySqlSslCheckBox.IsChecked = mysqlUseSsl;
                        }
                        break;
                    case 2: // PostgreSQL
                        if (ConnectionStringBuilder.TryParsePostgreSqlConnectionString(
                            ConnectionString,
                            out string pgsqlHost,
                            out string pgsqlDatabase,
                            out int pgsqlPort,
                            out string pgsqlUsername,
                            out string pgsqlPassword,
                            out bool pgsqlUseSsl))
                        {
                            PostgreSqlHost = pgsqlHost;
                            PostgreSqlDatabase = pgsqlDatabase;
                            PostgreSqlPort = pgsqlPort.ToString();
                            PostgreSqlUsername = pgsqlUsername;
                            PostgreSqlPasswordBox.Password = pgsqlPassword;
                            _postgreSqlPassword = pgsqlPassword;
                            PostgreSqlSslCheckBox.IsChecked = pgsqlUseSsl;
                        }
                        break;
                    case 3: // Firebird
                        if (ConnectionStringBuilder.TryParseFirebirdConnectionString(
                            ConnectionString,
                            out string databaseFile,
                            out string username,
                            out string password,
                            out string version))
                        {
                            FirebirdDatabaseFile = databaseFile;
                            
                            // Check if username/password are different from defaults
                            bool usernameIsDefault = username == "SYSDB";
                            bool passwordIsDefault = password == "Hosis11223344";
                            
                            if (FirebirdUsernameOverrideCheckBox != null)
                            {
                                FirebirdUsernameOverrideCheckBox.IsChecked = !usernameIsDefault;
                            }
                            
                            if (FirebirdPasswordOverrideCheckBox != null)
                            {
                                FirebirdPasswordOverrideCheckBox.IsChecked = !passwordIsDefault;
                            }
                            
                            FirebirdUsername = username;
                            _firebirdPassword = password;
                            if (FirebirdPasswordBox != null)
                                FirebirdPasswordBox.Password = password;
                            
                            if (FirebirdFormatComboBox != null)
                            {
                                // Set format selection based on version
                                if (version == "2.5")
                                    FirebirdFormatComboBox.SelectedIndex = 1;
                                else
                                    FirebirdFormatComboBox.SelectedIndex = 0;
                            }
                        }
                        break;
                }
            }
            finally
            {
                _isUpdatingConnectionString = false;
            }
        }

        #endregion

        #region Static Property Changed Handlers

        private static void OnConnectionStringChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ConnectionStringControl control)
            {
                control.ParseExistingConnectionString();
            }
        }

        private static void OnProviderIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ConnectionStringControl control && control.IsLoaded)
            {
                control.UpdatePanelVisibility();
                control.UpdateConnectionString();
                control.UpdateOptionAvailability();
            }
        }

        #endregion

        /// <summary>
        /// Updates the enabled/disabled state of UI controls based on the selected provider
        /// </summary>
        private void UpdateOptionAvailability()
        {
            // Check if UI is ready
            if (!IsLoaded || SqlServerPanel == null || MySqlPanel == null || 
                PostgreSqlPanel == null || FirebirdPanel == null)
            {
                return;
            }

            // Disable all input fields that aren't in the current panel
            // This will grey out fields that are not relevant to the selected provider

            // SQL Server controls
            bool isSqlServerActive = ProviderIndex == 0;
            SetControlsEnabled(SqlServerPanel, isSqlServerActive);
            
            // MySQL controls
            bool isMySqlActive = ProviderIndex == 1;
            SetControlsEnabled(MySqlPanel, isMySqlActive);
            
            // PostgreSQL controls
            bool isPostgreSqlActive = ProviderIndex == 2;
            SetControlsEnabled(PostgreSqlPanel, isPostgreSqlActive);

            // Firebird controls
            bool isFirebirdActive = ProviderIndex == 3;
            SetControlsEnabled(FirebirdPanel, isFirebirdActive);
            
            // Special handling for SQL Server authentication options
            if (isSqlServerActive)
            {
                UpdateSqlServerAuthFields();
            }
        }
        
        /// <summary>
        /// Helper method to set IsEnabled property on all input controls within a panel
        /// </summary>
        private void SetControlsEnabled(Panel panel, bool isEnabled)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(panel))
            {
                // Enable/disable interactive elements
                if (child is TextBox textBox)
                {
                    // Skip Firebird username/password fields - they have their own binding logic
                    if (textBox == FirebirdUsernameTextBox)
                        continue;
                    textBox.IsEnabled = isEnabled;
                }
                else if (child is PasswordBox passwordBox)
                {
                    // Skip Firebird password field - it has its own binding logic
                    if (passwordBox == FirebirdPasswordBox)
                        continue;
                    passwordBox.IsEnabled = isEnabled;
                }
                else if (child is CheckBox checkBox)
                {
                    checkBox.IsEnabled = isEnabled;
                }
                else if (child is RadioButton radioButton)
                {
                    radioButton.IsEnabled = isEnabled;
                }
                else if (child is ComboBox comboBox)
                {
                    comboBox.IsEnabled = isEnabled;
                }
                else if (child is Button button)
                {
                    button.IsEnabled = isEnabled;
                }
                else if (child is Panel childPanel)
                {
                    // Recursively process child panels
                    SetControlsEnabled(childPanel, isEnabled);
                }
            }
        }

        #region Profile Management

        private void LoadProfiles()
        {
            if (ProfileComboBox == null) return;
            
            try
            {
                ProfileComboBox.ItemsSource = null;
                var profiles = _profileManager.GetProfiles().OrderBy(p => p.Name).ToList();
                profiles.Insert(0, new ConnectionProfile { Name = "(Select a profile...)" });
                ProfileComboBox.ItemsSource = profiles;
                ProfileComboBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load profiles: {ex.Message}");
            }
        }
        
        private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingProfile || ProfileComboBox.SelectedItem is not ConnectionProfile profile || 
                string.IsNullOrEmpty(profile.Provider))
                return;
                
            _isLoadingProfile = true;
            try
            {
                LoadProfileIntoFields(profile);
                _profileManager.UpdateLastUsed(profile.Name);
                
                // Notify shared profile system about the change
                NotifySharedProfileChanged(profile);
            }
            finally
            {
                _isLoadingProfile = false;
            }
        }
        
        private void SaveProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var currentProfile = GetCurrentProfileFromFields();
                if (string.IsNullOrEmpty(currentProfile.Provider))
                {
                    MessageBox.Show("Please configure a connection before saving a profile.", 
                        "No Connection", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                var saveWindow = new SaveProfileWindow(currentProfile)
                {
                    Owner = Window.GetWindow(this)
                };
                
                if (saveWindow.ShowDialog() == true && saveWindow.Profile != null)
                {
                    _profileManager.SaveProfile(saveWindow.Profile);
                    LoadProfiles();
                    
                    // Select the newly saved profile
                    var profiles = ProfileComboBox.ItemsSource as List<ConnectionProfile>;
                    var savedProfile = profiles?.FirstOrDefault(p => p.Name == saveWindow.Profile.Name);
                    if (savedProfile != null)
                    {
                        ProfileComboBox.SelectedItem = savedProfile;
                    }
                    
                    MessageBox.Show($"Profile '{saveWindow.Profile.Name}' saved successfully!",
                        "Profile Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save profile: {ex.Message}", 
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ManageProfiles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var manageWindow = new ManageProfilesWindow(_profileManager)
                {
                    Owner = Window.GetWindow(this)
                };
                
                if (manageWindow.ShowDialog() == true && manageWindow.SelectedProfile != null)
                {
                    LoadProfiles();
                    
                    // Select the chosen profile
                    var profiles = ProfileComboBox.ItemsSource as List<ConnectionProfile>;
                    var selectedProfile = profiles?.FirstOrDefault(p => p.Name == manageWindow.SelectedProfile.Name);
                    if (selectedProfile != null)
                    {
                        ProfileComboBox.SelectedItem = selectedProfile;
                    }
                }
                else
                {
                    // Just refresh the list in case profiles were modified
                    LoadProfiles();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open profile manager: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private ConnectionProfile GetCurrentProfileFromFields()
        {
            var profile = new ConnectionProfile();
            
            // Get provider
            var providerNames = new[] { "SqlServer", "MySQL", "PostgreSQL", "Firebird" };
            profile.Provider = ProviderIndex < providerNames.Length ? providerNames[ProviderIndex] : "SqlServer";
            
            switch (ProviderIndex)
            {
                case 0: // SQL Server
                    profile.Server = SqlServerServer ?? "";
                    profile.Database = SqlServerDatabase ?? "";
                    profile.Username = SqlServerUsername ?? "";
                    profile.Password = _sqlServerPassword ?? "";
                    profile.UseWindowsAuth = SqlServerWindowsAuthRadioButton?.IsChecked == true;
                    profile.TrustServerCertificate = SqlServerTrustCertCheckBox?.IsChecked == true;
                    break;
                    
                case 1: // MySQL
                    profile.Server = MySqlServer ?? "";
                    profile.Database = MySqlDatabase ?? "";
                    profile.Username = MySqlUsername ?? "";
                    profile.Password = _mySqlPassword ?? "";
                    profile.Port = int.TryParse(MySqlPort, out int mysqlPort) ? mysqlPort : 3306;
                    profile.UseSsl = MySqlSslCheckBox?.IsChecked == true;
                    break;
                    
                case 2: // PostgreSQL
                    profile.Server = PostgreSqlHost ?? "";
                    profile.Database = PostgreSqlDatabase ?? "";
                    profile.Username = PostgreSqlUsername ?? "";
                    profile.Password = _postgreSqlPassword ?? "";
                    profile.Port = int.TryParse(PostgreSqlPort, out int pgPort) ? pgPort : 5432;
                    profile.UseSsl = PostgreSqlSslCheckBox?.IsChecked == true;
                    break;
                    
                case 3: // Firebird
                    profile.Server = "localhost";
                    profile.DatabasePath = FirebirdDatabaseFile ?? "";
                    profile.Username = FirebirdUsername ?? "";
                    profile.Password = _firebirdPassword ?? "";
                    profile.ReadOnlyConnection = FirebirdReadOnlyCheckBox?.IsChecked == true;
                    profile.FirebirdVersion = FirebirdFormatComboBox?.SelectedIndex == 0 ? "3.0" : "2.5";
                    break;
            }
            
            return profile;
        }
        
        private void LoadProfileIntoFields(ConnectionProfile profile)
        {
            // Set provider first
            var providerNames = new[] { "SqlServer", "MySQL", "PostgreSQL", "Firebird" };
            var providerIndex = Array.FindIndex(providerNames, p => p.Equals(profile.Provider, StringComparison.OrdinalIgnoreCase));
            if (providerIndex >= 0)
            {
                ProviderIndex = providerIndex;
            }
            
            // Load provider-specific fields
            switch (profile.Provider.ToLower())
            {
                case "sqlserver":
                    SqlServerServer = profile.Server;
                    SqlServerDatabase = profile.Database;
                    SqlServerUsername = profile.Username;
                    _sqlServerPassword = profile.Password;
                    if (SqlServerPasswordBox != null)
                        SqlServerPasswordBox.Password = profile.Password;
                    if (SqlServerWindowsAuthRadioButton != null && SqlServerSqlAuthRadioButton != null)
                    {
                        SqlServerWindowsAuthRadioButton.IsChecked = profile.UseWindowsAuth;
                        SqlServerSqlAuthRadioButton.IsChecked = !profile.UseWindowsAuth;
                    }
                    if (SqlServerTrustCertCheckBox != null)
                        SqlServerTrustCertCheckBox.IsChecked = profile.TrustServerCertificate;
                    break;
                    
                case "mysql":
                    MySqlServer = profile.Server;
                    MySqlDatabase = profile.Database;
                    MySqlUsername = profile.Username;
                    MySqlPort = profile.Port > 0 ? profile.Port.ToString() : "3306";
                    _mySqlPassword = profile.Password;
                    if (MySqlPasswordBox != null)
                        MySqlPasswordBox.Password = profile.Password;
                    if (MySqlSslCheckBox != null)
                        MySqlSslCheckBox.IsChecked = profile.UseSsl;
                    break;
                    
                case "postgresql":
                    PostgreSqlHost = profile.Server;
                    PostgreSqlDatabase = profile.Database;
                    PostgreSqlUsername = profile.Username;
                    PostgreSqlPort = profile.Port > 0 ? profile.Port.ToString() : "5432";
                    _postgreSqlPassword = profile.Password;
                    if (PostgreSqlPasswordBox != null)
                        PostgreSqlPasswordBox.Password = profile.Password;
                    if (PostgreSqlSslCheckBox != null)
                        PostgreSqlSslCheckBox.IsChecked = profile.UseSsl;
                    break;
                    
                case "firebird":
                    FirebirdDatabaseFile = profile.DatabasePath;
                    FirebirdUsername = profile.Username;
                    _firebirdPassword = profile.Password;
                    if (FirebirdPasswordBox != null)
                        FirebirdPasswordBox.Password = profile.Password;
                    if (FirebirdReadOnlyCheckBox != null)
                        FirebirdReadOnlyCheckBox.IsChecked = profile.ReadOnlyConnection;
                    if (FirebirdFormatComboBox != null)
                        FirebirdFormatComboBox.SelectedIndex = profile.FirebirdVersion == "3.0" ? 0 : 1;
                    break;
            }
            
            // Update connection string
            UpdateConnectionString();
        }

        #endregion

        #region Shared Profile System

        public void SetSharedProfileManager(ConnectionProfileManager sharedManager, Action<ConnectionProfile?, string> profileChangedCallback)
        {
            _profileManager = sharedManager;
            _sharedProfileChangedCallback = profileChangedCallback;
            
            // Determine tab identifier from parent window context
            _tabIdentifier = DetermineTabIdentifier();
            
            // Reload profiles from shared manager
            if (IsLoaded)
            {
                LoadProfiles();
            }
        }

        public void LoadSharedProfile(ConnectionProfile profile)
        {
            _isLoadingProfile = true;
            try
            {
                // Load the profile without triggering the shared callback
                LoadProfileIntoFields(profile);
                
                // Update the selected item in the dropdown
                if (ProfileComboBox?.ItemsSource is List<ConnectionProfile> profiles)
                {
                    var matchingProfile = profiles.FirstOrDefault(p => p.Name == profile.Name);
                    if (matchingProfile != null)
                    {
                        ProfileComboBox.SelectedItem = matchingProfile;
                    }
                }
            }
            finally
            {
                _isLoadingProfile = false;
            }
        }

        private string DetermineTabIdentifier()
        {
            // Try to determine which tab this control belongs to based on parent context
            var parent = Parent;
            while (parent != null)
            {
                if (parent is FrameworkElement element && element.Name != null)
                {
                    if (element.Name.Contains("Export")) return "Export";
                    if (element.Name.Contains("Import")) return "Import";
                    if (element.Name.Contains("Schema")) return "Schema";
                }
                parent = LogicalTreeHelper.GetParent(parent);
            }
            
            // Fallback - try to determine from connection string property bindings or naming
            return "Unknown";
        }

        private void NotifySharedProfileChanged(ConnectionProfile? profile)
        {
            _sharedProfileChangedCallback?.Invoke(profile, _tabIdentifier);
        }

        public void ClearProfile()
        {
            _isLoadingProfile = true;
            try
            {
                // Reset the dropdown to the first item (select a profile...)
                if (ProfileComboBox?.ItemsSource is List<ConnectionProfile> profiles && profiles.Count > 0)
                {
                    ProfileComboBox.SelectedIndex = 0;
                }
            }
            finally
            {
                _isLoadingProfile = false;
            }
        }

        #endregion

    }
}