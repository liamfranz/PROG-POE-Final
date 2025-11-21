using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClaimSystem
{
    public partial class MainWindow : Window
    {
        private const long MAX_FILE_BYTES = 5 * 1024 * 1024; // 5 MB
        private readonly string[] ALLOWED_EXTENSIONS = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx" };
        private readonly string appDataFolder;
        private readonly string filesFolder;
        private readonly string dataFilePath;
        private readonly string lecturersFilePath;

        public ObservableCollection<Claim> Claims { get; set; } = new ObservableCollection<Claim>();
        public ObservableCollection<Lecturer> Lecturers { get; set; } = new ObservableCollection<Lecturer>();

        private string uploadedFileFullPath = string.Empty;
        private string uploadedFileOriginalName = string.Empty;
        private Lecturer loggedInLecturer = null;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaimSystem");
            filesFolder = Path.Combine(appDataFolder, "Files");
            dataFilePath = Path.Combine(appDataFolder, "claims.json");
            lecturersFilePath = Path.Combine(appDataFolder, "lecturers.json");

            Directory.CreateDirectory(filesFolder);

            LoadClaimsFromDisk();
            LoadLecturersFromDisk();
            LoadHRLecturers();

            dgClaims.ItemsSource = Claims; // Manager view
            tabLecturerClaims.Visibility = Visibility.Collapsed;
            tabLecturerLogin.Visibility = Visibility.Visible;
        }

        #region Upload File
        private void UploadFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Documents (*.pdf;*.doc;*.docx;*.xls;*.xlsx)|*.pdf;*.doc;*.docx;*.xls;*.xlsx|All files|*.*",
                Title = "Select Supporting Document"
            };
            if (dlg.ShowDialog() == true)
            {
                var fi = new FileInfo(dlg.FileName);
                if (!ALLOWED_EXTENSIONS.Contains(fi.Extension.ToLower()))
                {
                    MessageBox.Show("Only PDF, DOC, DOCX, XLS and XLSX formats are allowed.", "Invalid File Type", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (fi.Length > MAX_FILE_BYTES)
                {
                    MessageBox.Show($"File too large. Maximum allowed size is {MAX_FILE_BYTES / (1024 * 1024)} MB.", "File Too Large", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                uploadedFileFullPath = dlg.FileName;
                uploadedFileOriginalName = fi.Name;
                txtFilePath.Text = fi.Name;
            }
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                var storedPath = btn.Tag as string;
                if (!string.IsNullOrWhiteSpace(storedPath) && File.Exists(storedPath))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(storedPath) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Unable to open file: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("File not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        #endregion

        #region Submit Claim
        private void SubmitClaim_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtLecturerName.Text) ||
                string.IsNullOrWhiteSpace(txtLecturerID.Text) ||
                string.IsNullOrWhiteSpace(txtLecturerEmail.Text) ||
                string.IsNullOrWhiteSpace(txtHoursWorked.Text) ||
                string.IsNullOrWhiteSpace(txtHourlyRate.Text))
            {
                MessageBox.Show("Please fill in all required fields.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!double.TryParse(txtHoursWorked.Text, out double hours) ||
                !double.TryParse(txtHourlyRate.Text, out double rate))
            {
                MessageBox.Show("Please enter valid numeric values for hours and rate.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string details = $"Lecturer Name: {txtLecturerName.Text}\nLecturer ID: {txtLecturerID.Text}\nEmail: {txtLecturerEmail.Text}\nHours Worked: {hours}\nHourly Rate: {rate}\nNotes: {txtNotes.Text}";
            var confirm = MessageBox.Show("Confirm submission:\n\n" + details, "Confirm Claim", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            string storedFilePath = null;
            string origFileName = null;
            if (!string.IsNullOrEmpty(uploadedFileFullPath) && File.Exists(uploadedFileFullPath))
            {
                origFileName = uploadedFileOriginalName;
                var newFileName = Guid.NewGuid().ToString() + Path.GetExtension(uploadedFileFullPath);
                storedFilePath = Path.Combine(filesFolder, newFileName);
                File.Copy(uploadedFileFullPath, storedFilePath, true);
            }

            // Automatic rejection if Total < 100
            double total = Math.Round(hours * rate, 2);
            string status = (total < 100) ? "Rejected" : "Pending";
            if (status == "Rejected")
            {
                MessageBox.Show("Claim automatically rejected: Total amount less than 100.", "Claim Rejected", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            var newClaim = new Claim
            {
                Id = Guid.NewGuid().ToString(),
                LecturerName = txtLecturerName.Text.Trim(),
                LecturerId = txtLecturerID.Text.Trim(),
                LecturerEmail = txtLecturerEmail.Text.Trim(),
                HoursWorked = hours,
                HourlyRate = rate,
                TotalAmount = total,
                Notes = txtNotes.Text.Trim(),
                Status = status,
                DateSubmitted = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                StoredFilePath = storedFilePath,
                OriginalFileName = origFileName
            };

            Claims.Add(newClaim);
            SaveClaimsToDisk();
            ClearClaimForm();
            dgClaims.Items.Refresh(); // Update Manager grid
        }

        private void ClearClaimForm()
        {
            txtLecturerName.Clear();
            txtLecturerID.Clear();
            txtLecturerEmail.Clear();
            txtHoursWorked.Clear();
            txtHourlyRate.Clear();
            txtNotes.Clear();
            txtFilePath.Text = "No file selected";
            uploadedFileFullPath = uploadedFileOriginalName = string.Empty;
        }
        #endregion

        #region Lecturer Login
        private void LoginLecturer_Click(object sender, RoutedEventArgs e)
        {
            var lecturer = Lecturers.FirstOrDefault(l => l.LecturerId == txtLoginID.Text.Trim() &&
                                                          l.PasswordHash == ComputeHash(txtLoginPassword.Password));
            if (lecturer != null)
            {
                loggedInLecturer = lecturer;
                tabLecturerLogin.Visibility = Visibility.Collapsed;
                tabLecturerClaims.Visibility = Visibility.Visible;
                lblLoggedInName.Text = $"Welcome, {lecturer.FullName}";

                // Filter claims for this lecturer only
                var lecturerClaims = Claims.Where(c => c.LecturerId == lecturer.LecturerId).ToList();

                // Auto-reject any claim < 100
                foreach (var claim in lecturerClaims)
                {
                    if (claim.Status == "Pending" && claim.TotalAmount < 100)
                        claim.Status = "Rejected";
                }

                dgLecturerClaims.ItemsSource = lecturerClaims;
                dgLecturerClaims.Items.Refresh();
                SaveClaimsToDisk();
            }
            else
            {
                MessageBox.Show("Invalid Lecturer ID or Password.", "Login Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void LogoutLecturer_Click(object sender, RoutedEventArgs e)
        {
            loggedInLecturer = null;
            tabLecturerLogin.Visibility = Visibility.Visible;
            tabLecturerClaims.Visibility = Visibility.Collapsed;
            txtLoginID.Clear();
            txtLoginPassword.Clear();
        }
        #endregion

        #region Registration
        private void RegisterLecturer_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtRegName.Text) ||
                string.IsNullOrWhiteSpace(txtRegID.Text) ||
                string.IsNullOrWhiteSpace(txtRegEmail.Text) ||
                string.IsNullOrWhiteSpace(txtRegPassword.Password))
            {
                MessageBox.Show("Please fill in all required fields.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (Lecturers.Any(l => l.LecturerId == txtRegID.Text.Trim()))
            {
                MessageBox.Show("Lecturer ID already exists.", "Registration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var newLecturer = new Lecturer
            {
                LecturerId = txtRegID.Text.Trim(),
                FullName = txtRegName.Text.Trim(),
                Email = txtRegEmail.Text.Trim(),
                PasswordHash = ComputeHash(txtRegPassword.Password)
            };

            Lecturers.Add(newLecturer);
            SaveLecturersToDisk();
            MessageBox.Show("Lecturer account created successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

            txtRegName.Clear();
            txtRegID.Clear();
            txtRegEmail.Clear();
            txtRegPassword.Clear();
        }
        #endregion

        #region Manager Actions
        private void ApproveRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Claim claim)
            {
                claim.Status = "Approved";
                dgClaims.Items.Refresh();
                SaveClaimsToDisk();
            }
        }

        private void RejectRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Claim claim)
            {
                claim.Status = "Rejected";
                dgClaims.Items.Refresh();
                SaveClaimsToDisk();
            }
        }
        #endregion

        #region HR Actions
        private void GenerateApprovedClaimsReport_Click(object sender, RoutedEventArgs e)
        {
            var approved = Claims.Where(c => c.Status == "Approved").ToList();
            if (!approved.Any())
            {
                txtReportOutput.Text = "No approved claims to display.";
                return;
            }

            StringBuilder sb = new StringBuilder();
            foreach (var c in approved)
            {
                sb.AppendLine($"{c.DateSubmitted} - {c.LecturerName} ({c.LecturerId}): Total R{c.TotalAmount}");
            }
            txtReportOutput.Text = sb.ToString();
        }

        private void SendInvoice_Click(object sender, RoutedEventArgs e)
        {
            string lecturerId = txtHRSendLecturerID.Text.Trim();
            var lecturer = Lecturers.FirstOrDefault(l => l.LecturerId == lecturerId);
            if (lecturer == null)
            {
                MessageBox.Show("Lecturer not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var claims = Claims.Where(c => c.LecturerId == lecturerId).ToList();
            if (!claims.Any())
            {
                MessageBox.Show("No claims found for this lecturer.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string report = $"Invoice / Claim Report for {lecturer.FullName} ({lecturer.LecturerId}):\n\n";
            foreach (var c in claims)
            {
                report += $"{c.DateSubmitted} - {c.Notes} - Total R{c.TotalAmount} - Status: {c.Status}\n";
            }

            MessageBox.Show(report, "Invoice / Claim Report", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        #endregion

        #region Utilities
        private void SaveClaimsToDisk()
        {
            File.WriteAllText(dataFilePath, JsonSerializer.Serialize(Claims));
        }

        private void LoadClaimsFromDisk()
        {
            if (File.Exists(dataFilePath))
            {
                var data = File.ReadAllText(dataFilePath);
                var claims = JsonSerializer.Deserialize<ObservableCollection<Claim>>(data);
                if (claims != null) Claims = claims;
            }
        }

        private void SaveLecturersToDisk()
        {
            File.WriteAllText(lecturersFilePath, JsonSerializer.Serialize(Lecturers));
        }

        private void LoadLecturersFromDisk()
        {
            if (File.Exists(lecturersFilePath))
            {
                var data = File.ReadAllText(lecturersFilePath);
                var lecturers = JsonSerializer.Deserialize<ObservableCollection<Lecturer>>(data);
                if (lecturers != null) Lecturers = lecturers;
            }
        }

        private void LoadHRLecturers()
        {
            // Can bind lecturers to combo box if needed
        }

        private string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(bytes);
        }
        #endregion

        #region Row Coloring
        private void dgLecturerClaims_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is Claim claim)
            {
                e.Row.Background = claim.Status switch
                {
                    "Rejected" => Brushes.LightCoral,
                    "Approved" => Brushes.LightGreen,
                    _ => Brushes.White
                };
            }
        }

        private void dgClaims_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is Claim claim)
            {
                e.Row.Background = claim.Status switch
                {
                    "Rejected" => Brushes.LightCoral,
                    "Approved" => Brushes.LightGreen,
                    _ => Brushes.White
                };
            }
        }
        #endregion
    }

    public class Claim
    {
        public string Id { get; set; }
        public string LecturerName { get; set; }
        public string LecturerId { get; set; }
        public string LecturerEmail { get; set; }
        public double HoursWorked { get; set; }
        public double HourlyRate { get; set; }
        public double TotalAmount { get; set; }
        public string Notes { get; set; }
        public string Status { get; set; } = "Pending";
        public string DateSubmitted { get; set; }
        public string StoredFilePath { get; set; }
        public string OriginalFileName { get; set; }
        public bool HasFile => !string.IsNullOrEmpty(StoredFilePath) && File.Exists(StoredFilePath);
    }

    public class Lecturer
    {
        public string LecturerId { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
    }
}
