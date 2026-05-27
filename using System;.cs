using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace VehicleServiceSystem
{
    #region Models

    public class ServiceRecord
    {
        // Metadata Fields
        public string RecordId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsActive { get; set; }
        public string Checksum { get; set; }

        // Domain Fields (Vehicle Service Specific)
        public string LicensePlate { get; set; }
        public string OwnerName { get; set; }
        public string ServiceType { get; set; } // e.g., Oil Change, Brake Repair
        public decimal Cost { get; set; }

        /// <summary>
        /// Generates a SHA256 checksum based on the core domain data to ensure data integrity.
        /// </summary>
        public string ComputeChecksum()
        {
            string rawData = $"{RecordId}|{LicensePlate}|{OwnerName}|{ServiceType}|{Cost:F2}|{CreatedAt:o}|{UpdatedAt:o}|{IsActive}";
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        /// <summary>
        /// Converts the record into a flat, safe CSV-like line string.
        /// </summary>
        public override string ToString()
        {
            return $"{RecordId};{LicensePlate};{OwnerName};{ServiceType};{Cost};{CreatedAt:o};{UpdatedAt:o};{IsActive};{Checksum}";
        }

        /// <summary>
        /// Parses a CSV line back into a ServiceRecord object.
        /// </summary>
        public static ServiceRecord FromString(string line)
        {
            var parts = line.Split(';');
            if (parts.Length < 9) return null;

            return new ServiceRecord
            {
                RecordId = parts[0],
                LicensePlate = parts[1],
                OwnerName = parts[2],
                ServiceType = parts[3],
                Cost = decimal.Parse(parts[4]),
                CreatedAt = DateTime.Parse(parts[5]),
                UpdatedAt = DateTime.Parse(parts[6]),
                IsActive = bool.Parse(parts[7]),
                Checksum = parts[8]
            };
        }
    }

    #endregion

    #region Audit Logger Component

    public class AuditLogger
    {
        private readonly string _auditFilePath;

        public AuditLogger(string auditFilePath)
        {
            _auditFilePath = auditFilePath;
        }

        public void Log(string action, string details)
        {
            try
            {
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ACTION: {action} | DETAILS: {details}";
                File.AppendAllText(_auditFilePath, logEntry + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Critical Logging Failure: {ex.Message}");
                Console.ResetColor();
            }
        }
    }

    #endregion

    #region Validation Component

    public static class Validator
    {
        public static bool ValidateLicensePlate(string plate, out string error)
        {
            if (string.IsNullOrWhiteSpace(plate))
            {
                error = "License plate cannot be empty.";
                return false;
            }
            if (plate.Length < 3 || plate.Length > 10)
            {
                error = "License plate must be between 3 and 10 alphanumeric characters.";
                return false;
            }
            error = null;
            return true;
        }

        public static bool ValidateOwnerName(string name, out string error)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Owner name cannot be empty.";
                return false;
            }
            error = null;
            return true;
        }

        public static bool ValidateServiceType(string service, out string error)
        {
            if (string.IsNullOrWhiteSpace(service))
            {
                error = "Service type cannot be empty.";
                return false;
            }
            error = null;
            return true;
        }

        public static bool ValidateCost(string costStr, out decimal cost, out string error)
        {
            if (!decimal.TryParse(costStr, out cost) || cost < 0)
            {
                error = "Cost must be a valid non-negative decimal value.";
                return false;
            }
            error = null;
            return true;
        }
    }

    #endregion

    #region File Repository Service

    public class VehicleRecordRepository
    {
        private readonly string _dataFilePath;
        private readonly AuditLogger _logger;

        public VehicleRecordRepository(string dataFilePath, AuditLogger logger)
        {
            _dataFilePath = dataFilePath;
            _logger = logger;
        }

        // Helper to load all lines safely
        private List<ServiceRecord> GetAllRawRecords()
        {
            var records = new List<ServiceRecord>();
            if (!File.Exists(_dataFilePath)) return records;

            var lines = File.ReadAllLines(_dataFilePath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var record = ServiceRecord.FromString(line);
                    if (record != null)
                    {
                        // Tamper Proof Verification
                        if (record.Checksum != record.ComputeChecksum())
                        {
                            _logger.Log("Error", $"Data Corruption detected for ID {record.RecordId}. Checksum mismatch.");
                            Console.ForegroundColor = ConsoleColor.DarkRed;
                            Console.WriteLine($"[WARNING] Data corruption or unauthorized manual modification detected for Record ID: {record.RecordId}!");
                            Console.ResetColor();
                        }
                        records.Add(record);
                    }
                }
                catch (Exception)
                {
                    _logger.Log("Error", "Malformed string record line encountered and skipped.");
                }
            }
            return records;
        }

        private void SaveAllRecords(List<ServiceRecord> records)
        {
            var lines = records.Select(r => r.ToString()).ToArray();
            File.WriteAllLines(_dataFilePath, lines);
        }

        public void Add(ServiceRecord record)
        {
            var records = GetAllRawRecords();
            records.Add(record);
            SaveAllRecords(records);
            _logger.Log("Add", $"Created dynamic record ID {record.RecordId} for Vehicle {record.LicensePlate}");
        }

        public List<ServiceRecord> GetAllActive()
        {
            _logger.Log("Read", "Fetched all active records.");
            return GetAllRawRecords().Where(r => r.IsActive).ToList();
        }

        public ServiceRecord GetById(string id)
        {
            _logger.Log("Read", $"Queried record containing ID: {id}");
            return GetAllRawRecords().FirstOrDefault(r => r.RecordId.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        public void Update(ServiceRecord updatedRecord)
        {
            var records = GetAllRawRecords();
            int index = records.FindIndex(r => r.RecordId.Equals(updatedRecord.RecordId, StringComparison.OrdinalIgnoreCase));
            if (index != -1)
            {
                records[index] = updatedRecord;
                SaveAllRecords(records);
                _logger.Log("Update", $"Modified fields for record ID {updatedRecord.RecordId}");
            }
        }

        public bool SoftDelete(string id)
        {
            var records = GetAllRawRecords();
            var record = records.FirstOrDefault(r => r.RecordId.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (record != null && record.IsActive)
            {
                record.IsActive = false;
                record.UpdatedAt = DateTime.Now;
                record.Checksum = record.ComputeChecksum();
                SaveAllRecords(records);
                _logger.Log("Delete-Soft", $"Marked record ID {id} as inactive.");
                return true;
            }
            return false;
        }

        public bool HardDelete(string id)
        {
            var records = GetAllRawRecords();
            var record = records.FirstOrDefault(r => r.RecordId.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (record != null)
            {
                records.Remove(record);
                SaveAllRecords(records);
                _logger.Log("Delete-Hard", $"Permanently purged record ID {id} from storage file.");
                return true;
            }
            return false;
        }
    }

    #endregion

    #region Report Generator Component

    public class ReportGenerator
    {
        private readonly VehicleRecordRepository _repository;
        private readonly AuditLogger _logger;

        public ReportGenerator(VehicleRecordRepository repository, AuditLogger logger)
        {
            _repository = repository;
            _logger = logger;
        }

        /// <summary>
        /// Aggregates system insights, computing revenue patterns metrics, total investments, and unique counts.
        /// </summary>
        public void GenerateFinancialMetricsReport()
        {
            _logger.Log("Report", "Generated Analytical Financial Report.");
            var records = _repository.GetAllActive();

            Console.WriteLine("\n==================================================");
            Console.WriteLine("        VEHICLE SERVICE METRICS REPORT            ");
            Console.WriteLine($"Generated On: {DateTime.Now}");
            Console.WriteLine("==================================================");

            if (!records.Any())
            {
                Console.WriteLine("No active database entries found to compile aggregate metrics.");
                Console.WriteLine("==================================================");
                return;
            }

            int totalRecords = records.Count;
            decimal totalRevenue = records.Sum(r => r.Cost);
            decimal averageCost = records.Average(r => r.Cost);
            int uniqueVehicles = records.Select(r => r.LicensePlate.ToUpper()).Distinct().Count();

            Console.WriteLine($"Total Active Service Records: {totalRecords}");
            Console.WriteLine($"Total Revenue Generated     : Php {totalRevenue:N2}");
            Console.WriteLine($"Average Cost Per Job       : Php {averageCost:N2}");
            Console.WriteLine($"Unique Vehicles Serviced    : {uniqueVehicles}");
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine("Breakdown by Service Type:");

            var serviceGroups = records.GroupBy(r => r.ServiceType, StringComparer.OrdinalIgnoreCase);
            foreach (var group in serviceGroups)
            {
                Console.WriteLine($" - {group.Key}: {group.Count()} Jobs done, Totaling: Php {group.Sum(g => g.Cost):N2}");
            }
            Console.WriteLine("==================================================");
        }
    }

    #endregion

    #region Program Menu / Controller

    class Program
    {
        private static string StorageDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DataStorage");
        private static string DataFile = Path.Combine(StorageDirectory, "services.dat");
        private static string AuditFile = Path.Combine(StorageDirectory, "audit.log");

        private static VehicleRecordRepository _repo;
        private static AuditLogger _logger;
        private static ReportGenerator _reportGenerator;

        static void Main(string[] args)
        {
            InitializeStorage();

            _logger = new AuditLogger(AuditFile);
            _repo = new VehicleRecordRepository(DataFile, _logger);
            _reportGenerator = new ReportGenerator(_repo, _logger);

            _logger.Log("System-Start", "Application loop initialized successfully.");

            bool running = true;
            while (running)
            {
                try
                {
                    Console.Clear();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("==================================================");
                    Console.WriteLine("   VEHICLE SERVICE RECORDS MANAGEMENT SYSTEM     ");
                    Console.WriteLine("==================================================");
                    Console.ResetColor();
                    Console.WriteLine("1. Add New Service Record");
                    Console.WriteLine("2. View All Active Records / Search");
                    Console.WriteLine("3. Update Existing Record");
                    Console.WriteLine("4. Delete Record (Soft Delete)");
                    Console.WriteLine("5. Generate System Metrics Report");
                    Console.WriteLine("6. View System Audit Trails");
                    Console.WriteLine("7. Admin - Hard Purge Record");
                    Console.WriteLine("8. Exit Application");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("==================================================");
                    Console.Write("Select an operation option (1-8): ");
                    Console.ResetColor();

                    string choice = Console.ReadLine();
                    switch (choice)
                    {
                        case "1": AddRecordFlow(); break;
                        case "2": ViewRecordsFlow(); break;
                        case "3": UpdateRecordFlow(); break;
                        case "4": DeleteRecordFlow(hardDelete: false); break;
                        case "5": _reportGenerator.GenerateFinancialMetricsReport(); PauseForUser(); break;
                        case "6": DisplayAuditTrailFlow(); break;
                        case "7": DeleteRecordFlow(hardDelete: true); break;
                        case "8":
                            running = false;
                            _logger.Log("System-Exit", "Application terminated normally by user command.");
                            break;
                        default:
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("Invalid choice input detected. Please select standard numeric items 1-8.");
                            Console.ResetColor();
                            PauseForUser();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log("Error-Fatal", $"Unhandled exception wrapper: {ex.Message}");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"An execution fault occurred within the UI context loop: {ex.Message}");
                    Console.ResetColor();
                    PauseForUser();
                }
            }
        }

        private static void InitializeStorage()
        {
            try
            {
                if (!Directory.Exists(StorageDirectory))
                {
                    Directory.CreateDirectory(StorageDirectory);
                }
                if (!File.Exists(DataFile))
                {
                    File.Create(DataFile).Close();
                }
                if (!File.Exists(AuditFile))
                {
                    File.Create(AuditFile).Close();
                }
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Initialization IO Fatal Failure: {ex.Message}");
                Environment.Exit(1);
            }
        }

        private static void AddRecordFlow()
        {
            Console.Clear();
            Console.WriteLine(">>> Add New Service Record <<<\n");

            string plate, owner, service, costStr, error;
            decimal cost;

            while (true)
            {
                Console.Write("Enter License Plate: "); plate = Console.ReadLine();
                if (Validator.ValidateLicensePlate(plate, out error)) break;
                Console.WriteLine($"[Error]: {error}");
            }

            while (true)
            {
                Console.Write("Enter Owner Full Name: "); owner = Console.ReadLine();
                if (Validator.ValidateOwnerName(owner, out error)) break;
                Console.WriteLine($"[Error]: {error}");
            }

            while (true)
            {
                Console.Write("Enter Service Type (e.g., Oil Change, Brake Repair): "); service = Console.ReadLine();
                if (Validator.ValidateServiceType(service, out error)) break;
                Console.WriteLine($"[Error]: {error}");
            }

            while (true)
            {
                Console.Write("Enter Total Operational Cost (Php): "); costStr = Console.ReadLine();
                if (Validator.ValidateCost(costStr, out cost, out error)) break;
                Console.WriteLine($"[Error]: {error}");
            }

            var newRecord = new ServiceRecord
            {
                RecordId = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(),
                LicensePlate = plate.Trim(),
                OwnerName = owner.Trim(),
                ServiceType = service.Trim(),
                Cost = cost,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                IsActive = true
            };
            newRecord.Checksum = newRecord.ComputeChecksum();

            _repo.Add(newRecord);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\nRecord saved successfully! Assigned ID: {newRecord.RecordId}");
            Console.ResetColor();
            PauseForUser();
        }

        private static void ViewRecordsFlow()
        {
            Console.Clear();
            Console.WriteLine(">>> View / Search Service Records <<<\n");
            Console.Write("Filter options: Press [Enter] to view all or type a search token (License Plate / Owner): ");
            string filter = Console.ReadLine()?.Trim();

            var records = _repo.GetAllActive();

            if (!string.IsNullOrEmpty(filter))
            {
                records = records.Where(r => r.LicensePlate.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                             r.OwnerName.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            DisplayTable(records);
            PauseForUser();
        }

        private static void UpdateRecordFlow()
        {
            Console.Clear();
            Console.WriteLine(">>> Update Existing Service Record <<<\n");
            Console.Write("Enter the 8-character ID of the record to update: ");
            string id = Console.ReadLine()?.Trim();

            var record = _repo.GetById(id);
            if (record == null || !record.IsActive)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Record reference identifier does not exist or is inactive.");
                Console.ResetColor();
                PauseForUser();
                return;
            }

            Console.WriteLine($"\nCurrently modifying entry for vehicle [{record.LicensePlate}] registered to {record.OwnerName}.");
            Console.WriteLine("Leave blank and press [Enter] to retain current parameters.");

            Console.Write($"New Service Type [{record.ServiceType}]: ");
            string service = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(service)) record.ServiceType = service.Trim();

            Console.Write($"New Cost [{record.Cost}]: ");
            string costStr = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(costStr))
            {
                if (Validator.ValidateCost(costStr, out decimal cost, out string err))
                {
                    record.Cost = cost;
                }
                else
                {
                    Console.WriteLine($"Invalid Cost adjustments string token ignored. Retaining original value: {err}");
                }
            }

            record.UpdatedAt = DateTime.Now;
            record.Checksum = record.ComputeChecksum(); // Recompute verification hash

            _repo.Update(record);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\nRecord modified and updated successfully.");
            Console.ResetColor();
            PauseForUser();
        }

        private static void DeleteRecordFlow(bool hardDelete)
        {
            Console.Clear();
            string modeText = hardDelete ? "HARD PURGE (Permanent)" : "SOFT DELETE (Deactivate)";
            Console.WriteLine($">>> Delete Service Record - MODE: {modeText} <<<\n");
            Console.Write("Enter the target Record ID: ");
            string id = Console.ReadLine()?.Trim();

            bool result = hardDelete ? _repo.HardDelete(id) : _repo.SoftDelete(id);

            if (result)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Operation successful. Record ID {id} was processed.");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Transaction failure: Record ID not found or operation inapplicable.");
            }
            Console.ResetColor();
            PauseForUser();
        }

        private static void DisplayAuditTrailFlow()
        {
            Console.Clear();
            Console.WriteLine(">>> System Logs & Audit Trail <<<\n");
            if (File.Exists(AuditFile))
            {
                string[] logs = File.ReadAllLines(AuditFile);
                int lastCount = Math.Min(logs.Length, 25); // Output only top last 25 operations for buffer safety
                Console.WriteLine($"Displaying last {lastCount} operations:");
                for (int i = logs.Length - lastCount; i < logs.Length; i++)
                {
                    Console.WriteLine(logs[i]);
                }
            }
            else
            {
                Console.WriteLine("No logs compiled.");
            }
            PauseForUser();
        }

        private static void DisplayTable(List<ServiceRecord> records)
        {
            Console.WriteLine("\n-------------------------------------------------------------------------------------------------");
            Console.WriteLine(string.Format("| {0,-8} | {1,-12} | {2,-20} | {3,-18} | {4,-12} |", "ID", "Plate No.", "Owner Name", "Service", "Cost (Php)"));
            Console.WriteLine("-------------------------------------------------------------------------------------------------");
            foreach (var r in records)
            {
                Console.WriteLine(string.Format("| {0,-8} | {1,-12} | {2,-20} | {3,-18} | {4,12:N2} |", 
                    r.RecordId, 
                    r.LicensePlate.Length > 12 ? r.LicensePlate.Substring(0,9)+"..." : r.LicensePlate, 
                    r.OwnerName.Length > 20 ? r.OwnerName.Substring(0,17)+"..." : r.OwnerName, 
                    r.ServiceType.Length > 18 ? r.ServiceType.Substring(0,15)+"..." : r.ServiceType, 
                    r.Cost));
            }
            Console.WriteLine("-------------------------------------------------------------------------------------------------\n");
        }

        private static void PauseForUser()
        {
            Console.WriteLine("\nPress any key to return to the Main Menu...");
            Console.ReadKey();
        }
    }

    #endregion
}
